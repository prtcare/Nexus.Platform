# ADR Standard

> **SUPERSEDED NUMBERING NOTICE (2026-09-05):** This document's own header
> (`**Owner:** DEVELOPER (Layer 07); custodianship passes to DATA (Layer 02)...`)
> and its worked examples (`02 DATA, 08 DELIVERY`) reflect the v2.1 twelve-layer
> model, in which 07 DEVELOPER was a numbered Platform layer and DELIVERY was
> numbered 08. Per the approved v2.2 renumbering (`LAYER_MODEL.md` §2.2, §4a),
> Nexus Forge and Nexus Developer (the product) now sit OUTSIDE the ten
> numbered Platform layers, and DELIVERY is renumbered 07 (from 08). The ADR
> process itself remains valid. Re-deriving this document's own ownership
> header and examples against the v2.2 model is Wave-D-adjacent decision work
> and is explicitly NOT done in this batch.

**Status:** Active
**Owner:** DEVELOPER (Layer 07); custodianship passes to DATA (Layer 02) when documents become
`Document` records at **M-02-2.1**
**Last updated:** 2026-08-21
**Layer:** cross-cutting
**Authoritative for:** what an ADR is and is not, the global numbering sequence, file naming, the
required front matter and sections, status values and their transitions, superseding, affected
layers and affected products, and when an ADR is mandatory.

Not authoritative for: the file-name **pattern** itself — `NAMING_STANDARDS.md` §42 owns it and this
document applies it; where documents live and how they are maintained generally —
`DOCUMENTATION_STANDARDS.md`; which technologies may be adopted — `TECHNOLOGY_STACK.md` §8; when a
work item may advance past architecture — `DEVELOPMENT_WORKFLOW.md` §2.1 state 2.

---

## 1. What an ADR is

An Architecture Decision Record captures **one decision, the situation that forced it, what else was
considered, and what it costs**. It is written when the decision is made and is never rewritten
afterwards. If the decision changes, a new ADR supersedes the old one and the old one stays on disk.

An ADR is not a design document, not a specification, not a runbook and not a plan. It answers *why
is it like this* for a reader who arrives eighteen months later and would otherwise assume the
current shape was an accident.

### 1.1 The value is in the alternatives and the consequences

The Decision section is the shortest useful part of an ADR. Anyone can read the code and see what
was decided. What is unrecoverable from the code is:

- **which alternatives were considered and why they lost** — without it, the same alternative is
  re-proposed every year and re-rejected from memory rather than from record;
- **what the decision costs** — without it, the costs are rediscovered as surprises and blamed on
  whoever is present.

An ADR with a thin Alternatives section is an ADR that will not survive its first challenge.

---

## 2. When an ADR is mandatory

| Situation | ADR required |
|---|---|
| A decision that is expensive to reverse | **Yes** |
| A decision that constrains other layers | **Yes** |
| Choosing or removing a technology | **Yes** — and it updates `TECHNOLOGY_STACK.md` |
| Changing a persistence, identity, tenancy or security model | **Yes** |
| Changing a published contract in `*.Contracts` | **Yes** |
| Changing an architectural invariant (dependency direction, no shared kernel, no product branching) | **Yes** |
| Adopting a cross-cutting convention that outlives its author | **Yes** |
| Deciding *not* to do something significant, deliberately | **Yes** — a recorded non-decision is a decision |
| Implementing a milestone as already specified in the roadmap | No |
| A refactor with no external consequence | No |
| A naming or formatting choice | No — that is a standards document |

The test: **would a competent engineer arriving later reasonably ask "why is it like this?" and be
unable to answer from the code?** If yes, write the ADR.

`DEVELOPMENT_WORKFLOW.md` §2.1 state 2 makes this concrete — an item enters *Architecture decided*
with "an ADR where the decision is durable" among its required evidence.

---

## 3. Numbering

**ADRs use ONE global sequence across the entire system.** Not per-layer, not per-repository, not
per-product.

