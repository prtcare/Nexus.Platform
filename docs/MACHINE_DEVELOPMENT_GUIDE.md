# Machine Development Guide

**Status:** TARGET — **no machine domain exists in Nexus.** Nothing described here is implemented.
This document is written now because retrofitting measurement traceability and safety separation
into a model designed only for software is expensive, and because the safety rules must be written
down before the first physical system arrives, not negotiated when it does
**Owner:** PRODUCTS (Layer 12) for the machine itself; ASSURANCE (09) for inspection, verification
and validation; GOVERNANCE (03) for registration and obligations
**Last updated:** 2026-08-21
**Layer:** 12 PRODUCTS, with 09 ASSURANCE cross-cutting
**Authoritative for:** how a machine product is structured in Nexus — project structure,
requirements, the mechanical/electrical/software separation, bill of materials, I/O documentation,
control logic, measurements, inspection, verification, validation, safety requirements, test
evidence, software versioning, machine release and configuration — **and the boundary between what
AI may do and what only a deterministic controller and a human may do.**

Not authoritative for: how a product is registered and composed generally —
`PRODUCT_DEVELOPMENT_GUIDE.md`; what counts as evidence and how inspection differs from testing —
`ASSURANCE_STANDARDS.md` §8; what technologies are approved — `TECHNOLOGY_STACK.md`.

---

## 1. The safety boundary — read this before anything else

> **Deterministic controllers own real-time motion, interlocks and emergency stop.**
> A PLC, or a real-time motion controller, holds every behaviour on which physical safety depends.
> Nexus is not in that loop, and no Nexus component — service, agent, model or human-facing UI — is
> ever placed in it.

> **AI may plan, diagnose, document and propose parameters.**
> That is the whole of its authority. It reasons over data, drafts documents, explains faults,
> suggests values and prepares work. It produces proposals.

> **AI must NEVER bypass a hard limit, an emergency stop, an operator approval or validated control
> logic.**
> Not under any instruction, any configuration, any urgency, any efficiency argument, and not
> "temporarily for a test". There is no flag, no mode and no permission that enables it. A system
> that can be talked out of a hard limit does not have one.

> **No agent may create, modify or waive a safety-critical acceptance criterion.**
> This is absolute and has no exception path. It restates `ASSURANCE_STANDARDS.md` §7.1 —
> **M-09-7.2** — where a safety-critical criterion cannot be waived by the ordinary deviation path,
> only by a **named human with recorded authority**.

Everything else in this document is structure. These four rules are the document.

### 1.1 The division of authority, stated as a table

| Concern | Deterministic controller | Nexus / AI | Human |
|---|---|---|---|
| Real-time motion and trajectory | **Owns** | Never | Commands via the controller |
| Interlocks | **Owns** | Never | Designs, validates |
| Emergency stop | **Owns** (hardwired, independent of software where required) | Never | Actuates |
| Hard limits — travel, force, speed, temperature, pressure | **Owns and enforces** | May *read*; may *propose a value inside the limit* | Sets, validated |
| Guarding and access control | **Owns** | Never | Designs, validates |
| Parameter values inside validated ranges | Applies | **May propose** | **Approves** |
| Fault diagnosis | Reports | **May analyse and explain** | Decides |
| Maintenance planning and scheduling | — | **May plan and propose** | Approves |
| Documentation, procedures, work instructions | — | **May draft** | Reviews, approves, owns |
| Measurement analysis and trend detection | — | **May analyse** | Judges conformance |
| Acceptance criteria — safety-critical | — | **Never creates, modifies or waives** | Named authority only |
| Acceptance criteria — non-safety | — | May draft for review | Approves |

The pattern: **AI proposes, a deterministic system enforces, a human decides.** Where those three
collapse into one actor, the safety argument collapses with them.

### 1.2 Why this is architectural and not a policy note

Nexus already has the mechanism that makes this enforceable, and it is not machine-specific:
an AI turn produces a `ProposedAction`, and a `ProposedAction` is executed by something else under
policy. `SideEffectClass` in `Nexus.Platform.Contracts/Tools/` already distinguishes read-only,
reversible write, irreversible write and external effect, and already requires explicit human
approval for the last two — `SECURITY_STANDARDS.md` §10.

