# Assurance Architecture

**Status:** TARGET — **nothing in this layer exists.** No `Nexus.Assurance.*` project, no
`assurance` schema, no acceptance criterion, no evidence record. Each gap names the milestone that
closes it
**Owner:** Durai
**Last updated:** 2026-08-21
**Layer:** 08 ASSURANCE (v2.2, was 09 ASSURANCE under v2.1 — see `LAYER_MODEL.md` §2.2) —
repository `Nexus.Platform`, schema `assurance`, cross-cutting
**Authoritative for:** the shape and boundaries of the ASSURANCE layer — the traceability chain as a
chain of rows, the entity model, where acceptance criteria attach, the separation of verification
from validation, the inspection model for physical work, evaluation as a verification method,
evidence, findings and their escalation, quality gates, release qualification, assurance profiles by
product type, and the safety carve-out.

**Not authoritative for:** test types, what a unit or integration or contract test must assert,
regression practice, test project structure and naming, or how to write a test —
`ASSURANCE_STANDARDS.md`. What "done" requires by risk tier — `DEFINITION_OF_DONE.md`. Executing
tests in a pipeline — `DELIVERY_ARCHITECTURE.md`. What needs testing and which work item it belongs
to — `DEVELOPER_ARCHITECTURE.md`. Machine safety rules and their rationale —
`MACHINE_DEVELOPMENT_GUIDE.md`.

---

## 1. Purpose

ASSURANCE exists to close one gap: **a green build is not a satisfied requirement.**

DELIVERY can truthfully report that everything compiled and every assertion held while the thing that
was asked for was never built. Nothing else in the architecture closes that gap, so without this
layer "done" means "someone believed it was done", and that belief is recorded nowhere, expires when
the session ends, and cannot be re-examined when the thing fails in production.

ASSURANCE makes *done* an evidenced claim rather than an opinion. It answers a different question
from every neighbouring layer:

| Layer | Question |
|---|---|
| 07 DEVELOPER | What must be proven, and which work item is it attached to |
| 08 DELIVERY | Did it build, and did the tests execute |
| **09 ASSURANCE** | **Was the requirement actually satisfied** |
| 10 OPERATIONS | Does the running system stay healthy |

It is deliberately broader than software testing. Nexus will build machines, manufacturing systems
and business processes, where inspection against a measured characteristic and validation against
intended use matter more than unit tests, and where the vocabulary of "test coverage" does not apply
at all. A layer designed only for software would have to be rebuilt the first time a physical
deliverable arrived.

---

## 2. The traceability chain — every link is a row

This is the layer's architecture in one line:

```
Requirement → AcceptanceCriterion → Verification/Validation Method
           → Test / Inspection / Evaluation → Evidence → Pass/Fail
           → Release Qualification
```

Seven links, and the design decision that matters is that **every one of them is a row, not a
convention.** A chain held together by convention breaks silently: a requirement acquires no
criterion, a criterion acquires no method, a pass verdict cites nothing, and none of it is visible
because absence leaves no record.

When every link is a row, absence becomes queryable:

| Missing link | Reported as |
|---|---|
| A work item with no `AcceptanceCriterion` | A traceability gap, per milestone (`M-09-1.1`) |
| A criterion with no `VerificationMethod` | A traceability gap |
| A `Pass` verdict with no `Evidence` | **Rejected at write time** (`M-09-1.2`) |
| A requirement with no satisfying work item | Uncovered — DEVELOPER's side, `M-07-7.1` |
| A pipeline covering no declared criterion | Recorded, but proves nothing |

The fourth row is the one that keeps the chain honest in the direction people forget. A `Pass` with
no evidence is not a weak record — it is the exact thing this layer exists to eliminate, so it is not
storable. The constraint is at write time, not at report time, because a report that lists invalid
rows is a report someone learns to ignore.

The first link crosses a layer boundary, deliberately.

---

## 3. The boundary that is most often got wrong

> **DEVELOPER owns `Requirement`. ASSURANCE owns its `AcceptanceCriterion`.**

A requirement is what someone wants. A criterion is how anyone would know it was delivered. They live
in different layers on purpose, because **the layer that judges must own the criterion** — otherwise
the layer being judged writes its own test, and the answer to "was this satisfied" comes from the
party with an interest in the answer being yes.

The link between them is polymorphic and crosses schemas: an `AcceptanceCriterion` references a
DEVELOPER node by layer, type and id, with **no cross-schema foreign key**. Cross-schema FKs would
couple the two layers' migrations, and rule 3 of parallel safety — two migrations on one DbContext
conflict on the model snapshot — would then make every ASSURANCE change and every DEVELOPER change
mutually exclusive. See `DEVELOPER_ARCHITECTURE.md` §7 and `DATABASE_ARCHITECTURE.md` §5.

