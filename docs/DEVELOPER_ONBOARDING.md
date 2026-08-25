# Developer Onboarding

**Status:** CURRENT — every command below is for the system as it stands on 2026-08-21; steps that
are not yet automated say so
**Owner:** DEVELOPER (Layer 07), with DELIVERY (08) owning the tooling it depends on
**Last updated:** 2026-08-21
**Layer:** 07 DEVELOPER
**Authoritative for:** the path a new developer takes from an empty machine to a merged pull
request — required software, cloning, SDK, restore, database, secrets, build, run, test, branch,
worktree, first change, review, and what to do when it breaks.

Not authoritative for: what the repositories contain — `REPOSITORY_STRUCTURE.md`; the running local
topology in detail — `LOCAL_DEVELOPMENT.md`; how to add a new *thing* —
`NEW_MODULE_GUIDE.md`; branch and commit mechanics — `GIT_WORKFLOW.md`; when a work item may advance
— `DEVELOPMENT_WORKFLOW.md`.

---

## If you only read one thing

```
git clone https://github.com/prtcare/Nexus.Platform.git      C:\Personal\Nexus.Platform
git clone https://github.com/prtcare/Nexus.Intelligence.git    C:\Personal\Nexus.Intelligence
git clone https://github.com/prtcare/Nexus.Experience.git    C:\Personal\Nexus.Experience
```

Three repositories, three `.slnx` solutions, one product API on `http://localhost:5299`, SQL Server
LocalDB underneath, packages from a folder at `C:\Personal\LocalNuGet`, and **two behaviour tests in
the entire system**.

Four things will bite you, in this order:

1. **Packages come from a local folder**, not a feed. If restore fails, someone has not run
   `pack-local.ps1`. This is CURRENT and known-broken-for-CI — **TARGET: M-08-1.1**.
2. **There is no CI.** Nothing checks your work but you and your reviewer. A pull request carries
   your assertion, and that is precisely the weakness the roadmap is closing.
3. **Worktrees go in a SIBLING directory, never nested.** On Windows a nested worktree cannot be
   renamed or removed while an agent runs from it, and the failure looks like a permissions bug.
4. **`Failed to determine the https port for redirect` is expected local noise.** It is not a
   defect and you have not broken anything.

And one rule that comes from a real incident: **push at every stage boundary, not every milestone.**
On 2026-08-20 all three repositories lost `.git\objects` simultaneously. Read `GIT_WORKFLOW.md` §2
before you accumulate a day of unpushed work.

---

## 1. Required software

| Software | Why | How you know the version |
|---|---|---|
| **Git** | The three repositories | Any current version |
| **.NET SDK 10** | Every project targets `net10.0` | **`global.json` pins the exact version — read it, do not guess** |
| **SQL Server LocalDB** | The development database | Ships with SQL Server Express / the Visual Studio data workload |
| **Node.js + npm** | `Nexus.Experience.Client` is a Vite app | Version not pinned anywhere — see §12 |
| **PowerShell** | Every script in the repositories is `.ps1` | Windows PowerShell or PowerShell 7 |
| An IDE | Visual Studio, Rider or VS Code | `.slnx` support requires a current version |

**Not required, because they do not exist in Nexus:** Docker or any container tooling, Python, any
cloud CLI, any logging or APM agent. `TECHNOLOGY_STACK.md` §7 lists these as **NOT SELECTED** —
installing one does not help and introducing one needs an ADR (next: **ADR-016**).

`dotnet-ef` is needed for migrations:

```powershell
dotnet tool install --global dotnet-ef
```

---

## 2. Clone the repositories

All three live side by side under `C:\Personal`. **The paths matter** — `nuget.config` refers to
`C:\Personal\LocalNuGet` and worktrees are created as siblings.

```powershell
cd C:\Personal
git clone https://github.com/prtcare/Nexus.Platform.git    Nexus.Platform
git clone https://github.com/prtcare/Nexus.Intelligence.git  Nexus.Intelligence
git clone https://github.com/prtcare/Nexus.Experience.git  Nexus.Experience
```

Note the remote names did not match the local directory names before the renames — `Nexus-Int`
cloned into `Nexus.Int`, `Nexus-web` into `Nexus.Web`. That inconsistency was real and was corrected
by the repository renames (2026-08-24, `REPOSITORY_STRUCTURE.md` §12). Clone into the directory
names above so that every path in this document set is true on your machine.

