# Configuration Standards

**Status:** Active
**Owner:** DELIVERY (Layer 07 -- renumbered from 08, see LAYER_MODEL.md §2.2), with GOVERNANCE (03) owning the registry from M-03-6.1
**Last updated:** 2026-09-22 (W9.1 — CURRENT blocks corrected against verified code and measurement; see the runtime-configuration note below)
**Layer:** 07 DELIVERY — binding on every host and every client (renumbered from 08, see LAYER_MODEL.md §2.2)
**Authoritative for:** the configuration hierarchy, `appsettings` files, environment overrides,
environment variables, secret references, local developer configuration, feature flags, product
settings, runtime configuration, what may enter Git, what must never enter Git, naming, validation
and versioning.

Not authoritative for: how a secret is stored, resolved or rotated — `SECURITY_STANDARDS.md`;
connection string content and database targeting rules — `DATABASE_STANDARDS.md`; base URLs as an
API contract — `API_STANDARDS.md`; what may be committed as a git question — `GIT_WORKFLOW.md`.

---

## 1. Position

Configuration is everything that varies between one running instance and another. Anything that does
not vary is a constant and belongs in code, where it is type-checked and refactorable.

Two rules dominate everything below:

1. **A secret is never configuration.** It is a *reference* in configuration and a value resolved at
   runtime. See §6.
2. **A missing or invalid configuration value fails at startup**, loudly, with the key name — never
   at the first request that happens to need it. See §11.

---

## 2. The hierarchy

Later sources override earlier ones. This is ASP.NET Core's standard order and Nexus does not alter
it.

| # | Source | Committed | Environment | Purpose |
|---|---|---|---|---|
| 1 | `appsettings.json` | Yes | All | Defaults, and the complete key inventory |
| 2 | `appsettings.<Environment>.json` | Yes | One | Non-secret per-environment overrides |
| 3 | User secrets | **No** | Development only | Developer-local values |
| 4 | Environment variables | No | All | Deployed overrides, container and CI values |
| 5 | Command line | No | All | One-off overrides for a single run |
| 6 | `ISecretResolver` | No | All | **Every secret** — see §6 |

Source 1 is the inventory. Every key the application reads appears in `appsettings.json`, even when
its committed value is a placeholder or empty. A key that appears only in an environment variable is
a key nobody can discover without reading the source.

Frontend configuration is a separate mechanism entirely (§5).

---

## 3. `appsettings` files

### 3.1 What is verified

The verified inventory of repository configuration files confirms `Directory.Build.props`,
`global.json` and `nuget.config` per repository, `launchSettings.json` per host, and
`config/environment.ts` in the frontend. It does **not** enumerate the `appsettings.json` files
themselves.

`appsettings.json` is the ASP.NET Core default and every host reads it; the standards below apply to
those files. Before writing a script, a pipeline step or a document that names a specific
`appsettings` path, **read the host's project folder and confirm the file set**. Do not assume the
presence of `appsettings.Production.json` or `appsettings.Test.json` — those environments do not
exist yet (`SECURITY_STANDARDS.md` §13.2).

### 3.2 Rules

| Rule | Statement |
|---|---|
| One base file per host | `appsettings.json`, committed |
| Environment overrides | `appsettings.<Environment>.json`, matching `ASPNETCORE_ENVIRONMENT` exactly, including case |
| Complete inventory | Every key the host reads is present in the base file |
| Placeholders, not secrets | A secret-shaped key holds a reference or an empty value, never a value |
| Overrides are sparse | An environment file contains only what differs, never a full copy |
| Sectioned | Keys are grouped under a named section per concern |
| No environment detection in code | Behaviour varies by configuration value, not by `if (env == "Production")` |

That last rule matters more than it looks. `if (env.IsDevelopment())` scattered through a codebase
means production behaviour is only observable in production. Make the behaviour a configuration key
with a per-environment value, and the difference becomes visible in a diff.

### 3.3 Section naming

```json
{
  "Nexus": {
    "Platform": { },
    "Intelligence": { },
    "Data": { }
  }
}
```

