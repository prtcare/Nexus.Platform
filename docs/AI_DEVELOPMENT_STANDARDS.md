# AI Development Standards

**Status:** CURRENT for the contracts and the pipeline, which exist and compile; **TARGET for almost
everything stateful, every guardrail and every evaluation** — each marked with its milestone
**Owner:** AI (Layer 04)
**Last updated:** 2026-08-21
**Layer:** 04 AI — `Nexus.Intelligence`
**Authoritative for:** how AI capability is built in Nexus — provider abstraction, model selection,
prompts and their versioning, context, memory, retrieval, tools and tool permission, agents,
structured output, usage and cost, evaluation, guardrails, fallback, timeout, retry, citations, and
the verification of AI results.

Not authoritative for: who may invoke what — `SECURITY_STANDARDS.md` §9 and §10; what counts as
evidence — `ASSURANCE_STANDARDS.md` §13; the HTTP shape of the Intelligence API —
`API_STANDARDS.md`; where the repository sits and what is in it — `REPOSITORY_STRUCTURE.md` §3.3;
the mechanical procedure for adding an agent — `NEW_MODULE_GUIDE.md` §7.

---

## 1. The three invariants

Everything else is negotiable. These are not.

> **1. AI never sees product structure.**
> Intelligence receives a `ContextBundle` of flattened `ContextItem` values. It does not know what a
> `Workspace` is, what a `Milestone` is, or that a Chat product exists. Any change that teaches it is
> a defect regardless of what it makes easier.

> **2. `ScopeRef` is opaque.**
> A consumer passes a `ScopeRef`; Intelligence carries it through and hands it back. Intelligence
> never parses it, never branches on it, and never resolves it. Resolution belongs to the consumer.

> **3. Provider credentials never leave CORE.**
> `Nexus.Intelligence` holds no API key, ever. It calls `IModelGateway`. The credential lives with
> the gateway implementation in `Nexus.Platform.Providers.*`, which is layer 01.

This seam is described in the architecture as the single best-designed boundary in the system, and
it is currently true. **Keeping it true is a standard, not an aspiration** — it is enforced by
`BoundaryRuleTests.cs` in `Nexus.Intelligence.Architecture.Tests`, and any new rule stated in this
document should end up there.

The invariants exist because of what they buy: **AI improvements apply to every consumer at once.**
The moment Intelligence knows about one product, improving it for the second means changing it for
the first.

---

## 2. Provider abstraction

Layer 01 CORE owns model access. `Nexus.Platform.Contracts/Models/` holds 13 types; the ones that
matter for every AI change:

| Type | Project | Role |
|---|---|---|
| `IModelGateway` | `Nexus.Platform.Contracts` | The one way a model is invoked. Everything goes through it |
| `INamedModelGateway` | `Nexus.Platform.Core` | A gateway that identifies which provider it is |
| `RoutingModelGateway` | `Nexus.Platform.Core/Models/` | Selects among named gateways and delegates |
| `IModelCatalog` / `AggregatingModelCatalog` | Contracts / Core | What models exist, assembled from sources |
| `IModelCatalogSource` | `Nexus.Platform.Core` | One provider's contribution to the catalogue |
| `ModelDescriptor` | Contracts | A model's identity and capabilities |
| `ModelInvocation` | Contracts | One call |
| `ModelUsage` | Contracts | What one call consumed |

Rules:

1. **Never call a provider SDK directly.** `OpenAI.dll` is referenced by
   `Nexus.Platform.Providers.OpenAI` and by nothing else. A direct SDK call anywhere else is a
   boundary violation even when it works.
2. **A new provider is a new `Nexus.Platform.Providers.<Vendor>` project** implementing
   `INamedModelGateway` and contributing an `IModelCatalogSource`. Nothing above layer 01 changes.
3. **`RoutingModelGateway` already proves the abstraction with one provider.** That is the point of
   it: the abstraction was validated before a second provider justified it.
