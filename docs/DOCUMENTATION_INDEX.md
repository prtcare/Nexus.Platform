# Documentation Index

> **Status** Authoritative · **Owner** Durai · **Last updated** 2026-08-21 · **Architecture version** v2.2
> **Authoritative for** what every Nexus document is, who owns it, when it must change, and what must never be duplicated.

This index is the entry point. If you are new — human or agent — read [DEVELOPER_ONBOARDING.md](DEVELOPER_ONBOARDING.md) first, then come back here.

---

## 1. The rule that makes this index worth having

**One subject, one document.** If a document restates something another owns, the restatement is wrong the day the owner changes. Every entry below names exactly what it is authoritative for. If you need to write something and cannot find its owner here, that is a gap — add a document and add a row. Do not append it to whatever document you happen to be editing.

Three consequences:

1. **Link, do not copy.** `CSHARP_STANDARDS.md` does not restate EF Core rules; it links to `DATABASE_STANDARDS.md`.
2. **A fact lives with its owner.** Structured development state lives in DEVELOPER, not in markdown. Documents *describe*; they do not *duplicate state*.
3. **Contradiction is a defect.** If two documents disagree, the one listed as authoritative here wins, and the other is a bug to be fixed — not a matter of taste.

---

## 2. Authoritative vs generated vs transitional

| Class | Meaning | Examples |
|---|---|---|
| **Authoritative** | Hand-maintained. The single source of truth for its subject. | All standards below |
| **Transitional** | Hand-maintained now, machine-owned later. Has a named milestone that ends its life in this form. | `nexus-roadmap.yaml` → imported to DEVELOPER at `M-07-1.1` |
| **Generated** | Produced by a tool. Never edit by hand. | None yet. OpenAPI output when `M-08-1.2` publishes it |
| **Historical** | Superseded but preserved for the decision record. Never delete. | ADRs, `GIT_RECOVERY_2026-08-20.md` |

`nexus-roadmap.yaml` is a deliberate hybrid and the only one. It is hand-written but mechanically validated, and it exists to be imported. Its `generated:` field records when it was last assembled, not that a tool authored it.

---

## 2a. Current implementation state

| Document | Authoritative for | Update when |
|---|---|---|
| [CURRENT_STATE.md](CURRENT_STATE.md) | What is actually built and running right now, separate from what the roadmap plans — completed capability, the active step, temporary mechanisms and their closing milestone, known documentation gaps, and open blockers. | A milestone completes, a temporary mechanism closes, a documentation gap closes, or a blocker appears or clears. |

---

## 3. Architecture — what Nexus is

**Two gates (v2.2).** GATE A *Development Ready* is the earliest safe point at which internal business systems can begin; GATE B *Foundation Ready* confirms the broader reusable foundation. GATE B work runs in parallel with business development and never blocks it. See `NEXUS_MASTER_ARCHITECTURE.md` §12.

