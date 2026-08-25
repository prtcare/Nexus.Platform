# Repository Structure

**Status:** TRANSITION — three repositories exist; five are the target; the three-repo rename is **DONE** (2026-08-24)
**Owner:** DELIVERY (Layer 08), with GOVERNANCE (03) recording the repository set
**Last updated:** 2026-08-21
**Layer:** 08 DELIVERY
**Authoritative for:** which repositories exist, which are targets, what each contains, the
repository→layer mapping, solution files, project layout, where tests live, where documentation
lives, where infrastructure will live, the build and package files at repository root, and what must
never be committed to any of them.

Not authoritative for: the *names* of repositories, solutions and projects as a naming rule — that
is `NAMING_STANDARDS.md` §1–§3, and this document uses those names rather than restating the rule;
branch, worktree and commit mechanics — `GIT_WORKFLOW.md`; which technologies the projects
reference — `TECHNOLOGY_STACK.md`; what a `.csproj` version pin means — `STACK_VERSION_POLICY.md`.

---

## 1. The map

**CURRENT — three repositories on disk, one non-repository package feed.**

| Local path | Remote | Solution | Holds |
|---|---|---|---|
| `C:\Personal\Nexus.Platform` | `github.com/prtcare/Nexus.Platform` | `Nexus.Platform.slnx` | Platform foundation |
| `C:\Personal\Nexus.Intelligence` | `github.com/prtcare/Nexus.Intelligence` | `Nexus.Intelligence.slnx` | Intelligence |
| `C:\Personal\Nexus.Experience` | `github.com/prtcare/Nexus.Experience` | `Nexus.Experience.slnx` | Chat product + web client |
| `C:\Personal\LocalNuGet` | — | — | Package feed on disk. **Not a git repository.** |

**TARGET — five repositories.** Do not write scripts, pipelines or documentation that assume any of
these exist today.

| Target repository | Origin | Layers it houses |
|---|---|---|
| `Nexus.Platform` | `NexusAI` renamed | 01 CORE, 02 DATA, 03 GOVERNANCE, 05 AUTOMATION, 06 PRODUCT CORE, 08 DELIVERY, 09 ASSURANCE, 10 OPERATIONS |
| `Nexus.Intelligence` | `Nexus.Int` renamed | 04 AI |
| `Nexus.Experience` | `Nexus.Web` renamed | 11 EXPERIENCE |
| `Nexus.Developer` | new | 07 DEVELOPER |
| `Nexus.Products.<Name>` | new, one per product | 12 PRODUCTS |

Layer 08 DELIVERY is the one entry that is not wholly contained by a repository: the DELIVERY
*schema* and its aggregates live in `Nexus.Platform`, but the pipelines DELIVERY governs live in
each repository's own `.github/workflows/`.

---

## 2. Layer → repository → project → schema

The single table a reader should be able to answer "where does this go?" from. Schema names are the
layer's short name, lowercase.

| # | Layer | Repository (TARGET) | Schema | Projects that exist today |
|---|---|---|---|---|
| 01 | CORE | `Nexus.Platform` | `core` | `Nexus.Platform.Contracts`, `.Core`, `.Identity`, `.Providers.OpenAI`, `.Providers.Anthropic`, `.Tools` |
| 02 | DATA | `Nexus.Platform` | `data` | `Nexus.Platform.Persistence` (a 308-byte stub) |
| 03 | GOVERNANCE | `Nexus.Platform` | `governance` | none |
| 04 | AI | `Nexus.Intelligence` | `ai` | `Nexus.Intelligence.Contracts`, `.Core`, `.Context`, `.Memory`, `.Agents`, `.Api` |
| 05 | AUTOMATION | `Nexus.Platform` | `automation` | none |
| 06 | PRODUCT CORE | `Nexus.Platform` | `product_core` | none |
| 07 | DEVELOPER | `Nexus.Developer` | `developer` | none — the repository does not exist |
| 08 | DELIVERY | `Nexus.Platform` + per-repo pipelines | `delivery` | none |
| 09 | ASSURANCE | `Nexus.Platform` | `assurance` | none |
| 10 | OPERATIONS | `Nexus.Platform` | `operations` | none |
| 11 | EXPERIENCE | `Nexus.Experience` | `experience` | `Nexus.Experience.Client` (frontend only; the engine does not exist) |
| 12 | PRODUCTS | `Nexus.Products.<Name>` | own database per product | `Nexus.Products.Chat.Domain`, `.Application`, `.Infrastructure`, `.Api` |

