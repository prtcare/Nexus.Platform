# Definition of Done

**Status:** Active
**Owner:** DEVELOPER (Layer 07), enforced by ASSURANCE (Layer 09)
**Last updated:** 2026-08-21
**Layer:** 07 DEVELOPER, with ASSURANCE (09) and DELIVERY (08) cross-cutting
**Authoritative for:** the conditions under which a work item may be called done, the completion
profiles by risk tier, which conditions are mandatory in each profile, who may declare completion,
and what a declaration of done must leave behind.

Not authoritative for: what counts as proof — `ASSURANCE_STANDARDS.md`; the state model and its
transitions — `DEVELOPMENT_WORKFLOW.md` §2; what a reviewer checks —
`CODE_REVIEW_CHECKLIST.md`; branch and merge mechanics — `GIT_WORKFLOW.md`.

---

## 1. The position

**A work item is not done because the code exists.**

That sentence is the whole document. Everything below is the consequence.

The roadmap states it as a governing principle — *a feature is not complete because code was
written; it is complete when its requirements are verified and acceptance evidence exists* — and
ASSURANCE exists as a layer for the same reason: **a green build is not a satisfied requirement.**
DELIVERY can truthfully report that everything compiled and every test passed while the thing that
was asked for was never built.

The four failure modes this document exists to prevent, all of which have a green build:

| Failure | What it looks like |
|---|---|
| **Built the wrong thing** | Every test passes; the acceptance criterion was never read |
| **Built half the thing** | The happy path works; no error path exists |
| **Built it unsafely** | It works and it leaks a token into a log line |
| **Built it invisibly** | It works and nobody else can tell — no record, no evidence, no updated document |

---

## 2. The eleven conditions

Every condition below is defined once here. Which ones are **mandatory** depends on the profile
(§4); no condition is ever *forbidden*, and the strictest profile requires all eleven.

| # | Condition | Satisfied when | Owner |
|---|---|---|---|
| 1 | **Implementation complete** | Every task and subtask in the work item is done, and the diff stays inside the declared scope — projects, schemas, contracts | Worker |
| 2 | **Build green** | The change compiles and the architecture tests pass | DELIVERY |
| 3 | **Required tests pass** | Every test the work item's profile requires has run, with counts and outcome recorded | DELIVERY |
| 4 | **Acceptance criteria verified** | Each mandatory criterion has a verification run with a verdict | ASSURANCE |
| 5 | **Assurance evidence recorded** | Each Pass verdict points at stored evidence someone else can inspect | ASSURANCE |
| 6 | **Security requirements satisfied** | The security conditions in §6 hold for the change | Worker, checked at review |
| 7 | **Documentation updated** | Every document the change falsified is corrected in **this** change | Worker |
| 8 | **Review complete** | A recorded human decision by someone who is not the worker | DEVELOPER |
| 9 | **Integration complete** | Merged into the integration branch, and that branch is green **after** the merge | DEVELOPER |
| 10 | **No unresolved blocking defect** | No open Critical or Major defect against this work item | ASSURANCE |
| 11 | **Structured DEVELOPER state updated** | The work item, its run, its build record and its result reflect reality | DEVELOPER |

### 2.1 The four that are most often skipped

**Condition 5 — evidence.** *"The tests pass"* is not evidence; a test run record with counts and
outcome is. `ASSURANCE_STANDARDS.md` §10 lists what qualifies, and **a verdict of Pass with no
linked evidence is rejected at write time** — a database constraint, not a review convention.

**Condition 7 — documentation.** The specific decay mode of this system: most of these standards
are marked TARGET, so a completing milestone silently falsifies several at once, and a stale TARGET
reads as perfectly correct. If your work item made a TARGET real, changing the marker to CURRENT is
part of your work item — `DOCUMENTATION_STANDARDS.md` §11.

**Condition 9 — integration.** *Green before the merge* is not green. Integration into the milestone
branch is sequential and each merge is verified green **after** it lands (M-07-5.1). A red
integration build halts the batch (T-07-5.1.2.2).

**Condition 11 — structured state.** A work item done in reality and open in the graph makes every
downstream answer wrong: dependency resolution, parallel-safety classification, derived progress. It
is the condition with no visible symptom and the widest blast radius.

---

## 3. What "done" is not