**A machine command is an irreversible external effect in the most literal sense available.** The
existing classification is sufficient to describe it, which means the machine domain does not need a
special-case safety mechanism; it needs the general one to be actually implemented. Today it is not:
`EmptyToolCatalog` and `EmptyToolGateway` mean **no tool can be invoked at all**, and
`Nexus.Platform.Tools/ToolProvider.cs` is a 231-byte stub. **M-01-7.1 Tool registry and invocation**
is the milestone, and its permission model must land with it, not after.

---

## 2. What exists today — nothing

| Claim | Reality |
|---|---|
| A machine domain in Nexus | **None.** No project, no schema, no aggregate |
| A controller integration | None |
| Inspection or measurement records | None. `ASSURANCE_STANDARDS.md` §8 is TARGET |
| A safety-critical criterion mechanism | None. **M-09-7.2**, phase P5 |
| **LinuxCNC** | **NOT SELECTED.** Nothing related is present in any repository |
| Any real-time or embedded technology | None selected |
| Python, containers, any cloud compute | **NOT SELECTED** — `TECHNOLOGY_STACK.md` §7 |

**LinuxCNC is a candidate, not a decision.** It is named in the roadmap as one of two possible
deterministic controller families (PLC or LinuxCNC) for a boring-machine retrofit. It is a
**PRODUCTS-layer (12) choice**, decided under **M-12-1.1 Product template and integration
checklist**, and **it must not influence any layer 01–11 decision.** Do not add it to a stack
document, do not build an abstraction shaped around it, and do not describe Nexus as supporting it.

The roadmap places machine automation in **phase P5, behind an explicit gate.** That is deliberate:
a system with no authentication, no authorization, no CI, no deployment and two behaviour tests is
not a system that should be near a machine.

---

## 3. Machine project structure

A machine is a **product** (layer 12) and follows `PRODUCT_DEVELOPMENT_GUIDE.md` in full —
registration, Product Core, capability declaration, its own database, the five profiles. What
follows is what a machine product adds.

```
Nexus.Products.<Machine>/
  src/
    Nexus.Products.<Machine>.Domain/
      Machine/            Machine, MachineId, MachineStatus, IMachineRepository
      Requirement/        machine requirements, traced to criteria
      BillOfMaterials/    BOM items, revisions, sourcing references
      IoPoint/            every input and output, named and typed
      ControlProgram/     a reference to a controller program version — NOT the program
      Parameter/          named parameters with validated ranges
      Characteristic/     what is inspected, with nominal and tolerance
      Measurement/        measured values, instrument, calibration state
      Configuration/      the as-built configuration of one physical unit
      Release/            machine release: mechanical + electrical + software revisions
    Nexus.Products.<Machine>.Application/
    Nexus.Products.<Machine>.Infrastructure/
    Nexus.Products.<Machine>.Api/
    Nexus.Products.<Machine>.Client/          operator and engineering UI — never a control surface
  docs/
    requirements/  safety/  electrical/  mechanical/  io/  procedures/
```

Aggregate folders follow the standard shape — `<Name>.cs`, `<Name>Id.cs`, `<Name>Status.cs`,
`I<Name>Repository.cs` — and every persisted entity follows `DATABASE_STANDARDS.md` §12.

**The critical structural rule:**

> **Nexus stores the *record of* the control program. It does not store, generate, compile, deploy
> or execute the control program.**

`ControlProgram` is a reference: which program, which version, which hash, who validated it, when,
and against which criteria. The program itself lives in the controller's own toolchain and version
control, under that toolchain's rules. A Nexus record that could be mistaken for the executable
truth is worse than no record.

---

## 4. Requirements

1. Every machine requirement is a record, with an id, a statement, a source and an owner.
2. Every requirement is **classified**: functional, performance, interface, environmental,
   regulatory, or **safety**.
3. **Safety requirements are marked at creation** and inherit the safety-critical rules in §9.
4. Every requirement traces to at least one acceptance criterion, and every criterion to a
   verification method — `ASSURANCE_STANDARDS.md` §3 owns the chain.
5. A requirement with no verification method is not a requirement; it is an aspiration.
6. Requirements state **what and how well**, never how. "The spindle stops within X ms of an E-stop
   signal" is a requirement; "use relay K3" is a design decision.
7. Changing a requirement is a change event with a reason and an approver. **Changing a safety
   requirement additionally requires a named human authority** — §9.

**TARGET — M-07-7.1 Requirements** gives requirements a home; **M-09-1.1** gives criteria one.
Until both exist, requirements live in `docs/requirements/` under git, which is version control but
not traceability.

---

## 5. Mechanical, electrical and software separation