The rest of the boundaries:

| Not owned | Owner |
|---|---|
| What needs testing, and which work item it belongs to | 07 DEVELOPER |
| Executing build and test pipelines | 08 DELIVERY |
| Runtime health of a deployed system | 10 OPERATIONS |
| The formal test report **document** | 02 DATA — ASSURANCE owns the *result*, DATA owns the document |

---

## 4. The entity model

Twenty-one entities in five groups. `DATA_OWNERSHIP.md` §4 holds the canonical list.

### 4.1 Specification — what will be proven, and how

| Entity | Is | Milestone |
|---|---|---|
| `AcceptanceCriterion` | A checkable statement, linked to exactly one DEVELOPER `Requirement` or `WorkItem`. `Ref` = `ACC-` | `M-09-1.1` |
| `VerificationMethod` | One of **Test, Inspection, Analysis, Demonstration, Evaluation**. Carries the profile it belongs to | `M-09-1.1` |
| `ValidationMethod` | How intended use is confirmed, as distinct from specification conformance | `M-09-3.2` |
| `TestCase` | A reusable specification of one test, executed many times | `M-09-3.1` |
| `InspectionCharacteristic` | A measurable property with nominal, tolerance, unit and measurement method | `M-09-4.1` |

Criterion text must be **checkable**. Empty or unfalsifiable phrasing is rejected at write time —
"the system should be fast" is not a criterion, it is a wish, and storing it produces a chain that
looks complete and proves nothing.

### 4.2 Plans — declared before building, not assembled after failing

| Entity | Is | Milestone |
|---|---|---|
| `QualityPlan` | What proving this product will require, overall | `M-09-3.2` |
| `VerificationPlan` | The verification obligations for a scope | `M-09-3.2` |
| `ValidationPlan` | The validation obligations for a scope | `M-09-3.2` |
| `TestPlan` | A planned set of test cases with its own coverage report against criteria | `M-09-3.1` |
| `InspectionPlan` | The characteristics to be measured on a physical item | `M-09-4.1` |

Plans are all P3, and that ordering is intentional. Per-work-item criteria (`M-09-1.1`) come first
because they are what the gate needs; plans come later because planning assurance for a product you
have not started is guesswork. The progression is criteria → plans, not plans → criteria.

### 4.3 Execution and evidence

| Entity | Is | Milestone |
|---|---|---|
| `VerificationRun` | Method, criterion, actor, timestamp, verdict | `M-09-1.2` |
| `ValidationRun` | The same for validation | `M-09-3.2` |
| `InspectionRun` | The same for inspection, with measurements | `M-09-4.1` |
| `Evidence` | An immutable reference to what proved it. `EvidenceKind` = `PipelineRun`, `Document`, `Measurement`, `Screenshot`, `Attestation` | `M-09-1.2` |

`Evidence` is **immutable once written** and holds an *opaque reference* plus a kind. It does not
copy the artifact it points at. Copying would create a second source of truth that drifts from the
first; an opaque reference means a `PipelineRun` stays DELIVERY's, a document stays DATA's, and
ASSURANCE owns only the claim that this thing proved that criterion.

### 4.4 Findings

| Entity | Is | Milestone |
|---|---|---|
| `Defect` | A specific failure, carrying the evidence that revealed it | `M-09-2.1` |
| `Deviation` | An approved, time-bounded exception to a gate. Reason, approver, expiry | `M-09-1.3` |
| `NonConformance` | Repeated defects sharing a root cause — a systemic problem | `M-09-2.2` |
| `CorrectiveAction` | An owner, a due date, and a verification of its own | `M-09-2.2` |

### 4.5 Verdict

| Entity | Is | Milestone |
|---|---|---|
| `QualityGate` | The set of criteria that must pass before a transition may complete | `M-09-1.3` |
| `QualificationResult` | A derived verdict for a scope — a work item, a release | `M-09-1.2`, `M-09-5.1` |
| `TraceabilityLink` | The stored edge across Requirement, Criterion, Method, Evidence | `M-09-1.1` |
| `AssuranceProfile` | Which methods are mandatory for a product type | `M-09-7.1` |

---

## 5. Verification and validation are recorded separately

> **Verification asks: was it built right? Validation asks: was it the right thing?**

They are separate entities, separate runs and separate plans, and conflating them is the most
expensive mistake available in this layer — because a system can pass every verification it has and
still be the wrong system, and nothing in a verification record can surface that.