| Not done | Why |
|---|---|
| "It builds on my machine" | Not evidence — `ASSURANCE_STANDARDS.md` §10 |
| "The tests pass" | Which tests, how many, on which commit? |
| "It works, I tried it" | An undated demonstration with no witness is not a demonstration |
| "I'll write the tests next" | Then the work item is not done; it is in state 9 |
| "The docs are a bit out of date now" | Condition 7 is not optional in any profile |
| A PR description listing what was verified | A PR description is not a record — `GIT_WORKFLOW.md` §8 |
| Merged to `main` | Merging is condition 9, not the definition |
| Deployed | Delivery is state 15. Done is not the same as delivered |

---

## 4. Profiles

**One definition of done for a boring machine, an ERP posting rule and a CSS change is either
absurdly heavy or dangerously light.** Profiles fix that by varying which conditions are mandatory —
not by varying the standard.

Two independent dimensions, and conflating them is the mistake this section prevents:

| Dimension | Decides | Owner |
|---|---|---|
| **Risk tier** | Which of the eleven **conditions** are mandatory | This document, §4.1 |
| **Product profile** | Which **verification methods** are mandatory | `ASSURANCE_STANDARDS.md` §7 |

A change can be low risk in a safety-critical product (a label on an internal dashboard) or
security-critical in an ordinary one (a tenant filter). The tier is a property of the **change**;
the profile is a property of the **product**.

### 4.1 Risk tiers

| Condition | Mechanical | Standard | Security-critical | Safety-critical |
|---|---|---|---|---|
| 1 Implementation complete | ● | ● | ● | ● |
| 2 Build green | ● | ● | ● | ● |
| 3 Required tests pass | architecture only | ● | ● | ● |
| 4 Acceptance criteria verified | ○ | ● | ● | ● |
| 5 Assurance evidence recorded | ○ | ● | ● | ● |
| 6 Security requirements satisfied | ● | ● | ●● | ●● |
| 7 Documentation updated | ● | ● | ● | ● |
| 8 Review complete | ● | ● | ●● two reviewers | ●● named authority |
| 9 Integration complete | ● | ● | ● | ● |
| 10 No blocking defect | ● | ● | ● | ●● no open defect of any severity |
| 11 DEVELOPER state updated | ● | ● | ● | ● |

● mandatory ●● mandatory and strengthened ○ not mandatory

**Mechanical / low-risk.** A change with no behavioural consequence: a rename with no public
surface, a comment, a formatting pass, a doc-only edit, a test added to existing behaviour. It still
builds, still passes architecture tests, still gets reviewed, still updates state. **What makes it
mechanical is that it changes nothing a criterion could describe** — which is why conditions 4 and 5
are not mandatory. If you find yourself writing an acceptance criterion for it, it is not mechanical.

**Standard.** The default. Every work item is Standard unless someone argues it down to Mechanical
or up. When in doubt, Standard.

**Security-critical.** Anything touching authentication, authorization, tenancy, secrets, encryption,
personal data, audit, tool permissions, agent permissions, or the deployment path. Strengthened:
condition 6 requires the full §6 checklist and evidence for each item; condition 8 requires two
reviewers, at least one of whom did not participate in the design.

`SECURITY_STANDARDS.md` §4.3 states the rule that shapes this tier — **the tenant isolation test is
written before the tenant filter.** In this tier, the proof precedes the implementation rather than
following it.

**Safety-critical.** **No safety-critical domain exists in Nexus today.** The tier is defined now
because retrofitting it is how safety systems fail. **TARGET — M-09-7.2 Safety-critical profile,
phase P4.** Its three rules are absolute and quoted from `ASSURANCE_STANDARDS.md` §7.1: a
safety-critical criterion cannot be waived by the ordinary deviation path, only by a named human
with recorded authority; it is enumerable per product with its verification state always current;
and it **may not be created, modified or waived by any agent.**

### 4.2 Product profiles

The product profile adds mandatory **methods** to condition 3 and condition 4. Owned by
`ASSURANCE_STANDARDS.md` §7 and not restated: Software, AI, ERP, Machine, Consumer.

Two consequences belong here:

- **Selecting a profile is a declaration, not a code change** (M-09-7.1 acceptance criterion). A
  profile is data. There is no `if (Product == X)` anywhere, in ASSURANCE or elsewhere.
- **A profile can only add.** An AI work item is Software *plus* evaluation. A profile that removed
  a method would be a deviation, and deviations are records with an approver and an expiry —
  `ASSURANCE_STANDARDS.md` §11.3.

### 4.3 Choosing a tier

Assigned when the work item is scoped (state 4), by the person scoping it, and recorded on the work
item. Escalate the tier — never quietly reduce it — if any of these become true mid-work:

- the diff reaches outside its declared scope;
- it touches identity, tenancy, secrets, audit or permissions;
- it changes a published contract in a `*.Contracts` project;
- it adds or edits an EF migration;
- it introduces a new external dependency.

**Reducing a tier is a decision with a name attached**, recorded on the work item. Reducing it
because the work is nearly finished and the tier is inconvenient is the failure this rule exists to
catch.

---

## 5. Condition 7 in detail: documentation

Done includes leaving the documentation true. Specifically:

| If the change… | Then… |
|---|---|
| Made a **TARGET** real | Change the marker to CURRENT and remove the milestone reference |
| Changed a convention | Update the owning standard and its `Last updated` |
| Added or removed a technology | `TECHNOLOGY_STACK.md`, plus an ADR — `ADR_STANDARD.md` §2 |
| Made an architectural decision | An ADR at the next number, currently ADR-016 |
| Added a persisted entity | The checklist in `DATABASE_STANDARDS.md` §12 |
| Added an endpoint | `API_STANDARDS.md` conformance; OpenAPI is generated |
| Departed from a standard deliberately | A **Deviation** record with reason, approver and expiry — not a code comment |
| Completed a milestone | Any document that cited it as a target |

**Documentation is updated in the same change, not in a follow-up work item.** A follow-up
documentation item is a work item that will be deprioritised on the day it is created and will be
wrong before it is picked up.

---

## 6. Condition 6 in detail: security

`SECURITY_STANDARDS.md` is authoritative. Condition 6 is satisfied when every applicable line holds:

| Check | Applies to |
|---|---|
| No secret, key, token or connection string in source, config or history | Every change |
| No log line, metric dimension or telemetry event contains a secret, a token or a full prompt body | Every change |
| No personal data logged beyond an identifier | Every change |
| Every query is tenant-filtered, and a test proves cross-tenant access fails | Anything reading tenant data |
| A cross-tenant resource returns `404`, never `403` | Any endpoint |
| No new dependency without a stack decision | Every change |
| A tool's `SideEffectClass` is declared and honoured | Anything touching `IToolGateway` |
| An agent's permissions are bounded and unchanged by the change itself | Anything an agent executes |
| No raw SQL built by string concatenation | Anything touching persistence |

**CURRENT and unavoidable:** there is no authentication and no authorization in Nexus today, and the
only access control that ever existed — Dataverse row-level security — leaves with **M-02-1.4 Delete
Dataverse**. Between that milestone and M-01-1.2 / M-01-2.1 / M-01-3.1 the system has **no access
control of any kind**. In that window, condition 6 includes one further check: **nothing carrying
real data is exposed to a real user.**

---

## 7. Who declares done

| Condition | Declared by | Never declared by |
|---|---|---|
| 1, 6, 7 | The worker | — |
| 2, 3 | DELIVERY, from a pipeline record | A human reading build output |
| 4, 5, 10 | ASSURANCE, from verification runs | The worker |
| 8 | A reviewer who is not the worker | The worker |
| 9, 11 | DEVELOPER | — |

**No self-approval.** The reviewer in state 13 is never the worker from state 9 —
`DEVELOPMENT_WORKFLOW.md` §2.2. This applies to agent workers without exception: an agent may not
approve its own work, another agent's work in the same run, or its own acceptance criteria. An agent
declaring its own work done is the most likely way this definition gets hollowed out, because the
declaration will be confident, complete and unverifiable.

---

## 8. Enforcement — how this stops being aspirational

A definition of done that lives only in a document is a definition of done that is met when everyone
is calm and skipped when they are not.

**M-09-1.3 Quality gate V1** is what makes it real. Its outcome, quoted: *an integration cannot
complete while a mandatory criterion is unverified. **This is what makes Definition of Done
enforceable rather than aspirational.*** Its acceptance criteria:

- A `QualityGate` declares which criteria must pass before a work item may integrate.
- **DEVELOPER's `IntegrationRun` blocks when the gate is not satisfied, and names the failing
  criterion.**
- A gate can be waived only with a recorded `Deviation` carrying a reason and an approver.
- The Foundation Gate acceptance test itself runs through this gate.

The gate returns *satisfied, or the list of unmet criteria — never a bare false* (S-09-1.3.1.2.1). A
gate that says only "blocked" trains people to route around it.

### 8.1 The dependency chain

