# Nexus V2.1 Migration Runbook

Three solutions, interleaved. Companion to `NEXUS_ARCHITECTURE_V2.md` — read that first.

**Repos**

| Short name | Path | Becomes |
|---|---|---|
| `AI` | `C:\Personal\NexusAI` | Nexus Platform (libraries) |
| `INT` | `C:\Personal\Nexus.Int` | Nexus Intelligence (API) |
| `WEB` | `C:\Personal\Nexus.Web` | Chatbot product (API + React) |

**Rules for every stage**

- Each stage ends with a green build in the repo it touches.
- Each stage is one commit. `git reset --hard HEAD~1` undoes exactly one.
- Never edit a `_migrate/` folder's contents in a stage that isn't assigned to it.
- Package changes require a re-pack: `.\pack-local.ps1` in the producer, `dotnet restore` in the consumer.

---

## Stage 0 — Baseline (manual, 10 minutes)

```powershell
# --- AI ---
cd C:\Personal\NexusAI
git status                        # must be clean
dotnet build NexusAI.slnx         # MUST succeed
git tag pre-v2
git checkout -b arch/v2

# --- WEB ---
cd C:\Personal\Nexus.Web
git status
git tag pre-v2
git checkout -b arch/v2
cd src\Nexus.Web.Client
npm install                       # node_modules is missing
npm run build                     # confirm the client builds today
cd C:\Personal\Nexus.Web

# --- feed ---
mkdir C:\Personal\LocalNuGet -Force
```

If `dotnet build` fails, stop and fix it on `main` first. Your canonical docs already flag
"no clean build recorded for this handoff" as critical debt — migrating on top of it means
you can't tell migration errors from pre-existing ones.

> `Nexus.Web` is currently on branch `feature/dashboard-api-integration` with unmerged work.
> Decide before you start: merge it to `main` first, or branch `arch/v2` from it. Do not
> leave it stranded.

---

## Stage 0.5 — House rules in all three repos

Run once per repo. `claude` in each, then:

```
Create CLAUDE.md at this repo root. Read NEXUS_ARCHITECTURE_V2.md (it is in
C:\Personal\NexusAI) for the full detail and summarise, concisely:

ARCHITECTURE — Nexus V2.1, three solutions:
  Nexus.AI  = Nexus Platform. The backbone between products and AI: model providers,
              model catalog, tool execution, credentials, identity, metering, audit.
              Holds NO product entity and NO product database. Ships as NuGet libraries.
  Nexus.Int = Nexus Intelligence. The deciding layer: intent, policy, planning, agent
              selection, model selection, context ranking, memory, results, evaluation.
              Schema-agnostic. Never calls a vendor SDK. Deployed at /intelligence/v1.
  Nexus.Web = the Chatbot product. React client + .NET API + Dataverse. Owns Workspace,
              Project, Conversation, Message, Knowledge, WorkItem, Artifact, Branch,
              Snapshot, Session, ADR. Deployed at /api/v1.

THE RULE — Intelligence decides. Platform executes. Products own the data and the UX.

REFERENCE RULES — enforced by Directory.Build.props and the architecture tests:
  Nexus.Web may reference ONLY the Nexus.Intelligence.Contracts package.
  Nexus.Int may reference ONLY Nexus.Platform.* packages.
  Nexus.AI may reference only vendor SDKs.
  No type named Workspace, Project, Conversation, ConversationMessage, Knowledge,
  WorkItem, Artifact, Branch, Snapshot, Session or Adr may appear in Nexus.AI or Nexus.Int.

PACKAGES — local feed at C:\Personal\LocalNuGet. Producers run .\pack-local.ps1.
  Versions float as 0.1.0-* because NuGet caches by version; a re-pack of the same
  version would not be picked up.

CODE STYLE — match the existing codebase: file-scoped namespaces, sealed classes,
  explicit constructors with readonly fields, `required` init properties on records,
  CancellationToken last with a default, strongly typed IDs converted only at boundaries,
  no MediatR. Use TimeProvider, not a custom IClock.

BUILD — .NET 10 (global.json pins 10.0.302).

WHEN UNSURE — if a change would make Intelligence or Platform need to know a product's
shape, the ContextBundle contract is wrong. Fix the contract, never the boundary.
```

---

