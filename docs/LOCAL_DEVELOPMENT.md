# Local Development

**Status:** CURRENT, with a correction layered on top. This described the exact local topology on
2026-08-21, when the canonical root was `C:\Personal\` and package restore depended on a local
folder-based feed. **Both have since changed: the canonical active root is now `D:\NEXUS\` (`C:\Personal`
is historical/rollback only — see `POST_GATE_A_C_PERSONAL_RETIREMENT_REPORT.md`), and `M-08-1.1`
(GitHub Packages feed reachable from CI) is done** — every repository restores `Nexus.Platform.*`/
`Nexus.Intelligence.*` packages from GitHub Packages, not a local folder. Paths below are corrected;
the rest of the transitional detail (LocalDB, no event bus, secrets mechanism, etc.) is unaffected
by the root/feed change and remains as originally written. Every remaining transitional decision is
still marked TRANSITION with the milestone that ends it.
**Owner:** DELIVERY (Layer 07 -- renumbered from 08, see LAYER_MODEL.md §2.2)
**Last updated:** 2026-09-09 (path/feed correction only; body text otherwise dated 2026-08-21)
**Layer:** 07 DELIVERY (renumbered from 08, see LAYER_MODEL.md §2.2)
**Authoritative for:** the local machine topology — where the repositories sit, how packages flow,
which ports are bound, how the database is reached, where local secrets come from, how the processes
communicate, the order they start in, how to stop and restart them, how to debug across them, and
the local failure modes and their causes.

Not authoritative for: installing any of it the first time — `DEVELOPER_ONBOARDING.md`; what lives
in which repository — `REPOSITORY_STRUCTURE.md`; the configuration hierarchy and what may be
committed — `CONFIGURATION_STANDARDS.md`; connection string rules — `DATABASE_STANDARDS.md`;
worktree mechanics — `GIT_WORKFLOW.md` §5.

---

## 1. The topology, in one picture

```
                     D:\NEXUS\
                        │
  ┌─────────────────────┼──────────────────────┬──────────────────────┐
  │                     │                      │                      │
Platform             Intelligence         Products\Experience   GitHub Packages
                                                                  (feed; not local)
  │                     │                      │                      ▲
  │ dotnet restore ─────┴──────────────────────┴──────────────────────┘

  Browser
     │  http
     ▼
  Nexus.Experience.Client     (Vite dev server, port printed at startup)
     │  VITE_ base URL
     ▼
  Nexus.Products.Chat.Api     http://localhost:5299        /api/v1
     │                             │
     │ IIntelligenceClient         │ EF Core / Microsoft.Data.SqlClient
     ▼                             ▼
  Nexus.Intelligence.Api      SQL Server LocalDB  (MSSQLLocalDB)
  /intelligence/v1                 NexusChatDbContext
  port: read launchSettings.json
     │
     ▼
  Nexus.Platform.Providers.OpenAI  →  OpenAI  (key via set-openai-key.ps1)