4. **Credentials are resolved, not passed.** **TARGET — M-01-5.1** via `ISecretResolver`;
   **CURRENT** via `set-openai-key.ps1`.

**CURRENT.** OpenAI is the only working provider. `Providers.Anthropic/AnthropicModelGateway.cs` is
a **306-byte stub**. Multi-provider routing is **M-01-6.2**, whose acceptance criteria are that
Anthropic, Google, OpenRouter and DeepSeek gateways implement `INamedModelGateway`, that a routing
rule selects a cheaper model for a low-latency-class request, and that **provider failure falls back
without failing the turn**.

---

## 3. The turn pipeline

`Nexus.Intelligence.Core/Turns/` — the sequence every request travels. Knowing it is how you find
where a change belongs.

```
IntelligenceTurnRequest  (ContextBundle, TurnConstraints, ActorRef, ScopeRef, TurnInput)
        │
   IntentClassifier      what is being asked
   PolicyGate            is it permitted
   ContextSelector       which context items are used
   AgentSelector         which agent handles it
   ModelSelector         which model
   PromptStep            assemble the prompt
   ModelStep             invoke through IModelGateway
   ToolLoop              tool calls, if any
   ResponseComposer      reply, citations, plan, proposed actions, trace
        │
IntelligenceTurnResponse (ReplyPayload, Citation[], PlanStep[], ProposedAction[],
                          DecisionTrace, UsageSummary, TurnError?)
```

`TurnPipeline` orchestrates. `InMemoryTurnTraceStore` records the trace — **in memory, lost on
restart**. `TurnRequestValidation` in `Nexus.Intelligence.Api/Endpoints/` validates at the edge.

| Rule | Statement |
|---|---|
| One place per concern | A change to model choice goes in `ModelSelector`, not into `PromptStep` because it was closer |
| The pipeline is the API | Endpoints (`Turns`, `Plans`, `Results`, `Capabilities`, `Health`) are thin over it |
| Every turn produces a `DecisionTrace` | Why this context, this agent, this model. Without it no AI behaviour is explicable |
| A turn never throws to the caller | Failures become `TurnError` in the response — `API_STANDARDS.md` §7 |

> **TARGET — M-04-1.1 durable Intelligence stores.** `InMemoryTurnTraceStore` and
> `InMemoryResultReportStore` are placeholders. Without durable traces there is no result loop, and
> nothing can explain why a decision was made after the process restarts.

---

## 4. Model selection

`ModelSelector` decides which model serves a turn, using `TurnConstraints` and the `IModelCatalog`.

1. **Selection is a pipeline step, not a caller's choice.** A consumer expresses constraints —
   latency class, cost class, capability needed — not a model name.
2. **Never hard-code a model name outside the catalogue and the selector.**
3. **The selection is recorded in `DecisionTrace`.** "Why did it use that model" must be answerable.
4. **Selection never branches on product identity.** It cannot: it does not know one.

**TARGET.** Cost- and latency-aware routing arrives with **M-01-6.2** (multi-provider routing) and
becomes measurable with **M-04-4.1** (per-turn cost attribution). A `ModelRoute` record makes the
rules data rather than code.

---

## 5. Context — the seam

`Nexus.Intelligence.Contracts/Context/`:

| Type | Meaning |
|---|---|
| `ContextBundle` | Everything the consumer decided is relevant. The whole input surface |
| `ContextItem` | One flattened piece: id, kind, body, trust, when, author, relevance hint |
| `ContextItemKind` | What sort of thing it is — never which product it came from |
| `TrustLevel` | How much the content may be relied on |
| `Citation` | Which context item produced which part of the answer |
| `PersistenceHint` / `PersistenceHintKind` | Whether this content may be retained |

**How a consumer participates:** it flattens its own aggregates into `ContextItem` values and hands
over a `ContextBundle`. `ChatContextBundleMapper` in `Nexus.Experience` is the reference implementation and
is one of exactly two things in the entire system with a behaviour test
(`Chat/ChatContextBundleMapperTests.cs`).