**Read that table honestly: seven of the twelve layers have no project at all.** Nexus is three
repositories with a foundation, an intelligence pipeline and one product — not twelve implemented
layers. `DATABASE_STANDARDS.md` §2 owns the physical database strategy behind the schema column;
**the layer-schema convention itself is TARGET — M-02-1.5 Layer schema convention.** The one
migration that exists used schema `org`.

---

## 3. What is inside each repository today

### 3.1 `NexusAI` → `Nexus.Platform` (renamed 2026-08-24)

```
C:\Personal\Nexus.Platform\
  Nexus.Platform.slnx
  Directory.Build.props
  global.json
  nuget.config
  pack-local.ps1
  set-openai-key.ps1
  .github\workflows\            EXISTS AND IS EMPTY
  .git-broken\                  residue of the 2026-08-20 incident
  docs\                         the numbered canonical set 00-12 and the ADR series
  src\
    Nexus.Platform.Contracts\
      Governance\   AuditEntry, IAuditLog, IQuotaPolicy, IUsageMeter, QuotaVerdict, UsageRecord
      Identity\     IIdentityService, IProductRegistry, ITenantResolver, ResolvedIdentity
      Models\       13 types incl. IModelCatalog, IModelGateway, ModelDescriptor,
                    ModelInvocation, ModelUsage
      Secrets\      ISecretResolver
      Tools\        IToolCatalog, IToolGateway, ToolDescriptor, ToolInvocation, ToolResult,
                    SideEffectClass
    Nexus.Platform.Core\
      Governance\   ConsoleAuditLog, InMemoryUsageMeter, PermissiveQuotaPolicy
      Models\       AggregatingModelCatalog, RoutingModelGateway, IModelCatalogSource,
                    INamedModelGateway
    Nexus.Platform.Identity\      IdentityProvider.cs — 240-byte STUB
    Nexus.Platform.Persistence\   PlatformStore.cs — 308-byte STUB
    Nexus.Platform.Providers.OpenAI\
    Nexus.Platform.Providers.Anthropic\  AnthropicModelGateway.cs — 306-byte STUB
    Nexus.Platform.Tools\         ToolProvider.cs — 231-byte STUB
  tests\
    Nexus.Platform.Architecture.Tests\   PlatformBoundaryTests.cs
    Nexus.Platform.Tests\                .csproj with ZERO .cs files
```

Repository root also carries `SQL_PROMPTS_STAGE_1B_2A.md`, `SQL_PROMPTS_STAGE_2B_2C.md`,
`FRONTEND_PROMPTS_F0_F4.md`, `DOCS_CONSOLIDATION_PROMPT.md`, `nexus-v2-restructure.ps1` and
`README.md`.

### 3.2 The empty gitignored husks — read before touching them

Eight directories sit in `NexusAI` under a dead `NexusAI.*` prefix: `NexusAI.Agents`, `NexusAI.Api`,
`NexusAI.Application`, `NexusAI.Core`, `NexusAI.Domain`, `NexusAI.Foundation`, `NexusAI.Host`,
`NexusAI.Infrastructure`. They are **empty and gitignored**. They are residue from the pre-V2
structure, not projects.

| Rule | Statement |
|---|---|
| Never add a file to one | Adding a file to a gitignored directory produces work that is invisible to git and will be lost |
| Never reference one | Nothing in `Nexus.Platform.slnx` should point at them |
| Never revive one | A new project uses the `Nexus.<Capability>.<Role>` pattern — `NAMING_STANDARDS.md` §3 |