| Document | Authoritative for | Update when |
|---|---|---|
| [NEXUS_MASTER_ARCHITECTURE.md](../NEXUS_MASTER_ARCHITECTURE.md) | The whole architecture. Current state, the 12 layers, responsibility and data-ownership matrices, Foundation Gate, entity migration. | A layer's responsibility changes, the gate moves, or a decision in Part 21 is revisited |
| [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md) | The short version. What Nexus is in ten minutes. | The master architecture changes materially |
| [LAYER_MODEL.md](LAYER_MODEL.md) | The 12 layers in detail — short and long names, purpose, what each owns and does not own, repository, schema, projects, minimum-before-the-gate scope, and the old-name mapping. | A layer is added, renamed or merged, or a layer's scope or gate slice changes |
| [DEPENDENCY_RULES.md](DEPENDENCY_RULES.md) | What may depend on what, and how it is enforced. | A dependency rule changes or a new architecture test is added |
| [DATA_OWNERSHIP.md](DATA_OWNERSHIP.md) | Which layer owns which structured fact. The domain-owns-the-fact rule, the complete entity-to-layer mapping, and the migration matrix for every entity that exists today. | An entity moves layer, or a new entity's home is decided |
| [DATABASE_ARCHITECTURE.md](DATABASE_ARCHITECTURE.md) | Physical database strategy — one platform database, schema per layer, one database per product. | The physical strategy changes or a layer splits to its own database |
| [BOUNDED_CONTEXTS.md](BOUNDED_CONTEXTS.md) | The context map — bounded contexts within each layer, their public contracts and extension models. | A context is added, split or its extension point changes |
| [EVENT_ARCHITECTURE.md](EVENT_ARCHITECTURE.md) | **Integration** events — when to use one, naming, ownership, versioning, idempotency, failure handling. Telemetry events belong to OBSERVABILITY_STANDARDS. | An event contract is added or the sync-vs-event rule changes |
| [INTEGRATION_ARCHITECTURE.md](INTEGRATION_ARCHITECTURE.md) | How layers, products and external systems connect — including the machine integration boundary. | An integration point is added or a boundary rule changes |
| [SECURITY_ARCHITECTURE.md](SECURITY_ARCHITECTURE.md) | Trust boundaries, identity flow, tenant isolation, permission architecture. Architectural only — coding rules are in SECURITY_STANDARDS.md. | A trust boundary or isolation mechanism changes |
| [PRODUCT_ARCHITECTURE.md](PRODUCT_ARCHITECTURE.md) | The standard product shape, capability composition, and the eight-dimension product state model. | The product framework or state model changes |
| [DEVELOPER_ARCHITECTURE.md](DEVELOPER_ARCHITECTURE.md) | DEVELOPER as system of record — entity model, dependency graph, worker model, worktree isolation, parallel safety, V1a vs later. | A DEVELOPER entity, rule or autonomy level changes |
| [DELIVERY_ARCHITECTURE.md](DELIVERY_ARCHITECTURE.md) | Source to running system — git, CI, artifacts, environments, deployment, backup, DR. | A delivery mechanism or environment profile changes |
| [ASSURANCE_ARCHITECTURE.md](ASSURANCE_ARCHITECTURE.md) | The traceability model, verification vs validation, inspection, evidence, quality gates, assurance profiles. | A verification method, profile or gate rule changes |
| [OPERATIONS_ARCHITECTURE.md](OPERATIONS_ARCHITECTURE.md) | Runtime ownership and the boundary with DELIVERY and ASSURANCE. | An observability or incident mechanism changes |
| [EXPERIENCE_ARCHITECTURE.md](EXPERIENCE_ARCHITECTURE.md) | The conversation engine, ScopeRef, IScopeResolver, the context handoff. | The conversation core or scope resolution changes |
| [AI_ARCHITECTURE.md](AI_ARCHITECTURE.md) | The current Nexus.Intelligence.* technical architecture — turn pipeline, context seam, provider abstraction. | The Intelligence contract surface or pipeline changes |
| `nexus-roadmap.yaml` | All structured work: features, milestones, work items, tasks, subtasks, dependencies, phases. | Any work is added, rephased, or completed — until `M-07-1.1` imports it |

---

## 4. Stack and language standards — how to write it

| Document | Authoritative for | Update when |
|---|---|---|
| [TECHNOLOGY_STACK.md](TECHNOLOGY_STACK.md) | Every approved technology, why it was chosen, where it is used, who owns it. | A technology is adopted, rejected or removed |
| [STACK_VERSION_POLICY.md](STACK_VERSION_POLICY.md) | How versions are pinned and upgraded across .NET, npm and NuGet. | The pinning mechanism or upgrade cadence changes |
| [NAMING_STANDARDS.md](NAMING_STANDARDS.md) | Naming for everything — code, database, API, git, frontend, roadmap IDs, documents. | A new category of thing needs a name |
| [CODE_CONVENTIONS.md](CODE_CONVENTIONS.md) | Cross-language conventions and where languages legitimately differ. | A convention changes for more than one language |
| [CSHARP_STANDARDS.md](CSHARP_STANDARDS.md) | C#-specific rules. | A C# rule changes |
| [TYPESCRIPT_REACT_STANDARDS.md](TYPESCRIPT_REACT_STANDARDS.md) | TypeScript and React rules, frontend structure. | A frontend rule changes |
| [DATABASE_STANDARDS.md](DATABASE_STANDARDS.md) | How to write schema and migrations. Id/Seq/Ref, cascade rules, all ADR-014 decisions. | A schema convention changes |
| [API_STANDARDS.md](API_STANDARDS.md) | HTTP surface conventions. | An API convention changes |
| [AI_DEVELOPMENT_STANDARDS.md](AI_DEVELOPMENT_STANDARDS.md) | Building on the AI layer — providers, prompts, context, agents, tools, evaluation. | The Intelligence contract surface changes |

---

## 5. Process standards — how to work