Rules:

1. **The mapper belongs to the consumer, never to Intelligence.** Intelligence gaining a mapper for
   a product is the invariant breaking.
2. **Add mappings one field at a time and measure each.** Nine mappings shipped together cannot be
   individually evaluated; two can. A field that does not change which context is selected is a
   field that should not be in the prompt.
3. **`ContextSelector` chooses from what it was given.** It does not fetch. (RAG changes this — §7 —
   and that is precisely why RAG is a milestone rather than an enhancement.)
4. **Ranking is `Nexus.Intelligence.Context/Ranking/`** — `IContextRanker`,
   `KeywordContextRanker`, `RankingOptions`, `RankedContextItem`. `KeywordContextRankerTests.cs` is
   the other of the two behaviour tests. **Do not change ranking without a way to measure the
   change** — §13.

### 5.1 TrustLevel is the prompt-injection defence

`TrustLevel` on `ContextItem` is not metadata. It is the boundary between *content* and
*instruction*.

> Content retrieved from a document, a web page, another user's message, a supplier PDF or a
> maintenance note is **untrusted**. Untrusted content is **data to be reasoned about, never
> instructions to be followed.**

An agent that treats retrieved text as an instruction has been compromised by whoever wrote that
text. The mechanism only works if every prompt-assembling and every agent path honours it —
`SECURITY_STANDARDS.md` §9.

---

## 6. Prompts

`Nexus.Intelligence.Context/Prompting/` — `IPromptAssembler`, `PromptAssembler`, `PromptRequest`,
`AssembledPrompt`. `PromptStep` in the pipeline calls it.

1. **All prompt construction goes through `PromptAssembler`.** No string concatenation in an agent,
   an endpoint or a product.
2. **A prompt is assembled from parts** — instruction, constraints, context, input — with the
   section order a property of the assembler, not of the caller.
3. **Untrusted context is clearly delimited** from instructions in the assembled prompt. §5.1.
4. **A prompt never contains a secret**, and **a prompt body is never logged** — that is an absolute
   prohibition, an acceptance criterion of **M-10-1.1**, and it applies while debugging too.
5. **`AssembledPrompt` is what was actually sent.** It belongs in the trace, subject to the logging
   prohibition — the trace store and the log store have different access controls.

### 6.1 Prompt versioning — TARGET

> **TARGET — M-04-2.2 Prompt versioning.** `Prompt` and `PromptVersion` become records, and the
> acceptance criterion is that **two prompt versions can be compared on the same question set.**

**CURRENT: a prompt change is an edit.** It has no version, no comparison and no way to know whether
it helped. Until M-04-2.2:

- A prompt change is its own commit with its own rationale.
- The commit says what was expected to improve.
- The change is small enough to attribute an effect to.

M-04-2.2 depends on **M-04-2.1 Citations proven against a live model**, and M-04-5.1 evaluation
depends on M-04-2.2. That chain is the whole reason citations are a **P0 exit criterion**: until you
can see which context produced an answer, every subsequent AI change is unfalsifiable.

---

## 7. Memory and retrieval

### 7.1 Memory

`Nexus.Intelligence.Memory/` — `IMemoryStore`, `InMemoryMemoryStore`, `MemoryRecord`, `MemoryQuery`,
`MemoryKind`.

**CURRENT: memory is in-process and does not survive a restart.** Code written against
`IMemoryStore` is correct; code that assumes memory persists is not.

> **TARGET — M-04-1.2 Durable memory.** Acceptance: a memory written in one session is retrievable
> in the next; memory is scoped by tenant and by `MemoryKind`; and `InMemoryMemoryStore` **remains
> only as a test double**.

Rules: always go through `IMemoryStore`; always scope by tenant; use `MemoryKind` rather than
inventing conventions inside the body; and keep memory distinct from curated knowledge — knowledge
is DATA's `KnowledgeItem` with an approval lifecycle and provenance, memory is what this actor said
and did.

### 7.2 Retrieval and RAG — TARGET