**Their disposition is undecided.** Deleting them is safe in the sense that they contain nothing;
it is unproven in the sense that nobody has confirmed no tool, script or `.slnx` entry still
mentions them, and the repository lost its object database on 2026-08-20 to an unconfirmed cause.
The repository rename (2026-08-24) was the natural moment to settle it — that rename walked every
path in the repository anyway — but the call was not made and the eight husks remain. Until a
decision lands: leave them, and do not put anything in them.

### 3.3 `Nexus.Int` → `Nexus.Intelligence` (renamed 2026-08-24)

```
C:\Personal\Nexus.Intelligence\
  Nexus.Intelligence.slnx
  Directory.Build.props   global.json   nuget.config   pack-local.ps1
  .git-broken\
  NO .github DIRECTORY AT ALL
  src\
    Nexus.Intelligence.Contracts\
      Turns\    17 types — IntelligenceTurnRequest, IntelligenceTurnResponse, ScopeRef, ActorRef,
                TurnConstraints, DecisionTrace, PlanStep, ProposedAction, UsageSummary,
                ReplyPayload, TurnError, TurnInput, ...
      Context\  ContextBundle, ContextItem, ContextItemKind, TrustLevel, Citation,
                PersistenceHint, PersistenceHintKind
      Results\  ResultReport, ResultOutcome
      Client\   IIntelligenceClient
    Nexus.Intelligence.Core\
      Turns\      IntentClassifier, PolicyGate, ContextSelector, AgentSelector, ModelSelector,
                  PromptStep, ModelStep, ToolLoop, ResponseComposer, TurnPipeline,
                  InMemoryTurnTraceStore
      Planning\   Planner
      Execution\  ExecutionEngine
    Nexus.Intelligence.Context\
      Ranking\    KeywordContextRanker, RankingOptions, RankedContextItem, IContextRanker
      Prompting\  PromptAssembler, AssembledPrompt, PromptRequest, IPromptAssembler
    Nexus.Intelligence.Memory\   IMemoryStore, InMemoryMemoryStore, MemoryRecord, MemoryQuery,
                                 MemoryKind
    Nexus.Intelligence.Agents\
      Abstractions\ IAgent, IAgentRegistry, IAgentDispatcher, IAgentRuntime, AgentContext,
                    AgentMetadata, AgentType, AgentResult
      BuiltIn\      DeveloperAgent.cs — 974-byte STUB
      AgentRegistry.cs, AgentDispatcher.cs
    Nexus.Intelligence.Api\
      Endpoints\           Turns, Plans, Results, Capabilities, Health, TurnRequestValidation
      Tooling\             EmptyToolCatalog, EmptyToolGateway
      ResultReports\       InMemoryResultReportStore, IResultReportStore
      DependencyInjection\ IntelligenceServiceCollectionExtensions
      Program.cs
  tests\
    Nexus.Intelligence.Architecture.Tests\  BoundaryRuleTests.cs
    Nexus.Intelligence.Tests\               Ranking\KeywordContextRankerTests.cs
```

`AI_DEVELOPMENT_STANDARDS.md` owns what these types mean and how to extend them.

### 3.4 `Nexus.Web` → `Nexus.Experience` (renamed 2026-08-24)