| Document | Authoritative for | Update when |
|---|---|---|
| [DEVELOPMENT_WORKFLOW.md](DEVELOPMENT_WORKFLOW.md) | The path from requirement to operations, and when an item may change state. | A state transition or its entry condition changes |
| [GIT_WORKFLOW.md](GIT_WORKFLOW.md) | Branches, worktrees, commits, merges, recovery, parallel worker isolation. | A git rule changes |
| [ASSURANCE_STANDARDS.md](ASSURANCE_STANDARDS.md) | How anything is proven — test, inspection, validation, evidence, profiles. | A verification method or profile changes |
| [DEFINITION_OF_DONE.md](DEFINITION_OF_DONE.md) | What "done" requires, by risk tier. | A completion condition changes |
| [CODE_REVIEW_CHECKLIST.md](CODE_REVIEW_CHECKLIST.md) | What a reviewer checks. | A new class of defect gets past review |
| [SECURITY_STANDARDS.md](SECURITY_STANDARDS.md) | Security **coding rules** — how to implement auth, isolation, secrets and permissions. Architecture is in SECURITY_ARCHITECTURE.md. | A security control changes |
| [CONFIGURATION_STANDARDS.md](CONFIGURATION_STANDARDS.md) | Configuration hierarchy and what may never enter git. | A configuration mechanism changes |
| [OBSERVABILITY_STANDARDS.md](OBSERVABILITY_STANDARDS.md) | Logging, correlation, metrics, health, **telemetry** events. Integration events belong to EVENT_ARCHITECTURE.md. | A telemetry convention changes |
| [ERROR_HANDLING.md](ERROR_HANDLING.md) | The failure taxonomy and error-to-response mapping. | A new error class appears |
| [AI_DEVELOPMENT_GOVERNANCE.md](AI_DEVELOPMENT_GOVERNANCE.md) | The architect / coding-model responsibility boundary, the development loop, the review gate, and the AGENTS.md contract every repository must satisfy. Not to be confused with AI_DEVELOPMENT_STANDARDS.md, which governs building Nexus's own AI layer, a different subject. | The role boundary, loop, or AGENTS.md contract changes. |

---

## 6. Guides — how to start

| Document | Authoritative for | Update when |
|---|---|---|
| [DEVELOPER_ONBOARDING.md](DEVELOPER_ONBOARDING.md) | Zero to first pull request. | Any setup step changes |
| [LOCAL_DEVELOPMENT.md](LOCAL_DEVELOPMENT.md) | The exact current local topology — ports, feeds, startup order, common failures. | The local setup changes |
| [REPOSITORY_STRUCTURE.md](REPOSITORY_STRUCTURE.md) | Repositories, solutions, projects, folders, and what belongs where. | A repository or project is added, renamed or moved |
| [NEW_MODULE_GUIDE.md](NEW_MODULE_GUIDE.md) | Creating a module, endpoint, entity, migration, feature, agent, workflow, test or document. | A creation procedure changes |
| [PRODUCT_DEVELOPMENT_GUIDE.md](PRODUCT_DEVELOPMENT_GUIDE.md) | Standing up a new product without contaminating shared layers. | The product framework changes |
| [MACHINE_DEVELOPMENT_GUIDE.md](MACHINE_DEVELOPMENT_GUIDE.md) | Machine and engineering projects, and the safety constraints that bound them. | A machine convention or safety rule changes |
| AGENTS.md (repository root, one per repository) | The coding-model entry point for that specific repository — mandatory reading, repository-specific rules, known temporary mechanisms, the architect-approval boundary. | The repository's mandatory-reading set or temporary mechanisms change. |

---

## 7. Meta — how documentation itself works

| Document | Authoritative for | Update when |
|---|---|---|
| **DOCUMENTATION_INDEX.md** (this file) | What exists, who owns it, what is authoritative. | **Any document is created, merged, renamed or archived. No exceptions.** |
| [DOCUMENTATION_STANDARDS.md](DOCUMENTATION_STANDARDS.md) | Document format, front matter, lifecycle, archival, the no-duplication rule. | The documentation format changes |
| [ADR_STANDARD.md](ADR_STANDARD.md) | ADR numbering, structure and template. | The ADR format changes |

---

## 8. Decision records

One global sequence. Never renumber, never reuse, never delete.

| ADR | Subject | Status |
|---|---|---|
| ADR-014 | Azure SQL migration — supersedes the Dataverse persistence decision | Accepted, in progress (P0) |
| ADR-015 | ProjectBrief as a context source | Accepted, implementation deferred |
| ADR-016 | *next available* | — |