Then create the package feed folder, which is **not** a git repository and must never become one:

```powershell
mkdir C:\Personal\LocalNuGet
```

**Antivirus.** The 2026-08-20 incident is consistent with antivirus quarantining extensionless zlib
blobs out of `.git\objects`. An exclusion for `C:\Personal` was recommended and **has never been
confirmed applied**. Apply one, and confirm it. `GIT_WORKFLOW.md` §2.

---

## 3. Install the SDK that `global.json` pins

Each repository has its own `global.json`. Read it and install exactly that SDK.

```powershell
Get-Content C:\Personal\Nexus.Platform\global.json
dotnet --list-sdks
dotnet --version          # run inside the repository — it resolves through global.json
```

If `dotnet --version` inside a repository errors with a message about `global.json`, the pinned SDK
is not installed. Install it; do not edit `global.json` to match what you have. Changing a pin is a
`STACK_VERSION_POLICY.md` decision, not a local convenience.

---

## 4. Restore packages — the honest version

**CURRENT.** `nuget.config` in each repository points at `C:\Personal\LocalNuGet`, a folder.
`Nexus.Platform.*` and `Nexus.Intelligence.*` packages are produced by `pack-local.ps1` and consumed
from there.

Order matters, because Web consumes Intelligence and Platform:

```powershell
cd C:\Personal\Nexus.Platform
.\pack-local.ps1

cd C:\Personal\Nexus.Intelligence
.\pack-local.ps1

cd C:\Personal\Nexus.Experience
dotnet restore Nexus.Experience.slnx
```

If a restore fails with "unable to find package `Nexus.Platform.Contracts`", the answer is almost
always that `pack-local.ps1` has not been run in the producing repository since the version changed.
Run it there, then restore again.

> **TARGET — M-08-1.1 Package feed reachable from CI.** A local file feed is invisible to every
> build agent, so the first pipeline written against today's `nuget.config` cannot restore. The
> milestone replaces it with GitHub Packages. Until then, **packing is a serialised operation**: two
> workers packing the same version at once will race, so announce it before you run it
> (`GIT_WORKFLOW.md` §5.3).

`LOCAL_DEVELOPMENT.md` §3 owns the local package flow in full.

---

## 5. Database — SQL Server LocalDB

```powershell
sqllocaldb info
sqllocaldb start MSSQLLocalDB
```

Apply the migrations for the Chat product:

```powershell
cd C:\Personal\Nexus.Experience
dotnet ef database update `
  --project src\Nexus.Products.Chat.Infrastructure `
  --startup-project src\Nexus.Products.Chat.Api
```

`NexusChatDbContextFactory` exists so `dotnet ef` can construct the context at design time without
starting the host.

**Not automated.** There is no seed script, no reset script and no `docker compose up`. Migrations
are applied by hand. If you need a clean database, drop it and re-run the update.

**Parallel-worker warning.** Two developers or two worktrees running `dotnet ef database update`
against the *same* LocalDB database will interleave, and the migration history table will end up
disagreeing with the model snapshot. Each worktree gets its own database name —
`CONFIGURATION_STANDARDS.md` §7.1, `DEVELOPMENT_WORKFLOW.md` §4 rule 3.

Schema rules, the Id/Seq/Ref pattern and migration naming are `DATABASE_STANDARDS.md`.

---

## 6. Secrets

**CURRENT.** The OpenAI key is set by a script in `Nexus.Platform`:

```powershell
cd C:\Personal\Nexus.Platform
.\set-openai-key.ps1
```

Run it if your work touches a model path. If it does not, you do not need a key — and note that the
live-model verifications (citations, usage metering) have been blocked on OpenAI credit, so a key
alone is not sufficient to prove a turn end to end.

> **TARGET — M-01-5.1 Real secret resolver.** `ISecretResolver` exists in
> `Nexus.Platform.Contracts/Secrets/` as a contract with no production implementation. When it
> lands, secrets are resolved through it and the script goes away.

**Rules that apply from your first commit**, not from when the resolver lands:

| Rule | Statement |
|---|---|
| No secret in Git | Not in `appsettings*.json`, not in `launchSettings.json`, not in a script |
| `launchSettings.json` is committed | So its `environmentVariables` block must never hold a secret |
| Developer-specific values go in user secrets | `dotnet user-secrets` — never committed |
| Frontend developer values go in `.env.local` | Never committed; `VITE_` prefix |