## Stages 1–3 — The restructure script (manual)

```powershell
cd C:\Personal\NexusAI
.\nexus-v2-restructure.ps1              # dry run, read it
.\nexus-v2-restructure.ps1 -Execute
```

Then commit in each repo:

```powershell
cd C:\Personal\NexusAI  ; git add -A ; git commit -m "refactor(v2): platform skeleton, product/intelligence code copied out"
cd C:\Personal\Nexus.Int; git add -A ; git commit -m "chore: initial Nexus.Int solution from NexusAI extraction"
cd C:\Personal\Nexus.Web; git add -A ; git commit -m "refactor(v2): product .NET projects from NexusAI extraction"
```

Nothing builds yet — namespaces still say `NexusAI.*`. **NexusAI still holds the originals**;
they're deleted in Stage 9, after all three solutions are green.

---

## Stage 4 — Nexus.AI: the Platform

`cd C:\Personal\NexusAI` then `claude`:

```
Build the Platform. Read NEXUS_ARCHITECTURE_V2.md sections 1.1, 2.3 and 3.2 first.

Platform executes model and tool calls, meters them, and audits them. It must never learn
what a Conversation is.

1. Author src/Nexus.Platform.Contracts. Namespace Nexus.Platform.Contracts.
   Models/     IModelCatalog, IModelGateway, ModelDescriptor, ModelCapabilities,
               LatencyClass, ModelQuery, ModelInvocation, ModelMessage, ModelRole,
               ModelInvocationResult, ModelStreamChunk, InvocationIdentity
   Tools/      IToolCatalog, IToolGateway, ToolDescriptor, ToolInvocation, ToolResult,
               SideEffectClass
   Governance/ IUsageMeter, UsageRecord, IQuotaPolicy, QuotaVerdict, IAuditLog, AuditEntry
   Identity/   IIdentityService, ITenantResolver, ResolvedIdentity, IProductRegistry
   Secrets/    ISecretResolver

   InvocationIdentity carries exactly TenantId, ProductId, TurnId, UserId. Nothing more.
   That is the metering key and it must not be able to express a product's structure.

   Shape every contract as if it were HTTP: async, DTO in / DTO out, no shared mutable
   state, nothing IQueryable crossing the boundary. Platform runs in-process today but
   must be liftable into a service without changing callers.

2. Convert src/Nexus.Platform.Contracts/_migrate:
     ILLMProvider  -> IModelGateway   (add ModelId to the invocation; add StreamAsync)
     ChatRequest   -> ModelInvocation
     ChatResponse  -> ModelInvocationResult  (keep Success/Error; add Usage and ModelUsed)
     ChatMessage   -> ModelMessage
   Delete the _migrate folder.

3. Convert src/Nexus.Platform.Providers.OpenAI/_migrate:
     OpenAIProvider -> OpenAIModelGateway : IModelGateway
     plus OpenAIModelCatalogSource contributing ModelDescriptors.
   Keep OpenAIOptions and its configuration binding.
   Every call goes through IQuotaPolicy.CheckAsync before, and IUsageMeter.RecordAsync +
   IAuditLog.AppendAsync after.
   Delete the _migrate folder.

4. Implement src/Nexus.Platform.Core:
     AggregatingModelCatalog   fans out to registered catalog sources
     RoutingModelGateway       resolves ModelId to the right provider gateway
     InMemoryUsageMeter, PermissiveQuotaPolicy, ConsoleAuditLog   (real ones come later)
     PlatformServiceCollectionExtensions.AddNexusPlatform(IConfiguration)

5. Leave Providers.Anthropic, Tools, Identity and Persistence as scaffolds: one
   interface-shaped placeholder file each with a // TODO(V2) comment naming the stage
   that fills it in.

6. Fill tests/Nexus.Platform.Architecture.Tests with NetArchTest rules:
   Platform_MustNotReference_IntelligenceOrProducts
   Platform_MustNotContain_ProductTypeNames  (scan for Workspace, Project, Conversation,
     ConversationMessage, Knowledge, WorkItem, Artifact, Branch, Snapshot, Session, Adr)

Acceptance: dotnet build Nexus.AI.slnx succeeds, dotnet test passes, and no _migrate
folder remains. Report the result.
```

Then publish:

```powershell
.\pack-local.ps1
git add -A ; git commit -m "feat(v2): Nexus Platform contracts, OpenAI gateway, core"
```

---

## Stage 5 — Nexus.Int: the Intelligence

`cd C:\Personal\Nexus.Int` then `dotnet restore` (picks up the Platform packages), then `claude`:

```
Build the Intelligence layer. Read C:\Personal\NexusAI\NEXUS_ARCHITECTURE_V2.md
sections 1.2, 2.3 and 3.1 first.

Intelligence decides what to do, where and how. It is schema-agnostic: it must compile
with zero knowledge of any product's entities.

1. Author src/Nexus.Intelligence.Contracts exactly as section 3.1 specifies:
   IntelligenceTurnRequest, ScopeRef, ActorRef, TurnInput, TurnInputKind, ContextBundle,
   ContextItem, ContextItemKind, TrustLevel, TurnConstraints, IntelligenceTurnResponse,
   TurnOutcomeKind, ReplyPayload, PlanPayload, ProposedAction, Citation, DecisionTrace,
   PersistenceHint, PersistenceHintKind, UsageSummary, TurnError, ResultReport,
   IIntelligenceClient.

   This project must reference NOTHING except the framework. No Platform types leak into
   it — products must never see IModelGateway. Check its csproj has no PackageReference
   to Nexus.Platform.*.

2. Convert src/Nexus.Intelligence.Context/Ranking/_migrate:
   IKnowledgeRanker/KeywordKnowledgeRanker generalise to IContextRanker/KeywordContextRanker
   operating on ContextItem. Score = keyword overlap × trust weight × recency decay.
   Keep the existing keyword logic as the baseline scorer. Delete the _migrate folder.

3. Rework Nexus.Intelligence.Context/Prompting: move from "knowledge + user prompt" to
   "ranked ContextBundle + system frame + user input". Group items by ContextItemKind.
   Emit stable ids so Citations can point back at ContextItem.Id. Take the model's
   ContextWindow as a parameter and fit to it — do not hardcode a size.

4. Build the turn pipeline in Nexus.Intelligence.Core as explicit ordered steps (no
   mediator, matching the existing handler style):
     IntentClassifier -> PolicyGate -> ContextRanker -> AgentSelector -> ModelSelector
     -> PromptAssembler -> IModelGateway.InvokeAsync -> ToolLoop -> ResponseComposer
   ModelSelector calls IModelCatalog.ListAsync and picks by required capability, then
   TurnConstraints.MaxCost, then LatencyBudget. Record every choice as a DecisionTrace —
   the explanation endpoint depends on it.

   Rewrite Planning/Planner.cs: it currently returns four hardcoded work items. Make it
   call the model with a structured-output prompt and return a real decomposition.
   Rewrite Execution/ExecutionEngine.cs: it currently always dispatches AgentType.Developer.
   Make it use AgentSelector.

5. In Nexus.Intelligence.Agents change AgentContext from
   (ProjectId, ConversationId, WorkspaceId, AgentType) to (ScopeRef, ActorRef, AgentType).
   Those three product ids are exactly the leak this migration removes.

6. Convert src/Nexus.Intelligence.Memory/_migrate: the Memory domain model becomes
   MemoryRecord keyed by (TenantId, ProductId, ScopeRef) with no product foreign keys.
   Add IMemoryStore with an in-memory implementation. Delete the _migrate folder.

7. Nexus.Intelligence.Api: minimal endpoints in the existing style (static MapXEndpoints
   extension methods, thin, delegating to handlers), all under /intelligence/v1:
     POST /turns   POST /results   GET /turns/{id}/explanation
     POST /plans   GET /capabilities
   Wire AddNexusPlatform in Program.cs. Swagger on.

8. Fill tests/Nexus.Intelligence.Architecture.Tests:
   Intelligence_MustNotReference_Products
   Intelligence_MustNotReference_VendorSdks   (no OpenAI, Azure or Dataverse types)
   Contracts_MustNotReference_Platform
   Intelligence_MustNotContain_ProductTypeNames

Acceptance: dotnet build Nexus.Int.slnx succeeds; dotnet test passes; POST
/intelligence/v1/turns returns a real model reply given a hand-written ContextBundle;
no file under src/ contains the words Workspace, Conversation, WorkItem, Dataverse or
OpenAI. Report each check.
```