**CURRENT: there is no retrieval.** No embeddings, no vector store (**NOT SELECTED** —
`TECHNOLOGY_STACK.md` §7), no index. Context comes entirely from what the consumer supplied, ranked
by `KeywordContextRanker`.

| Milestone | Brings | Acceptance |
|---|---|---|
| **M-02-4.1** Full-text and structured search | An index | Term search returns ranked results with source links |
| **M-02-4.2** Embeddings and vector retrieval | Semantic retrieval | A semantically related document is retrieved where keyword ranking returns nothing; quality is **compared against `KeywordContextRanker` on a fixed question set** |
| **M-02-4.3** RAG orchestration | Intelligence assembling context from retrieval | **A turn cites a document neither the product nor the user supplied** |

**M-02-4.3 changes the seam and must not break it.** Once Intelligence assembles context itself, it
retrieves from DATA — documents and knowledge — and never from a product's own store. Retrieved
content carries a `TrustLevel` and a `Citation`, exactly as supplied content does. Retrieval is not
a licence to learn product structure.

**Keyword ranking is deliberately sufficient until then**: until there is enough content for
retrieval quality to be measurable, a vector store is a technology decision made without evidence.

---

## 8. Tools

`Nexus.Platform.Contracts/Tools/` — `IToolCatalog`, `IToolGateway`, `ToolDescriptor`,
`ToolInvocation`, `ToolResult`, `SideEffectClass`.

**CURRENT: no tool can be invoked.** `Nexus.Intelligence.Api/Tooling/` contains `EmptyToolCatalog`
and `EmptyToolGateway`; `Nexus.Platform.Tools/ToolProvider.cs` is a **231-byte stub**. `ToolLoop`
exists in the pipeline and has nothing to loop over. **This is the only reason the absence of tool
permissions is not currently an exposure.**

> **TARGET — M-01-7.1 Tool registry and invocation.** Acceptance: `SideEffectClass` gates whether a
> tool requires approval before execution, and every invocation is audited with arguments and
> outcome. **Its permission model must land with it, not after.**

`SideEffectClass` answers one question before a tool runs: *what can this do that cannot be undone?*

| Class | Requirement |
|---|---|
| Read-only | Permission check |
| Reversible write | Permission check, audited |
| Irreversible write | Permission check, audited, **explicit approval** |
| External effect | Permission check, audited, **explicit human approval** |

Rules — `SECURITY_STANDARDS.md` §10 owns them; the AI-side consequences:

1. **A tool is invoked only through `IToolGateway`, from `IToolCatalog`.** Never directly, never by
   an agent holding a reference to an implementation.
2. **Every tool declares its `SideEffectClass` before registration.** No default.
3. **Every invocation is bounded** — a timeout and a bounded result size.
4. **The AI acts as the user and can never exceed the user.** A turn carries the user's identity and
   permissions, never elevated ones. If the user cannot read a document, no prompt makes it visible.
5. **Anything irreversible or external becomes a `ProposedAction`, not a tool call.** AUTOMATION
   executes proposals under policy, with an approval gate — **M-05-5.1**. This is the mechanism that
   makes the machine-domain rules in `MACHINE_DEVELOPMENT_GUIDE.md` §1 enforceable rather than
   aspirational.

---

## 9. Agents

`Nexus.Intelligence.Agents/` — `Abstractions/` holds `IAgent`, `IAgentRegistry`, `IAgentDispatcher`,
`IAgentRuntime`, `AgentContext`, `AgentMetadata`, `AgentType`, `AgentResult`; `AgentRegistry` and
`AgentDispatcher` sit beside it; `BuiltIn/DeveloperAgent.cs` is a **974-byte stub**.

The procedure for adding one is `NEW_MODULE_GUIDE.md` §7. The standards:

1. **An agent reasons; it does not act.** Effects become `ProposedAction` values.
2. **An agent receives context only through `AgentContext`.** It never queries a database, calls an
   endpoint or reaches into a product. This is invariant 1 at the agent level.