A machine is three disciplines that fail differently and are verified differently. Nexus keeps them
separate as records and links them at the release.

| Discipline | Artefacts | Verified by | Owns |
|---|---|---|---|
| **Mechanical** | Drawings, tolerances, materials, assemblies, fits | Inspection and measurement | Physical conformance |
| **Electrical** | Schematics, I/O list, panel layout, cabling, protection, **safety circuit** | Inspection, continuity and function test | Power, signal, and the safety circuit |
| **Software** | Controller program, HMI, Nexus-side application | Test, simulation, function test | Sequencing and logic |

Rules:

1. **Each discipline has its own revision.** They are not one version number. A cable change does
   not bump the control program revision, and pretending otherwise destroys the ability to say what
   changed.
2. **A machine release names all three revisions** — §12.
3. **The safety circuit is electrical, not software.** Where a hazard requires it, protection is
   hardwired and independent of the control program. Software may *observe* it; software does not
   *constitute* it.
4. **A change in one discipline triggers an impact assessment in the other two.** A mechanical
   change that alters travel changes the software's limits and possibly the safety circuit.
5. **No discipline is verified by another's evidence.** A passing software test says nothing about a
   tolerance.

---

## 6. Bill of materials

1. The BOM is a **versioned record**: item, quantity, specification, and a reference to the source
   or supplier — held as a reference, never as a secret or a price agreement.
2. Every **safety-relevant** item is marked. Emergency stop devices, safety relays, light curtains,
   interlock switches, rated fasteners.
3. A safety-relevant item may only be substituted with a documented equivalence assessment and a
   named human approval. **An agent may propose a substitution and must never approve one.**
4. Items with a service life, a calibration interval or an expiry carry that date.
5. The BOM links to the machine **configuration** (§11) — what is specified versus what is actually
   fitted to unit number three.
6. Licences and obligations attached to any purchased component are registered in GOVERNANCE —
   **M-03-5.1 Licence registry**.

---

## 7. I/O documentation

The I/O list is the interface between the three disciplines and is the single most useful artefact
for diagnosing a machine.

Every point carries: a stable name, address, direction, signal type, physical device, function,
**fail-safe state**, and whether it is safety-related.

| Rule | Statement |
|---|---|
| One authoritative I/O list | Not one per discipline. Three lists produce three machines |
| Names are stable | An I/O point's name outlives its address. Renaming is a change event |
| **Fail-safe state is documented per point** | What this signal means when the wire breaks. A point without one is undesigned |
| Safety-related points are marked | And are subject to §9 |
| Units are explicit in the name or the column | `DATABASE_STANDARDS.md` §11.4 — this is the domain where an ambiguous `Length` causes physical damage rather than a display bug |
| Nexus mirrors, never masters | The controller's I/O configuration is the master. A drift check compares them |

**AI's role here is drafting and cross-checking**: producing the document, finding points present in
the schematic and absent from the program, flagging points with no fail-safe state. It does not
assign addresses to safety points and it does not write the configuration.

---

## 8. Control logic

1. **Control logic lives in the controller.** Its authoring, review and version control follow the
   controller toolchain's rules, not Nexus's.
2. **Nexus records** which program version is validated, against which requirements, by whom, when,
   and with what evidence.
3. **Safety logic is separated from process logic**, and where a hazard requires it, safety
   functions are implemented in a rated safety device rather than in general control logic.
4. **Interlocks are enforced by the controller.** Not by an operator UI, not by a Nexus service, not
   by an agent, and not by a check in a web API.
5. **Every parameter the control program exposes has a validated range**, and the controller
   enforces the range. A parameter is not "safe because the UI limits it".
6. **A parameter change is proposal → approval → application → record.** AI may propose. A human
   approves. The controller applies within its enforced range. Nexus records all three.
7. **AI does not generate control logic for a safety function.** For non-safety logic, AI-drafted
   code is a draft that enters the same review and validation path as any other, and is never
   deployed on the strength of having been generated.
8. **Simulation is not validation.** It is useful evidence, and it is not sufficient — §10.

---

## 9. Safety requirements

1. A safety requirement is identified as such **at creation**, not classified later.
2. It carries the hazard it mitigates, the risk assessment that identified it, and its verification
   method.
3. Its acceptance criterion is **safety-critical**, and safety-critical criteria — per
   `ASSURANCE_STANDARDS.md` §7.1, **M-09-7.2**:
   - **cannot be waived by the ordinary deviation path** — only by a named human with recorded
     authority;
   - are **enumerable per product**, with verification state always current;
   - **may not be created, modified or waived by any agent.**