| | Verification | Validation |
|---|---|---|
| Asks | Does it conform to the specification | Does it satisfy the actual need |
| Against | The stated requirement | Intended use, in context |
| Typical method | Test, Analysis, Inspection | Demonstration, user acceptance, process trial |
| Fails when | The build does not match what was written down | What was written down did not describe the need |
| Layer state | `VerificationMethod`, `VerificationRun` — `M-09-1.1`, `M-09-1.2` | `ValidationMethod`, `ValidationRun` — `M-09-3.2` |

The asymmetry in the roadmap is deliberate and honest: verification is in GATE A, validation is P3.
GATE A needs to stop unproven code from integrating; it does not need to answer whether the product
was the right idea, because at GATE A the product is Nexus itself and the answer is being discovered
by building it. Validation becomes mandatory when the first ERP module reaches real users, and
`M-09-3.2` sits alongside `M-12-1.1` for exactly that reason.

### The five methods

`Test`, `Inspection`, `Analysis`, `Demonstration`, `Evaluation`. The set is closed at `M-09-1.1` and
covers every kind of deliverable Nexus will produce. Their detailed application is
`ASSURANCE_STANDARDS.md` §4; what matters architecturally is that a method is a **row with a
profile**, so a product type can make one mandatory without a code change.

---

## 6. Inspection — proving physical things

`M-09-4.1`, P3. Software is proven by executing tests. A machined part is proven by **measuring it**,
and no amount of test vocabulary describes that.

An `InspectionCharacteristic` carries four fields and each is load-bearing:

| Field | Why |
|---|---|
| **Nominal** | The target value |
| **Tolerance** | The band within which the item conforms |
| **Unit** | Explicit on every characteristic. **A unitless tolerance is rejected** |
| **Measurement method** | How it was measured determines what the number means |

A measurement outside tolerance produces a `Fail` **and a `Defect` automatically** — not a flag for
someone to review. The automation is the point: an out-of-tolerance measurement that a human decides
not to escalate is exactly the failure mode that inspection regimes exist to prevent.

The unit rule deserves its bluntness. A tolerance of "0.05" is not a tolerance. Millimetres and
inches differ by a factor of twenty-five, and the failure is silent until something does not fit.

At P4, `M-09-4.2` adds `Instrument` and `CalibrationRecord`: a measurement records what instrument
produced it and whether that instrument was in calibration, and a measurement taken with an
out-of-calibration instrument is flagged rather than silently accepted. That is the point at which a
measurement chain becomes defensible to an external auditor.

`MACHINE_DEVELOPMENT_GUIDE.md` owns the machine domain itself and its safety rules.

---

## 7. Evaluation — proving AI

`M-09-6.1`, P3. AI output cannot be verified by assertion. There is no equality check for "was that a
good answer", and a test suite that asserts on exact model output is a test suite that fails on the
next model version for no defect.

Evaluation is therefore **a verification method like any other**, not a parallel quality regime. An
AI evaluation run writes `Evidence` against an `AcceptanceCriterion`, carrying its score and the
question set it was scored on. Three consequences:

1. A criterion can require a **minimum score**, and a drop below it fails the gate — the same gate
   that blocks a work item for a failing unit test.
2. **Citation correctness is expressible as a criterion.** Whether an answer's citations resolve to
   the context that produced it is checkable, and it is the single most useful AI criterion available
   because it is objective.
3. AI quality claims are qualified through the same chain as everything else, so "the assistant got
   better" becomes a verdict with evidence rather than an impression.

The harness that produces the scores is AI's (`M-04-5.1`). ASSURANCE consumes its output. See
`AI_ARCHITECTURE.md`.

---

## 8. Findings and how they escalate

Three distinct records for three distinct situations, and the distinction is what makes the data
useful:

| Record | Situation | Escalates to |
|---|---|---|
| `Defect` | This specific thing failed | A DEVELOPER `WorkItem`, without retyping its context |
| `Deviation` | This gate is knowingly not satisfied, and we proceed anyway | Expiry — an expired deviation **re-blocks the gate** |
| `NonConformance` | This keeps happening, and the cause is systemic | A `CorrectiveAction` with an owner, a due date and its own verification |

Two rules hold the model together.

**A `Defect` cannot close while its criterion still fails.** Closing a defect is not a decision; it is
a consequence of the criterion passing. Allowing manual closure reintroduces exactly the opinion this
layer removed.

**An expired `Deviation` re-blocks rather than lapsing silently.** A waiver that quietly becomes
permanent is worse than no waiver, because the original decision was made with a timeframe in mind
and nobody is told when that timeframe passes.

