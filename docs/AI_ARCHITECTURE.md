# AI Architecture

**Status:** MIXED, and the mix is the point — **the contracts and the turn pipeline exist, compile and
run; almost everything stateful behind them is in-memory and does not survive a restart.** Every
capability below carries one of four maturity bands
**Owner:** Durai
**Last updated:** 2026-08-21
**Layer:** 04 AI — repository `Nexus.Intelligence`, assemblies `Nexus.Intelligence.*`, schema `ai`
**Authoritative for:** the shape and boundaries of the AI layer — the project map, where the model
gateway lives and why, the context seam, the turn pipeline and its steps, planning, agents, memory,
the tool abstraction, result reports, usage, citations, provider abstraction, and the maturity of
each of those against the two gates.

**Not authoritative for:** how to build on the layer — prompt rules, context rules, tool rules, the
three invariants in full, guardrail practice, evaluation practice — `AI_DEVELOPMENT_STANDARDS.md`.
Provider credentials and the tool gateway themselves — CORE, `SECURITY_STANDARDS.md`. Conversation
storage — `EXPERIENCE_ARCHITECTURE.md`. Evaluation as a verification method —
`ASSURANCE_ARCHITECTURE.md`.

---

## 1. The four maturity bands

Every capability in this document carries one of four bands. They are used consistently and they are
not interchangeable with phases.

| Band | Means |
|---|---|
| **CURRENT** | Exists on disk today and works, with any caveat stated |
| **GATE A MINIMUM** | Required before business systems may begin. Small, deliberately |
| **GATE B** | Foundation Ready. Runs in parallel with business development and **must never block it** |
| **FUTURE** | P3 and beyond. Valuable, unscheduled against a gate |

The distinction that gets lost most often is between CURRENT and working. **A compiling interface is
not a working capability.** Most of this layer's shapes are right and most of its implementations are
placeholders — a defensible position, because the contracts were designed before the storage, but
only safe if nobody mistakes the two.

---

## 2. Naming — there is no rename

**Layer short name: AI. Technical assemblies: `Nexus.Intelligence.*`, unchanged.** Short architecture
names exist for human comprehension; stable technical names are not changed without technical value,
and there is none here — a rename would touch every namespace, using, package id and consuming
project to produce a system that behaves identically. **No rename work exists anywhere in the
roadmap**, and none should be proposed. Where this document says "AI" it means the layer; where it
says `Nexus.Intelligence.Core` it means the assembly.

---

## 3. Project map — CURRENT

`Nexus.Int`, remote `github.com/prtcare/Nexus-Int`, solution `Nexus.Int.slnx`, deployed at
`/intelligence/v1`.

| Project | Holds | Band |
|---|---|---|
| `Nexus.Intelligence.Contracts` | `Turns/` (17 types), `Context/`, `Results/`, `Client/IIntelligenceClient` | CURRENT |
| `Nexus.Intelligence.Core` | `Turns/` the pipeline and its nine steps, `Planning/Planner`, `Execution/ExecutionEngine` | CURRENT |
| `Nexus.Intelligence.Context` | `Ranking/` `KeywordContextRanker`, `IContextRanker`, `RankingOptions`, `RankedContextItem`; `Prompting/` `PromptAssembler`, `AssembledPrompt`, `PromptRequest`, `IPromptAssembler` | CURRENT |
| `Nexus.Intelligence.Memory` | `IMemoryStore`, `InMemoryMemoryStore`, `MemoryRecord`, `MemoryQuery`, `MemoryKind` | CURRENT, in-memory only |
| `Nexus.Intelligence.Agents` | `Abstractions/`, `AgentRegistry`, `AgentDispatcher`, `BuiltIn/DeveloperAgent.cs` | CURRENT, the agent is a **974-byte stub** |
| `Nexus.Intelligence.Api` | `Endpoints/` Turns, Plans, Results, Capabilities, Health, `TurnRequestValidation`; `Tooling/`; `ResultReports/`; `DependencyInjection/` | CURRENT |
| `Nexus.Intelligence.Evaluation` | — | **FUTURE.** The project does not exist |