4. **Emergency stop and hard limits are enforced in hardware or in rated safety devices**,
   independent of the general control program, wherever the risk assessment requires it.
5. **No software path may disable, delay, mask or override a safety function.** Not a maintenance
   mode, not a commissioning flag, not a diagnostic tool, not an AI-proposed optimisation.
6. **No AI-proposed action may be applied to a machine without explicit human approval**, and the
   approval is recorded with who, when and what was approved.
7. A machine with an unverified safety-critical criterion **is not released** — §12.
8. Safety verification evidence has no expiry-by-assumption: if a change touches a safety function,
   its verification is re-executed, not inherited.

**The rule that catches the realistic failure.** The dangerous scenario is not an agent seizing a
machine; it is an agent producing a plausible, well-argued proposal that a tired human approves at
the end of a shift. Therefore: safety-critical approvals are made by a **named authority** rather
than whoever is present, the proposal's origin (agent or human) is recorded, and the approval
records what was approved rather than that approval occurred.

---

## 10. Measurement, inspection, verification and validation

`ASSURANCE_STANDARDS.md` §4 and §8 own these definitions. The machine-specific consequences:

**Inspection is not testing.** Its result is a *measured value judged against a tolerance*, not a
pass or fail; its repeatability is subject to instrument accuracy and operator variation; and its
traceability is to a **calibrated instrument with a calibration date**, not to a commit.

An inspection record carries: the characteristic, the nominal value, the tolerance, the measured
value, the instrument, and the instrument's calibration state.

> **A measurement from an instrument whose calibration has lapsed is not evidence.** It is a number.

| Milestone | Brings |
|---|---|
| **M-09-4.1** Inspection plans and characteristics | What is inspected and against what |
| **M-09-4.2** Measurement evidence and instrument traceability | The measured value, the instrument, the calibration chain |

Both are phase P3, ahead of the P5 machine gate — deliberately, so the assurance model exists before
a physical system needs it.

**Verification asks: was it built right?** Against the specification, by test, inspection,
demonstration, analysis or simulation.

**Validation asks: is it the right machine?** Does it do the job, in its actual environment, with
its actual operators, on its actual material. **Validation cannot be performed in simulation and
cannot be performed by an agent.** It requires the physical machine, the real process and a
qualified human judgement.

**Every unit is named.** `DATABASE_STANDARDS.md` §11.4: the column names its unit or it is wrong.

**AI's role**: analysing measurement sets, detecting trends and drift, flagging out-of-tolerance
results, drafting inspection plans for human approval, and explaining why a characteristic is
failing. **AI does not judge conformance and does not sign off.**

---

## 11. Test evidence and machine configuration

**Evidence** — `ASSURANCE_STANDARDS.md` §10. Machine-specific requirements:

1. Every verification produces evidence that names the **physical unit**, not just the design.
2. Evidence records the machine's **configuration at the time of the test** — which mechanical,
   electrical and software revisions were fitted.
3. Evidence records the **instrument** used, where a measurement was taken.
4. Evidence is immutable. A re-test produces new evidence; it does not amend old evidence.
5. Photographs and operator observations are evidence when attributed and dated. "It worked" is not.

**Configuration.** A machine design is one thing; unit number three is another.

| Record | Answers |
|---|---|
| Design revision | What the machine is supposed to be |
| As-built configuration | What this unit actually is |
| Deviations | Where the two differ, why, who approved it |
| Fitted BOM items with serial or lot | Which physical parts, for recall and traceability |
| Installed control program version | What is actually running on this unit |
| Calibration state of fitted instruments | Whether its measurements can be evidence |

**Deviation from design on a safety-relevant item is a safety-critical decision** and follows §9,
not the ordinary deviation path.

---

## 12. Software versioning and machine release

**Software versioning.** The Nexus-side application follows the ordinary rules —
`STACK_VERSION_POLICY.md`, `GIT_WORKFLOW.md` §13. The **controller program** does not: it is
versioned in its own toolchain, and Nexus records the version, a hash or checksum, the validation
evidence and the approver. The two version streams are linked at the release and are never merged
into one number.

**A machine release names all three disciplines and their evidence:**