```
ADR-014   Azure SQL replaces Dataverse for the Chat product    (exists, Accepted)
ADR-015   ProjectBrief as a first-class context source         (exists, Proposed)
ADR-016   ← the next number
```

| Rule | Statement |
|---|---|
| One sequence | There is no `ADR-CORE-001` and never will be |
| Three digits, zero-padded | `ADR-016`, not `ADR-16` |
| Allocated when the ADR is written | Not when it is accepted |
| Never reused | A rejected or withdrawn ADR keeps its number permanently |
| Never renumbered | Twelve documents cite `ADR-014`; renumbering breaks all of them silently |

**Why one sequence, stated once so it is not relitigated:** decisions in Nexus routinely cross
layers. ADR-014 is filed against a product but changes DATA's persistence model, DELIVERY's
migration mechanics and SECURITY's access-control story simultaneously. A per-layer sequence forces
a filing decision at the moment the decision is least understood, and produces two ADRs numbered 007
that mean different things. A single sequence has one cost — allocating a number requires knowing
the last one — and that cost disappears at **M-02-2.1**, when the document store allocates it.

### 3.1 Collision under parallel work

Two workers writing ADRs simultaneously will both reach for `ADR-016`.

**CURRENT:** allocate the number **and push the file immediately**, even as a stub with Status
`Proposed` and nothing but a title. The push is the allocation. This follows the same lesson as the
2026-08-20 incident — `GIT_WORKFLOW.md` §2.5: unpushed work is invisible to everyone else.

**TARGET — M-02-2.1:** an ADR is a `Document` with a `Ref` computed PERSISTED column, allocated by
the database. Concurrent allocation stops being a convention and becomes a constraint, exactly as
`WKS-00000001` already works for `Workspace` — `DATABASE_STANDARDS.md` §3.

### 3.2 The gap below 014

ADR-014 records `Supersedes: ADR-002 (Dataverse is the operational source of truth)`. **ADR-001 to
ADR-013 are not present as files in the current documentation set.** They were made and recorded
elsewhere; ADR-002's content survives only as the sentence ADR-014 quotes when superseding it.

This is a **TRANSITION** condition, not a licence to renumber:

- The sequence continues from 015. Numbers 001–013 stay spent.
- When an earlier ADR is found, it is filed at its own number, whatever the date.
- **WI-02-2.1.2 Import the existing document set** imports the ADR series into the document store,
  and **T-02-2.1.2.2 establish which file remains authoritative during transition** is where the
  missing numbers are reconciled or recorded as lost. Recorded as lost is an acceptable outcome;
  quietly reusing the numbers is not.

---

## 4. File naming and location

`NAMING_STANDARDS.md` §42 owns the pattern:

```
ADR-<nnn>_<SCREAMING_SNAKE>.md
```

Real examples: `ADR-014_AZURE_SQL_MIGRATION.md`, `ADR-015_PROJECT_BRIEF.md`.

The slug is short and stable. It names the **subject**, not the outcome, so that a superseding ADR
about the same subject reads as a related document rather than a contradiction.

ADRs live with the rest of the canonical documentation set — `DOCUMENTATION_STANDARDS.md` §3. An ADR
never lives only in a pull request, an issue, a chat transcript or a comment in code.

---

## 5. Front matter

Every ADR carries these fields, in this order, before any prose.

| Field | Required | Rule |
|---|---|---|
| `Status` | Yes | One of §6, capitalised exactly |
| `Date` | Yes | ISO `yyyy-MM-dd`. The date the status was last set, not the date the file was touched |
| `Owner` | Yes | The person accountable for the decision. A name, not a team, and not an agent |
| `Supersedes` | Yes | An ADR number, or the word `nothing` |
| `Superseded by` | When superseded | The ADR number that replaced it. Added to the old ADR at the moment the new one is accepted |
| `Affected layers` | Yes | Layer numbers and short names, e.g. `02 DATA, 08 DELIVERY` |
| `Affected products` | Yes | Product names, or `none` for a platform-wide decision |
| `Related milestones` | Where known | Milestone ids, e.g. `M-02-1.4` |
| `Depends on` | Where applicable | ADRs that must hold for this one to make sense |