Two test projects exist: `Nexus.Intelligence.Architecture.Tests` (`BoundaryRuleTests.cs`) and
`Nexus.Intelligence.Tests` (`Ranking/KeywordContextRankerTests.cs`) — the second being **one of only
two behaviour tests in the entire system.**

---

## 4. The model gateway lives in CORE

This is the layer's most consequential structural decision and it looks wrong at first reading: the
AI layer does not own model access.

| Component | Where | What |
|---|---|---|
| `IModelGateway`, `IModelCatalog`, `ModelDescriptor`, `ModelInvocation`, `ModelUsage` | `Nexus.Platform.Contracts/Models/` | The contracts — 13 types |
| `RoutingModelGateway`, `AggregatingModelCatalog`, `IModelCatalogSource`, `INamedModelGateway` | `Nexus.Platform.Core/Models/` | The routing implementation |
| `OpenAIModelGateway` and the OpenAI SDK | `Nexus.Platform.Providers.OpenAI` | The only working provider |
| `AnthropicModelGateway` | `Nexus.Platform.Providers.Anthropic` | **A 306-byte stub** |
| `ISecretResolver` | `Nexus.Platform.Contracts/Secrets/` | How a credential is obtained |

The reason is one invariant:

> **Provider credentials never leave CORE. There is no API key anywhere in `Nexus.Intelligence`.**

If the AI layer held credentials, every consumer of AI would transitively hold a path to them, and
the blast radius of any AI-layer defect would include the account. Keeping the gateway in CORE means
the AI layer holds an interface reference and nothing else — it can *ask for a completion* and cannot
*authenticate to a provider*. The second consequence is that **model access is available to layers
that are not AI**: a layer needing one completion does not take a dependency on reasoning, agents,
context and memory to get it.

**CURRENT caveat.** `set-openai-key.ps1` in NexusAI handles the OpenAI key today; `ISecretResolver`
exists as a contract and is not the live path. **GATE A MINIMUM — `M-01-5.1`** makes it real, and
`M-01-6.1` verifies the OpenAI path end to end.

---

## 5. Provider abstraction

Three layers of indirection, each earning its place:

```
IModelGateway            the contract consumers see
  └── RoutingModelGateway        selects a named gateway
        └── INamedModelGateway         one provider's implementation
              └── OpenAIModelGateway   the only one that works
```

`AggregatingModelCatalog` composes `IModelCatalogSource`s so that available models are discovered
rather than configured in one list that drifts. The rule: **a direct provider SDK call outside
`Nexus.Platform.Providers.<Vendor>` is forbidden.** `OpenAI.dll` and `System.ClientModel` are
referenced by exactly one project, and a second provider is a new project rather than a branch inside
an existing one.

| Band | State |
|---|---|
| CURRENT | One provider works. `AnthropicModelGateway` is a 306-byte stub |
| FUTURE | `M-01-6.2` multi-provider routing — Anthropic, Google, OpenRouter and DeepSeek are named as targets; **the order is not decided** |

`Azure.Identity` and `Azure.Core` are present in the solution but arrived with Dataverse. **Do not
treat them as a chosen authentication path** until something selects them.

---

## 6. The context seam — the best boundary in the system

### 6.1 `ContextBundle` and `ContextItem`

`Nexus.Intelligence.Contracts/Context/` holds seven types: `ContextBundle`, `ContextItem`,
`ContextItemKind`, `TrustLevel`, `Citation`, `PersistenceHint`, `PersistenceHintKind`.

The seam in one sentence:

> **Consumers flatten their own entities into `ContextItem` and hand over a `ContextBundle`. AI never
> sees the entity.**

DEVELOPER maps a milestone outcome to `Kind` `Objective` and a blocking dependency to `Kind`
`Constraint`. An ERP would map its own objects to the same small vocabulary. AI receives a list of
typed, trust-rated items and has no way to know — and no need to know — what produced them.