`CorrectiveAction` having *its own verification* is what separates it from a task. A corrective action
that is marked done without evidence that the systemic cause is gone is a `NonConformance` that has
been closed rather than fixed.

---

## 9. Quality gates — `M-09-1.3`

A `QualityGate` declares which criteria must pass before a transition may complete. The transition
that matters at GATE A is integration.

> **DEVELOPER's `IntegrationRun` blocks when the gate is not satisfied, and names the failing
> criterion.**

Three design properties:

| Property | Reason |
|---|---|
| The gate **evaluation API returns the list of unmet criteria**, never a bare false | "Blocked" that does not say why is an obstacle, not a control |
| A gate can be waived **only** with a recorded `Deviation` | An undocumented override is indistinguishable from the gate not working |
| **The GATE A acceptance test itself runs through this gate** | A gate that has never blocked anything is untested |

This is the milestone that makes `DEFINITION_OF_DONE.md` enforceable rather than aspirational. Until
`M-09-1.3`, the definition of done is a document people are asked to follow; after it, a work item
that does not meet it cannot integrate. The distinction between a standard and a control is exactly
whether something mechanical stops you.

---

## 10. Release qualification — `M-09-5.1`

A release carries a qualification verdict **derived from the criteria in its scope, never entered by
hand**. Three properties:

- DELIVERY's promotion to Production is **blocked by an unqualified release**.
- The qualification **names every unmet criterion**, so the decision to proceed anyway is an informed
  one.
- It is derived, so it cannot be stale — a criterion that regresses after qualification changes the
  verdict.

`M-09-5.2` adds regression qualification at P3: a criterion transitioning `Pass` → `Fail` is reported
**as a regression, distinctly from a new failure**, along with the run that last passed. The
distinction matters because the two demand different responses — a new failure means something was
never proven, a regression means something that worked stopped working, and the second one has a
known-good point to bisect against.

`M-09-5.3` at P4 assembles evidence, verdicts and traceability into an external-facing certification
pack for a declared scope. That is only possible because §2 made every link a row; a chain held by
convention cannot be exported.

---

## 11. Assurance profiles — `M-09-7.1`

A game, an ERP module and a boring machine do not share a definition of *adequately verified*. A
product selects an `AssuranceProfile` and inherits its mandatory methods.

> **A profile decides which methods are mandatory, not which are possible.**

Selecting a profile is a **declaration, not a code change**, and an architecture test forbids
branching on product identity inside ASSURANCE — the same `no if (Product == X)` rule that governs
the whole system.

### Architecture-level examples

**Software.** Mandatory: `Test` for behaviour, `Test` for architecture boundaries, `Analysis` for
contract compatibility. Criteria attach to work items. Evidence is overwhelmingly `PipelineRun`. A
release qualifies when its criteria pass and no regression is open. Validation is light because the
specification and the need are usually written by the same person.

**AI.** Mandatory: `Evaluation` against a fixed question set, plus a citation-correctness criterion.
Evidence is an evaluation run carrying a score. Criteria carry **minimum scores** rather than binary
outcomes, and a score drop fails the gate. Verification here proves the pipeline behaved; validation
asks whether the answers were actually useful, and only a human can supply that.

**ERP.** Mandatory: `Demonstration` — process validation and user acceptance. The distinctive feature
is that **validation dominates verification**: an ERP module whose every test passes and whose
process does not match how the business actually works is a failed module. Evidence is
`Attestation` and `Document` far more than `PipelineRun`, and criteria attach to a business process
rather than to a work item.

**Machine.** Mandatory: `Inspection` with characteristics, tolerances and units; `Validation` against
intended use; and the **safety-critical carve-out** in §12. Evidence is `Measurement`, tied to an
instrument and its calibration state. A machine is qualified when every characteristic is inside
tolerance, measured by an in-calibration instrument, and every safety criterion is verified — not
when a test suite is green, because there is no test suite.

---

## 12. The safety carve-out — `M-09-7.2`

Two absolutes. They are not defaults, not policy, and not subject to a profile.

> **1. A criterion marked safety-critical cannot be waived by the ordinary `Deviation` path.**
> It can be released only by a named human with recorded authority.

> **2. No agent may create, modify or waive a safety-critical criterion.**

The first exists because the deviation path is designed to be usable — reason, approver, expiry, and
proceed. That usability is correct for a schedule risk and catastrophic for a guard interlock. The
ordinary path must therefore *not reach* safety criteria at all; the exception is a different
mechanism with a different approver, not the same mechanism with a stricter warning.