```
C:\Personal\Nexus.Experience\
  Nexus.Experience.slnx
  Directory.Build.props   global.json   nuget.config
  .git-broken\
  NO .github DIRECTORY AT ALL
  src\
    Nexus.Products.Chat.Domain\
      Common\  AggregateRoot, Entity, IRepository
      Adr\  Artifact\  Branch\  Conversation\  ConversationMessage\  Knowledge\  Project\
      Session\  Snapshot\  WorkItem\  Workspace\
        each: <Name>.cs, <Name>Id.cs, <Name>Status.cs, I<Name>Repository.cs
    Nexus.Products.Chat.Application\
    Nexus.Products.Chat.Infrastructure\
      Sql\  NexusChatDbContext.cs, NexusChatDbContextFactory.cs
            Configurations\  WorkspaceConfiguration.cs
            Conventions\     StronglyTypedIdConverters.cs
            Repositories\    SqlWorkspaceRepository.cs
            Migrations\      20260820180802_InitialSqlSchema.cs
      Dataverse implementations for the other 10 aggregates — being REMOVED, ADR-014 Stage 3
    Nexus.Products.Chat.Api\
      Endpoints\  Artifacts, Branches, Chat, ConversationMessage, Conversations, Knowledge,
                  Projects, Sessions, Snapshots, WorkItems, WorkSpaces, HealthEndpoint
      Program.cs, ChatProductModule.cs
    Nexus.Experience.Client\        React + TypeScript + Vite
  tests\
    Nexus.Products.Chat.Architecture.Tests\  BoundaryTests.cs
    Nexus.Products.Chat.Tests\               Chat\ChatContextBundleMapperTests.cs
```

**This repository currently violates its own target boundary**, and knowingly so: it holds both the
Chat *product* (layer 12) and the web *client* (layer 11). The split happens when the Chat product moves to `Nexus.Products.Chat` — the `Nexus.Web` → `Nexus.Experience`
rename (2026-08-24) has already completed. Until then, treat
`Nexus.Experience.Client` as EXPERIENCE code that happens to be co-located, and do not deepen the coupling —
`TYPESCRIPT_REACT_STANDARDS.md` and `PRODUCT_DEVELOPMENT_GUIDE.md` §11.

`Nexus.Experience.Client/src/` structure is owned by `TYPESCRIPT_REACT_STANDARDS.md` §1 and is not repeated
here.

---

## 4. Folder structure — the rule for every repository

```
<Repository>\
  <Repository>.slnx           one solution, at the root
  Directory.Build.props       shared MSBuild properties for every project
  global.json                 the pinned .NET SDK
  nuget.config                package sources
  .gitignore
  README.md
  .github\workflows\          pipelines — TARGET, M-08-1.2
  docs\                       documentation, where the repository owns any
  src\                        every production project, one directory each
  tests\                      every test project, one directory each
  infra\                      infrastructure as code — TARGET, does not exist anywhere
  scripts\                    .ps1 scripts (currently these sit at the repository root)
```

| Rule | Statement |
|---|---|
| Production code under `src/` | Never at the repository root, never under `tests/` |
| Test projects under `tests/` | Never inside the project they test |
| One directory per project | Directory name equals project name equals assembly name equals root namespace |
| Namespace mirrors folder | `CSHARP_STANDARDS.md` §1 — no exceptions exist and none is permitted |
| No nested repositories | A repository never contains another repository's `.git` |
| `bin`/`obj` gitignored | Per project, per worktree |

**CURRENT deviation:** `pack-local.ps1`, `set-openai-key.ps1` and the `*_PROMPTS_*.md` files sit at
the repository root rather than in `scripts/` and `docs/`. That is drift, corrected opportunistically
when those files are next edited — not as standalone churn.

---

## 5. Solutions

One `.slnx` per repository, at the repository root, named for the repository —
`NAMING_STANDARDS.md` §2.

| Repository | Solution | Note |
|---|---|---|
| `Nexus.Platform` | `Nexus.Platform.slnx` | Previously `Nexus.AI.slnx`; renamed with the repository (2026-08-24). |
| `Nexus.Intelligence` | `Nexus.Intelligence.slnx` | Previously `Nexus.Int.slnx`; renamed with the repository (2026-08-24). |
| `Nexus.Experience` | `Nexus.Experience.slnx` | Previously `Nexus.Web.slnx`; renamed with the repository (2026-08-24). |

`.slnx` is the XML solution format. **Do not add `.sln` files.** A repository with both formats will
have two disagreeing project lists within a month, and the one the build agent picks is not the one
the developer edited.

Every project in `src/` and `tests/` belongs to the repository's solution. A project that builds but
is not in the solution is a project that no full-solution build ever compiles, which is exactly how
the husks in §3.2 came to exist.

---

## 6. Build and package files at repository root