`TrustLevel` is on the item rather than on the bundle because trust varies within a single bundle. A
reviewed development result and a three-month-old note are both context and are not equally
believable. DEVELOPER derives trust from record age and review state; another consumer would derive
it differently; AI only consumes it.

`PersistenceHint` is how a consumer signals that something in a turn is worth remembering, without
either side owning the other's storage.

### 6.2 `ScopeRef` opacity

`ScopeRef` lives in `Nexus.Intelligence.Contracts/Turns/` and travels through the whole path — the
conversation, the engine, the turn, the model invocation — opaque at every stop.

> **AI never parses, branches on, or resolves a `ScopeRef`.**

Resolution belongs to the consumer, via `IScopeResolver` (`M-11-2.1`). AI receives the result.
`EXPERIENCE_ARCHITECTURE.md` §3 owns the mechanism; this document owns the obligation not to break it.

**This seam must not be broken to make any feature easier.** Every shortcut past it looks locally
reasonable — *the ranker would work better if it knew this was a milestone* — and each one converts a
reusable layer into one product's assistant. `BoundaryRuleTests.cs` enforces it, and the architecture
gate at `M-08-1.4` makes the enforcement mechanical.

| Band | State |
|---|---|
| CURRENT | The seam **works and is tested** — `ChatContextBundleMapperTests.cs`, `KeywordContextRankerTests.cs` |
| GATE A MINIMUM | Minimum context handling for development assistance. The seam already satisfies it |

---

## 7. The turn pipeline

`Nexus.Intelligence.Core/Turns/`. `TurnPipeline` composes nine steps in order:

| # | Step | Does | Band |
|---|---|---|---|
| 1 | `IntentClassifier` | What kind of request is this | CURRENT |
| 2 | `PolicyGate` | Is it permitted, under what constraints | CURRENT, **permissive** |
| 3 | `ContextSelector` | Which items from the bundle are relevant | CURRENT, keyword ranking |
| 4 | `AgentSelector` | Which agent, if any, handles it | CURRENT |
| 5 | `ModelSelector` | Which model | CURRENT, configuration-driven |
| 6 | `PromptStep` | Assemble the prompt via `PromptAssembler` | CURRENT |
| 7 | `ModelStep` | Invoke through `IModelGateway` | CURRENT |
| 8 | `ToolLoop` | Iterate tool calls | CURRENT shape, **no tool can run** |
| 9 | `ResponseComposer` | Reply, citations, decision trace, usage | CURRENT |

The pipeline is a real strength of the current system: it exists, it runs end to end, and it produces
a `DecisionTrace` alongside the answer. `IntelligenceTurnRequest` and `IntelligenceTurnResponse` are
the boundary types; `TurnConstraints`, `ActorRef`, `ReplyPayload`, `UsageSummary`, `TurnError` and
`TurnInput` are the supporting shapes, all in `Contracts/Turns/`.

Three caveats that matter more than the structure:

**`PolicyGate` is permissive.** `PermissiveQuotaPolicy` in `Nexus.Platform.Core/Governance/` enforces
nothing. The shape of enforcement exists; the enforcement does not. `M-01-3.1` and the quota work
behind it close it.

**`ToolLoop` iterates over nothing.** `EmptyToolCatalog` and `EmptyToolGateway` in
`Nexus.Intelligence.Api/Tooling/` mean no tool can be invoked — §10.

**Step ordering is architecture.** `PolicyGate` runs at position 2, before context selection and
before any model call, because a request that is not permitted must be refused before it costs money
or touches data. Moving it later would be a security change disguised as an optimisation.

`TurnTrace` records what each step decided. **CURRENT: `InMemoryTurnTraceStore` — lost on restart.**
**GATE A MINIMUM — `M-04-1.1`** makes it durable, retrievable by turn id after a host restart, with a
retention window, and demotes the in-memory store to a test double.

---

## 8. Planning and execution