| Rule | Statement |
|---|---|
| Root | `Nexus`, then the layer or component |
| Case | PascalCase, matching the C# options class property names |
| Depth | Three levels maximum |
| Units in the name | `TimeoutSeconds`, `MaxRetryCount`, `CacheDurationMinutes` — never bare `Timeout` |
| Booleans read as true | `EnableSwagger`, not `SwaggerDisabled` — negative flags invert in the reader's head |

The units rule is the same one that governs database columns (`DATABASE_STANDARDS.md` §11.4). A key
named `Timeout` with the value `30` has been read as seconds and as milliseconds by different people
on the same team, and both were reasonable.

### 3.4 Binding

Configuration is bound to a strongly-typed options class per section — never read key-by-key with
string literals scattered through the code. The options class is validated at startup (§11), and
nothing outside the options class knows the key names.

---

## 4. Environment variables

| Rule | Statement |
|---|---|
| Separator | `__` (double underscore) maps to `:` in the configuration hierarchy |
| Prefix, .NET hosts | `NEXUS_` for Nexus-owned values |
| Prefix, frontend | **`VITE_`** — required by Vite, and confirmed in use |
| Case | Upper snake case: `NEXUS_PLATFORM__DATABASE__COMMANDTIMEOUTSECONDS` |
| Framework variables | `ASPNETCORE_ENVIRONMENT` is the framework's; do not rename or shadow it |
| Never a secret value | An environment variable may hold a secret *reference*; the value comes from `ISecretResolver` (§6) |
| Documented | Every variable a host requires is listed in that host's README |

`ASPNETCORE_ENVIRONMENT` is set to `Development` in `launchSettings.json` for the Intelligence host,
which is the confirmed pattern for local runs.

---

## 5. Frontend configuration

**CURRENT.** `Nexus.Experience.Client/src/config/environment.ts` is the single place the React client reads
configuration, and the `VITE_` prefix is confirmed in use.

| Rule | Statement |
|---|---|
| One module | Every environment value is read in `config/environment.ts`, nowhere else |
| Prefix | `VITE_` — Vite only exposes variables with this prefix to client code |
| Typed | The module exports a typed object; components never touch `import.meta.env` |
| Validated at load | A missing required variable fails at startup with the variable name |
| **Build-time, not runtime** | Vite inlines these values into the bundle at build time |

### 5.1 The consequence of build-time inlining

Two things follow, and both are frequently learned the hard way:

1. **Changing a `VITE_` variable requires a rebuild.** It is not a restart-and-reload change.
2. **Every `VITE_` variable is public.** It is inlined into JavaScript that any user can read.
   A `VITE_` variable is therefore *incapable* of holding a secret. There is no such thing as a
   private frontend configuration value, and prefixing a key differently does not make it private —
   it makes it absent.

An API key that must reach a third party goes through the backend. Always.

**TARGET — W9's ruling changes (1), and only (1).** The Owner has ruled that environment-specific client
configuration must move **out of build-time compilation**: `VITE_*`, `API_INTERNAL_URL` and equivalent
environment endpoints must not require different application builds for DEV/TEST/PROD, and one
application artifact must be promotable unchanged through environments. The reason is the build-once
rule W9 exists to establish — and W9.0 measured the cost of not having it, in two places: the Experience
client's `VITE_*` values and MarketSurvey's `API_INTERNAL_URL` build argument both bake an environment
into an artifact, so promoting those units today would require a rebuild, which is exactly what the rule
forbids.

Point **(2) is unchanged and is not negotiable**: every frontend configuration value is public, so a
frontend variable remains incapable of holding a secret regardless of how the value is delivered. A
runtime configuration document fixes *when* the value is resolved; it does not make it private.

The mechanism (a runtime configuration document fetched from the client's own origin is the working
proposal) is not yet implemented, and this section will be rewritten when it is.

### 5.2 Typical values

| Value | Kind |
|---|---|
| API base URL | Public, per environment |
| Feature toggles for UI affordances | Public |
| Environment name, for display | Public |
| Anything at all secret | **Impossible — see §5.1** |