`SECURITY_STANDARDS.md` §5 owns secrets; `CONFIGURATION_STANDARDS.md` §7 owns local configuration;
`GIT_WORKFLOW.md` §12 owns what to do when a secret reaches history.

---

## 7. Build

Three solutions, three commands:

```powershell
dotnet build C:\Personal\Nexus.Platform\Nexus.Platform.slnx
dotnet build C:\Personal\Nexus.Intelligence\Nexus.Intelligence.slnx
dotnet build C:\Personal\Nexus.Experience\Nexus.Experience.slnx
```

Build in that order the first time — Web consumes packages produced by the other two.

`Nexus.AI.slnx` used to sit in the `NexusAI` directory and contained `Nexus.Platform.*` projects — a
naming defect, not a mistake on your machine. It was renamed with the repository (2026-08-24) to
`Nexus.Platform.slnx` (`REPOSITORY_STRUCTURE.md` §12).

Frontend:

```powershell
cd C:\Personal\Nexus.Experience\src\Nexus.Experience.Client
npm install
```

---

## 8. Run

### 8.1 Chat API

```powershell
cd C:\Personal\Nexus.Experience
dotnet run --project src\Nexus.Products.Chat.Api
```

Listens on **`http://localhost:5299`**. Swagger is available via Swashbuckle in development.
Health: `GET http://localhost:5299/api/v1/health` — confirm the exact route in `HealthEndpoint.cs`
rather than assuming it.

You will see:

```
warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3]
      Failed to determine the https port for redirect.
```

**Expected. Not a defect. Do not "fix" it.** `LOCAL_DEVELOPMENT.md` §7 explains why.

### 8.2 Intelligence API

```powershell
cd C:\Personal\Nexus.Intelligence
dotnet run --project src\Nexus.Intelligence.Api
```

`Nexus.Intelligence.Api` has its own `launchSettings.json`. **Read the port out of it.** It is not
recorded in this document set because it has not been verified, and a wrong port in onboarding
documentation costs more than a missing one. Routes are served under `/intelligence/v1`.

### 8.3 Frontend

```powershell
cd C:\Personal\Nexus.Experience\src\Nexus.Experience.Client
npm run dev
```

Vite prints the URL it binds. The client reads `VITE_`-prefixed variables through
`src/config/environment.ts`; point its API base URL at `http://localhost:5299` via `.env.local`.
**Verify the script names in `package.json`** — they were not read for this document, and `dev` is
the Vite convention rather than a confirmed fact about this project.

Startup order and how the three talk to each other: `LOCAL_DEVELOPMENT.md` §5–§6.

---

## 9. Run the tests

```powershell
dotnet test C:\Personal\Nexus.Platform\Nexus.Platform.slnx
dotnet test C:\Personal\Nexus.Intelligence\Nexus.Intelligence.slnx
dotnet test C:\Personal\Nexus.Experience\Nexus.Experience.slnx
```

What you are running, stated plainly:

| Project | Contains |
|---|---|
| `Nexus.Platform.Architecture.Tests` | `PlatformBoundaryTests.cs` |
| `Nexus.Platform.Tests` | **nothing — a `.csproj` with zero `.cs` files** |
| `Nexus.Intelligence.Architecture.Tests` | `BoundaryRuleTests.cs` |
| `Nexus.Intelligence.Tests` | `Ranking/KeywordContextRankerTests.cs` |
| `Nexus.Products.Chat.Architecture.Tests` | `BoundaryTests.cs` |
| `Nexus.Products.Chat.Tests` | `Chat/ChatContextBundleMapperTests.cs` |

**Two behaviour tests. Three architecture test files. Zero frontend tests.** The suite runs in
seconds, and a change that breaks one of them has broken something real — that is exactly why they
are worth running every time despite how few there are. The three `*.Architecture.Tests` projects
are the only mechanical enforcement of the layer boundaries in the whole system.

`ASSURANCE_STANDARDS.md` owns what to add and in what order. **Do not create a new empty test
project**; `Nexus.Platform.Tests` already demonstrates what that costs.

---

## 10. Create a branch and a worktree

Branch from the milestone's integration branch, not from `main`:

```powershell
cd C:\Personal\Nexus.Experience
git fetch --prune
git worktree add ..\Nexus.Experience.work\WI-02-1.5.1-a -b work/WI-02-1.5.1-a integration/M-02-1.5
```