```

Three .NET processes at most, one Node process, and one LocalDB instance. Nothing is containerised,
nothing is orchestrated, and nothing is deployed anywhere.

(Historical: before `M-08-1.1`, the package feed was a local folder at `C:\Personal\LocalNuGet`,
populated by a manual `pack-local.ps1` run per repository — "a folder pretending to be a package
feed." That folder-feed path is retired for normal use now that GitHub Packages is reachable from
CI — see §3 — though one interim bridge script, `pack-productcore-local.ps1`, still writes there for
`Nexus.Developer`'s transition; do not treat it as the canonical flow.)

---

## 2. Repository locations

| Path | Repository | Solution | Purpose locally |
|---|---|---|---|
| `D:\NEXUS\Platform` | `github.com/prtcare/Nexus.Platform` | `Nexus.Platform.slnx` | Produces the `Nexus.Platform.*` packages the other two consume |
| `D:\NEXUS\Intelligence` | `github.com/prtcare/Nexus.Intelligence` | `Nexus.Intelligence.slnx` | Produces `Nexus.Intelligence.*` packages; hosts the Intelligence API |
| `D:\NEXUS\Products\Experience` | `github.com/prtcare/Nexus.Experience` | `Nexus.Experience.slnx` | Hosts the Chat API and the web client |
| `D:\NEXUS\Products\Developer` | `github.com/prtcare/Nexus.Developer` | `Nexus.Developer.slnx` | The Developer product; consumes `Nexus.ProductCore.Contracts` |
| `D:\NEXUS\<Repo>\.forge\worktrees\<name>\` | — | — | Governed worktrees, siblings of the repository's own tree |

**`D:\NEXUS` is the canonical active root** (see `POST_GATE_A_C_PERSONAL_RETIREMENT_REPORT.md` for the
full retirement record). `nuget.config` in each repository now names only `nuget.org` and the
`github-prtcare` GitHub Packages source — no repository's `nuget.config` names a local machine path.
Cloning to a different drive/location works normally; nothing in the current setup is path-load-bearing
the way the old `C:\Personal\LocalNuGet` folder feed was.

`C:\Personal` itself remains on disk as a historical/rollback copy only (do not develop there); any
`.git-broken\` forensic-residue folders mentioned in older revisions of this document lived under that
retired root and are not part of the current `D:\NEXUS` topology.

---

## 3. The local package strategy

**`M-08-1.1` DONE (2026-09-09).** GitHub Packages is the live feed; the local-folder mechanism
described below is historical context for why the current shape was chosen, not the current
mechanism.

```
Nexus.Platform      ── dotnet pack + push ──►  GitHub Packages (github-prtcare)  ◄── nuget.config ── Nexus.Developer
Nexus.Intelligence  ── dotnet pack + push ──►                                    ◄── nuget.config ── Nexus.Experience
```

`Nexus.Platform.*` and `Nexus.Intelligence.*` are consumed as **NuGet packages**, not as project
references across repository boundaries. That keeps the repositories genuinely independent, and it
is now also reachable from a hosted build agent — the original gap this section described.

| Property (historical, pre-`M-08-1.1`) | Consequence | Current state |
|---|---|---|
| The feed was a folder on one machine | No build agent could see it | Fixed — GitHub Packages is reachable from every repo's CI |
| Publishing was a manual script run | Nothing recorded what was published, when, or from which commit | Publishing still happens via a script (`dotnet pack`/`nuget push`, or CI), but the destination is a real, addressable feed with package history |
| Versions were whatever the producing repository said | A consumer could silently resolve a stale package | Unchanged — still version the package deliberately when you change a public contract |
| Two workers packing at once raced | Packing was a **serialised, announced** operation — `GIT_WORKFLOW.md` §5.3 | Still good practice; less likely to corrupt a shared folder now that the feed isn't one |

**Working rule (unchanged):** when you change anything in `Nexus.Platform.*` or
`Nexus.Intelligence.*` that a consumer uses, you must pack and push a new version before the consumer
will see it. "It works in my solution but not in the other repository" is nearly always an unpublished
change.

```powershell
cd D:\NEXUS\Platform      ; .\pack-productcore-local.ps1   # or CI's publish step
cd D:\NEXUS\Intelligence  ; <repo's own pack/publish script>
cd D:\NEXUS\Products\Experience ; dotnet restore Nexus.Experience.slnx
```

---

## 4. Ports

| Service | Address | Source of truth |
|---|---|---|
| **Chat API** (`Nexus.Products.Chat.Api`) | **`http://localhost:5299`** | Its `launchSettings.json`, confirmed in `api_run.log` |
| **Intelligence API** (`Nexus.Intelligence.Api`) | **Read its own `launchSettings.json`** | Not recorded here — the port has not been verified |
| **Vite dev server** (`Nexus.Experience.Client`) | Printed by Vite at startup | Vite's own output |

**The Intelligence port is deliberately absent from this document.** It has a `launchSettings.json`
of its own; read the value from there. A guessed port in a topology document is worse than a missing
one, because a reader will trust it and spend an hour on a connection error that documentation
caused.

Route prefixes: Chat serves `/api/v1`, Intelligence serves `/intelligence/v1` —
`API_STANDARDS.md` §3.