`Nexus.Intelligence.Core/Planning/Planner` and `Core/Execution/ExecutionEngine`, with `PlanStep` and
`ProposedAction` in `Contracts/Turns/` and a `Plans` endpoint in the API.

`Planner` decomposes a request into `PlanStep`s. `ExecutionEngine` runs what may be run. The
distinction that governs both:

> **An irreversible or external effect is not executed. It becomes a `ProposedAction`.**

`ProposedAction` is a contract type rather than a convention, which makes the refusal to act
structural. An agent that wants to delete, send or spend produces a proposal, and a human decides —
the same shape as DEVELOPER requiring a recorded `Review` before integration
(`DEVELOPER_ARCHITECTURE.md` §13) and ASSURANCE forbidding an agent from touching a safety-critical
criterion (`ASSURANCE_ARCHITECTURE.md` §12). Three layers, one principle.

| Band | State |
|---|---|
| CURRENT | `Planner`, `ExecutionEngine`, `PlanStep`, `ProposedAction` and the `Plans` endpoint exist |
| FUTURE | `M-04-3.2` gives agents governed tool access, at which point the proposal boundary starts being exercised |

---

## 9. Agents

`Nexus.Intelligence.Agents`. `Abstractions/` holds `IAgent`, `IAgentRegistry`, `IAgentDispatcher`,
`IAgentRuntime`, `AgentContext`, `AgentMetadata`, `AgentType`, `AgentResult`. `AgentRegistry` and
`AgentDispatcher` are real. `BuiltIn/DeveloperAgent.cs` is **974 bytes** — a stub.

`DeveloperAgent` is the layer's GATE A contribution and it is small on purpose:

**GATE A MINIMUM — `M-04-3.1`.** The agent answers which work items are safe to run together, given a
bundle, and cites the dependency records it reasoned from. Its acceptance criterion is the seam
restated: **it receives only `ContextItem`s and holds no reference to any DEVELOPER type.**

That criterion is what makes the agent architecturally interesting rather than merely useful. The
parallel-safety answer is computed by `Nexus.Developer.Graph` from declared data (`M-07-2.2`); the
agent *explains* it against a bundle. AI reasons about a flattened view; DEVELOPER decides. Reversing
those roles would put scheduling logic inside a model call, where it cannot be unit-tested and cannot
be relied upon.

Every agent registers capability metadata so `AgentSelector` can choose without a lookup table of
names.

---

## 10. Tools

| Layer | Piece |
|---|---|
| CORE, `Nexus.Platform.Contracts/Tools/` | `IToolCatalog`, `IToolGateway`, `ToolDescriptor`, `ToolInvocation`, `ToolResult`, `SideEffectClass` |
| AI, `Nexus.Intelligence.Api/Tooling/` | `EmptyToolCatalog`, `EmptyToolGateway` |

The abstraction lives in CORE for the same reason the model gateway does: tools have side effects,
side effects need governance, and governance is not the AI layer's to grant itself. `SideEffectClass`
is the type that carries it — a tool declares what class of effect it has, and the policy that
permits or refuses is evaluated outside the reasoning that wants to call it.

**A tool called outside `IToolGateway` is forbidden.** There is no second path.

| Band | State |
|---|---|
| CURRENT | **No tool can run.** `EmptyToolCatalog` / `EmptyToolGateway` are the registered implementations |
| FUTURE | `M-01-7.1` tool registry and invocation; `M-04-3.2` agents invoking tools, with every invocation audited |

---

## 11. Memory

`Nexus.Intelligence.Memory`: `IMemoryStore`, `InMemoryMemoryStore`, `MemoryRecord`, `MemoryQuery`,
`MemoryKind`.

| Band | State |
|---|---|
| CURRENT | In-memory. **Nothing survives a restart** |
| **GATE B — `M-04-1.2`** | SQL-backed `IMemoryStore`. A memory written in one session is retrievable in the next, scoped by tenant and `MemoryKind`. `InMemoryMemoryStore` becomes a test double |

