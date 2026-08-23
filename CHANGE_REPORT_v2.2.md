# Nexus v2.2 — Change Report

**Date** 2026-08-21 · **From** v2.1 · **To** v2.2
**Scope** Surgical corrections only. The architecture was not redesigned; the documentation set was not regenerated.
**No application code was modified. No migrations were run. No repositories were restructured.**

---

## 1. Development Ready Gate added

One gate was too heavy to stand in front of real business value. There are now two, and only the first blocks business development.

**GATE A — Development Ready.** Closes at `M-07-5.3`. The earliest safe point at which internal business systems can begin. Minimum only:

| Layer | In GATE A |
|---|---|
| CORE | Identity foundation · authentication · organisation/tenant · basic authorization · secrets · minimum audit for development accountability |
| DATA | Azure SQL foundation · EF Core migration convention · schema ownership · database standards · minimum persistence DEVELOPER needs |
| AI | Working model gateway · AI callable from DEVELOPER · minimum context handling |
| DEVELOPER | The full V1a chain through simultaneous development, build/test capture, review, progress, controlled integration |
| DELIVERY | Package feed reachable from CI · git integration · branch/worktree rules · CI build · automated test execution · branch protection · results to DEVELOPER · source backup minimum |
| ASSURANCE | Acceptance Criterion · Verification Method · Evidence · Pass/Fail · basic quality gate |
| GOVERNANCE | Product identity only |
| PRODUCT CORE | Workspace · Project · Subproject only |
| EXPERIENCE | **Nothing** |
| AUTOMATION | **Nothing** |
| OPERATIONS | Structured logging with correlation only |

**Seven milestones moved out of the gate,** each with the reason recorded in the roadmap itself so the decision survives without this report:

| Milestone | Was | Now | Why |
|---|---|---|---|
| `M-01-4.2` usage metering | P1 | P2 | Not in the GATE A CORE minimum. Audit is; metering is not. |
| `M-04-1.2` durable AI memory | P0 | P2 | Must not block business development. |
| `M-11-1.1` conversation core | P1 | P2 | EXPERIENCE is not in the GATE A minimum. |
| `M-11-1.2` boundary enforcement | P1 | P2 | Follows the conversation core. |
| `M-11-2.1` scope resolution | P1 | P2 | Follows the conversation core. |
| `M-11-3.1` reusable chat surface | P1 | P2 | Follows scope resolution. |
| `M-07-6.1` DEVELOPER scope resolver | P1 | P2 | Depends on EXPERIENCE, which left GATE A. |

**One milestone moved in:** `M-07-7.1` Requirements, P2 → **P1**. The GATE A DEVELOPER minimum names `Requirement` explicitly, and the milestone was sitting in P2 — a contradiction between the gate definition and the work.

**DEVELOPER V1a has no conversation surface at GATE A.** It has an API and a work-graph view. That is enough to coordinate three workers and removes roughly three weeks from the critical path.

## 2. Foundation Gate redefined as GATE B

Not deleted — redefined as the broader maturity checkpoint. Closes at the end of P2 when all seven are complete:

`M-04-1.2` durable AI memory · `M-11-2.1` scope resolution · `M-11-3.1` chat surface · `M-07-3.2` autonomous dispatch · `M-08-5.1` automated deployment · `M-10-2.2` metrics and tracing · `M-09-5.1` release qualification

**The rule that makes two gates worth having:** GATE B work runs in parallel with business development and must never pause or block it. A business system waiting on GATE B is a scheduling error, not a dependency. **Check 12 fails the build if any business-system milestone declares a dependency on a GATE B closer.**

## 3. Business-system eligibility moved earlier

| | v2.1 | v2.2 |
|---|---|---|
| Gate that unblocks them | Foundation Gate (heavy) | **GATE A Development Ready** (7 milestones lighter) |
| Phase | P2 | P2 |

The phase number did not move. What moved is the weight of the gate in front of it, so P2 arrives sooner. Eight systems eligible: `F-12-8` Business OS/ERP (recommended first), `F-12-9` CRM/Field Data, `F-12-10` Engine Works, `F-12-11` Retreads, `F-12-12` Transport, `F-12-13` Knowledge Systems, `F-12-14` Internal Tools, `F-12-15` Machine Development. `F-12-16` Machine Automation remains P5 and safety-gated.

Eligible means pulled by business need, not scheduled.

## 4. Technical AI rename removed