**Why `Affected layers` and `Affected products` are mandatory.** They are the fields that make an ADR
findable by the person who needs it. A developer starting work in AUTOMATION needs the four ADRs
that constrain AUTOMATION, not a chronological list of twenty. They are also the join key for
**M-02-2.1**'s polymorphic `DocumentLink`, which links a document to any layer entity by layer id,
entity type and entity id with no foreign key. An ADR without them imports as an orphan.

**CURRENT:** neither existing ADR carries `Owner`, `Affected layers` or `Affected products`, and the
two use different front-matter layouts — ADR-014 uses stacked bold lines, ADR-015 a single
middot-separated line. **New ADRs use the form in §9.** The two existing files are brought into line
when they are next edited for another reason, not as standalone churn.

---

## 6. Status

| Status | Meaning | Who may set it |
|---|---|---|
| **Proposed** | Written, circulated, not yet agreed. Nothing may be built on it | The author |
| **Accepted** | Agreed. Binding. Code that contradicts it is a defect | The Owner, after review |
| **Superseded** | Replaced by a later ADR. Kept permanently | Set when the superseding ADR is accepted |
| **Rejected** | Considered and declined. Kept permanently — the reasoning is the value | The Owner |
| **Deprecated** | Still true of the current system, but on a stated path out | The Owner, naming the milestone |

Legal transitions:

```
Proposed ──▶ Accepted ──▶ Deprecated ──▶ Superseded
    │                          │
    └──▶ Rejected              └──▶ Superseded
```

Three rules:

- **An ADR file is never deleted.** ADR-014 states it directly: *"Do not delete ADR-002 — the
  maintenance rule in that document says supersede, never erase."* A deleted ADR takes the reasoning
  with it and guarantees the decision is remade blind.
- **An accepted ADR is never edited to change its decision.** Typos, broken links and added
  cross-references, yes. A changed decision is a new ADR.
- **`Proposed` is not permission.** ADR-015 is `Proposed`, and `ProjectBrief` is correspondingly
  absent from `Nexus.Products.Chat.Domain`'s eleven aggregates. That is the standard working as
  intended.

---

## 7. Required sections

In this order. A section with nothing to say says so in one line; it is not omitted.

| # | Section | Contains | Failure mode when weak |
|---|---|---|---|
| 1 | **Context** | The situation that forces a decision. Facts, constraints, the cost of doing nothing | The decision reads as arbitrary |
| 2 | **Decision** | What is decided, in the present tense, unambiguously | Ambiguity is resolved differently by each reader |
| 3 | **Alternatives** | Each option considered, and **why it lost** | The same option is re-proposed annually |
| 4 | **Consequences** | What this costs — positive, negative and neutral | Costs arrive as surprises |
| 5 | **Affected layers and products** | Which layers and products change, and how | Nobody downstream learns it happened |
| 6 | **Verification** | How anyone can tell the decision is actually being followed | The ADR is aspirational |

### 7.1 Context

Written as facts, not as narrative. ADR-014's Context is the model: it states what ADR-002 chose and
why that reasoning was sound at the time, then states precisely what changed — the V2.1 restructure
confined all Dataverse code to `Nexus.Products.Chat.Infrastructure`, which made the store
replaceable *"at a cost that will never be lower than it is today."*

That last clause is the whole Context in one sentence: it names the force and the timing together.

### 7.2 Decision

Present tense, active, specific. *"The Chat product persists to Azure SQL Database."* Not "we will
migrate", not "we should consider". If a reader can implement two different systems from the
Decision section, it is not finished.

### 7.3 Alternatives