**Durable memory is explicitly not in GATE A**, and it was moved out in v2.2. The reasoning is worth
keeping, because "the AI should remember things" is an intuitive requirement to smuggle forward:
GATE A's AI need is *a working model gateway, AI callable from DEVELOPER, and minimum context
handling*. A DEVELOPER agent answering a parallel-safety question does so from the bundle it was
handed on this turn. It does not need to recall last week — the work graph is the durable memory of
development, and it is DEVELOPER's, persisted in SQL, and queryable.

`M-04-1.2` is `parallel_safe: false` and one of the seven milestones that close GATE B. Its **tenant
scoping on every read path** is a work-item-level requirement (`T-04-1.2.1.2`), not a later hardening
pass: memory is the one AI store that accumulates across sessions, which makes it the one where a
missing tenant filter leaks data that was never on the current request.

---

## 12. Result reports and the result loop

`Contracts/Results/` holds `ResultReport` and `ResultOutcome`. `Nexus.Intelligence.Api/ResultReports/`
holds `IResultReportStore` and `InMemoryResultReportStore`, with a `Results` endpoint.

A `ResultReport` records what an AI-produced action actually achieved. It is one of four distinct
verdict types — `WorkflowResult` (05), `ResultReport` (04), `DevelopmentResult` (07),
`QualificationResult` (09) — never merged into a shared `Result` type, because four different
questions produce four different verdicts.

| Band | State |
|---|---|
| CURRENT | In-memory. Lost on restart |
| **GATE A MINIMUM — `M-04-1.1`** | Durable, retrievable by id after a restart |
| FUTURE — `M-04-5.3`, P5 | The **result loop**: a proposed action is traceable to the development result it produced |

`M-04-5.3` is what makes self-improvement possible rather than performative. Without it, Nexus can
propose work (`M-07-9.1`) but cannot know whether its previous proposals helped — and a system that
proposes at scale without measuring outcomes is generating volume, not value.

---

## 13. Usage, cost and quota

| Piece | Where | State |
|---|---|---|
| `IUsageMeter`, `UsageRecord`, `IQuotaPolicy`, `QuotaVerdict` | `Nexus.Platform.Contracts/Governance/` | CURRENT contracts |
| `InMemoryUsageMeter`, `PermissiveQuotaPolicy` | `Nexus.Platform.Core/Governance/` | CURRENT, **enforce nothing, survive nothing** |
| `ModelUsage`, `UsageSummary` | Platform contracts, `Contracts/Turns/` | CURRENT, per-turn shape exists |

| Band | Milestone |
|---|---|
| GATE B | `M-01-4.2` durable usage metering; `M-04-4.1` per-turn cost attribution to tenant, product and work item |
| FUTURE | `M-04-4.2` capability and cost-aware routing; `M-07-3.3` model assignment per run; `M-10-4.1` cost monitoring and anomaly alerting |

Usage metering was moved out of GATE A in v2.2. The chain that makes cost meaningful runs
`M-01-4.2` → `M-04-4.1` → `M-10-4.1` and ends in OPERATIONS rather than here, because a spend figure
nobody is alerted about is accounting rather than control — `OPERATIONS_ARCHITECTURE.md` §7.

---

## 14. Citations

`Citation` is in `Contracts/Context/`. The frontend already renders it — `CitationsPanel.tsx`,
`citationTargets.ts`, `useCitationTarget.ts` in `Nexus.Web.Client/src/features/chat/`.

**CURRENT: built end to end, and never proven against a live model.**

That gap is more serious than it sounds, and `M-04-2.1` exists to close it in P0 rather than later:

> Until citations render and are proven, every intelligence change — ranking weights, trust levels,
> prompt section order — is **unfalsifiable**.

Swagger can confirm an endpoint returned 200. It cannot confirm the context was good. Citations are
the only mechanism in the system that connects an answer back to the items that produced it, which
makes them the observability of the reasoning path, not a UI nicety.