3. **Registered and dispatched, never constructed.** `AgentRegistry` holds them, `AgentSelector`
   chooses, `AgentDispatcher` runs.
4. **`AgentMetadata` declares capabilities honestly.** `AgentSelector` selects on the declaration; an
   over-claimed capability produces a silently wrong agent choice.
5. **Honour `TrustLevel`** — §5.1.
6. **The reasoning goes into `DecisionTrace`.** An agent whose reasoning is not recorded cannot be
   evaluated or debugged.
7. **Agents are proven by evaluation, not by unit test** — §13.

> **TARGET — M-04-3.1 Developer agent.** `DeveloperAgent` stops being a stub and answers questions
> about work, dependencies and parallel safety **using supplied context**. It is the pattern every
> later agent follows, and "using supplied context" is the load-bearing phrase: the temptation will
> be to let it read the work graph directly.

---

## 10. Structured output

1. **A structured response is a contract**, defined as a record in
   `Nexus.Intelligence.Contracts` — `ReplyPayload`, `PlanStep`, `ProposedAction` are the existing
   shapes.
2. **Model output is parsed and validated before it becomes a response.** Model output is untrusted
   input; a JSON blob that happens to deserialise is not a validated contract.
3. **A contract violation is retried, not surfaced.** That is the exact acceptance criterion of
   **M-04-5.2 Guardrails and output validation**: *a structured-output contract violation is
   rejected and retried, not surfaced.*
4. **Retries are bounded** and count toward the turn's usage and cost. A silent retry loop is a
   silent cost loop.
5. **Never `dynamic`, never a raw `string` passed up the stack** — `CSHARP_STANDARDS.md`.

**CURRENT: nothing validates model output.** `ResponseComposer` assembles; it does not adjudicate.

---

## 11. Usage and cost

| Type | Where | Role |
|---|---|---|
| `ModelUsage` | `Nexus.Platform.Contracts/Models/` | What one invocation consumed |
| `UsageSummary` | `Nexus.Intelligence.Contracts/Turns/` | What one turn consumed |
| `UsageRecord`, `IUsageMeter` | `Nexus.Platform.Contracts/Governance/` | The durable record |
| `IQuotaPolicy`, `QuotaVerdict` | `Nexus.Platform.Contracts/Governance/` | Whether it is allowed |

**CURRENT: `InMemoryUsageMeter` loses everything on restart, and `PermissiveQuotaPolicy` enforces
nothing.** Cost is therefore not enforceable today, and any statement about spend is an estimate.

Rules: every turn carries a `UsageSummary`; every invocation reports `ModelUsage`; usage is
attributed to actor and tenant; quota is checked through `IQuotaPolicy` even while the policy is
permissive — **the call site is what matters, because a permissive implementation is replaceable and
a missing call site is a rewrite.**

**TARGET** — **M-01-4.2** durable usage metering, **M-04-4.1** per-turn cost attribution,
**M-10-4.1** cost monitoring.

---

## 12. Guardrails, fallback, timeout and retry

| Concern | CURRENT | TARGET |
|---|---|---|
| **Guardrails** | None. Nothing validates or constrains model output | **M-04-5.2** |
| **Fallback** | None. A provider failure fails the turn | **M-01-6.2** — *provider failure falls back without failing the turn* |
| **Timeout** | Every model and tool invocation must be bounded — `CODE_CONVENTIONS.md` §13 | Unchanged |
| **Retry** | Bounded, with backoff, only on transient failures — `CODE_CONVENTIONS.md` §14 | Structured-output retry under M-04-5.2 |
| **Cancellation** | `CancellationToken` on every async path — `CSHARP_STANDARDS.md` §5 | Unchanged |

Rules that apply now, before the milestones:

1. **No unbounded model call.** A model call with no timeout is a request that can hang a turn
   indefinitely, and the caller has a user waiting.