Each alternative gets its own short block: what it was, what it offered, and the specific reason it
lost. "Rejected as unsuitable" is not a reason. **"Do nothing" is always one of the alternatives**,
and it is the one most often omitted — it is also the one a future reader most wants to see
considered.

### 7.4 Consequences

Honest and specific, including what gets worse. ADR-014's real consequences included losing
Dataverse row-level security, which is the whole reason for the window described in
`SECURITY_STANDARDS.md` §1 where the system has **no access control of any kind**. An ADR that lists
only benefits is marketing.

State consequences in three groups: what improves, what gets worse, and what becomes possible or
impossible later.

### 7.5 Verification

New, and the section most often missing. It names the mechanism that makes the decision **checkable**
rather than remembered:

- An architecture test — `PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs`, `BoundaryTests.cs` are
  the three that exist and use NetArchTest.
- An acceptance criterion in ASSURANCE — **M-09-1.1**.
- A named review item in `CODE_REVIEW_CHECKLIST.md`.
- Or, honestly: "none — this is verified by review only."

A decision with no verification mechanism decays quietly. Writing "none" is acceptable; leaving the
section out hides the fact.

---

## 8. Superseding

When a decision is replaced:

1. The **new** ADR gets the next number and lists `Supersedes: ADR-<nnn>`.
2. The **old** ADR's `Status` becomes `Superseded` and it gains `Superseded by: ADR-<nnn>`.
3. The old ADR's body is **not** edited. It remains the true record of what was decided then.
4. Any standards document that cited the old ADR is updated to cite the new one — the
   no-duplication rule in `DOCUMENTATION_STANDARDS.md` §7 means there should be few such citations.

A partial supersede is still a full supersede. If a new ADR changes half of an old one, it supersedes
the whole thing and restates the half that survives. "ADR-021 supersedes §3 of ADR-014" produces a
decision that exists in two places, which is the condition this standard is designed to prevent.

---

## 9. Template

Copy this file, replace the angle-bracketed parts, delete the guidance in italics.

```markdown
# ADR-<nnn> — <Short decision title in sentence case>

**Status:** Proposed
**Date:** <yyyy-MM-dd>
**Owner:** <Name of the person accountable>
**Supersedes:** <ADR-nnn | nothing>
**Superseded by:** <ADR-nnn | —>
**Affected layers:** <e.g. 02 DATA, 08 DELIVERY>
**Affected products:** <product names | none>
**Related milestones:** <e.g. M-02-1.4, M-08-1.2 | none>
**Depends on:** <ADR-nnn | nothing>

---

## 1. Context

*The situation that forces a decision. Facts and constraints, not narrative. What is true today,
what changed, and what doing nothing costs. Name real types, files and milestones.*

## 2. Decision

*Present tense, active, unambiguous. One decision. If a reader could build two different systems
from this section, it is not finished.*

## 3. Alternatives considered

### 3.1 <Alternative>
*What it was, what it offered, and the specific reason it lost.*

### 3.2 <Alternative>
*As above.*

### 3.3 Do nothing
*Always considered. What continuing as-is would cost, and when that cost arrives.*

## 4. Consequences

**What improves**
- …

**What gets worse**
- *Be specific. An ADR that lists only benefits is marketing.*

**What becomes possible or impossible later**
- …

## 5. Affected layers and products

| Layer / Product | Effect | Action required |
|---|---|---|
| <02 DATA> | <what changes> | <what someone must do> |

## 6. Verification

*How anyone can tell this decision is being followed: an architecture test, an ASSURANCE acceptance
criterion, a review checklist item — or, honestly, "none; verified by review only".*

## 7. References

- <sibling documents, milestones, prior ADRs>
```

---

## 10. The two live examples

Read these before writing a new ADR. They are the house style, imperfections included.

### 10.1 `ADR-014_AZURE_SQL_MIGRATION.md` — Accepted, 2026-08-18

