# Nexus V2 Migration — Claude Code Runbook

Run these in order, in the **Developer PowerShell** terminal inside Visual Studio, at the repo root.
Each stage ends with a green build. **Do not start the next stage until the current one compiles.**

Companion documents:

- `NEXUS_ARCHITECTURE_V2.md` — the target architecture (read this first)
- `nexus-restructure.ps1` — the mechanical file move

---

## Stage 0 — Baseline (do this by hand, 5 minutes)

```powershell
cd <repo root>            # the folder containing NexusAI.slnx

git status                # must be clean
dotnet --version          # expect 10.0.3xx per global.json
dotnet restore
dotnet build              # MUST succeed before you touch anything

git tag pre-v2
git push origin pre-v2    # if you have a remote
```

If `dotnet build` fails, stop. Fix the build first — the canonical docs already flag
"no clean build recorded for this handoff" as critical debt. Migrating on top of a broken
build means you cannot tell migration errors from pre-existing ones.

Then drop these three files at the repo root and commit them:

```powershell
# copy NEXUS_ARCHITECTURE_V2.md, CLAUDE_CODE_MIGRATION_PROMPTS.md, nexus-restructure.ps1 here
git add NEXUS_ARCHITECTURE_V2.md CLAUDE_CODE_MIGRATION_PROMPTS.md nexus-restructure.ps1
git commit -m "docs: V2 architecture blueprint and migration runbook"
```

---

## Stage 0.5 — Give Claude Code the rules permanently

Before any prompt, create `CLAUDE.md` at the repo root. Every Claude Code session in this repo
reads it automatically, which stops the layers drifting back together six weeks from now.

**Prompt:**

```
Create CLAUDE.md at the repo root for this solution. It must state, concisely:

ARCHITECTURE — Nexus V2, three layers. Full detail in NEXUS_ARCHITECTURE_V2.md.
  Nexus Platform      = the backbone between products and AI. Model providers, model
                        catalog, tool execution, credentials, identity, metering, audit.
                        It holds NO product entity and NO product database.
  Nexus Intelligence  = the deciding layer. Intent, policy, planning, agent selection,
                        model selection, context ranking, memory, results, evaluation.
                        It is schema-agnostic and never calls a vendor SDK.
  Nexus Products      = the experiences. Product #1 is Nexus Chat and it owns Workspace,
                        Project, Conversation, Message, Knowledge, WorkItem, Artifact,
                        Branch, Snapshot, Session, ADR and its Dataverse solution.

THE RULE — Intelligence decides. Platform executes. Products own the data and the UX.

REFERENCE RULES — enforced by tests/Nexus.Architecture.Tests, build-breaking:
  Nexus.Products.*     may reference only Nexus.Intelligence.Contracts + Nexus.Shared.Kernel
  Nexus.Intelligence.* may reference only Nexus.Platform.Contracts + Nexus.Shared.Kernel
  Nexus.Platform.*     may reference only Nexus.Shared.Kernel + vendor SDKs
  Nexus.Host           is the composition root and is exempt
  No product type name (Workspace, Project, Conversation, ConversationMessage, Knowledge,
  WorkItem, Artifact, Branch, Snapshot, Session, Adr) may appear in any Nexus.Intelligence.*
  or Nexus.Platform.* assembly.

CODE STYLE — match the existing codebase exactly:
  file-scoped namespaces, sealed classes, primary-constructor-free explicit constructors
  with readonly fields, `required` init properties on records, CancellationToken as the last
  parameter with a default, strongly typed IDs converted only at boundaries, no MediatR.

BUILD — .NET 10 (global.json pins 10.0.302). `dotnet build Nexus.slnx`.

WHEN UNSURE — if a change would make Intelligence or Platform need to know a product's shape,
that is a signal the ContextBundle contract is wrong. Fix the contract, never the boundary.
```

Commit it.

---

## Stage 1 — Run the restructure script

Not a Claude Code prompt — run it yourself and read the output.

```powershell
.\nexus-restructure.ps1                    # dry run first, read every line
.\nexus-restructure.ps1 -Execute
git status                                 # ~150 renames
```

Expected end state: 66 moves, 14 deletions, 24 new `.csproj` files, a new `Nexus.slnx`,
and five `_migrate` folders holding the code that needs rewriting rather than moving.

