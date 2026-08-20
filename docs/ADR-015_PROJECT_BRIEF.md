# ADR-015 — ProjectBrief as a first-class context source

**Status:** proposed · **Date:** 2026-08-20 · **Supersedes:** nothing
**Depends on:** ADR-014 (Azure SQL). **Blocks:** nothing — but see §6 on sequencing.

---

## 1. Context — the highest-trust item in the bundle is the least informative

`ChatContextBundleMapper` currently maps a `Project` to a single `ContextItem`:

```
Kind  = Objective
Trust = Authoritative
Body  = the project's name
```

`PromptAssembler` orders sections `Objective, Constraint, Decision, Fact, Document,
Artifact, Outcome, Instruction, Message`. So the **first thing the model reads**, in the
slot reserved for what the work is *for*, is a bare string like `"Project Alpha"`.

Be precise about the harm, because it is not what it first looks like. The ranker computes

```
score = (0.15 + 0.85 × matches/terms) × trustWeight × recencyDecay × (relevanceHint ?? 1.0)
```

so a low-keyword item does **not** dominate on score — `Authoritative` (1.6) with minimal
overlap lands around 0.24, well under a `Curated` fact with real overlap at 1.0. The damage
is *positional and semantic*, not arithmetic:

- The most prominent section of the prompt is occupied by a token that carries no meaning.
- `Authoritative` is the strongest trust signal in the system, and it is being spent on a
  label. Every other `Authoritative` item now shares a tier with something worthless, which
  is a slow way to make a trust scale meaningless.
- The model has no statement of purpose, constraints, or current state — so it infers them
  from the message history, which is exactly the drift a persistent system exists to prevent.

Meanwhile `T_007_ProjectBrief` has existed in Dataverse since Phase 2, unmodelled in C#,
containing precisely the fields that gap needs.

## 2. Decision

**Model `ProjectBrief` as an aggregate owned by the Chat product, and map it to *several*
`ContextItem`s of different kinds — not one.**

A brief is not one fact. It is a purpose, a set of constraints, a set of decisions, and a
current state, and the bundle already has distinct kinds for each. Flattening it into a
single `Objective` blob would waste the ranking and section machinery that already exists.

### 2.1 The field-to-kind mapping

| Brief field | `ContextItem.Kind` | Trust | Why |
|---|---|---|---|
| `Purpose` | `Objective` | Authoritative | This is what the `Objective` slot was built for |
| `CurrentState` | `Objective` | Authoritative | Where the work actually stands |
| `CurrentPhase` | `Objective` | Authoritative | Short; often decides which answer is appropriate |
| `CurrentDirection` | `Objective` | Authoritative | Where it is heading next |
| `ImportantConstraints` | `Constraint` | Authoritative | Already its own section, currently fed only by `WorkItem` at `Curated` |
| `KeyDecisions` | `Decision` | Authoritative | Complements `Adr`; the brief holds the summary, ADRs hold the reasoning |
| `CurrentArchitecture` | `Fact` | Authoritative | Structural truth, not an aspiration |
| `OpenQuestions` | `Instruction` | Authoritative | **See §2.2 — this one is different** |
| `CurrentMilestone` → | `Objective` | Authoritative | Resolved through the FK; the milestone's name and target date |

Each emitted item gets a distinct `Id` (`brief:{briefId}:purpose`, `:constraints`, and so
on) so citations point at *which part* of the brief was used, not just "the brief". A
citation the user cannot trace to a specific claim is decoration.

Empty fields emit **nothing**. A brief with only `Purpose` filled in produces one item, not
nine empty ones. This matters more than it sounds: an empty `Constraint` item still consumes
a section header and a token budget slot, and still tells the model that constraints were
considered and found to be none — which is a lie when the truth is that nobody filled the
field in.

### 2.2 `OpenQuestions` maps to `Instruction`, deliberately

The other eight fields are statements about the project. `OpenQuestions` is a statement
about the *limits of what is known* — and the useful behaviour is for the model to treat
those as things it must not quietly assume answers to.

`Instruction` is the right kind because it is the only one the prompt treats as directive
rather than informational. The mapper should render it as such, e.g.
`"These questions are open and unresolved. Do not assume an answer to them; say so if the
request depends on one: …"`.

If this proves too blunt in practice, the alternative is a new `Kind` — but adding a kind
touches `Nexus.Intelligence.Contracts`, which every product depends on, so it is not a
change to make speculatively. Try the mapping first; measure; then decide.

### 2.3 Trust decays with review age

`ProjectBrief` carries `LastReviewedOn` and `Version`. **Use them.**

A brief reviewed last week is authoritative. A brief last reviewed eight months ago, on a
project that has moved since, is a confident description of a system that no longer exists —
and `Authoritative` (1.6) makes it *outrank* the recent conversation messages that contradict
it. That is the worst possible failure mode: the system is most sure exactly where it is most
wrong.

So the brief's trust is a function of review age, not a constant:

| Reviewed within | Trust | Weight |
|---|---|---|
| 30 days | `Authoritative` | 1.6 |
| 90 days | `Approved` | 1.3 |
| 180 days | `Curated` | 1.0 |
| older | `Reported` | 0.7 |

This is a **product-side** decision — the mapper chooses the trust level, and Intelligence
just honours it. No Intelligence change is required, which is the seam working as designed.