The second is the sharper of the two. An agent that can create a safety criterion can create a weak
one; an agent that can modify one can widen a tolerance; an agent that can waive one can waive it
under time pressure it has no way to evaluate. The prohibition is absolute and mirrored in
`AI_DEVELOPMENT_STANDARDS.md` §16 and `MACHINE_DEVELOPMENT_GUIDE.md` §1, because it must be
unavoidable from whichever document someone arrives at.

Supporting properties: safety criteria are **enumerable per product**, and their verification state is
**always current** — a safety criterion whose state has to be reconstructed is a safety criterion
nobody is watching.

---

## 13. GATE A minimum — five things, and nothing else

| In GATE A | Milestone |
|---|---|
| `AcceptanceCriterion` | `M-09-1.1` |
| `VerificationMethod` | `M-09-1.1` |
| `Evidence` | `M-09-1.2` |
| Pass / Fail verdict | `M-09-1.2` |
| Basic quality gate | `M-09-1.3` |

Plus the two supporting behaviours those milestones carry: the traceability gap report per milestone,
and the rejection of a `Pass` with no evidence.

**Explicitly not in GATE A:** plans of any kind, `TestCase`, inspection, validation, defects,
non-conformance, corrective actions, release qualification, regression qualification, AI evaluation,
profiles, and the safety-critical profile. The full layer is a P3-and-beyond build.

The reason for the smallness is the gate's own logic. GATE A is *Development Ready* — the earliest
safe point at which business systems can begin. It needs exactly one assurance property: **a work
item cannot integrate while a mandatory acceptance criterion is unverified.** Five entities deliver
that. Everything else is capability that makes each additional product cheaper, which is GATE B and
P3 work, and it must never pause business development.

### Current reality

| Thing | State |
|---|---|
| `Nexus.Assurance.*` projects | **Do not exist** |
| `assurance` schema | **Does not exist** |
| Acceptance criteria anywhere | **None** |
| Evidence records | **None** |
| Tests in the whole system | Five files; **two of them are behaviour tests** |
| Anything that blocks a merge | **Nothing.** No branch protection exists yet — `M-08-1.4` |

Today, "done" means someone believed it was done. That is the honest statement of current state and
it should be read as the motivation for the layer rather than as a criticism of it.

---

## 14. Boundaries with the sibling layers

| Layer | The seam |
|---|---|
| 07 DEVELOPER | Owns `Requirement`; ASSURANCE owns `AcceptanceCriterion`. ASSURANCE blocks `IntegrationRun` |
| 08 DELIVERY | A `PipelineRun` becomes `Evidence`. ASSURANCE blocks promotion to Production |
| 10 OPERATIONS | ASSURANCE proved the requirement was satisfied; OPERATIONS proves it stays healthy in production |
| 04 AI | AI runs the evaluation harness; ASSURANCE records its score as `Evidence` against a criterion |
| 02 DATA | ASSURANCE owns the verdict; DATA owns the test report document |
| 03 GOVERNANCE | Compliance obligations (`M-03-4.1`) select which criteria are mandatory; certification packs reference them |

---

## 15. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Where the AI evaluation question set lives | `M-04-5.1`; DATA is the likely home | Not yet decided |
| Whether `Evidence` may hold an uploaded artefact directly or only a reference | Volume and retention, at `M-09-1.2` | Not yet decided |
| Who holds "recorded authority" for a safety-critical release | `M-09-7.2`; it is a role in CORE, not a string | **Not yet decided** — must be settled before any machine work |
| Default deviation expiry | `M-09-1.3` | Not yet decided |
| Whether ERP process validation reuses `Demonstration` or needs its own method | First ERP module, `M-09-3.2` | Not yet decided |

---

## 16. References

- `ASSURANCE_STANDARDS.md` — test types, regression, evidence rules, test project structure and
  naming, AI evaluation practice, machine inspection practice, assurance profiles in detail.
- `DEFINITION_OF_DONE.md` — the completion conditions this layer makes enforceable.
- `DEVELOPER_ARCHITECTURE.md` — §11 controlled integration, §13 human governance, the `Requirement`
  side of the criterion boundary.
- `DELIVERY_ARCHITECTURE.md` — the pipeline results that become evidence, and promotion.
- `OPERATIONS_ARCHITECTURE.md` — the boundary after deployment.
- `AI_ARCHITECTURE.md` — the evaluation harness and citation model.
- `MACHINE_DEVELOPMENT_GUIDE.md` — the machine domain, its safety boundary, and measurement practice.
- `DATA_OWNERSHIP.md` — §4 the entity list, and the requirement/criterion split.
- `DEVELOPMENT_WORKFLOW.md` — §3, the four-layer test ownership split.