**The solution will not build yet.** Every namespace still says `NexusAI.*`. That is Stage 2.

```powershell
git add -A
git commit -m "refactor: relocate files into V2 layer structure (namespaces not yet updated)"
```

---

## Stage 2 — Namespace and using rewrite

**Prompt:**

```
The solution was just restructured into the Nexus V2 three-layer layout. Every file moved
but no namespace changed, so nothing compiles. Fix that mechanically and only that.

Rewrite every namespace declaration and every using directive to match the new assembly
each file now lives in:

  NexusAI.Domain.<X>              -> Nexus.Products.Chat.Domain.<X>
  NexusAI.Application.<X>         -> Nexus.Products.Chat.Application.<X>
  NexusAI.Infrastructure.<X>      -> Nexus.Products.Chat.Infrastructure.<X>
  NexusAI.Api.<X>                 -> Nexus.Products.Chat.Api.<X>

Except for the files that changed layer — use the folder they are in now, not the old name:

  src/shared/Nexus.Shared.Kernel/**            -> Nexus.Shared.Kernel.<Folder>
  src/intelligence/Nexus.Intelligence.Core/**  -> Nexus.Intelligence.Core.<Folder>
  src/intelligence/Nexus.Intelligence.Context/** -> Nexus.Intelligence.Context.<Folder>
  src/intelligence/Nexus.Intelligence.Agents/** -> Nexus.Intelligence.Agents.<Folder>

Rules:
- Do NOT touch anything inside a folder named _migrate. Those are Stage 3 and 4 rewrites.
- Do NOT change any type name, signature, or logic. Namespaces and usings only.
- Use file-scoped namespaces everywhere (the codebase already does).
- Note AgentRuntime.cs currently declares namespace NexusAI.Infrastructure.Agents while
  living in NexusAI.Core - put it in Nexus.Intelligence.Agents.Abstractions with its siblings.
- Note the Dataverse mappers and repositories reference domain types heavily; they stay in
  Nexus.Products.Chat.Infrastructure and reference Nexus.Products.Chat.Domain.

Then build each project bottom-up and report what still fails:
  dotnet build src/shared/Nexus.Shared.Kernel
  dotnet build src/products/chat/Nexus.Products.Chat.Domain
  dotnet build src/products/chat/Nexus.Products.Chat.Application
  dotnet build src/products/chat/Nexus.Products.Chat.Infrastructure

Expect Application and Infrastructure to still fail on the missing ILLMProvider and
IPromptBuilder and IKnowledgeRanker - those moved layers on purpose. List those errors
separately from real mistakes.
```

Commit when the Domain and Kernel projects build clean.

---

## Stage 3 — Platform: contracts and the OpenAI provider

**Prompt:**

