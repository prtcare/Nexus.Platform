# Roadmap and Milestones

Last reviewed 2026-08-20.

## Progress summary

| Phase / Milestone | Status |
|---|---|
| Phase 1 — Foundation | Complete |
| Phase 2 M0 — Core foundation rework | Complete — superseded by the V2.1 three-solution split |
| **V2.1 — Three-solution restructure** | **Complete**, tagged `v2-arch` in all three repos |
| Phase 2 M1 — Persistence backend | **In progress** — migrating Dataverse → Azure SQL (ADR-014). Step 1 below |
| Phase 2 M2 — Persistent memory / intelligence | Partial — memory now wholly in Intelligence (D-2), nothing durable yet |
| Phase 2 M3 — API contract and frontend | **In progress** — F0 complete. Step 2 below |
| Phase 2 M4 — Developer / Visual Studio agent | Early foundation |
| Phase 2 M5 — Power Platform compiler | Planned, partially designed |
| Phase 2 M6 — CNC / machine automation agent | Planned research |
| Phase 3 — Products and business integration | Not fully scoped |

---

# The current sequence

Four steps, in this order, one at a time. This supersedes the older "Gate A–D" ordering
below, which was written when there was one solution and one database.

## Step 1 — Move the existing tables to Azure SQL

The eleven aggregates already modelled in C#. Nothing new is designed here; this is a
port, and Dataverse data is disposable, so it is a schema rewrite rather than a data
migration.

| Stage | Aggregates | Prompt |
|---|---|---|
| 1 | Workspace — the leak test | **done, passed** |
| 1b | Workspace → `org` schema, `Seq`/`Ref`, `Reference` on the aggregate | `SQL_PROMPTS_STAGE_1B_2A.md` |
| 2a | Project, Conversation, ConversationMessage | `SQL_PROMPTS_STAGE_1B_2A.md` |
| 2b | Knowledge, Adr, WorkItem, Artifact | `SQL_PROMPTS_STAGE_2B_2C.md` |
| 2c | Session, Branch, Snapshot | `SQL_PROMPTS_STAGE_2B_2C.md` |
| 3 | Delete Dataverse entirely | `ADR-014` §2 Stage 3 |

**Gate:** every repository interface the Domain declares resolves to a SQL implementation
with `Nexus:Persistence=Sql`; a chat turn round-trips end to end; the
`Microsoft.PowerPlatform.Dataverse.Client` package reference and the
`System.Security.Cryptography.Xml` pin are both gone.

Finish with Stage 3 rather than deferring it. Carrying both persistence stacks doubles the
surface area of every subsequent change, and it is 7.2 MB of Dataverse assemblies in every
build output. The strangler pattern is a means of arriving safely, not a destination.

**One consequence to plan for:** Dataverse's row-level security leaves with Dataverse.
Nothing replaces it. That raises identity from "open debt" to a dependency — see Step 3b.

## Step 2 — The chatbot frontend (phase 1)

| Stage | Scope | State |
|---|---|---|
| F0 | Single HTTP path through `ApiClient`, `/api/v1` base, dead `products` feature removed | **done** (`267b4b7`) |
| F1 | Insights not Intelligence, `system` not `platform`, one env variable | prompt written |
| F2 | The chat UI — thread, composer, conversation list | prompt written |
| F3 | Citations, usage, decision trace | prompt written |
| F4 | Loading/empty states, error boundary, keyboard, dead-end audit | prompt written |

All in `FRONTEND_PROMPTS_F0_F4.md`. **One prompt at a time**, each ending in build and
commit, `/clear` between — F0–F4 were pasted together once and only F0 ran.

**Gate:** send a real chat turn in a browser, see the answer, and see *which context
produced it*.

F3 is the stage that matters beyond the UI. Until citations render, every intelligence
change — ranking weights, trust levels, prompt section order, the `ContextBundle` mapper —
is unfalsifiable. Swagger tells you an endpoint returned 200; it cannot tell you whether the
assembled context produced a good answer.

## Step 3 — The ten tables that exist only in Dataverse

These have no C# aggregate, so they are **domain design**, not porting. That is a different
kind of work and deserves its own prompts, written when we get there rather than now.

They also split cleanly on one dependency, and the split matters:

### Step 3a — no identity required

| Table | Why first |
|---|---|
| `ProjectBrief` | **ADR-015.** The single biggest answer-quality lever — the `Objective` slot currently holds a bare project name at `Authoritative` trust |
| `ProjectMilestone` | Central to the product hierarchy; `WorkItem` already has a `milestone` FK waiting |
| `MilestoneCriterion` | Completes the milestone model; its `evidence` column feeds the empty `Outcome` context section |
| `ConversationSummary` | Long conversations currently push history out of the token window with nothing to fall back on |
| `ConversationLink` | Cheap; relates conversations that share a subject |