Two consequences worth stating plainly. First, this makes staleness *visible* rather than
silent: a brief sliding to `Reported` will start losing slots to fresher context, and the
answers will degrade gradually instead of confidently. Second, it gives `LastReviewedOn` a
real job, which is the only way a "please review this" field ever gets maintained.

## 3. Schema — three anomalies to fix on the way in

`ProjectBrief`, `ProjectMilestone` and `MilestoneCriterion` are three of the ten unmodelled
tables. Their Dataverse definitions carry problems that must not be carried into SQL.

**A — `ProjectBrief.project` is not required.** A brief with no project is meaningless. In
SQL the FK is `NOT NULL`, and the relationship is **one brief per project**, enforced by a
unique index on `ProjectId`. If versioning of briefs is wanted later, that is a history
table, not multiple live rows — otherwise "the brief" becomes ambiguous at the exact moment
the mapper needs to pick one.

**B — duplicate status columns on both milestone tables.**

```
ProjectMilestone    projectmilestonestatus  required, NO choice values defined
                    status                  optional, six values defined
MilestoneCriterion  milestonecriteriastatus required, NO choice values defined
                    status                  optional, five values defined
```

Same shape as the `*01` duplicates ADR-014 Rule 1 deletes. Keep the column that has actual
values; drop the empty one.

Note this **inverts ADR-014 Rule 3**. That rule says the C# enum is authoritative because a
working enum existed while the Dataverse export defined nothing. Here there is no C# enum at
all — these tables were never modelled — so the Dataverse optionset is the only evidence
that exists, and the C# enum should be *created from it*:

```csharp
public enum MilestoneStatus  { Planned, Active, Blocked, Review, Completed, Cancelled }
public enum CriterionStatus  { Pending, InProgress, Completed, Blocked, NotApplicable }
```

Rule 3's intent was "prefer the source that is real". Applied here, that points the other
way. Worth recording, because the rule as written reads like a blanket preference for C#,
and it isn't.

**C — `MilestoneCriterion.evidence`.** A free-text column recording *why* a criterion was
judged complete. It has no C# counterpart and nothing reads it — which by ADR-014 Rule 5
argues for dropping it. **Do not drop it.** Unlike `Project.projecttype`, this one has an
obvious consumer the moment the brief exists: evidence of completion is exactly the kind of
`Outcome` context the bundle has a section for and currently never fills. Keep it, and note
it as deliberately-unused-for-now rather than dead.

## 4. What this does not change

- **No Intelligence change.** New `ContextItem`s of existing kinds, at existing trust
  levels. `Nexus.Intelligence.Contracts` is untouched. If this design required a contract
  change, that would be evidence the seam was drawn wrong.
- **No Platform change.**
- **No new `Kind`.** See §2.2.
- The brief is **product data**, owned by `Nexus.Web`, in the `project` SQL schema as
  `project.ProjectBrief`. A future product will have its own idea of what context means.

## 5. Consequences

**Good.** The `Objective`, `Constraint` and `Decision` sections carry real content for the
first time. Answers stop inferring project purpose from message history. `LastReviewedOn`
acquires a function. Citations become traceable to a specific claim within the brief.

**Costs, stated honestly.** The brief consumes token budget on every single turn — nine
potential items in the highest-trust tier, competing for the same window as conversation
history. `PromptAssembler` reserves 25% for the response and estimates 4 chars/token; a
verbose brief could crowd out recent messages, which is a *different* failure from the one
being fixed but not obviously a better one. Mitigations, in order of preference:

1. Field-level length limits at the domain boundary — a `Purpose` is a paragraph, not an
   essay. Enforce in the aggregate, not the database.
2. `RelevanceHint` below 1.0 on the less situational fields (`CurrentArchitecture`,
   `KeyDecisions`) so they yield to conversation when the window is tight.
3. Only if 1 and 2 prove insufficient: summarise the brief before mapping.

**The thing that will actually go wrong.** Briefs will be written once and never reviewed,
and §2.3's decay will quietly demote them until they contribute nothing — at which point the
system is back to where it is today, but with more machinery. The decay is a mitigation, not
a fix. The real fix is making review cheap, which is a UI problem (§6) and not solvable here.

## 6. Sequencing — design now, build after F3

**Do not implement this before the chat UI can measure it.**

The whole claim of this ADR is that answers get better. That claim is currently
unfalsifiable: with no citations panel there is no way to see which brief fields were
selected, at what trust, or whether they displaced something more useful. Building it now
means writing nine mappings and a trust-decay rule on the strength of an argument, and
finding out months later whether the argument was right.

After F3, the test is concrete: take a real project, send the same prompt with and without a
brief, and compare which context items were selected. That is a fifteen-minute experiment
that either supports §2.3's trust table or corrects it — and correcting a trust table costs
one constant, while correcting it after it has shaped a year of answers costs credibility.

Suggested order once F3 lands:

1. SQL: `project.ProjectBrief`, `project.ProjectMilestone`, `project.MilestoneCriterion`
   (this is SQL Stage 2d, and these three are its most valuable members).
2. Domain aggregate + repository, with the field-length limits from §5.
3. Mapper: `Purpose` and `ImportantConstraints` **only**. Measure.
4. Add the remaining fields one at a time, measuring each. A field that does not change
   which context is selected is a field that should not be in the prompt.
5. Trust decay last — it only matters once briefs are old enough to test it, which they
   will not be on day one.

Step 3 is the important one. Nine mappings shipped together cannot be individually
evaluated; two mappings can.