2. **Retry only what is transient.** A malformed structured output is not transient in the same
   sense as a 503; retrying it without changing anything repeats the cost and the failure.
3. **Retries are visible** in `UsageSummary` and `DecisionTrace`.
4. **A failed turn returns `TurnError`**, not an exception and not a plausible-looking empty answer.
   An AI system that fails by inventing is worse than one that fails loudly.
5. **A guardrail is not a prompt instruction.** "Do not do X" in a prompt is a request. A guardrail
   is a check on the output. Until M-04-5.2, say plainly that Nexus has requests, not guardrails.

---

## 13. Evaluation and AI result verification

**AI behaviour cannot be verified by unit test.** A model invocation is not deterministic and its
output space is not enumerable. The method is **Evaluation**: score behaviour against a fixed
question set and a declared threshold — `ASSURANCE_STANDARDS.md` §13.

| Milestone | Brings |
|---|---|
| **M-04-5.1** Evaluation harness | `Evaluation`, `EvaluationRun`. *A regression in answer quality fails a pipeline* |
| **M-09-6.1** Evaluation as a verification method | An evaluation run's output counts as **Evidence**, with its score and its question set |
| **M-04-5.3** Result loop | A proposed action traced to the development result it produced |

Rules:

1. **The threshold is declared before the run.** A score chosen after seeing the result is not a
   criterion. This is the single most important rule in AI assurance and the easiest to break.
2. **The question set is part of the evidence**, not an implementation detail. An evaluation whose
   questions are unrecorded proves nothing later.
3. **Citation correctness is expressible as a criterion**, not a subjective judgement — §14.
4. **A ranking, prompt or model change is evaluated on the same question set as its predecessor.**
   Comparing across question sets compares nothing.
5. **An evaluation score is not a pass by itself.** ASSURANCE decides whether the requirement was
   satisfied; the score is input to that decision.

**CURRENT: there is no evaluation of any kind.** Two behaviour tests exist in the whole system, and
one of them (`KeywordContextRankerTests`) is the only automated check on any AI behaviour at all.

---

## 14. Citations

`Citation` in `Nexus.Intelligence.Contracts/Context/`; on the frontend, `CitationsPanel.tsx`,
`citationTargets.ts` and `useCitationTarget.ts`.

1. **Every claim that came from context carries a citation** to the `ContextItem` that produced it.
2. **A citation resolves to a real context item.** One that points at nothing is a defect and is
   **mechanically detectable** — which is why this is the highest-value AI criterion in Nexus.
3. **Removing an item from the bundle removes it from the citations.** That is an M-04-2.1
   acceptance criterion and it is the test that the citation is real rather than decorative.
4. **Citations are the falsification mechanism for everything else.** Without them, a ranking change,
   a prompt change or a trust-level change cannot be judged: Swagger can tell you an endpoint
   returned 200; it cannot tell you the context was good.

> **M-04-2.1 Citations proven against a live model is a P0 exit criterion.** The frontend was built
> for it; it has never been proven against a live model because of an OpenAI credit block. Until it
> closes, **every claim about AI improvement in Nexus is an anecdote.**

---

## 15. Current state — the honest table

| Capability | State | Closed by |
|---|---|---|
| Model gateway and routing abstraction | **Works**, one provider | M-01-6.2 for more |
| Turn pipeline | **Works** end to end structurally | — |
| Context bundle seam and mapper | **Works**, and is tested | — |
| Keyword ranking | **Works**, and is tested | M-02-4.2 for semantic |
| Prompt assembly | **Works** | M-04-2.2 for versioning |
| Citations | Built; **never proven against a live model** | **M-04-2.1** |
| Turn traces | In-memory; lost on restart | M-04-1.1 |
| Result reports | In-memory; lost on restart | M-04-1.1 |
| Memory | In-memory; lost on restart | M-04-1.2 |
| Agents | Registry and dispatcher real; **`DeveloperAgent` is a 974-byte stub** | M-04-3.1 |
| Tools | **`EmptyToolCatalog` / `EmptyToolGateway` — no tool can run** | M-01-7.1 |
| Retrieval / RAG | **Does not exist** | M-02-4.1 → 4.2 → 4.3 |
| Guardrails / output validation | **Does not exist** | M-04-5.2 |
| Evaluation | **Does not exist** | M-04-5.1, M-09-6.1 |
| Usage metering | In-memory | M-01-4.2, M-04-4.1 |
| Quota enforcement | `PermissiveQuotaPolicy` — enforces nothing | M-01-3.1 + quota work |
| Second provider | `AnthropicModelGateway` is a 306-byte stub | M-01-6.2 |
| Cross-host correlation of a turn | **No logging library is selected at all** | M-10-1.1 |