The Chat API is at `http://localhost:5095` locally. The Intelligence API is at `http://localhost:5000`.
Both were **measured from `launchSettings.json` in W9.1** — this paragraph previously said 5299, which no
host in the estate has ever served, and described the Intelligence port as unverified. Read the file
before quoting a port (`API_STANDARDS.md` §1); the numbers here are a convenience, not the authority.

---

## 6. Secret references

The rule, in one line: **configuration holds a reference to a secret; `ISecretResolver` holds the
path to its value; neither holds the value.**

```json
{
  "Nexus": {
    "Providers": {
      "OpenAI": { "ApiKeyRef": "NEXUS_OPENAI_API_KEY" }
    }
  }
}
```

The example above was `"nexus/openai/api-key"` until W9.1. The **shipped** convention is an upper-snake
environment-variable name, which is what §14 requires and what the AI Head's README documents; the
reference is resolved by `EnvironmentSecretResolver`, which reads the environment. A path-shaped example
described a mechanism the estate does not have.

| Rule | Statement |
|---|---|
| Naming | A secret reference key ends in `Ref` — `ApiKeyRef`, `SigningKeyRef`, `ConnectionStringRef` |
| Committed | The **reference** is committed; the value never is |
| Resolution | Through `ISecretResolver` — `SECURITY_STANDARDS.md` §5 |
| Never inline | A secret value in any `appsettings` file is a defect, in every environment |
| Never in a comment | Including "// old key, no longer used" |

**CURRENT — corrected in W9.1 against the code.** `ISecretResolver` is a contract in
`Nexus.Platform.Contracts/Secrets/` **and it has an implementation**: `EnvironmentSecretResolver`
(`Nexus.Platform.Core/Secrets/EnvironmentSecretResolver.cs`), roadmap item **WI-01-5.1.1**, registered by
`AddNexusPlatform` as `TryAddSingleton<ISecretResolver, EnvironmentSecretResolver>()` so that a host may
bind its own. `Nexus.Intelligence.Api` resolves `Platform:Providers:OpenAI:ApiKeyRef` through it, and the
committed value is the environment-variable **name**, never the credential. This paragraph previously
said the contract "has no implementation"; that was stale, and planning against it would have rebuilt
something that already exists. `set-openai-key.ps1` remains for local operator setup.

**TARGET — M-01-5.1 Real secret resolver**, whose acceptance criteria include: no provider key,
connection string or signing key appears in any appsettings file in any repository, and a secret scan
runs in CI and fails the build on a match. **The scanning half is now met in Platform** — see §13.1.

### 6.1 Connection strings

A connection string is a secret. It carries a server, a database and, in a deployed environment,
credentials. It is referenced, never inlined.

The local LocalDB connection string is the narrow exception: it contains no credential, names a
machine-local instance, and is useless to anyone who is not sitting at that machine. It may be
committed in `appsettings.Development.json`. **Every other connection string is a secret reference.**

Which database a given environment targets is a configuration question; the rules about what is
inside that database are `DATABASE_STANDARDS.md`.

---

## 7. Local developer configuration

**CURRENT.** Local development runs against SQL Server LocalDB, the Chat API on
`http://localhost:5299`, packages from `C:\Personal\LocalNuGet` via `nuget.config`, and the OpenAI
key via `set-openai-key.ps1`. HTTPS redirect warns "Failed to determine the https port for redirect"
— **expected local noise, not a defect**.

| Mechanism | Committed | Use |
|---|---|---|
| `appsettings.Development.json` | Yes | Shared non-secret development defaults |
| User secrets | **No** | Developer-specific values and local secrets |
| `.env.local` (frontend) | **No** | Developer-specific `VITE_` values |
| `launchSettings.json` | Yes | Profiles, ports, `ASPNETCORE_ENVIRONMENT` |
| Environment variables | No | Anything a developer overrides for one session |

`launchSettings.json` is committed because ports and profiles are shared knowledge. It therefore
**must never contain a secret**, and its `environmentVariables` block is a common place for one to
appear by accident.

### 7.1 Parallel workers

Each worker operates in its own worktree in a sibling directory (`GIT_WORKFLOW.md` §5). Each needs
its own local configuration, and none of it is committed:

| Dimension | Per-worker |
|---|---|
| Database | Its own LocalDB database name |
| Port | Its own, if two hosts run simultaneously |
| User secrets | Its own store |
| Build output | Inside its own worktree, gitignored |

Two workers sharing a LocalDB database will interleave migration history and produce a migration
history table that disagrees with the model snapshot — see `DEVELOPMENT_WORKFLOW.md` §4.

---

## 8. Feature flags

**TARGET — M-10-5.1 Runtime feature flags.** No feature flag mechanism exists today.

The milestone's outcome is exact: behaviour can be enabled per environment, tenant or member without
redeploying, and **a flag change takes effect without a restart and is audited**.

| Rule | Statement |
|---|---|
| Named for the behaviour | `EnableStreamingResponses`, not `Flag17` |
| Positive | The flag being on means the feature is on |
| Default off | A new flag defaults to off in every environment |
| Scoped | Environment, tenant or member — the scope is declared at creation |
| Audited | Every change is audited — `SECURITY_STANDARDS.md` §8.3 |
| Time-boxed | A release flag has a removal date recorded when it is created |
| Never a permission | A flag decides *whether a feature exists*; authorization decides *who may use it* |

That last row is the one that causes real damage. A feature flag used to hide functionality from
users who must not have it is an access control implemented in the presentation layer, and it fails
the moment someone calls the endpoint directly. Authorization is `SECURITY_STANDARDS.md` §3.

### 8.1 Flag types and lifetimes

| Type | Lifetime | Removal |
|---|---|---|
| Release flag | Until the feature is fully rolled out | Removed with the flag check, promptly |
| Experiment flag | Until the experiment concludes | Removed with the losing branch |
| Operational kill switch | Permanent | Never removed; reviewed periodically |
| Entitlement | Not a flag | Use plans and entitlements — M-06-3.2 |

A release flag that outlives its rollout is a permanent untested code path. Every flag carries a
work item id for its removal at the moment it is created, or it will not be removed.

---

## 9. Product settings

**TARGET — M-06-5.1 Scoped settings.** Product settings are distinct from application configuration
in three ways, and conflating them is a design error:

| | Application configuration | Product settings |
|---|---|---|
| Varies by | Environment | Tenant, workspace, project, user |
| Stored in | Files and environment variables | The database |
| Changed by | Deployment | The user, at runtime |
| Committed | The defaults are | Never — it is data |
| Owned by | DELIVERY | PRODUCT CORE |

A value a user can change is data. It gets a table, a schema, an audit trail, a tenant filter and a
permission — everything in `DATABASE_STANDARDS.md` and `SECURITY_STANDARDS.md` applies to it. It
does not go in `appsettings.json`, and a value in `appsettings.json` cannot be made per-tenant by
adding a dictionary keyed on tenant id.

Resolution order for a scoped setting: user → project → workspace → tenant → product default. The
first level that has a value wins, and the level a value came from is visible to whoever is looking
at it.

---

## 10. Runtime configuration

| Kind | Change takes effect | Mechanism |
|---|---|---|
| Startup configuration | On restart | `appsettings`, environment variables |
| Reloadable configuration | On file change | `IOptionsMonitor` |
| Feature flags | Immediately, no restart | **TARGET — M-10-5.1** |
| Product settings | Immediately | Database read per request or per scope |
| Secrets | On resolution, per the resolver's cache policy | `ISecretResolver` |

Most configuration is startup configuration, and that is the right default. Reloadable configuration
means a value can change between two lines of the same request; only mark a value reloadable when
something genuinely needs to change without a restart, and make sure the code reading it can cope
with the value changing underneath it.

Never make a security-relevant value reloadable without an audit entry. A signing key or a
permission default that changes silently at runtime is a change nobody can reconstruct afterwards.

---

## 11. Validation

| Rule | Statement |
|---|---|
| Validated at startup | Before the host accepts a request |
| **Fail fast** | An invalid or missing required value stops startup |
| Named in the error | The error message names the key, and never prints the value |
| Typed | Bound to an options class with data annotations or explicit validation |
| Ranges checked | Timeouts, sizes and counts have bounds, and the bounds are enforced |
| Required marked | A required value with no default is declared required, not defaulted to empty |