### Step 3b — blocked on identity

| Table | Blocked because |
|---|---|
| `WorkspaceMember`, `Team`, `TeamMember`, `ProjectMember` | Membership rows that nothing enforces are decorative. `ChatTurnIdentity` still returns a hardcoded tenant and placeholder permissions |
| `AccessGrant` | Same, more so — it *is* the enforcement table |

**Do identity before 3b, not after.** Step 1 removes Dataverse's row-level security, so
after Step 1 there is no authorization anywhere in the system. Building the tables that
describe access, while nothing checks access, produces a schema that looks safe and isn't —
which is worse than an obviously absent feature.

Sequence within Step 3: **3a → identity → 3b.**

## Step 4 — Connect everything to the chatbot (phase 2)

Two halves, and the first is where the value is:

**Context.** Extend `ChatContextBundleMapper` to read the new aggregates — briefs, milestones,
summaries, criterion evidence. Per ADR-015 §6, add them **one field at a time and measure
each**. Nine mappings shipped together cannot be individually evaluated; two can. A field
that does not change which context is selected is a field that should not be in the prompt.

**Interface.** Project brief editing, milestone and criterion tracking, membership
management, conversation linking. Driven by F4's dead-end audit, which will have produced a
concrete list of places the UI has nowhere to go.

**Gate:** a project with a brief and milestones produces measurably different — and better —
answers than the same project without them, demonstrated through the citations panel.

---

# Beyond the four steps

## Phase 2 M2 — Intelligence and durable context

Everything stateful in Intelligence is currently in-memory: `InMemoryUsageMeter`,
`PermissiveQuotaPolicy`, `ConsoleAuditLog`, `InMemoryMemoryStore`, turn traces. Nothing
survives a restart, cost is not enforceable, and the Result Loop cannot exist until traces
and outcomes persist.

Scope: knowledge approval and retrieval quality; conversation summaries; context selection by
workspace/project/milestone; persistent memory separate from curated knowledge; ADR capture
and supersession; results, feedback and evaluation; multi-provider routing.

**Completion criteria:** Nexus can explain which context it used, retain an approved
decision, link advice to its actual result, and resume a project without pasting old
conversations.

## Phase 2 M4 — Developer agent

An agent that inspects a checked-out solution, proposes a bounded change, edits allowed
files, runs build and tests, and presents evidence for approval.

Safeguards: repository-scoped permissions; explicit plan and changed-file summary; no secret
exposure; human approval for deployment and destructive operations; build/test evidence;
recorded outcome linked to the task and artifact.

## Phase 2 M5 — Power Platform compiler

Compile a governed workbook or specification into Dataverse / Power Platform solution
components: tables, columns, relationships, keys, choices, roles, flows, business rules,
deployment config, validation, drift detection.

Note this is **unaffected by ADR-014**. Dataverse is being removed as the Chat product's
persistence; it remains a legitimate *target* for a tooling product. Deliver incrementally:
schema validation → table/column generation → relationships and choices → security →
automation → packaging → drift detection.

## Phase 2 M6 — Machine automation

Connect Nexus planning and knowledge to controlled industrial automation, beginning with
boring-machine retrofit research and measurement-assisted workflows.

Machine control is safety-critical. Deterministic controllers — PLC or LinuxCNC — own
real-time motion and interlocks. AI may plan, diagnose, document or propose parameters, and
must never bypass hard limits, emergency stops, operator approval, or validated control
logic.

## Phase 3 — Products and business integration

Separate products and clients built on the platform: the public chatbot/workspace product,
Vault by Nexus, PRT internal operational clients and ERP modules, developer and Power
Platform tooling, knowledge-capture and machine-assistance tools.

Scope one product vertical at a time and prove user value before broad platform expansion.
The three-solution split exists precisely so a second product can arrive without disturbing
the first.

---

## Immediate next actions

1. **Run SQL Stage 1b** — `SQL_PROMPTS_STAGE_1B_2A.md`. Acceptance check 5 is the one that
   matters: two successive workspace POSTs returning `WKS-00000001` and `WKS-00000002`.
2. Then 2a, 2b, 2c, then Stage 3.
3. Then F1 → F2 → F3 → F4, one prompt at a time.
4. Add OpenAI credit and close the three outstanding smoke-test verifications — citations
   populated, usage metering populated, assistant message persisted. These have been blocked
   since the V2.1 migration and are cheap to clear.