The `Nexus.Intelligence.* → Nexus.AI.*` rename is gone from the roadmap, the transform and the master architecture. **Layer short name stays AI; assemblies stay `Nexus.Intelligence.*`.**

> Short architecture names are for human comprehension. Stable technical names are not changed without technical value.

**Check 13** now fails the build if any milestone exists whose only purpose is a namespace rename.

## 5. Roadmap dependency changes

| Milestone | Change | Reason |
|---|---|---|
| `M-04-3.1` DeveloperAgent | depends on `M-04-1.1` instead of `M-04-1.2` | It needs durable **traces**, not durable **memory**. This is what unblocks GATE A. |
| `M-07-5.3` GATE A acceptance | `M-11-2.1` removed | GATE A no longer requires EXPERIENCE scope resolution. |
| `M-05-1.3`, `M-03-3.3`, `M-05-3.2` | `M-01-8.1` added | All three require publishing an event but never declared a dependency on the milestone that creates the only publishing mechanism. Undeclared, this is how an event bus gets built by accident, early, by whoever hits the requirement first. |

Dependency count 203 → 206.

## 6. Contradictory ownership references corrected

| Where | Was | Now |
|---|---|---|
| Master architecture entity matrix | `Workspace` → 12 PRODUCTS (Chat), **KEEP**, "Chat's own organising concept" | `Workspace` → **06 PRODUCT CORE**, **MOVE**, a reusable scope primitive |
| Same | `Project` → 12 PRODUCTS (Chat), **KEEP** | `Project` → **06 PRODUCT CORE**, **MOVE** |
| Roadmap layer 03, 05 `does_not_own` | "09 Operations", "11 Products" | "10 OPERATIONS", "12 PRODUCTS" |
| Roadmap layer 07 `note` | "the Layer 10 scope resolver" | "the Layer 11 EXPERIENCE scope resolver" |
| Roadmap layer 08 `does_not_own` | "Runtime health — Layer 09" | "Layer 10 OPERATIONS" |
| `M-07-6.1` acceptance | "Layer 10 contains no Developer type" | "Layer 11 EXPERIENCE contains no DEVELOPER type" |
| `M-12-1.1` acceptance | "register a Layer 10 scope resolver" · "modifying a layer below 11" | "Layer 11 EXPERIENCE" · "below 12" |

All were leftovers from the v2.0 → v2.1 renumbering, in free-text fields that the structured remap did not reach.

**EXPERIENCE/Chat wording** was already correct in substance. The roadmap's layer-11 note now states it once, precisely: EXPERIENCE owns the reusable conversation capability; an optional consumer-facing Nexus Chat *application* may later exist under PRODUCTS and would consume this engine; the universal conversation engine is never a Chat product. References describing `Nexus.Web` as containing the Chat product today were left alone — that is accurate current state, not a contradiction.

## 7. Two further corrections found during the pass

**Feature phase was drifting from its milestones.** EXPERIENCE features `F-11-1`, `F-11-2`, `F-11-3` still said `phase: P1` while every milestone under them had moved to P2. Feature phase is now **derived** — the earliest phase among its milestones — and **check 14** fails if any feature disagrees with its own children.

**Two different things were both called "events".** `OBSERVABILITY_STANDARDS.md` §6 defined events as `snake_case`, past tense, diagnostic, no handlers — that is telemetry. The roadmap requires `PipelineCompleted`, `JobEscalated`, `CertificateExpiring` — integration events, PascalCase, with handlers and contracts. Neither document acknowledged the other. `OBSERVABILITY_STANDARDS.md` is now scoped to telemetry events with a pointer to `EVENT_ARCHITECTURE.md`; that is the only pre-existing standard edited, and only because a direct contradiction required it.

## 8. Files created (12)

| File | Purpose |
|---|---|
| `docs/BOUNDED_CONTEXTS.md` | The context map — contexts within each layer, contracts, extension models |
| `docs/EVENT_ARCHITECTURE.md` | Integration events. **Explicitly does not create an event bus by existing** — CURRENT is direct calls and DI, nothing else |
| `docs/INTEGRATION_ARCHITECTURE.md` | Layer, product, external and machine integration boundaries |
| `docs/SECURITY_ARCHITECTURE.md` | Trust boundaries, identity flow, tenant isolation, permission architecture |
| `docs/PRODUCT_ARCHITECTURE.md` | Standard product shape, capability composition, eight-dimension state model |
| `docs/DEVELOPER_ARCHITECTURE.md` | System of record, worker model, worktree isolation, parallel safety, V1a vs later |
| `docs/DELIVERY_ARCHITECTURE.md` | Source to running system; GATE A minimum vs later maturity |
| `docs/ASSURANCE_ARCHITECTURE.md` | Traceability model, verification vs validation, inspection, evidence, gates, profiles |
| `docs/OPERATIONS_ARCHITECTURE.md` | Runtime ownership and the boundary with DELIVERY and ASSURANCE |
| `docs/EXPERIENCE_ARCHITECTURE.md` | Conversation engine, ScopeRef, IScopeResolver, three-consumer worked example |
| `docs/AI_ARCHITECTURE.md` | Current `Nexus.Intelligence.*` architecture across CURRENT / GATE A / GATE B / FUTURE |
| `CHANGE_REPORT_v2.2.md` | This report |