| File | Owns | Present in |
|---|---|---|
| `Directory.Build.props` | MSBuild properties applied to every project — target framework, nullable, language version, analyzer settings | All three |
| `global.json` | The pinned .NET SDK version. `dotnet` refuses to build with a different one | All three |
| `nuget.config` | Package sources | All three |
| `.gitignore` | Includes `bin`, `obj`, the husk directories, `.env.local`, local settings | All three |
| `pack-local.ps1` | Packs and pushes to `C:\Personal\LocalNuGet` | `Nexus.Platform`, `Nexus.Intelligence` |
| `set-openai-key.ps1` | Sets the OpenAI key locally | `Nexus.Platform` |

**`global.json` is the reason a developer does not choose an SDK version.** Install what it pins;
`DEVELOPER_ONBOARDING.md` §3 gives the command that reads it.

**`nuget.config` currently points at `C:\Personal\LocalNuGet`.** This is **CURRENT and structurally
temporary**: a local file feed is unreachable from any build agent, so the first pipeline written
against today's `nuget.config` fails to restore. **TARGET — M-08-1.1 Package feed reachable from
CI** replaces it with GitHub Packages. `LOCAL_DEVELOPMENT.md` §3 owns the local package flow;
`STACK_VERSION_POLICY.md` owns what may be pinned where.

Version pinning belongs in `Directory.Build.props` and the `.csproj` files, not in this document —
see `STACK_VERSION_POLICY.md` §2.

---

## 7. Where tests live

`tests/<ProductionProjectName>.Tests`, `.IntegrationTests`, `.Architecture.Tests` or
`.Assurance.Tests` — `ASSURANCE_STANDARDS.md` §9 owns the suffix meanings, the folder-mirroring rule
and the deliberate advice *not* to create test projects to satisfy a naming table.

**CURRENT — five test files across three repositories, of which exactly two are behaviour tests:**

| Repository | Project | Files |
|---|---|---|
| `Nexus.Platform` | `Nexus.Platform.Architecture.Tests` | `PlatformBoundaryTests.cs` |
| `Nexus.Platform` | `Nexus.Platform.Tests` | **none — a `.csproj` with zero `.cs` files** |
| `Nexus.Intelligence` | `Nexus.Intelligence.Architecture.Tests` | `BoundaryRuleTests.cs` |
| `Nexus.Intelligence` | `Nexus.Intelligence.Tests` | `Ranking/KeywordContextRankerTests.cs` |
| `Nexus.Experience` | `Nexus.Products.Chat.Architecture.Tests` | `BoundaryTests.cs` |
| `Nexus.Experience` | `Nexus.Products.Chat.Tests` | `Chat/ChatContextBundleMapperTests.cs` |

There are **zero frontend tests**. `Nexus.Experience.Client` has no test framework at all —
`TYPESCRIPT_REACT_STANDARDS.md` §19.

The three architecture test projects are the only mechanical enforcement of anything in this
document. They are why "put it in the right project" is a rule with teeth rather than a preference.

---

## 8. Where documentation lives