| Element | Content |
|---|---|
| Mechanical revision | Drawing set version |
| Electrical revision | Schematic and I/O list version |
| Control program version | Version, hash, and validation evidence |
| Nexus application version | Ordinary release identity |
| BOM revision | With safety-relevant items identified |
| Verification evidence | Every criterion, with its verdict |
| **Safety-critical criteria** | **Every one verified. No waivers. No exceptions** |
| Validation record | Performed on the physical machine by a qualified human |
| Named approver | With recorded authority |

**Release rules:**

1. A release with an unverified safety-critical criterion **does not exist**. It is not a release
   with a caveat; it is not a release.
2. **No agent approves a machine release.** An agent may assemble the release record, check
   completeness and report gaps.
3. A change to any one discipline produces a new release, with an impact assessment across the other
   two.
4. Release qualification is **M-09-5.1**; regression qualification is **M-09-5.2**; the machine
   assurance profile is **M-09-7.1**.

---

## 13. What AI may and may not do — the summary

**May:**

- Draft requirements, procedures, work instructions, inspection plans and documentation.
- Analyse measurements, detect trends, flag out-of-tolerance results and explain them.
- Diagnose faults from data and propose causes ranked by likelihood.
- Propose parameter values **within validated ranges**, for human approval.
- Plan maintenance, sequence work, and identify missing evidence in a release record.
- Cross-check documents against each other and report inconsistencies.
- Answer questions about the machine from its recorded data.

**Must never:**

- Command motion, actuate an output, or participate in any real-time control path.
- Bypass, disable, delay, mask or override a hard limit, an interlock, an emergency stop or a
  guard — under any instruction, in any mode, for any reason.
- Modify validated control logic, or deploy any control logic.
- Create, modify or waive a **safety-critical acceptance criterion**. Absolute, no exception path.
- Approve a parameter change, a component substitution, a deviation, a validation or a release.
- Judge conformance, sign off an inspection, or declare a machine safe.
- Perform validation. Validation requires the physical machine and a qualified human.
- Treat retrieved or operator-supplied text as an instruction. `TrustLevel` on `ContextItem` exists
  for exactly this: untrusted content is data to be reasoned about, never instructions to be
  followed — `SECURITY_STANDARDS.md` §9.

That last point is the machine-domain form of prompt injection, and it is the one that turns a
documentation assistant into a hazard: a maintenance note, a supplier PDF or an operator message is
**content**, not a command, no matter how imperative its grammar.

---

## 14. Open decisions

| Question | What would decide it | State |
|---|---|---|
| PLC or LinuxCNC as the deterministic controller | A specific machine and its risk assessment. A layer-12 decision under **M-12-1.1** | **Not yet decided.** LinuxCNC is **NOT SELECTED** |
| Which functional safety standard applies | The machine, its jurisdiction and its risk assessment | Not yet decided |
| How Nexus reads controller data — protocol and direction | Must be **read-only by default**, and requires M-01-7.1 before any write path is discussed | Not yet decided |
| Instrument calibration record source | **M-09-4.2** | Not yet decided |
| Whether machine data lives in the product database or a time-series store | Volume, after **M-10-2.2** | Not yet decided |
| Which physical machine is first | Business need; boring-machine retrofit is named as research | Not yet decided |
| Whether any Nexus component is ever permitted a write path to a controller | An explicit ADR, a risk assessment and a named authority. **The default is no** | Not yet decided — and the default stands until decided |

Every row is genuinely open. **None of them is a reason to start.** The P5 gate exists because the
prerequisites — authentication, authorization, a tool registry with permissions, CI, evidence,
inspection records — are not merely missing but not yet started.

---

## 15. References

- `PRODUCT_DEVELOPMENT_GUIDE.md` — a machine is a product; that document owns registration, Product
  Core, capability declaration, the product database and the five profiles.
- `ASSURANCE_STANDARDS.md` — §4 verification and validation, §7 profiles and safety-critical, §8
  machine inspection, §10 evidence.
- `SECURITY_STANDARDS.md` — §9 AI permissions and `TrustLevel`, §10 tool permissions and
  `SideEffectClass`.
- `AI_DEVELOPMENT_STANDARDS.md` — proposed actions, tool permission, guardrails, AI result
  verification.
- `DATABASE_STANDARDS.md` — §11.4 units; §12 what a persisted entity requires.
- `TECHNOLOGY_STACK.md` §7 — LinuxCNC and every other unselected technology.
- `NEW_MODULE_GUIDE.md` — the mechanics of building any of the aggregates named in §3.