**The shapes are right and most implementations are placeholders.** That is a deliberate and
defensible position — the contracts were designed before the storage — but it is only safe if
nobody mistakes a compiling interface for a working capability. Write against the interfaces; do not
assume anything behind them survives a restart.

---

## 16. What must never happen

| Never | Why |
|---|---|
| A product type in `Nexus.Intelligence.Contracts` | Invariant 1. **No shared kernel** |
| Intelligence parsing or branching on `ScopeRef` | Invariant 2 |
| An API key in `Nexus.Intelligence` | Invariant 3 |
| A direct provider SDK call outside `Nexus.Platform.Providers.<Vendor>` | §2 |
| A prompt built by string concatenation outside `PromptAssembler` | §6 |
| A prompt body, completion, token or secret in a log line | Absolute — `SECURITY_STANDARDS.md` §11, M-10-1.1 |
| Untrusted content treated as an instruction | §5.1 — prompt injection |
| A tool called outside `IToolGateway` | §8 |
| An irreversible or external effect performed by an agent | It becomes a `ProposedAction` |
| A model call with no timeout | §12 |
| A ranking or prompt change with no way to measure it | §13 |
| A threshold chosen after seeing the score | §13 rule 1 |
| An agent creating, modifying or waiving a safety-critical criterion | Absolute — `ASSURANCE_STANDARDS.md` §7.1, M-09-7.2 |
| An AI-generated answer presented without its citations | §14 |

---

## 17. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Vector store technology | **M-02-4.2** | **Not yet decided** — NOT SELECTED |
| Which providers beyond OpenAI, and in what order | **M-01-6.2** names Anthropic, Google, OpenRouter and DeepSeek as targets; the order is open | Not yet decided |
| Whether local models are ever hosted | No compute environment exists — **M-08-4.1** first | Not yet decided |
| Where the evaluation question set lives | **M-04-5.1**; DATA is the likely home | Not yet decided |
| Whether turn traces live in the `ai` schema or a time-series store | Volume, after **M-10-2.2** | Not yet decided |
| Prompt storage format and diffing | **M-04-2.2** | Not yet decided |

---

## 18. References

- `SECURITY_STANDARDS.md` — §9 AI permissions and `TrustLevel`, §10 tool permissions and
  `SideEffectClass`, §11 the logging prohibitions.
- `ASSURANCE_STANDARDS.md` — §13 AI evaluation, §7.1 safety-critical criteria, §10 evidence.
- `NEW_MODULE_GUIDE.md` §7 — the procedure for adding an agent.
- `REPOSITORY_STRUCTURE.md` §3.3 — the full contents of `Nexus.Intelligence`.
- `PRODUCT_DEVELOPMENT_GUIDE.md` §10 — the consumer side of the `ContextBundle` seam.
- `MACHINE_DEVELOPMENT_GUIDE.md` §1 and §13 — what AI may and may not do where physical safety is
  involved.
- `API_STANDARDS.md` — the `/intelligence/v1` surface, error shape, versioning.
- `CODE_CONVENTIONS.md` §12–§14 — cancellation, timeouts, retries.
- `TECHNOLOGY_STACK.md` §7 — vector store and everything else not selected.
- `LOCAL_DEVELOPMENT.md` — running Intelligence locally and why its port is not written down.