**Parallel workers.** If two worktrees run a host at once, the second needs its own port. Change it
in that worktree's local profile and **do not commit it** — `launchSettings.json` is committed
because ports are shared knowledge, which is precisely why a per-worker port must not go into it.
`CONFIGURATION_STANDARDS.md` §7.1.

---

## 5. Database connections

**SQL Server LocalDB**, instance `MSSQLLocalDB`, reached through `Microsoft.Data.SqlClient` under EF
Core.

```powershell
sqllocaldb info
sqllocaldb start MSSQLLocalDB
```

| Fact | Value |
|---|---|
| Context | `NexusChatDbContext` in `Nexus.Products.Chat.Infrastructure/Sql/` |
| Design-time factory | `NexusChatDbContextFactory` — lets `dotnet ef` build the context without the host |
| Migration on disk | `20260820180802_InitialSqlSchema.cs` |
| Schema used by that migration | **`org`** |
| Applying migrations | Manual: `dotnet ef database update` |

**TRANSITION — the schema.** The one migration that exists uses schema `org`. The convention is one
schema per layer (`core`, `data`, `governance`, `ai`, `automation`, `product_core`, `developer`,
`delivery`, `assurance`, `operations`, `experience`) plus one database per product. **M-02-1.5 Layer
schema convention** closes the gap. Until it lands, `org` is what is on your machine, and **nothing
new goes into it** — `DATABASE_STANDARDS.md` §2.

Where the connection string lives, and the rule that it never carries credentials into Git, are
`CONFIGURATION_STANDARDS.md` §3 and `SECURITY_STANDARDS.md` §5.

**Each worktree needs its own database.** Two `dotnet ef database update` runs against one database
interleave and leave a migration history table that disagrees with the model snapshot. This is the
isolation dimension that is missed most often.

---

## 6. Local secrets

| Secret | CURRENT mechanism | TARGET |
|---|---|---|
| OpenAI API key | `set-openai-key.ps1` in `D:\NEXUS\Platform` | **M-01-5.1** — resolved through `ISecretResolver` |
| Anything else | User secrets (`dotnet user-secrets`), per project, never committed | Same |
| Frontend values | `.env.local`, `VITE_` prefix, never committed | Same |

`ISecretResolver` exists today only as a contract in `Nexus.Platform.Contracts/Secrets/`. There is
no production implementation. Write new code against the interface where you can; do not build a
second ad-hoc key-reading path beside the script.

**Provider credentials never leave CORE.** Intelligence never holds an API key — it calls
`IModelGateway`, and the gateway implementation in `Nexus.Platform.Providers.OpenAI` holds the
credential. This is an architectural invariant, not a local convenience —
`AI_DEVELOPMENT_STANDARDS.md` §2.

---

## 7. How the services communicate

| From | To | Mechanism |
|---|---|---|
| Browser | Chat API | HTTP, base URL from `VITE_`-prefixed config via `src/config/environment.ts`, through `api/ApiClient.ts` |
| Chat API | Intelligence API | HTTP through `IIntelligenceClient` (`Nexus.Intelligence.Contracts/Client/`) |
| Chat API | LocalDB | EF Core / `Microsoft.Data.SqlClient` |
| Intelligence API | OpenAI | `IModelGateway` → `RoutingModelGateway` → `Nexus.Platform.Providers.OpenAI` |
| Any process | Any other | **Nothing else.** No message broker, no event bus, no queue |

There is **no in-process event bus** (that is M-01-8.1) and **no message broker** (NOT SELECTED —
`TECHNOLOGY_STACK.md` §7). Every interaction above is a synchronous HTTP call or a database call.
If your design needs asynchrony, it needs a milestone first.

**The seam to protect.** The Chat product flattens its aggregates into `ContextItem` values and
hands Intelligence a `ContextBundle`; `ScopeRef` is opaque to Intelligence. `ChatContextBundleMapper`
is where that happens, and it is one of only two things in the system with a behaviour test. Do not
make the Intelligence call easier by passing a product type across it.

---

## 8. Startup order

For the full stack:

1. `sqllocaldb start MSSQLLocalDB`
2. `dotnet ef database update` — only if migrations have changed since your last run
3. **Intelligence API** — `dotnet run --project src\Nexus.Intelligence.Api` from `D:\NEXUS\Intelligence`
4. **Chat API** — `dotnet run --project src\Nexus.Products.Chat.Api` from `D:\NEXUS\Products\Experience`
5. **Vite** — `npm run dev` from `D:\NEXUS\Products\Experience\src\Nexus.Experience.Client`

Order 3 before 4 only matters for the first chat turn; the Chat API starts fine without Intelligence
running and fails at the point a turn is dispatched. That failure mode is worth knowing: **a chat
turn that errors while every other endpoint works usually means Intelligence is not running**, not
that the chat path is broken.

You do not need the whole stack for most work:

| Working on | Run |
|---|---|
| A Chat endpoint, aggregate or migration | LocalDB + Chat API |
| Ranking, prompt assembly, the turn pipeline | Intelligence API alone; drive it with Swagger |
| A frontend feature against existing endpoints | Chat API + Vite |
| A live model turn end to end | Everything, plus a key, plus OpenAI credit |
| Platform contracts or gateways | Build only; then `pack-local.ps1` and rebuild a consumer |

---

## 9. Stopping and restarting

| Action | How |
|---|---|
| Stop a host | `Ctrl+C` in its window |
| Stop Vite | `Ctrl+C` |
| Restart after a C# change | Stop and `dotnet run` again — there is no watch configured |
| Restart after a frontend change | Nothing — Vite hot-reloads |
| Restart after a package change | `pack-local.ps1` in the producer, then restart the consumer |
| Stop LocalDB | `sqllocaldb stop MSSQLLocalDB` — rarely needed |
| Reset the database | Drop it and `dotnet ef database update` again. There is no reset script and no seed data |

**No feature flags, no hot configuration reload.** Any configuration change requires a restart.
Runtime flags are **TARGET — M-10-5.1**, whose acceptance criterion is explicitly that a flag change
takes effect without a restart.

---

## 10. Debugging

| Situation | Approach |
|---|---|
| One host | Attach the IDE debugger to the `.Api` project directly |
| Chat → Intelligence | Two IDE instances, or start one host from the CLI and debug the other |
| The frontend | Browser devtools; the network tab shows the exact `ApiClient` request |
| A model call | Break in the gateway implementation. **Do not add a log line that prints the prompt** |
| A migration | `dotnet ef migrations script` to see the DDL before applying it |
| A repository query | EF Core's generated SQL — `api_run.log` shows the pattern, e.g. `INSERT INTO [org].[Workspace] (...) OUTPUT INSERTED.[Ref], INSERTED.[Seq] VALUES (...)` |

**There is no cross-process correlation.** No logging library is selected at all — no Serilog, no
NLog, nothing (`TECHNOLOGY_STACK.md` §7). You cannot follow one request from the browser through the
Chat API into an Intelligence turn and out to a model call by an id, because there is no id and
nowhere to write it. **TARGET — M-10-1.1 Correlation across hosts**, whose acceptance criteria are
exactly that: a correlation id from the edge, propagated through Experience, the turn and the model
invocation; one request retrievable end to end; and no log line containing a secret, a token or a
full prompt body.

Until then, debugging across the two APIs is manual: match by timestamp and payload shape.

**The prompt-logging prohibition applies while debugging.** It is not a production-only rule. A
prompt printed to a console during debugging ends up in a screenshot, a paste or a captured log —
`SECURITY_STANDARDS.md` §11.

---

## 11. Common failures