Failing at startup is not a harsh choice; it is the kind choice. A host that starts with a missing
database configuration and fails on the first request produces an incident. A host that refuses to
start produces a deployment failure, which is visible, attributable and reversible.

The error message never prints the value, because the most common invalid configuration value is a
malformed secret.

---

## 12. What may enter Git

| May be committed | Why |
|---|---|
| `appsettings.json` | Defaults and the key inventory |
| `appsettings.Development.json` | Non-secret local defaults |
| `launchSettings.json` | Ports and profiles are shared knowledge |
| `Directory.Build.props` | Build configuration is shared |
| `global.json` | SDK pinning is shared |
| `nuget.config` | Feed configuration — **feed credentials are not** |
| `.env.example` | The frontend variable inventory, with placeholder values |
| Secret **references** | `"ApiKeyRef": "nexus/openai/api-key"` |
| The LocalDB connection string | It carries no credential and is machine-local |

## 13. What must never enter Git

| Never committed | Notes |
|---|---|
| **Any API key** | OpenAI, Anthropic, or any other provider |
| **Any password** | Including a development one |
| **Any token** | Access, refresh, personal access, feed credential |
| **Any signing key** | JWT signing keys above all |
| **Any connection string with credentials** | Including Azure SQL |
| **Any certificate with a private key** | `.pfx`, `.p12`, and any `.pem` holding a key |
| `.env`, `.env.local` | Only `.env.example` is committed |
| User secrets | They live outside the repository by design |
| `.git-broken` | It is recovery state, not source — `GIT_WORKFLOW.md` §14 |
| Build output | `bin`, `obj`, `dist`, `node_modules` |
| Any real user's data | Not as a fixture, not as a test case |
| A secret in a **commit message** | It survives in history |
| A secret in a **comment** | Including a disabled one |

### 13.1 Enforcement

**CURRENT — corrected in W9.1.** The premise of this paragraph was wrong: **there is CI.** Four
repositories (`Nexus.Platform`, `Nexus.Intelligence`, `Nexus.Developer`, `Nexus.Experience`) run
`.github/workflows/build.yml` since M-08-1.x. What was missing was the *scan*, not the pipeline — and the
consequence is on record: W9.0 found a live-class credential committed in a tracked file in another
repository of this estate, reachable from every clone of it.

**CURRENT — the scan now exists in Platform.** `Nexus.Platform.Architecture.Tests/SecretScanGateTests`
scans this repository with the provider-neutral engine in `Nexus.Delivery.Core` and fails the build on a
match. Platform's CI runs the whole solution with no `--filter` by design, so the gate runs on every push
and pull request and cannot be bypassed by editing a workflow. Measured at W9.1: `verdict=Clean
scanned=241 skipped=725 findings=0`.

**TARGET — M-01-5.1 remains open for the other repositories.** Platform scans itself; Intelligence,
Forge, Developer, Experience, ControlPanel, WorkBench, Graphify and MarketSurvey do not yet. A gate that
covers one repository of nine has closed a third of the risk, and the W9.0 finding was in one of the
eight that are not covered.

### 13.2 If a secret is committed

**Rotate first.** The full procedure is `SECURITY_STANDARDS.md` §5.5 and `GIT_WORKFLOW.md` §12.
Removing the file in a later commit does not remove the secret from history, and history is what an
attacker reads.

---

## 14. Naming

| Element | Convention | Example |
|---|---|---|
| Section | PascalCase, matching the options class | `Nexus:Platform:Database` |
| Key | PascalCase | `CommandTimeoutSeconds` |
| Environment variable | Upper snake, `__` for hierarchy | `NEXUS_PLATFORM__DATABASE__COMMANDTIMEOUTSECONDS` |
| Frontend variable | `VITE_` + upper snake | `VITE_API_BASE_URL` |
| Secret reference key | Suffix `Ref` | `ApiKeyRef` |
| Feature flag | `Enable` + behaviour | `EnableStreamingResponses` |
| Boolean | Positive assertion | `EnableSwagger`, never `DisableSwagger` |
| Duration | Suffix the unit | `TimeoutSeconds`, `CacheDurationMinutes` |
| Size | Suffix the unit | `MaxUploadBytes` |
| Count | Suffix `Count` | `MaxRetryCount` |