```
Build the Platform layer. Platform is the backbone between products and AI: it executes,
meters and audits model and tool calls. It must never learn what a Conversation is.

1. In src/platform/Nexus.Platform.Contracts, author the contracts from
   NEXUS_ARCHITECTURE_V2.md section 3.2. Namespace Nexus.Platform.Contracts.

   Models/     IModelCatalog, IModelGateway, ModelDescriptor, ModelCapabilities,
               LatencyClass, ModelInvocation, ModelMessage, ModelRole,
               ModelInvocationResult, ModelStreamChunk, InvocationIdentity
   Tools/      IToolCatalog, IToolGateway, ToolDescriptor, ToolInvocation, ToolResult,
               SideEffectClass
   Governance/ IUsageMeter, UsageRecord, IQuotaPolicy, QuotaVerdict, IAuditLog, AuditEntry
   Identity/   IIdentityService, ITenantResolver, ResolvedIdentity, IProductRegistry
   Secrets/    ISecretResolver

   InvocationIdentity carries exactly: TenantId, ProductId, TurnId, UserId. Nothing more.
   That is the metering key. It must not be able to express a product's structure.

2. Convert src/platform/Nexus.Platform.Contracts/_migrate:
   ILLMProvider  -> IModelGateway  (add ModelId to the invocation, add streaming)
   ChatRequest   -> ModelInvocation
   ChatResponse  -> ModelInvocationResult (keep Success/Error, add Usage and ModelUsed)
   ChatMessage   -> ModelMessage
   Delete the _migrate folder when done.

3. Convert src/platform/Nexus.Platform.Providers.OpenAI/_migrate:
   OpenAIProvider -> OpenAIModelGateway : IModelGateway, plus OpenAIModelCatalogSource
   contributing ModelDescriptors. Keep OpenAIOptions and its configuration binding.
   Route every call through IQuotaPolicy.CheckAsync before and
   IUsageMeter.RecordAsync + IAuditLog.AppendAsync after.
   Delete the _migrate folder when done.

4. In src/platform/Nexus.Platform.Core add:
   AggregatingModelCatalog (fans out to registered catalog sources)
   RoutingModelGateway (resolves ModelId to the right provider gateway)
   InMemoryUsageMeter, PermissiveQuotaPolicy, ConsoleAuditLog  (real ones come later)
   PlatformServiceCollectionExtensions.AddNexusPlatform(IConfiguration)

5. Leave Nexus.Platform.Providers.Anthropic, .Tools, .Identity and .Persistence as
   scaffolds: one interface-shaped placeholder file each, marked with a // TODO(V2)
   comment naming the stage that fills it in.

Acceptance: `dotnet build src/platform/*` succeeds, and no Platform project references
anything under Nexus.Intelligence or Nexus.Products. Verify with:
  Select-String -Path src\platform\*\*.csproj -Pattern "Nexus.(Intelligence|Products)"
must return nothing.
```

---

## Stage 4 — Intelligence: contracts and the turn pipeline

**Prompt:**

```
Build the Intelligence layer. Intelligence decides what to do, where and how. It is
schema-agnostic: it must compile with zero knowledge of any product's entities.

1. In src/intelligence/Nexus.Intelligence.Contracts, author the full contract from
   NEXUS_ARCHITECTURE_V2.md section 3.1, verbatim where the doc gives code:
   IntelligenceTurnRequest, ScopeRef, ActorRef, TurnInput, TurnInputKind, AttachmentRef,
   ContextBundle, ContextItem, ContextItemKind, TrustLevel, ContextSourceRef,
   TurnConstraints, IntelligenceTurnResponse, TurnOutcomeKind, ReplyPayload, PlanPayload,
   ProposedAction, Citation, DecisionTrace, PersistenceHint, PersistenceHintKind,
   UsageSummary, TurnError.

   Also add the typed client interface products will use:
     public interface IIntelligenceClient
     {
         Task<IntelligenceTurnResponse> SendTurnAsync(IntelligenceTurnRequest request, CancellationToken ct = default);
         Task ReportResultAsync(ResultReport report, CancellationToken ct = default);
     }

   This assembly must reference ONLY Nexus.Shared.Kernel. No Platform types leak into it -
   products must never see IModelGateway.

2. Convert src/intelligence/Nexus.Intelligence.Context/Ranking/_migrate:
   IKnowledgeRanker/KeywordKnowledgeRanker generalise to
   IContextRanker/KeywordContextRanker operating on ContextItem, scoring by
   keyword overlap * trust weight * recency decay. Keep the existing keyword logic as
   the baseline scorer. Delete the _migrate folder.

3. Move the prompt builder in Nexus.Intelligence.Context/Prompting from
   "knowledge + user prompt" to "ranked ContextBundle + system frame + user input",
   grouping items by ContextItemKind and emitting stable ids so Citations can point back
   at ContextItem.Id. It must fit the selected model's ContextWindow - take the window
   size as a parameter, do not hardcode.

4. In Nexus.Intelligence.Core build the turn pipeline as explicit ordered steps
   (no mediator, matching the existing handler style):
     IntentClassifier -> PolicyGate -> ContextRanker -> AgentSelector ->
     ModelSelector -> PromptAssembler -> IModelGateway.InvokeAsync -> ToolLoop ->
     ResponseComposer
   ModelSelector calls IModelCatalog.ListAsync and picks by required capability, then
   TurnConstraints.MaxCost, then LatencyBudget. Record every choice as a DecisionTrace -
   the explanation endpoint depends on it.

   Rewrite Planning/Planner.cs: it currently returns four hardcoded work items. Make it
   call the model with a structured-output prompt and return a real decomposition.
   Rewrite Execution/ExecutionEngine.cs: it currently always dispatches AgentType.Developer.
   Make it use AgentSelector.

5. In Nexus.Intelligence.Agents, change AgentContext from
   (ProjectId, ConversationId, WorkspaceId, AgentType) to (ScopeRef Scope, ActorRef Actor,
   AgentType Type) - those three product ids are exactly the leak this migration removes.

6. In Nexus.Intelligence.Memory, convert the _migrate Memory domain model into
   MemoryRecord keyed by (TenantId, ProductId, ScopeRef) with no product foreign keys.
   Add IMemoryStore with an in-memory implementation. Delete the _migrate folder.

7. In Nexus.Intelligence.Api expose minimal endpoints matching the existing endpoint style
   (static MapXEndpoints extension methods, thin, delegating to handlers):
     POST /intelligence/v1/turns
     POST /intelligence/v1/results
     GET  /intelligence/v1/turns/{id}/explanation
     POST /intelligence/v1/plans
     GET  /intelligence/v1/capabilities

Acceptance: `dotnet build src/intelligence/*` succeeds, and
  Select-String -Path src\intelligence\*\*.csproj -Pattern "Nexus.Products"
returns nothing. Also grep the source: no file under src/intelligence may contain the words
Workspace, Conversation, WorkItem, Dataverse or OpenAI.
```

---

## Stage 5 — Rewire the chat turn through Intelligence

This is the stage that actually proves the architecture. Take it slowly.

**Prompt:**

```
Rewire the Chat product's chat turn to go through Intelligence instead of calling a model
provider directly. This is the seam the whole V2 architecture exists for.

Current flow in Nexus.Products.Chat.Application/Chat/Commands/SendChat/SendChatHandler.cs:
  load conversation -> load project -> persist user message -> load history ->
  IKnowledgeRetrievalService.RetrieveAsync -> IPromptBuilder.Build -> ILLMProvider.ChatAsync
  -> persist assistant message

Target flow (NEXUS_ARCHITECTURE_V2.md section 4):
  load conversation -> load project -> persist user message -> load history + knowledge +
  project context -> map to ContextBundle -> IIntelligenceClient.SendTurnAsync ->
  persist assistant message -> apply PersistenceHints

Do this:

1. Add Nexus.Products.Chat.Application/Chat/Context/ChatContextBundleMapper.cs.
   It maps product entities to canonical ContextItems:
     ConversationMessage -> ContextItem { Kind = Message, Trust = Reported,
                                          Author = role, OccurredAt = CreatedOn }
     Knowledge           -> ContextItem { Kind = Fact, Trust = Curated or Approved
                                          based on KnowledgeStatus }
     Adr                 -> ContextItem { Kind = Decision, Trust = Authoritative }
     Project name/brief  -> ContextItem { Kind = Objective, Trust = Authoritative }
     WorkItem (open)     -> ContextItem { Kind = Constraint, Trust = Curated }
   This mapper is the ONLY place in the product that knows the canonical shape. It is also
   the only place that will need touching when a new context kind is added.

2. Build ScopeRef as:
     Kind = "conversation"
     Key  = conversation.Id.Value.ToString()
     Path = [ $"workspace:{project.WorkspaceId.Value}",
              $"project:{project.Id.Value}",
              $"conversation:{conversation.Id.Value}" ]
   Intelligence treats all of that as opaque text. Never send a raw entity.

3. Add Nexus.Products.Chat.Infrastructure/Intelligence/HttpIntelligenceClient.cs
   implementing IIntelligenceClient over HttpClient, registered with AddHttpClient,
   base address from configuration key "Nexus:IntelligenceBaseUrl", with the standard
   retry/timeout handling. IdempotencyKey = a deterministic hash of
   (conversationId, prompt, userMessageId) so a retried POST does not double-charge.

4. Rewrite SendChatHandler to use it. Delete its ILLMProvider and IPromptBuilder
   dependencies entirely - the product must end up with zero references to any model type.

5. Handle the response:
   - Outcome Reply    -> persist assistant ConversationMessage as today
   - Outcome Clarification -> persist as an assistant message, flag it in the result
   - Outcome Refusal / Failed -> return SendChatResult(false, ..., error)
   - Citations -> return them in SendChatResponse so the frontend can render sources
   - PersistenceHints of kind KnowledgeCandidate -> create a Knowledge record with
     KnowledgeStatus = pending approval. Never auto-approve; ADR-005 requires explicit
     user approval for consequential changes.
   - Usage -> return it so the UI can show cost

6. Update Endpoints/Chat/SendChatResponse.cs to carry reply, citations and usage.

7. Keep IKnowledgeRetrievalService in the product - fetching from the product's own
   Dataverse is a product concern. Only the ranking moved to Intelligence.

Acceptance:
  Select-String -Path src\products\**\*.cs -Pattern "ILLMProvider|OpenAI|ModelInvocation"
must return nothing.
  dotnet build Nexus.slnx must succeed.
```

---

## Stage 6 — Host consolidation

**Prompt:**

```
Consolidate to a single host. The repo previously had two competing entry points
(NexusAI.Api and NexusAI.Host) - the canonical docs flag this as high-priority debt.

1. host/Nexus.Host/_migrate/Program.cs holds the old API bootstrap. Split it:
   - Product wiring -> src/products/chat/Nexus.Products.Chat.Api/ChatProductModule.cs
     with `public static IServiceCollection AddChatProduct(this IServiceCollection, IConfiguration)`
     and `public static WebApplication MapChatProduct(this WebApplication)` that maps every
     existing endpoint group under /api/v1.
   - Intelligence wiring -> src/intelligence/Nexus.Intelligence.Api/IntelligenceModule.cs
     with the same shape, mapping under /intelligence/v1.
   - Everything else -> a new host/Nexus.Host/Program.cs.
   Delete the _migrate folder.

2. Nexus.Host/Program.cs must:
   - AddNexusPlatform(configuration)      // providers, metering, identity
   - AddNexusIntelligence(configuration)  // turn pipeline, agents, memory
   - AddChatProduct(configuration)        // domain, Dataverse, HTTP intelligence client
   - configure Swagger with two tagged groups: "Nexus Chat" and "Nexus Intelligence"
   - keep the existing CORS policy but read allowed origins from configuration
     instead of the hardcoded http://localhost:5173
   - map the health endpoint at /health
   - UseHttpsRedirection, UseAuthorization as today

3. Merge appsettings.json from the old Api and Host into host/Nexus.Host/appsettings.json.
   Structure it as:
     { "Nexus": { "IntelligenceBaseUrl": "...", "Cors": { "AllowedOrigins": [...] } },
       "Platform": { "Providers": { "OpenAI": { "Model": "" } } },
       "Dataverse": { "Url": "", "TenantId": "", "ClientId": "" } }
   No secrets in the file. Keys stay in user secrets - the UserSecretsId is already on
   Nexus.Host.csproj.

4. Update host/Nexus.Host/Properties/launchSettings.json to launch Nexus.Host with
   the swagger route, and delete any launch profile referencing NexusAI.Api or NexusAI.Host.

5. Delete NexusAI.slnLaunch.user if it still exists.

Acceptance: `dotnet run --project host/Nexus.Host` serves Swagger showing both API groups,
and GET /health returns 200.
```

---

## Stage 7 — Enforce, then update the edges

**Prompt:**

```
Make the architecture self-enforcing, then update the frontend and the docs.

1. Fill in tests/Nexus.Architecture.Tests using NetArchTest.Rules. Write one test per rule
   from NEXUS_ARCHITECTURE_V2.md section 2, each with a failure message that names the rule:

   - Products_MustNotReference_Platform
   - Products_MustOnlyReference_IntelligenceContracts
   - Intelligence_MustNotReference_Products
   - Intelligence_MustNotReference_VendorSdks    (assert no OpenAI, Azure, Dataverse types)
   - Platform_MustNotReference_IntelligenceOrProducts
   - Platform_MustNotContain_ProductTypeNames
     (scan Nexus.Platform.* and Nexus.Intelligence.* for types named Workspace, Project,
      Conversation, ConversationMessage, Knowledge, WorkItem, Artifact, Branch, Snapshot,
      Session, Adr - fail on any hit)
   - SharedKernel_MustReference_Nothing

   Then deliberately break one rule, confirm the test fails, and revert. A boundary test
   that has never failed is not a boundary test.

2. Frontend (../Nexus.Web/src/Nexus.Web.Client):
   - src/api/ApiClient.ts: base path becomes /api/v1
   - .env.development and .env.example: keep exactly one variable, VITE_NEXUS_API_URL,
     pointing at the product API. Add a comment: the frontend must never be given an
     Intelligence or Platform URL.
   - Rename src/pages/IntelligencePage.tsx to InsightsPage.tsx and update AppRoutes.
     The frontend must not have a page named after an internal layer it cannot see.
     Have it render citations, decisions and usage returned through the product API.
   - Delete the stray duplicate src/features/workspaces/WorkspaceContext.tsx that sits
     one level above Nexus.Web.Client (the real one is inside the client folder).
   - Scaffold src/features/chat/ - the chatbot product still has no chat UI. Build
     ChatPanel.tsx, useSendChat.ts and chatApi.ts against POST /api/v1/chat, rendering
     the reply plus its citations.

3. Rewrite the canonical documentation to V2. In "NexusAI Documentation":
   - 02_ARCHITECTURE_AND_MODULES.md: replace the layer descriptions with V2
   - 04_API_CONTRACT.md: rebase all routes to /api/v1, add the Intelligence contract
   - 08_DECISIONS_AND_TECHNICAL_DEBT.md: add ADR-011 superseding ADR-009 with the corrected
     Platform scope (backbone only, no product data), and ADR-012 for the decide/execute
     split. Do not delete ADR-009 - the doc's own maintenance rule says supersede, never erase.
   - 11_FUTURE_OF_NEXUS_AI.md: correct the Platform section
   - 12_NEXUS_ENTITY_MODEL_AND_RELATIONSHIPS.md: note that all 21 tables belong to the
     Chat product, and that Memory is retired from the product schema (decision D-2)
   - Delete the nested documentation .zip files; git tags replace them

4. Tag: git tag v2-arch
```

---

## Verification checklist

Do not consider the migration done until every line is true.

| # | Check | Command |
|---|---|---|
| 1 | Solution builds | `dotnet build Nexus.slnx` |
| 2 | All tests pass | `dotnet test Nexus.slnx` |
| 3 | Architecture tests fail when violated | add a bad reference, build, revert |
| 4 | No `_migrate` folders remain | `Get-ChildItem -Recurse -Directory -Filter _migrate` |
| 5 | Products know no models | `Select-String -Path src\products\**\*.cs -Pattern "ILLMProvider\|OpenAI\|ModelInvocation"` → empty |
| 6 | Intelligence knows no products | `Select-String -Path src\intelligence\**\*.cs -Pattern "Workspace\|Conversation\|Dataverse"` → empty |
| 7 | Platform knows no products | `Select-String -Path src\platform\**\*.cs -Pattern "Workspace\|Conversation\|Knowledge"` → empty |
| 8 | One host | `Get-ChildItem -Recurse -Filter Program.cs` → exactly one, under `host/` |
| 9 | Frontend has one API URL | grep `.env.*` → only `VITE_NEXUS_API_URL` |
| 10 | Chat works end to end | send a message in the UI, get a reply with citations |
| 11 | Round trip persisted | reload the conversation, both messages are in Dataverse |
| 12 | Usage recorded | the turn appears in the usage ledger with a cost |

Items 10–12 are the real test. Items 1–9 only prove the layers are separated;
10–12 prove they still work together.

---

## If a stage goes wrong

Each stage is one commit. Roll back a single stage with:

```powershell
git reset --hard HEAD~1
```

Roll back the entire migration with:

```powershell
git checkout main
git branch -D arch/v2
```

`pre-v2` is tagged, so the original solution is always one `git checkout pre-v2` away.

---

## A note on sequencing

Stages 3 and 4 can be worked in either order — Platform and Intelligence do not depend on
each other's implementations, only on contracts. Stage 5 depends on both.

Resist the temptation to do Stage 5 early to "see it work". The whole value of this
migration is that the chat turn crosses two real contracts; wiring it before those
contracts are settled just reproduces the monolith with more files.