Then publish the contract:

```powershell
.\pack-local.ps1
git add -A ; git commit -m "feat(v2): Intelligence contracts, turn pipeline, API"
```

---

## Stage 6 — Nexus.Web: namespaces and routes

`cd C:\Personal\Nexus.Web`, `dotnet restore`, then `claude`:

```
The Chat product's .NET code was copied here from the NexusAI repo. Every namespace still
says NexusAI.*, so nothing compiles. Fix that mechanically and only that.

  NexusAI.Domain.<X>         -> Nexus.Products.Chat.Domain.<X>
  NexusAI.Application.<X>    -> Nexus.Products.Chat.Application.<X>
  NexusAI.Infrastructure.<X> -> Nexus.Products.Chat.Infrastructure.<X>
  NexusAI.Api.<X>            -> Nexus.Products.Chat.Api.<X>

Rules:
- Do NOT touch src/Nexus.Products.Chat.Api/_migrate. That is Stage 7.
- Do NOT change any type name, signature or logic. Namespaces, usings and route prefixes only.
- File-scoped namespaces everywhere.
- Replace any IClock / SystemClock usage with TimeProvider. Those files did not come across
  on purpose.
- Rebase every endpoint route from /api/... to /api/v1/...
- The Chat feature folder lost its Prompting subfolder and the two ranker files on purpose —
  they belong to Intelligence now. Anything referencing IPromptBuilder or IKnowledgeRanker
  will not compile; leave those call sites broken and list them. Stage 7 fixes them.

Then build and report, separating expected cross-layer errors (IPromptBuilder,
IKnowledgeRanker, ILLMProvider) from real mistakes:
  dotnet build src/Nexus.Products.Chat.Domain
  dotnet build src/Nexus.Products.Chat.Application
  dotnet build src/Nexus.Products.Chat.Infrastructure
```

Commit when Domain and Infrastructure build clean.

---

## Stage 7 — Nexus.Web: rewire the chat turn

**This is the stage that proves the architecture.** Read the diff properly.

```
Rewire the chat turn to go through Intelligence instead of calling a model provider
directly. Read C:\Personal\NexusAI\NEXUS_ARCHITECTURE_V2.md section 5 first.

Current flow in Chat/Commands/SendChat/SendChatHandler.cs:
  load conversation -> load project -> persist user message -> load history ->
  IKnowledgeRetrievalService.RetrieveAsync -> IPromptBuilder.Build -> ILLMProvider.ChatAsync
  -> persist assistant message

Target:
  load conversation -> load project -> persist user message -> load history + knowledge +
  ADRs + project objective -> map to ContextBundle -> IIntelligenceClient.SendTurnAsync ->
  persist assistant message -> apply PersistenceHints

1. Add Application/Chat/Context/ChatContextBundleMapper.cs. It maps product entities to
   canonical ContextItems:
     ConversationMessage -> Kind=Message,   Trust=Reported,      Author=role, OccurredAt=CreatedOn
     Knowledge           -> Kind=Fact,      Trust=Curated or Approved from KnowledgeStatus
     Adr                 -> Kind=Decision,  Trust=Authoritative
     Project name/brief  -> Kind=Objective, Trust=Authoritative
     WorkItem (open)     -> Kind=Constraint,Trust=Curated
   This mapper is the ONLY place in the product that knows the canonical shape, and the
   only place to touch when a new context kind is added. Do not make it lossy — everything
   the old prompt builder had access to must survive the mapping.

2. Build ScopeRef as:
     Kind = "conversation"
     Key  = conversation.Id.Value.ToString()
     Path = [ $"workspace:{project.WorkspaceId.Value}",
              $"project:{project.Id.Value}",
              $"conversation:{conversation.Id.Value}" ]
   Intelligence treats all of it as opaque text. Never send a raw entity.

3. Add Infrastructure/Intelligence/HttpIntelligenceClient.cs implementing IIntelligenceClient
   over HttpClient, registered with AddHttpClient, base address from configuration key
   "Nexus:IntelligenceBaseUrl", with retry and timeout handling.
   IdempotencyKey = a deterministic hash of (conversationId, prompt, userMessageId) so a
   retried POST does not double-charge.

4. Rewrite SendChatHandler to use it. Delete its ILLMProvider and IPromptBuilder
   dependencies entirely.

5. Handle the response:
   Reply         -> persist assistant ConversationMessage as today
   Clarification -> persist as an assistant message, flag it in the result
   Refusal/Failed-> return SendChatResult(false, ..., error)
   Citations     -> return in SendChatResponse so the UI can render sources
   Usage         -> return it so the UI can show cost
   PersistenceHint KnowledgeCandidate -> create a Knowledge record with status pending
     approval. NEVER auto-approve: ADR-005 requires explicit user approval for
     consequential changes.

6. Update Endpoints/Chat/SendChatResponse.cs to carry reply, citations and usage.

7. Keep IKnowledgeRetrievalService in the product — fetching from the product's own
   Dataverse is a product concern. Only ranking moved to Intelligence.

8. Convert src/Nexus.Products.Chat.Api/_migrate/Program.cs into the product API bootstrap:
   AddChatProduct, Dataverse wiring, HttpIntelligenceClient, CORS from configuration
   (not the hardcoded http://localhost:5173), Swagger, /health, all routes under /api/v1.
   Delete the _migrate folder.

9. Fill tests/Nexus.Products.Chat.Architecture.Tests:
   Products_MustNotReference_Platform
   Products_MustOnlyReference_IntelligenceContracts
   Add a unit test for ChatContextBundleMapper asserting each entity kind maps to the
   right ContextItemKind and TrustLevel.

Acceptance:
  dotnet build Nexus.Web.slnx succeeds
  Select-String -Path src\*\**\*.cs -Pattern "ILLMProvider|OpenAI|ModelInvocation" is empty
  a chat turn works end to end against a running Nexus.Int
Report each check.
```