An abbreviation is never introduced in a key that does not already exist in the domain vocabulary.
`Db`, `Cfg` and `Msg` cost a reader more than they save a writer.

---

## 15. Versioning

| Change | Requirement |
|---|---|
| Adding a key with a safe default | Add it to `appsettings.json`; no coordination needed |
| Adding a required key | Deploy the configuration before the code that requires it |
| Renaming a key | Read both for one release, warn on the old, then remove |
| Removing a key | Remove the reader first, the key second |
| Changing a default | Announced — a silent default change is a behaviour change with no diff |
| **Changing a unit** | Rename the key. Never change what `Timeout: 30` means |

That last row is absolute. Reinterpreting an existing key's units breaks every environment that
already sets it, silently, with a value that is wrong by a factor of a thousand. Rename to
`TimeoutMilliseconds`, read both for one release, remove the old.

Configuration schema changes are noted in the same place as any other breaking change, and the
backward-compatibility rules in `API_STANDARDS.md` §15 apply in spirit: additive is safe, required
is not, renaming needs a transition period.

---

## 16. Current state summary

| Area | State |
|---|---|
| `appsettings` per host | Present per ASP.NET Core convention; **the exact file set is not in the verified inventory — read before scripting** |
| `launchSettings.json` | **CURRENT, measured in W9.1** — Chat API `http://localhost:5095`, Intelligence `http://localhost:5000`, Developer `http://localhost:5195`, AtlasGraph `http://localhost:5210` |
| `Directory.Build.props`, `global.json` | **CURRENT** — one per repository |
| `nuget.config` | **CURRENT** — points at `C:\Personal\LocalNuGet`. **TARGET — M-08-1.1** GitHub Packages |
| Frontend `VITE_` config | **CURRENT** — `config/environment.ts` |
| Secret management | **CURRENT, corrected in W9.1** — `ISecretResolver` is implemented (`EnvironmentSecretResolver`, WI-01-5.1.1) and bound by `AddNexusPlatform`. `set-openai-key.ps1` remains for local operator setup. **TARGET — M-01-5.1** for the remaining acceptance criteria |
| Secret scanning | **PARTIAL — Platform only, since W9.1.** `SecretScanGateTests` fails the build on a match; measured `verdict=Clean scanned=241`. The other eight repositories are **TARGET — M-01-5.1** |
| Environment definitions | **Ratified, not yet built.** The Owner has fixed the set as `ENV-DEV`, `ENV-TEST`, `ENV-PROD` (W9.1), and no environment infrastructure exists yet. **TARGET — M-08-4.1, M-08-6.1** |
| Feature flags | **None.** TARGET — M-10-5.1 |
| Product settings | **None.** TARGET — M-06-5.1 |
| Configuration validation at startup | **Not implemented on any host** — re-verified in W9.1 by searching all nine repositories for `ValidateOnStart` / `IValidateOptions` / `ValidateDataAnnotations`: zero hits |
| Configuration registry | **TARGET — M-03-6.1** GOVERNANCE |

The two most consequential gaps, restated after W9.1: **no environment exists yet**, though the three are
now named and ratified, so there is still nothing to configure *for* beyond a developer machine; and
**secret scanning covers one repository of nine**, so §13 is enforced everywhere else only by whoever is
reading the diff — which is exactly how the W9.0 finding got in.

---

## 17. References

- `SECURITY_STANDARDS.md` — secret storage, resolution, rotation, exposure response, environment access.
- `DATABASE_STANDARDS.md` — what the connection string connects to; schema and database targeting.
- `API_STANDARDS.md` — base URLs, ports, backward compatibility principles.
- `GIT_WORKFLOW.md` — repository rules, secrets in history, cleanup.
- `DEVELOPMENT_WORKFLOW.md` — per-worker isolation and local configuration.
- `ASSURANCE_STANDARDS.md` — test environment configuration and evidence.