Each was checked against existing documents first. None duplicates an authoritative equivalent — all eleven subjects were previously buried only inside the master architecture.

## 9. Files modified (5)

| File | Reason |
|---|---|
| `nexus-roadmap.yaml` | Two gates, business eligibility, seven milestones out of GATE A, Requirements in, six dependency changes, ownership references, feature-phase derivation, AI rename removed |
| `NEXUS_MASTER_ARCHITECTURE.md` | v2.2. Part 12 rewritten as two gates; §4.1 AI naming; entity matrix Workspace/Project ownership; Part 13 counts, thirteen→fifteen checks, business timing; EXPERIENCE minimum-V1 note; nine residual "Foundation Gate" references reconciled |
| `docs/DOCUMENTATION_INDEX.md` | Eleven new architecture rows; gate note; SECURITY and OBSERVABILITY disambiguation; prompt-file archive section |
| `docs/OBSERVABILITY_STANDARDS.md` | Scoped to telemetry events — the only pre-existing standard edited, required by a direct contradiction |
| `docs/DEPENDENCY_RULES.md` | 07 → 11 marked resolved rather than contested, citing master §4.2.1 (Contracts-only inversion) |

## 10. Files left unchanged (24 standards and guides)

`TECHNOLOGY_STACK` · `STACK_VERSION_POLICY` · `NAMING_STANDARDS` · `CODE_CONVENTIONS` · `CSHARP_STANDARDS` · `TYPESCRIPT_REACT_STANDARDS` · `DATABASE_STANDARDS` · `API_STANDARDS` · `GIT_WORKFLOW` · `DEVELOPMENT_WORKFLOW` · `ASSURANCE_STANDARDS` · `SECURITY_STANDARDS` · `CONFIGURATION_STANDARDS` · `ERROR_HANDLING` · `DEFINITION_OF_DONE` · `CODE_REVIEW_CHECKLIST` · `DEVELOPER_ONBOARDING` · `LOCAL_DEVELOPMENT` · `REPOSITORY_STRUCTURE` · `NEW_MODULE_GUIDE` · `PRODUCT_DEVELOPMENT_GUIDE` · `MACHINE_DEVELOPMENT_GUIDE` · `AI_DEVELOPMENT_STANDARDS` · `ARCHITECTURE_OVERVIEW` · `LAYER_MODEL` · `DATA_OWNERSHIP` · `DATABASE_ARCHITECTURE` · `ADR_STANDARD` · `DOCUMENTATION_STANDARDS`

None contradicted the corrections.

## 11. Files archived / superseded

**Recommended, not executed** — moving files on your machine is outside this task's scope:

| Item | Action | Note |
|---|---|---|
| `SQL_PROMPTS_STAGE_1B_2A.md`, `SQL_PROMPTS_STAGE_2B_2C.md` | Archive to `docs/archive/` | Still operative until `M-02-1.4` |
| `FRONTEND_PROMPTS_F0_F4.md` | Archive | Completed record |
| `DOCS_CONSOLIDATION_PROMPT.md`, `nexus-*.ps1` | Archive | Working material |
| `00_DOCUMENTATION_STANDARD.md`, `NEXUS_DOCUMENTATION_STANDARD.md` | Merge into `DOCUMENTATION_STANDARDS.md`, then archive | Two documents already covered one subject |
| `07_DEVELOPMENT_GUIDE.md`, `09_ROADMAP_AND_MILESTONES.md` | Superseded | Split across the new set / replaced by the roadmap |
| ADRs | **Keep, never delete** | Decision record |

## 12. Validation

Fifteen mechanical checks. All pass. Three are new in v2.2, and two of those exist specifically to keep the gates honest.