**Open issue.** ADR-014 records `Supersedes: ADR-002`, so ADRs 001–013 were made and their files are not in the set. Numbers 001–013 stay spent regardless. Whether they are recovered or formally recorded as lost is a human decision — see `ADR_STANDARD.md` §3.2.

---

## 9. Historical — preserved, never deleted

| Document | Why kept |
|---|---|
| `DATAVERSE_SCHEMA_REFERENCE.md` | The schema being migrated away from. Delete only after `M-02-1.4` and a review. |
| `NEXUS_ARCHITECTURE_V2.md` | Superseded by `NEXUS_MASTER_ARCHITECTURE.md` v2.1. Kept for the decision trail. |
| `docs\archive\NEXUS_MASTER_ARCHITECTURE.v2.1-draft.md` | Pre-v2.2 draft found already present in the repository at landing time (2026-08-23). Superseded in substance (layer count, gate structure, entity ownership) by NEXUS_MASTER_ARCHITECTURE.md v2.2. |

---

## 10. Consolidation status

The pre-v2.1 documentation set overlaps this one. Dispositions:

| Existing document | Action | Reason |
|---|---|---|
| `07_DEVELOPMENT_GUIDE.md` | **SUPERSEDE** | Split across `DEVELOPER_ONBOARDING`, `LOCAL_DEVELOPMENT`, `DEVELOPMENT_WORKFLOW`, `CSHARP_STANDARDS`, `GIT_WORKFLOW` |
| `09_ROADMAP_AND_MILESTONES.md` | **SUPERSEDE** | `nexus-roadmap.yaml` is authoritative for structured work |
| `00_DOCUMENTATION_STANDARD.md` | **MERGE** into `DOCUMENTATION_STANDARDS.md` | Same subject. Its unconfirmed location/numbering decisions are now settled |
| `NEXUS_ARCHITECTURE_V2.md` | **ARCHIVE** | Superseded by v2.1 |
| `NEXUS_MIGRATION_RUNBOOK.md` | **KEEP** | Still operative for ADR-014 stages |
| `ADR-014`, `ADR-015` | **KEEP** | Decision record. Reformat front matter to `ADR_STANDARD.md` |
| `DATAVERSE_SCHEMA_REFERENCE.md` | **KEEP until `M-02-1.4`** | Needed while the migration runs |
| Numbered set `01`–`06`, `08`, `10`–`12` | **REVIEW individually** | Not yet classified. Each either maps to a document above and is superseded, or covers something unowned and should be renamed into this scheme |
| `SQL_PROMPTS_*.md`, `FRONTEND_PROMPTS_*.md`, `DOCS_CONSOLIDATION_PROMPT.md` | **ARCHIVE after use** | Working prompts, not standards. Move out of the docs root |

**Three documents describing documentation is the no-duplication rule being broken by the documents that state it.** That is the first thing to fix.

### 10.1 Working prompt files — archive

`SQL_PROMPTS_STAGE_1B_2A.md`, `SQL_PROMPTS_STAGE_2B_2C.md`, `FRONTEND_PROMPTS_F0_F4.md`, `DOCS_CONSOLIDATION_PROMPT.md` and the `*.ps1` restructure scripts sit at the repository root. They are working material, not standards. Move them to `docs/archive/` and leave them there — the SQL prompts remain operative until `M-02-1.4`, and the frontend prompts are a completed record.

---

## 11. When documentation must change

Documentation is not optional and not deferred. It updates in the same work item as the change, per `DEFINITION_OF_DONE.md`.

| Trigger | Must update |
|---|---|
| A new technology is adopted | `TECHNOLOGY_STACK.md` |
| A layer's responsibility changes | `NEXUS_MASTER_ARCHITECTURE.md`, `LAYER_MODEL.md`, `DATA_OWNERSHIP.md` |
| An entity changes layer | `DATA_OWNERSHIP.md`, master architecture entity matrix |
| A schema convention changes | `DATABASE_STANDARDS.md` |
| A new endpoint pattern appears | `API_STANDARDS.md` |
| A decision with lasting consequence is made | A new ADR |
| Any document is created or retired | **This index** |
| A local setup step changes | `LOCAL_DEVELOPMENT.md`, `DEVELOPER_ONBOARDING.md` |
| A CURRENT→TARGET gap closes | Remove the marker and the milestone reference from every document carrying it |

That last row matters most. Every `CURRENT / TARGET / TRANSITION` marker in this set names the milestone that closes it. When the milestone completes, the marker goes. Stale markers are how a document set rots — it stops describing the system and starts describing a system that no longer exists.