```
M-09-1.1 criteria + methods ──▶ M-09-1.2 evidence + verdict ──▶ M-09-1.3 quality gate
                                          ▲                              │
M-08-1.2 pipelines ──▶ M-08-1.3 machine-readable results ──▶ M-07-4.1 build/test records
                                                                         │
                                                            M-07-5.1 review + integration
```

Every condition in §2 has a mechanism, and every mechanism is a milestone:

| Condition | Mechanism | Milestone | State today |
|---|---|---|---|
| 1 Implementation | Work graph and scope declaration | M-07-1.1 | **Absent** |
| 2 Build green | CI pipeline | M-08-1.2 | **Absent — `.github/workflows/` is empty** |
| 3 Tests pass | Machine-readable results → build records | M-08-1.3, M-07-4.1 | **Absent** |
| 4 Criteria verified | Acceptance criterion and method | M-09-1.1 | **Absent** |
| 5 Evidence | Evidence and verdict | M-09-1.2 | **Absent** |
| 6 Security | Identity, tenancy, authorization | M-01-1.2, M-01-2.1, M-01-3.1 | **Absent** |
| 7 Documentation | Document store | M-02-2.1 | Manual |
| 8 Review | Review model | M-07-5.1 | Manual |
| 9 Integration | Integration runner | M-07-5.1 | Manual |
| 10 Defects | Defect lifecycle | M-09-2.1 | **Absent** |
| 11 DEVELOPER state | Work graph | M-07-1.1 | **Absent** |

### 8.2 Waivers

A condition is waived only by a **Deviation** record carrying the criterion, why it is not met, who
accepted it, under what authority, and **an expiry**. A deviation with no expiry is a silent change
of standard. An expired deviation **re-blocks the gate rather than lapsing silently**
(S-09-1.3.2.1.1).

A safety-critical criterion cannot be waived by this path at all — §4.1.

---

## 9. Doing this today

**CURRENT: none of the enforcement exists.** States 1–9 are performed by people, unrecorded; states
10–16 do not exist as mechanisms. There is no CI, no assurance schema, no gate.

Until they exist, done is a **discipline**, and it is performed in this order:

1. Write the acceptance criterion **before** the code, in language that could fail. Put it in the
   work item or the commit message; a criterion invented afterwards describes what was built.
2. Finish the tasks; keep the diff inside the declared scope.
3. Build locally. Run the tests that exist — the two architecture test projects that apply, and the
   two behaviour tests. **Exactly two behaviour tests exist in the entire system**, so "tests pass"
   currently carries almost no information, and this step is worth no confidence at all.
4. Read `CODE_REVIEW_CHECKLIST.md` §1 against your own change before asking anyone else to.
5. Update every document the change falsified.
6. **Push.** Stage boundaries, not milestones — `GIT_WORKFLOW.md` §2.5. Unpushed proven work is
   invisible and, as 2026-08-20 demonstrated, losable.
7. Ask for review from someone who is not you.
8. Write down, in the commit or the work item, **what you verified and how** — so that when
   M-09-1.2 arrives, that sentence can become an evidence record instead of a memory.

Step 8 is the one that costs nothing now and is worth the most later. Every work item completed
before the gate exists is a work item whose evidence must be reconstructed or written off.

---

## 10. The honest summary

**Nexus cannot currently enforce any part of this document.** There is no CI, so condition 2 is an
assertion. There are two behaviour tests, so condition 3 is nearly vacuous. There is no assurance
schema, so conditions 4, 5 and 10 have nowhere to be recorded. There is no work graph, so condition
11 has nothing to update.

What is achievable today is conditions 1, 6, 7 and 8 by discipline, and writing down the criterion
and the verification in a form that survives until there is somewhere to put it.

**Nexus has never released. Nothing is qualified.** Saying so plainly is more useful than a
definition of done that everyone agrees with and nobody can check.

---

## 11. References

- `ASSURANCE_STANDARDS.md` — criteria, methods, evidence, verdicts, profiles, defects, gates.
- `DEVELOPMENT_WORKFLOW.md` §2 — the sixteen states and their entry conditions.
- `CODE_REVIEW_CHECKLIST.md` — condition 8, in checkable form.
- `SECURITY_STANDARDS.md` — condition 6, in full.
- `DOCUMENTATION_STANDARDS.md` §11 — condition 7, and when a document must be updated.
- `GIT_WORKFLOW.md` §§8, 9, 10 — pull requests, reviews and merge strategy.
- `OBSERVABILITY_STANDARDS.md` §12 — build and deployment telemetry behind conditions 2 and 3.