**GATE A MINIMUM — `M-04-2.1`.** A live turn returns citations that resolve to real context items;
the panel renders them in a browser; a context item removed from the bundle disappears from the
citations. The third criterion is the actual test — it proves the citation is derived rather than
decorative. The milestone also records a **baseline answer set** for later ranking comparison, which
is the first artefact of §16.

It follows that **an AI-generated answer is never presented without its citations.**

---

## 15. Retrieval

| Band | State |
|---|---|
| CURRENT | `KeywordContextRanker` over the supplied bundle. Works, and is tested |
| FUTURE | `M-02-4.1` full-text and structured search; `M-02-4.2` embeddings and vector retrieval; `M-02-4.3` RAG orchestration |

**RAG does not exist and is not on the GATE A path.** The distinction that makes that acceptable:
today, context is *supplied* by the consumer, not *retrieved* by AI. Retrieval becomes necessary when
the relevant context cannot be enumerated by the consumer — a DATA capability first (`M-02-4.x`) and
an AI capability second. **No vector store technology has been selected**; `M-02-4.2` decides it.

---

## 16. Evaluation and guardrails

| Band | Milestone | Delivers |
|---|---|---|
| FUTURE | `M-04-2.2` | Prompt versioning — two versions comparable on the same question set |
| FUTURE | `M-04-5.1` | Evaluation harness — a ranking, prompt or model change scored against a fixed question set; a quality regression **fails a pipeline** |
| FUTURE | `M-04-5.2` | Guardrails — a structured-output contract violation is rejected and retried, not surfaced |
| FUTURE | `M-09-6.1` | Evaluation **as a verification method**: an evaluation run writes ASSURANCE `Evidence` against an acceptance criterion |

`M-09-6.1` is the important one architecturally, because it refuses to create a parallel quality
regime for AI. An evaluation score is `Evidence`; a minimum score is an `AcceptanceCriterion`; a drop
below it fails the same quality gate a failing unit test fails — `ASSURANCE_ARCHITECTURE.md` §7.

Two rules from `AI_DEVELOPMENT_STANDARDS.md` §13 that constrain the architecture rather than the
practice: **no ranking or prompt change without a way to measure it**, and **no threshold chosen after
seeing the score.** The second is why `M-04-2.1` records a baseline before there is anything to
compare it to.

Where the evaluation question set lives is **not yet decided**; DATA is the likely home.

---

## 17. The maturity summary

| Capability | CURRENT | GATE A MIN | GATE B | FUTURE |
|---|---|---|---|---|
| Model gateway, routing abstraction | Works, one provider | `M-01-5.1`, `M-01-6.1` | | `M-01-6.2` |
| Turn pipeline | Works end to end | | | |
| Context seam and mapper | Works, tested | ✓ satisfied | | |
| Keyword ranking | Works, tested | ✓ satisfied | | `M-02-4.2` |
| Prompt assembly | Works | | | `M-04-2.2` |
| Citations | Built, **unproven live** | `M-04-2.1` | | |
| Turn traces | In-memory | `M-04-1.1` | | |
| Result reports | In-memory | `M-04-1.1` | | `M-04-5.3` |
| `DeveloperAgent` | 974-byte stub | `M-04-3.1` | | |
| Memory | In-memory | — | `M-04-1.2` | |
| Usage metering | In-memory | — | `M-01-4.2`, `M-04-4.1` | `M-10-4.1` |
| Quota enforcement | `PermissiveQuotaPolicy` | — | `M-01-3.1` + quota work | |
| Tools | **None can run** | — | — | `M-01-7.1`, `M-04-3.2` |
| Second provider | 306-byte stub | — | — | `M-01-6.2` |
| Retrieval / RAG | **Does not exist** | — | — | `M-02-4.1`→`4.3` |
| Guardrails | **Does not exist** | — | — | `M-04-5.2` |
| Evaluation | **Does not exist** | — | — | `M-04-5.1`, `M-09-6.1` |
| Cross-host correlation of a turn | **No logging library selected** | `M-10-1.1` | | |