---

## Stage 8 — Nexus.Web: the frontend

```
Rework the React client. Read C:\Personal\NexusAI\NEXUS_ARCHITECTURE_V2.md section 8.

Work in src/Nexus.Web.Client. Run npm install first if node_modules is missing.

1. Consolidate the two HTTP paths. features/projects/projectsApi.ts uses raw fetch with
   import.meta.env directly — no ApiError, no auth header, duplicated error strings.
   features/workspaces/workspacesApi.ts uses the nexusApi client properly. Convert
   projectsApi.ts to the workspacesApi.ts shape. Every request goes through ApiClient.

2. api/ApiClient.ts: base path becomes /api/v1. Update every feature api module to match.

3. .env.development and .env.example: keep exactly one variable, VITE_NEXUS_API_URL,
   pointing at the product API. Add a comment: the frontend must never be given an
   Intelligence or Platform URL — it cannot see those layers.

4. Rename pages/IntelligencePage.tsx to pages/InsightsPage.tsx; update routes/AppRoutes.tsx
   and layouts/AppLayout.tsx (nav label "Intelligence" -> "Insights"). The frontend must not
   have a page named after an internal layer it cannot see. Have it render the citations,
   decisions and usage that arrive THROUGH the product API.

5. Rename features/platform/ to features/system/ (it only calls /health). "Platform" now
   means something specific and this is not it.

6. AppLayout brand subtitle: "AI Platform" -> "Chat". The user is in a chatbot.

7. The four files in features/products/ are 0 bytes: Product.ts, ProductCard.tsx,
   productsApi.ts, useProducts.ts. Implement them minimally against GET /api/v1/products
   so ProductsPage is not linking at dead code. If no such endpoint exists yet, render a
   static list of one entry (this chatbot) with a TODO.

8. Build features/chat/ — the product currently has NO chat UI, which means Stage 7 can only
   be verified through Swagger. Create:
     chatApi.ts          POST /api/v1/chat, GET /api/v1/conversations/{id}/messages,
                         GET /api/v1/projects/{projectId}/conversations
     useSendChat.ts      TanStack Query mutation with optimistic user message
     useConversation.ts, useConversations.ts
     ConversationList.tsx   conversations for the active project
     MessageThread.tsx      role-styled message list, auto-scroll
     ChatPanel.tsx          thread + composer
     CitationsPanel.tsx     renders response.citations against the message they belong to
   Add pages/ChatPage.tsx and route /projects/:projectId/conversations/:conversationId.
   Add a "Conversations" entry to the project details page.

   Match the existing code style exactly: 4-space indent, single quotes, no semicolons,
   named function exports, feature-sliced folders, TanStack Query for server state.

9. Remove the empty src/hooks, src/styles and src/utils folders, or put something real
   in them.

Acceptance: npm run build succeeds, npx oxlint passes, and every network call in the app
goes through ApiClient to VITE_NEXUS_API_URL. Report each check.
```