| Location | Holds |
|---|---|
| `C:\Personal\Nexus.Platform\docs\` | The numbered canonical set 00–12, `README.md`, the ADR series, architecture, migration and incident records |
| Repository root of `Nexus.Platform` | The `*_PROMPTS_*.md` working files and `nexus-v2-restructure.ps1` — drift, per §4 |
| A product repository's `docs\` | Only what is specific to that product |

**One documentation home.** The canonical set lives in the Platform repository because it describes
the system, not one repository. A standard duplicated into a product repository is a standard that
will disagree with itself.

**TARGET — M-02-2.1 Document store.** Documents become `Document`/`DocumentVersion` records in the
DATA layer, and the acceptance criterion is explicit that the architecture set is stored as a
`Document` and linked to the milestones it describes. Until then the filesystem is the store, git is
the version history, and this document set is authoritative.

Naming — `NAMING_STANDARDS.md` §42. ADRs use **one global sequence**; ADR-014 and ADR-015 exist, so
the next is **ADR-016**. There is no per-repository ADR numbering.

---

## 9. Where infrastructure lives

**Nothing exists.** No Dockerfile, no compose file, no Bicep, no Terraform, no environment
definition, no deployment pipeline, in any repository. `.github/workflows/` exists in `NexusAI` and
is empty; `Nexus.Int` and `Nexus.Web` have no `.github` directory at all.

**TARGET.** Infrastructure lives in `infra/` in the repository that owns the deployable, and
pipelines live in `.github/workflows/` in each repository:

| Milestone | Brings |
|---|---|
| **M-08-1.1** Package feed reachable from CI | The precondition for every pipeline |
| **M-08-1.2** Pipelines on every repository | The first `.github/workflows/` content |
| **M-08-1.4** Branch protection and architecture gate | Makes the pipeline a gate rather than a report |
| **M-08-4.1** Environment model | What an environment *is*; until this exists there is nothing for IaC to describe |
| **M-08-4.2** Provisioning | The first `infra/` content |

Container tooling and any cloud provider beyond Azure SQL are **NOT SELECTED** —
`TECHNOLOGY_STACK.md` §7. Do not add a Dockerfile to a repository to "get started"; it fixes the
wrong variable before M-08-4.1 decides the environment model.

---

## 10. What belongs where

| Artefact | Repository | Directory |
|---|---|---|
| A universal abstraction with no implementation | `Nexus.Platform` | `src/Nexus.Platform.Contracts/<Area>/` |
| A default implementation of a Platform contract | `Nexus.Platform` | `src/Nexus.Platform.Core/<Area>/` |
| A vendor adapter | `Nexus.Platform` | `src/Nexus.Platform.Providers.<Vendor>/` |
| Anything in the turn pipeline | `Nexus.Intelligence` | `src/Nexus.Intelligence.Core/Turns/` |
| Ranking or prompt assembly | `Nexus.Intelligence` | `src/Nexus.Intelligence.Context/` |
| An agent | `Nexus.Intelligence` | `src/Nexus.Intelligence.Agents/BuiltIn/` |
| A product aggregate | the product's repository | `src/Nexus.Products.<Name>.Domain/<Aggregate>/` |
| An EF configuration, repository or migration | the product's repository | `src/Nexus.Products.<Name>.Infrastructure/Sql/` |
| An HTTP endpoint | the owning `.Api` project | `Endpoints/<Name>Endpoint.cs` |
| A React feature | `Nexus.Experience` | `Nexus.Experience.Client/src/features/<feature>/` |
| A standard or ADR | `Nexus.Platform` | `docs/` |
| A PowerShell script | the repository it operates on | `scripts/` (CURRENT: repository root) |

`NEW_MODULE_GUIDE.md` turns each row into a numbered procedure. This table is the index; that
document is the instruction.

---

## 11. What must NEVER belong anywhere

| Never committed | Why |
|---|---|
| A secret, key, connection string with credentials, or token | `SECURITY_STANDARDS.md` §5; `GIT_WORKFLOW.md` §12 covers what to do when one lands anyway |
| Build output — `bin`, `obj`, `.dll`, `.nupkg` | It is regenerable, it is large, and it makes every merge a conflict |
| The `LocalNuGet` feed | It is build output. It must never be made a git repository |
| A second solution format (`.sln`) | §5 |
| A product type inside `Nexus.Platform.Contracts` or `Nexus.Intelligence.Contracts` | **No shared kernel.** This is currently true and is enforced by the architecture tests |
| A reference from one product to another | Products never reference each other |
| A reference from a lower layer to a higher one | Dependency direction; NetArchTest enforces it |
| `if (Product == X)` branching in layers 01–11 | Capability packs are declared, not coded — `PRODUCT_DEVELOPMENT_GUIDE.md` §3 |
| A file inside a gitignored husk directory | §3.2 — the work is invisible to git |
| Anything inside `.git-broken\` | It is forensic residue of the 2026-08-20 incident, kept deliberately — `GIT_WORKFLOW.md` §2 |
| A nested `.git` | §4 |

The first four are hygiene. The middle five are the architecture. **A shared kernel is the failure
mode this system is most exposed to**, because it always arrives as a small convenience: one product
type in Contracts to avoid a mapper. The mapper is the point.

---

## 12. The rename path

The three renames are **DONE** as of 2026-08-24; this section records the path that was taken.

```
NexusAI      →  Nexus.Platform      (local dir, remote, Nexus.AI.slnx → Nexus.Platform.slnx)
Nexus.Int    →  Nexus.Intelligence  (local dir, remote, Nexus.Int.slnx → Nexus.Intelligence.slnx)
Nexus.Web    →  Nexus.Experience    (local dir, remote, solution; Chat product extracted out)
                Nexus.Web.Client    →  Nexus.Experience.Client