```
C:\Personal\Nexus.Experience\                          the repository
C:\Personal\Nexus.Experience.work\WI-02-1.5.1-a\       your worktree — a SIBLING
```

> **The Windows caveat.** A worktree nested inside a folder an agent is running from cannot be
> renamed or removed while that agent runs — Windows holds a lock on a running process's working
> directory, and the failure presents as a permissions error rather than a lock. **Worktrees go in a
> sibling directory. Never inside the repository, never inside another worktree.**

Each worktree has its own `bin`/`obj`, its own restore, its own local configuration and **its own
LocalDB database**. `GIT_WORKFLOW.md` §5 owns worktree lifecycle and cleanup;
`DEVELOPMENT_WORKFLOW.md` §4 owns whether your work item may run in parallel with another at all.

Branch naming, commit format and push rules are `GIT_WORKFLOW.md` §4, §6 and §7. The one to
internalise now: **push at every stage boundary.**

---

## 11. Your first change

A good first change is small, in one project, with no migration and no contract edit. Something like
adding a missing endpoint to an existing resource, or writing the third behaviour test in the
system.

1. Confirm the work item is classified `Can run now` — `DEVELOPMENT_WORKFLOW.md` §5.
2. Confirm nobody else is touching the same schema or contract — §4 of the same document, rule 3 is
   the one most often got wrong.
3. Work in your worktree.
4. Follow the procedure for whatever you are adding — `NEW_MODULE_GUIDE.md`.
5. Build, run, and exercise the actual behaviour. Swagger returning 200 is not evidence that the
   behaviour is right.
6. `dotnet test` on the repository's solution.
7. Commit and **push** — at every stage boundary, not at the end.
8. Rebase onto the integration branch and verify the behaviour again after the rebase.
9. Open the pull request.

**Where the trip wires are, in the order new developers hit them:**

| Trip wire | Rule |
|---|---|
| Putting a product type into `*.Contracts` | **No shared kernel.** The architecture test fails, and it should |
| Referencing a higher layer from a lower one | Dependency direction — NetArchTest fails |
| `if (Product == X)` | Forbidden everywhere. Capability packs are declared, not coded |
| Adding a migration in parallel with someone else | Two migrations on one `DbContext` conflict on the model snapshot even when they touch different tables |
| Logging a prompt, a token or a secret | Absolutely prohibited — `SECURITY_STANDARDS.md` §11 |
| Registering a converter outside `StronglyTypedIdConverters.cs` | Converters live there and nowhere else |
| Using schema `org` for something new | `org` is the legacy schema of the one existing migration — `DATABASE_STANDARDS.md` §2 |

---

## 12. Pull request and review

**CURRENT: there is no CI, so there is no green build to require.** A pull request today carries the
author's assertion that it built and behaved. Say what you verified and how, in specifics.

Include: the work item id (matching the branch name), what changed and why, what you verified, and
the evidence. `GIT_WORKFLOW.md` §8 owns the full checklist.

Your reviewer will check, roughly in the order that catches things:

1. Does the change match the work item's scope, and only that scope?
2. Do the layer boundaries hold — would NetArchTest pass?
3. Is there a migration, and does it follow `DATABASE_STANDARDS.md`?
4. Does anything log a secret, a token or a prompt body?
5. Is there an acceptance criterion, and does the evidence prove it?
6. Would this break an existing API caller? (`API_STANDARDS.md` §15)

**Do not merge your own work.** Work branch → integration is **squashed**; integration → `main` is a
**merge commit**. After the merge, remove the worktree with `git worktree remove` (not by deleting
the folder) and `git worktree prune`.

> **TARGET — M-08-1.2** gives pipelines a status check; **M-08-1.4** makes it a gate; **M-07-4.1
> Build and test records** replaces "the author said it built" with evidence from CI; **M-07-5.1**
> makes the reviewer a real Layer 01 User with a recorded decision rather than a name in a comment.

---