---

## Stage 9 — Nexus.AI: delete what moved out

Only once Nexus.Int and Nexus.Web both build.

```powershell
cd C:\Personal\NexusAI
.\nexus-v2-restructure.ps1 -Phase Cleanup            # dry run
.\nexus-v2-restructure.ps1 -Phase Cleanup -Execute
dotnet build Nexus.AI.slnx
git add -A ; git commit -m "refactor(v2): remove product and intelligence code from NexusAI"
```

NexusAI now contains Platform and nothing else. History is intact — `git checkout pre-v2`
brings the whole original solution back.

---

## Stage 10 — Docs and tags

```
Update the canonical documentation in "NexusAI Documentation" to V2.1:
  02_ARCHITECTURE_AND_MODULES.md — replace layer descriptions with the three solutions
  04_API_CONTRACT.md — rebase product routes to /api/v1, add the /intelligence/v1 contract
  08_DECISIONS_AND_TECHNICAL_DEBT.md — add ADR-011 superseding ADR-009 (corrected Platform
    scope: backbone only, no product data), ADR-012 (decide/execute split), ADR-013
    (three solutions, Platform as NuGet). Do NOT delete ADR-009 — the doc's own maintenance
    rule says supersede, never erase.
  11_FUTURE_OF_NEXUS_AI.md — correct the Platform section
  12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md — note that all 21 tables belong to the Chat
    product, and that Memory is retired from the product schema
Delete the nested documentation .zip files; git tags replace them.
```

```powershell
cd C:\Personal\NexusAI  ; git tag v2-arch
cd C:\Personal\Nexus.Int; git tag v2-arch
cd C:\Personal\Nexus.Web; git tag v2-arch
```

---

## Verification checklist

| # | Check | Where |
|---|---|---|
| 1 | `dotnet build Nexus.AI.slnx` | AI |
| 2 | `dotnet build Nexus.Int.slnx` | INT |
| 3 | `dotnet build Nexus.Web.slnx` | WEB |
| 4 | `npm run build` | WEB client |
| 5 | `dotnet test` passes in all three | all |
| 6 | Architecture tests fail when violated | add a bad reference, build, revert |
| 7 | No `_migrate` folders remain | `Get-ChildItem -Recurse -Directory -Filter _migrate` in all three |
| 8 | Product knows no models | `Select-String -Path C:\Personal\Nexus.Web\src\*\**\*.cs -Pattern "ILLMProvider\|OpenAI\|ModelInvocation"` → empty |
| 9 | Intelligence knows no products | same search for `Workspace\|Conversation\|Dataverse` in Nexus.Int → empty |
| 10 | Platform knows no products | same in NexusAI → empty |
| 11 | Frontend has one API URL | grep `.env.*` → only `VITE_NEXUS_API_URL` |
| 12 | **Chat works end to end** | run Nexus.Int and Nexus.Web, send a message in the UI, get a reply with citations |
| 13 | **Round trip persisted** | reload the conversation — both messages are in Dataverse |
| 14 | **Usage recorded** | the turn appears in the usage ledger with a cost |

Items 1–11 only prove the layers are separated. **12–14 prove they still work together.**

---

## When something breaks

```powershell
claude -c "the build fails, here are the errors: <paste>"   # continue the last session
git reset --hard HEAD~1                                      # undo one stage
git checkout main ; git branch -D arch/v2                    # undo everything in this repo
```

`pre-v2` is tagged in NexusAI and Nexus.Web. Nexus.Int is new, so deleting the folder undoes
it completely.

**Package staleness.** If Nexus.Int or Nexus.Web don't see your latest changes, you forgot
to re-pack. In the producer: `.\pack-local.ps1`. In the consumer: `dotnet restore`. The
timestamped version suffix exists precisely so this works — NuGet caches by version, and
re-packing the same number would be silently ignored.