new          →  Nexus.Developer
new          →  Nexus.Products.<Name>   one per product
```

What the rename touched — recorded here in the order it breaks if done partially:

1. The GitHub remote name and every clone's `origin` URL.
2. The local directory name — and therefore every worktree path and every `nuget.config` relative
   path.
3. The `.slnx` filename and every build command, script and future pipeline that names it.
4. Package identities produced by `pack-local.ps1`, and every `PackageReference` consuming them.
5. `Nexus.Web.Client` → `Nexus.Experience.Client`, and its project reference.
6. Every documentation path in this set.

**Rules that governed the transition.** Both the local directory and the remote change in the *same*
change — a repository whose remote name and directory name disagree is how the previous three-way
naming mess started. The rule was to push before starting and verify the remote after finishing,
because all three repositories lost `.git\objects` on 2026-08-20 and the root cause has never been
confirmed fixed (`GIT_WORKFLOW.md` §2). Extracting the Chat product into its own `Nexus.Products.Chat`
repository is a **separate** work item from the rename and remains pending; bundling the two would
have produced a diff nobody could review.

`Nexus.Developer` is created empty when DEVELOPER's first milestone (M-07-1.1 Work graph aggregates)
starts, not before. An empty repository is the same lie as an empty test project.

---

## 13. Open decisions

| Question | Decided by | State |
|---|---|---|
| Whether the eight `NexusAI.*` husks are deleted or left inert | The `NexusAI` → `Nexus.Platform` rename (2026-08-24) — it did not settle this | **Not yet decided** — §3.2 |
| Whether `Nexus.Platform.Tests` keeps its name | Whichever comes first: tests, or the rename | Not yet decided — `ASSURANCE_STANDARDS.md` §14 |
| Whether each product gets one repository or products share one | The second product. One product is not a pattern | Not yet decided |
| Where `IProductRegistry` finally lives | It sits in `Nexus.Platform.Contracts/Identity/` and conceptually belongs to GOVERNANCE | Moves when GOVERNANCE is built |
| Whether `infra/` is per-repository or its own repository | **M-08-4.1** Environment model | Not yet decided |
| Ordering of the `Nexus.Web` split versus its rename | History: the rename ran first (2026-08-24); the Chat-product split is still pending | Split pending — §3.4 |

---

## 14. References

- `NAMING_STANDARDS.md` — the naming rules for every artefact named in this document.
- `GIT_WORKFLOW.md` — branches, worktrees, commits, the 2026-08-20 incident, recovery.
- `DEVELOPER_ONBOARDING.md` — cloning these repositories and building them for the first time.
- `LOCAL_DEVELOPMENT.md` — the running topology on one machine.
- `NEW_MODULE_GUIDE.md` — the procedure for adding anything to any of these repositories.
- `PRODUCT_DEVELOPMENT_GUIDE.md` — creating `Nexus.Products.<Name>`.
- `TECHNOLOGY_STACK.md` / `STACK_VERSION_POLICY.md` — what the projects reference and how it is
  pinned.
- `ASSURANCE_STANDARDS.md` §9 — test project structure and the advice against empty test projects.
- `DATABASE_STANDARDS.md` §2 — the physical database strategy behind the schema column in §2.