| Symptom | Cause | Action |
|---|---|---|
| `warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3] Failed to determine the https port for redirect.` | HTTPS redirection middleware is registered; no HTTPS port is bound in the development profile | **None. Expected and harmless.** It appears on every local run and is not a defect. Do not remove the middleware, and do not add an HTTPS port to silence it |
| Restore cannot find `Nexus.Platform.*` / `Nexus.Intelligence.*` | The producing repository has not been packed since the version changed | `pack-local.ps1` there, then restore |
| A consumer builds against stale behaviour | Same cause; the old package version is still resolving | Pack, then `dotnet restore` — clear the local NuGet cache if the version number did not change |
| `dotnet` refuses to build, mentions `global.json` | The pinned SDK is not installed | Install it. Do not edit the pin |
| Chat turn fails, other endpoints fine | Intelligence API is not running | Start it. §8 |
| Model call fails or returns nothing | No key, or no OpenAI credit | `set-openai-key.ps1`. The credit block is a known outstanding item that has held up the citation and usage-metering verifications |
| Migration history disagrees with the model snapshot | Two worktrees updated the same LocalDB database | One database per worktree; drop and re-apply |
| SQL Server error 1785 on a migration | Multiple cascade paths | Only the owning parent cascades; reference FKs `Restrict`, self-references `NoAction` — `DATABASE_STANDARDS.md` §5.3 |
| A new table lands in schema `org` | Copied an existing configuration | Set the schema explicitly. `org` is legacy |
| Frontend network errors | `VITE_` base URL not `http://localhost:5299` | Fix `.env.local` |
| Port already in use | A previous host is still running, or two worktrees on one port | Stop it, or give the worktree its own port (uncommitted) |
| A worktree cannot be removed or renamed | It is nested inside a running agent's working directory | Windows file lock. Recreate as a **sibling** — `GIT_WORKFLOW.md` §5.1 |
| `.git\objects` missing across repositories | The 2026-08-20 failure mode | Stop. `GIT_WORKFLOW.md` §2 before touching anything |

---

## 12. Transitional decisions, listed together

Everything on this machine that is deliberately temporary, so nobody builds on it as though it were
settled.

| # | CURRENT | TARGET | Milestone |
|---|---|---|---|
| 1 | ~~`nuget.config` → `C:\Personal\LocalNuGet`, packed by hand~~ **DONE** | GitHub Packages, reachable from CI | **M-08-1.1** ✅ 2026-09-09 |
| 2 | OpenAI key via `set-openai-key.ps1` | Resolved through `ISecretResolver` | **M-01-5.1** |
| 3 | Migration in schema `org` | One schema per layer; one database per product | **M-02-1.5** |
| 4 | Dataverse packages still referenced | Azure SQL only; ~7.2 MB of packages removed | ADR-014 **Stage 3** |
| 5 | `InMemoryTurnTraceStore`, `InMemoryResultReportStore`, `InMemoryMemoryStore` | Durable stores | **M-04-1.1**, **M-04-1.2** |
| 6 | `EmptyToolCatalog`, `EmptyToolGateway` | A real tool registry and gateway | **M-01-7.1** |
| 7 | No logging library, no correlation | Correlation across hosts | **M-10-1.1** |
| 8 | No CI; nothing verifies a branch | Pipelines, then branch protection | **M-08-1.2**, **M-08-1.4** |
| 9 | `Nexus.Experience` holds both the Chat product and the client | `Nexus.Experience` + `Nexus.Products.Chat` | Repository split — `REPOSITORY_STRUCTURE.md` §12 |
| 10 | No authentication, no authorization; hardcoded tenant | Real identity and permission evaluation | **M-01-1.2**, **M-01-3.1** |
| 11 | Restart required for every configuration change | Runtime feature flags without restart | **M-10-5.1** |

Rows 5, 6 and 10 are the ones that most often mislead: the *shapes* are right and the
*implementations* are placeholders. Code that compiles against `IMemoryStore` is correct; code that
assumes memory survives a restart is not.

---

## 13. References

- `DEVELOPER_ONBOARDING.md` — installing and configuring all of this the first time.
- `REPOSITORY_STRUCTURE.md` — what is in each repository and the rename path.
- `CONFIGURATION_STANDARDS.md` — the configuration hierarchy, user secrets, `.env.local`, what may
  be committed.
- `DATABASE_STANDARDS.md` — schemas, migrations, the Id/Seq/Ref pattern, cascade behaviour.
- `SECURITY_STANDARDS.md` — secrets, and the prohibition on logging prompts, tokens and secrets.
- `GIT_WORKFLOW.md` — worktrees, the sibling-directory rule, the 2026-08-20 incident.
- `AI_DEVELOPMENT_STANDARDS.md` — the Intelligence contracts and the ContextBundle seam.
- `TECHNOLOGY_STACK.md` — what is in use, what is being removed, what is not selected.