GATE A takes three things from this layer and nothing else: **a working model gateway, AI callable
from DEVELOPER, and minimum context handling.** Three milestones deliver them — `M-04-1.1`,
`M-04-2.1`, `M-04-3.1` — supported by `M-01-5.1` and `M-01-6.1` in CORE.

The GATE B rule applies without exception: durable memory, usage metering and cost attribution run in
parallel with business development and **must never pause or block it.** A business system waiting for
durable AI memory is a scheduling error, not a dependency.

---

## 18. What must never happen

Restated here only because this document is where someone designing an AI feature will be. The
authoritative list is `AI_DEVELOPMENT_STANDARDS.md` §16.

| Never | Why |
|---|---|
| A product type in `Nexus.Intelligence.Contracts` | No shared kernel. Currently true — keep it true |
| AI parsing or branching on a `ScopeRef` | §6.2 |
| An API key anywhere in `Nexus.Intelligence` | §4 |
| A provider SDK call outside `Nexus.Platform.Providers.<Vendor>` | §5 |
| A tool called outside `IToolGateway` | §10 |
| An irreversible or external effect performed by an agent | It becomes a `ProposedAction` — §8 |
| A prompt body, completion, token or secret in a log line | Absolute — `M-10-1.1` redaction policy |
| An agent creating, modifying or waiving a safety-critical criterion | Absolute — `M-09-7.2` |
| An AI answer presented without its citations | §14 |

---

## 19. Boundaries with the sibling layers

| Layer | The seam |
|---|---|
| 01 CORE | Owns the model gateway, the tool gateway, secrets, audit and usage. AI holds interfaces only |
| 02 DATA | Owns documents, knowledge, retrieval. AI consumes; it does not store knowledge |
| 07 DEVELOPER | Flattens its graph into `ContextItem`s. `DeveloperAgent` holds no DEVELOPER type |
| 09 ASSURANCE | Evaluation runs produce `Evidence` against an `AcceptanceCriterion` |
| 10 OPERATIONS | Tracing attributes turn latency per pipeline step; per-turn cost feeds `CostRecord` |
| 11 EXPERIENCE | Owns conversation storage. Passes the `ContextBundle` through untouched |

---

## 20. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Vector store technology | `M-02-4.2` | **Not yet decided** — NOT SELECTED |
| Which providers beyond OpenAI, and in what order | `M-01-6.2` names four targets; the order is open | Not yet decided |
| Whether local models are ever hosted | No compute environment exists — `M-08-4.1` first | Not yet decided |
| Where the evaluation question set lives | `M-04-5.1`; DATA is the likely home | Not yet decided |
| Whether turn traces live in the `ai` schema or a time-series store | Volume, after `M-10-2.2` | Not yet decided |
| Prompt storage format and diffing | `M-04-2.2` | Not yet decided |
| Whether `Azure.Identity` becomes the chosen credential path | It arrived with Dataverse; nothing selected it | Not yet decided |

---

## 21. References

- `AI_DEVELOPMENT_STANDARDS.md` — the three invariants in full, provider rules, prompt rules, context
  rules, tools, agents, structured output, guardrails, evaluation practice, citations, and §16 in
  full.
- `EXPERIENCE_ARCHITECTURE.md` — `IScopeResolver`, scope kind registration, the untouched handoff.
- `DEVELOPER_ARCHITECTURE.md` — the work graph `DeveloperAgent` reasons about; §16 autonomy levels.
- `ASSURANCE_ARCHITECTURE.md` — evaluation as a verification method; the safety carve-out.
- `OPERATIONS_ARCHITECTURE.md` — correlation, tracing per pipeline step, cost monitoring.
- `SECURITY_STANDARDS.md` — AI permissions, `TrustLevel`, `SideEffectClass`, logging prohibitions.
- `DATA_OWNERSHIP.md` — §4 the AI entity list, §6 the four distinct `Result` types.
- `TECHNOLOGY_STACK.md` — what is confirmed in use and what is recorded as NOT SELECTED.