Dataverse → Azure SQL for the Chat product. **The best example of Context and Drivers in the set.**
It names four drivers — cost and licensing, query power, and two more — and states that *all four
applied, which is why this is not a marginal call*. It scopes itself explicitly: *"Applies to
`Nexus.Web` — the Chat product only. Platform and Intelligence are unaffected."* It supersedes
ADR-002 and instructs that ADR-002 not be deleted.

**Where it departs from this standard:** it bundles a full migration plan into the same file, so it
is simultaneously an ADR and a runbook; it has no `Owner`, no `Affected layers` and no `Affected
products`; and it carries an inline editorial note about a future docs pass. Its decision content is
authoritative regardless — the departures are noted so they are not copied.

**Everything ADR-014 decided that is now proven** — the Id/Seq/Ref pattern, schemas replacing
prefixes, converters confined to Infrastructure, the cascade rule under SQL Server error 1785 — is
documented in `DATABASE_STANDARDS.md`, not restated here.

### 10.2 `ADR-015_PROJECT_BRIEF.md` — Proposed, 2026-08-20

`ProjectBrief` as a first-class context source. **The best example of a precisely bounded problem
statement.** It shows exactly what `ChatContextBundleMapper` does today — a `Project` becomes one
`ContextItem` with `Kind = Objective`, `Trust = Authoritative`, and a body that is the project's
name — and then does something most ADRs skip: it **quantifies the harm honestly and corrects the
obvious overstatement**, showing from the ranking formula that the damage is positional and semantic
rather than arithmetic.

It is `Proposed`. `ProjectBrief` does not exist in `Nexus.Products.Chat.Domain`, whose eleven
aggregates are Adr, Artifact, Branch, Conversation, ConversationMessage, Knowledge, Project,
Session, Snapshot, WorkItem and Workspace. Nothing is built on a proposed ADR.

Note the recursion worth being aware of: **`Adr` is itself one of those eleven aggregates.** The
Chat domain already models an ADR as data. Which record becomes authoritative — the markdown file or
the `Adr` aggregate — is settled at §11.

---

## 11. CURRENT / TARGET / TRANSITION

| Aspect | State |
|---|---|
| One global sequence, next is ADR-016 | **CURRENT** — binding now |
| ADRs are markdown files in the documentation set | **CURRENT** |
| An `Adr` aggregate exists in `Nexus.Products.Chat.Domain` | **CURRENT** — modelled, not the authoritative store |
| Front matter with Owner, Affected layers, Affected products | **TRANSITION** — required on new ADRs; the two existing files predate it |
| Verification section | **TRANSITION** — required on new ADRs |
| ADRs as `Document` records with versioning and links | **TARGET — M-02-2.1 Document with versioning** |
| Number allocated by a `Ref` computed column rather than by convention | **TARGET — M-02-2.1** |
| An ADR linked to the milestones and work items it constrains | **TARGET — M-02-2.1 `DocumentLink`, M-07-1.1 work graph** |

**The transition rule, stated once:** until M-02-2.1 imports the document set, **the markdown file
is authoritative** and the `Adr` aggregate is an unpopulated model. After the import, the
`Document` record is authoritative and the markdown file is its rendering. There is never a period
in which both are edited independently — T-02-2.1.2.2 exists precisely to record which one wins on
each day of the transition.

---

## 12. References

- `NAMING_STANDARDS.md` §42 — the ADR file-name pattern and the single-sequence rule.
- `DOCUMENTATION_STANDARDS.md` — where documents live, front matter, superseding and archival.
- `DEVELOPMENT_WORKFLOW.md` §2.1 — state 2, where an ADR is required evidence.
- `TECHNOLOGY_STACK.md` §8 — adopting a technology, which requires an ADR.
- `DATABASE_STANDARDS.md` — everything ADR-014 decided and that is now proven in code.
- `ASSURANCE_STANDARDS.md` §3 — traceability, which an ADR's Verification section feeds.