| Metric | Value |
|---|---|
| Node count | **614** (12 layers · 90 features · 151 milestones · 108 work items · 140 tasks · 113 subtasks) |
| Unique IDs | **614 / 614** — no collisions |
| Dependency count | **206** |
| Broken dependencies | **0** |
| Circular dependencies | **0** — topological sort completes over all 151 milestones |
| Phase violations | **0** |

| # | Check | Result |
|---|---|---|
| 1 | All IDs unique | PASS |
| 2 | All parent references valid | PASS |
| 3 | All dependencies resolvable | PASS |
| 4 | No impossible phase dependencies | PASS |
| 5 | No circular dependency graph | PASS |
| 6 | Every milestone has acceptance criteria | PASS — 151/151 |
| 7 | Every gate has measurable evidence | PASS — `M-07-5.3`, `M-09-1.3` |
| 8 | Every GATE A minimum item has a dependency path | PASS |
| 9 | Business systems eligible after GATE A | PASS — 8 systems; `F-12-16` exempt as safety-gated |
| 10 | Parallel-safe classifications valid | PASS — 99 milestones carry a schema conflict group |
| 11 | GATE B closers exist and land by P2 | PASS — 7 closers |
| 12 | **No business system blocked by a GATE B closer** | PASS |
| 13 | **No project-rename churn remains** | PASS |
| 14 | **Feature phase consistent with its milestones** | PASS |
| 15 | **No milestone requires an event without depending on the bus** | PASS |

Phase distribution after the gate change:

| | P0 | P1 | P2 | P3 | P4 | P5 |
|---|---|---|---|---|---|---|
| Features | 7 | 18 | 19 | 35 | 8 | 3 |
| Milestones | 13 | 27 | 30 | 72 | 7 | 2 |

P1 went from 32 milestones to 27. That is what "lighter gate" means in practice.

## 13. Remaining issues

Genuine and unresolved. None blocks the corrections above.

| # | Issue | Needs |
|---|---|---|
| 1 | **Contested dependency cells.** 12 → 07, 12 → 09 and 07 → 09 are still marked contested in `DEPENDENCY_RULES.md`. 07 → 11 is now resolved. PRODUCTS depending on DEVELOPER is probably a build-time relationship rather than a runtime dependency, but that should be decided rather than assumed. | ADR-016 |
| 2 | **Product state dimensions not ratified.** `M-12-1.3` requires each of the eight marked derived or manual. `PRODUCT_ARCHITECTURE.md` §11 assigns them — `ProductLifecycleState` manual, the other seven derived — and marks the assignment provisional. | ADR-016 |
| 3 | **No capability-pack vocabulary exists.** `M-07-8.3` names Vault's composition verbatim but no catalogue is defined. The five compositions in `PRODUCT_ARCHITECTURE.md` §8 are marked illustrative, not ratified. | Decide at `M-07-8.3`, P3 |
| 4 | **ADRs 001–013 are missing.** ADR-014 records `Supersedes: ADR-002`, so they existed. Numbers stay spent either way. | Recover, or formally record as lost, before ADR-016 |
| 5 | **No logging library selected.** `M-10-1.1` correlation is GATE A scope and correlation is disproportionately expensive to retrofit. This is the sharpest unmade decision. | Decide during P0 |
| 6 | **Antivirus exclusion on `C:\Personal` never confirmed.** Recommended 2026-08-20 after all three repositories lost `.git\objects` simultaneously. | Verify today |
| 7 | **SQL Stage 1b still uncommitted.** `.git/logs/HEAD` unchanged since 2026-08-20 17:54 UTC. Complete, proven by `api_run.log`, and one machine failure from loss. | Commit today |

## 14. Recommended first implementation milestone

**Before the roadmap, and outside it: commit SQL Stage 1b, and confirm the antivirus exclusion.** Neither needs approval and both close open risk.

**Then `M-08-1.1` — package feed reachable from CI.**

It is first for three reasons, and v2.2 strengthens the case: it has no dependencies; it is roughly a day; and it now appears explicitly in the GATE A DELIVERY minimum because every other pipeline is blocked on it. Platform and Intelligence packages resolve only from `C:\Personal\LocalNuGet`, which no build agent can reach.

Then the first parallel batch — `M-08-1.2` CI pipelines, `M-02-1.2` SQL Stage 2a, `M-04-1.1` durable AI traces — three repositories, three distinct scopes, no shared migration.

---

*Change report ends. Architecture, roadmap and documentation are updated and saved. No implementation has begun.*