## 13. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Failed to determine the https port for redirect` | HTTPS redirect middleware with no HTTPS port bound in development | **Nothing. Expected noise.** |
| Restore cannot find `Nexus.Platform.*` or `Nexus.Intelligence.*` | `pack-local.ps1` not run in the producing repository | Run it there, then restore |
| `dotnet` refuses to build, mentions `global.json` | The pinned SDK is not installed | Install the pinned SDK. Do not edit the pin |
| `dotnet build` cannot open the solution | Old SDK or IDE without `.slnx` support | Update; do not add a `.sln` |
| `dotnet ef` not found | Global tool missing | `dotnet tool install --global dotnet-ef` |
| Migration history disagrees with the model snapshot | Two worktrees updated the same LocalDB database | Give each worktree its own database; drop and re-apply |
| SQL Server error 1785 on a migration | Multiple cascade paths | Only the owning parent cascades; reference FKs `Restrict`, self-references `NoAction` — `DATABASE_STANDARDS.md` §5.3 |
| Frontend calls fail with a network error | `VITE_` API base URL not pointing at `http://localhost:5299` | Set it in `.env.local` |
| A model call fails or returns nothing | No key, or no OpenAI credit | `set-openai-key.ps1`; the credit block is a known open item |
| A worktree cannot be removed or renamed | It is nested inside a running agent's working directory | Recreate it as a sibling. §10 |
| `git status` looks catastrophic; objects missing | See `GIT_WORKFLOW.md` §2 before doing anything else | Fresh clone + in-place `.git` swap |

---

## 14. What is not automated — say it out loud

So that you do not go looking for tooling that does not exist:

| Not automated | Consequence | Closed by |
|---|---|---|
| Continuous integration | Nothing builds or tests your branch but you | M-08-1.2 |
| Branch protection | `main` is protected by convention only | M-08-1.4 |
| Package publishing | `pack-local.ps1` by hand, to a folder | M-08-1.1 |
| Secret provisioning | A PowerShell script per machine | M-01-5.1 |
| Database provisioning and seeding | `dotnet ef database update` by hand; no seed data | — |
| Environment definition | There are no environments | M-08-4.1 |
| Deployment | Nothing has ever been deployed | M-08-5.1 |
| Frontend testing | No framework, zero tests | Tooling undecided — `ASSURANCE_STANDARDS.md` §14 |
| Log correlation | No logging library is selected at all | M-10-1.1 |
| Authentication and authorization | **There is none.** Identity is a stub | M-01-1.2, M-01-3.1 |

That last row deserves emphasis on day one: **there is no authentication and no authorization in
Nexus today.** `ChatTurnIdentity` returns a hardcoded tenant. Do not build anything that assumes a
caller has been authenticated, and do not write code whose safety depends on a permission check that
does not exist — `SECURITY_STANDARDS.md` §1.

---

## 15. Day one, in order

1. Install Git, the SDK from `global.json`, LocalDB, Node, PowerShell, `dotnet-ef`.
2. Apply and confirm an antivirus exclusion for `C:\Personal`.
3. Clone the three repositories into the paths in §2. Create `C:\Personal\LocalNuGet`.
4. `pack-local.ps1` in `Nexus.Platform`, then in `Nexus.Intelligence`.
5. Build all three solutions, in order.
6. `sqllocaldb start MSSQLLocalDB`, then `dotnet ef database update` for Chat.
7. `set-openai-key.ps1` if you need a model path.
8. Run the Chat API; confirm `http://localhost:5299` and ignore the HTTPS warning.
9. `npm install` and `npm run dev` in `Nexus.Experience.Client`; point it at 5299.
10. `dotnet test` all three. Note that you just ran two behaviour tests.
11. Read `_FACTS.md`, then `DEVELOPMENT_WORKFLOW.md` and `GIT_WORKFLOW.md` §2 and §5.
12. Take a work item, create a sibling worktree, and make one small change.

---

## 16. References

- `REPOSITORY_STRUCTURE.md` — what is in each repository and what must never be.
- `LOCAL_DEVELOPMENT.md` — the local topology, ports, startup order and failure modes in detail.
- `NEW_MODULE_GUIDE.md` — the numbered procedure for whatever you are adding.
- `DEVELOPMENT_WORKFLOW.md` — states, evidence, parallel safety, classification.
- `GIT_WORKFLOW.md` — branches, worktrees, commits, PRs, the 2026-08-20 incident.
- `DATABASE_STANDARDS.md` — migrations, the Id/Seq/Ref pattern, cascade rules.
- `CONFIGURATION_STANDARDS.md` — local configuration, user secrets, `.env.local`.
- `SECURITY_STANDARDS.md` — secrets, and the absence of authentication.
- `ASSURANCE_STANDARDS.md` — what a test must prove before it counts as evidence.
- `TECHNOLOGY_STACK.md` — what is approved, and what is deliberately not selected.
