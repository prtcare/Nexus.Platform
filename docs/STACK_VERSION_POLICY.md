# Stack Version Policy

> **Status:** TRANSITION — the pinning mechanisms exist in all three repositories; the enforcement that makes them meaningful does not exist yet
> **Owner:** Layer 08 DELIVERY (enforcement) / Layer 03 GOVERNANCE (record)
> **Last updated:** 2026-08-21
> **Layer:** Cross-cutting (08 DELIVERY)
> **Authoritative for:** how .NET SDK, NuGet package and npm package versions are pinned, when they are upgraded, what a breaking upgrade requires, how security patches are handled, how a technology is deprecated, and how obsolete stack use is detected

*Which* technologies are approved is **TECHNOLOGY_STACK.md**. This document is only about their versions.

---

## 1. The honest starting position

Two things must be said before any rule, because a policy that pretends otherwise is unusable.

**There is no CI.** `NexusAI\.github\workflows\` exists and is empty. `Nexus.Web` and `Nexus.Int` have no `.github` directory at all. Every rule below that says "the build fails" is **TARGET** until **M-08-1.2 Pipelines on every repository** lands, and every rule that says "merge is blocked" is TARGET until **M-08-1.4 Branch protection and architecture gate** lands. Until then the same rules are enforced by the person opening the pull request. Say so plainly rather than writing a policy that quietly does nothing.

**Only one version is verified.** `net10.0` is the target framework of every project. Every other version in Nexus — EF Core, `Microsoft.Data.SqlClient`, Swashbuckle, the OpenAI SDK, `System.ClientModel`, React, TypeScript, Vite, TanStack Query, NetArchTest — is recorded in TECHNOLOGY_STACK.md as *unpinned* because the exact value was not read from a project file. The EF Core assemblies on disk are 9.x-era; that is an observation about assemblies, not a pin.

The first action this policy demands is therefore **audit, not upgrade**: read the actual versions out of every `.csproj`, `global.json` and `package.json`, and record them. Everything else depends on it.

---

## 2. The pinning mechanisms that exist

Three files per repository do the work. All three exist in NexusAI, Nexus.Int and Nexus.Web.

### `global.json` — pins the .NET SDK

| Aspect | Rule |
|---|---|
| **What it does** | Fixes which .NET SDK the `dotnet` CLI selects for that repository, regardless of what else is installed on the machine. |
| **State** | CURRENT — one exists per repository. |
| **Policy** | Pin the SDK **version** with a **`rollForward` of `latestPatch`**. Patch-level drift is safe and carries security fixes; feature-band drift changes compiler and MSBuild behaviour between two developers on the same commit. |
| **Who changes it** | 08 DELIVERY, in a work item that touches nothing else. Never bundled with feature work. |
| **Non-negotiable** | All three repositories move to the same SDK version in the same change. Nexus.Web consumes packages produced by NexusAI and Nexus.Int; divergent SDKs produce divergent output for identical source. |

### `Directory.Build.props` — one place for shared MSBuild properties

| Aspect | Rule |
|---|---|
| **What it does** | MSBuild imports it automatically for every project below it, so properties set once apply repository-wide without editing each `.csproj`. |
| **State** | CURRENT — one exists per repository. Its exact contents were not verified; the rules below are what belongs in it. |
| **What belongs in it** | `TargetFramework` (`net10.0`), `Nullable`, `LangVersion` if overridden, `TreatWarningsAsErrors`, `ImplicitUsings`, assembly and package metadata, and — the important one — **`ManagePackageVersionsCentrally`** with the versions in a sibling `Directory.Packages.props`. |
| **What must never be in it** | A `PackageReference` that not every project needs. A property in `Directory.Build.props` is a statement that the rule holds for `Nexus.Platform.Contracts` and `Nexus.Products.Chat.Api` alike. |
| **Per-project override** | Allowed but must carry a comment saying why. An override with no explanation is a defect. |

### `nuget.config` — pins where packages come from

| Aspect | Rule |
|---|---|
| **What it does** | Names the feeds NuGet restores from. Currently points at `C:\Personal\LocalNuGet`. |
| **State** | **TRANSITION.** The flow is `pack-local.ps1` (in NexusAI and Nexus.Int) → `C:\Personal\LocalNuGet` → consumed via `nuget.config`. `LocalNuGet` is not a git repository and is unreachable from any build agent. |
| **Consequence** | No pipeline can restore Nexus.Platform or Nexus.Intelligence packages. This blocks every pipeline that follows. |
| **TARGET** | GitHub Packages — **M-08-1.1 Package feed reachable from CI**. |
| **Policy once it lands** | Feeds are enumerated explicitly with `<clear/>` first, so a machine-level `NuGet.config` cannot silently inject a source. Package-source mapping restricts `Nexus.*` to the Nexus feed so an upstream package cannot shadow a first-party one. |

### npm — the frontend

| Aspect | Rule |
|---|---|
| **What it does** | `package.json` declares ranges; the lock file pins the resolved graph. |
| **State** | CURRENT for `Nexus.Web.Client`; the declared versions were not verified. |
| **Policy** | The lock file is committed and is the pin. `npm ci` — never `npm install` — in any automated or reproducible context. A pull request that changes the lock file without changing `package.json` must explain why in its description. |
| **Ranges** | Caret ranges are acceptable in `package.json` **because** the lock file is committed. Without a committed lock file they would not be. |

### Where a version may be declared — precedence

A version declared in two places is a version that will disagree with itself. This is the order of authority, highest first.

| Rank | Location | Declares | Rule |
|---|---|---|---|
| 1 | `global.json` | .NET SDK | One per repository. Nothing else may state an SDK version. |
| 2 | `Directory.Packages.props` (**TARGET**) | Every NuGet package version | Once central package management is on, a version in a `.csproj` is an error, not an override. |
| 3 | `Directory.Build.props` | `TargetFramework`, language and compiler properties | Never a package version. |
| 4 | `<Project>.csproj` | Which packages this project needs — **not which versions** | A `Version=` attribute here is drift, and today it is the only mechanism available. |
| 5 | `nuget.config` | Where packages come from | Never what version. |
| 6 | `package-lock.json` | The resolved frontend graph | The pin. Generated, committed, never hand-edited. |
| 7 | `package.json` | Frontend ranges | Ranges only. The lock file resolves them. |

Documentation is **not** on this list. TECHNOLOGY_STACK.md describes what is pinned; it does not pin anything. When the two disagree, the file wins and the document is wrong.

### What is *not* pinned anywhere, and must be

| Gap | Consequence | Fix |
|---|---|---|
| No `Directory.Packages.props` verified | Each `.csproj` can carry its own version of the same package. `Nexus.Products.Chat.Api` and `Nexus.Products.Chat.Infrastructure` can disagree about EF Core, and the winner is whichever the restore graph picks. | Introduce central package management alongside the version audit in §1. |
| No lock file for NuGet | Restore is reproducible only by luck. | `RestorePackagesWithLockFile` once versions are centralised. |
| No SQL Server / LocalDB version recorded | The `Ref` computed column is T-SQL PERSISTED and dialect-bound; the engine version is a real dependency and is untracked. | Record it in TECHNOLOGY_STACK.md §4 during the audit. |

---

## 3. Version classes and what each permits

| Class | Example | Rule | Approval |
|---|---|---|---|
| **Patch** | EF Core 9.0.1 → 9.0.2 | Take it. Batch patches into one change per repository per cycle. | None beyond review. |
| **Minor** | Swashbuckle 6.5 → 6.6 | Take it after the build passes and the two behaviour tests run. Read the release notes for the word *obsolete*. | Reviewer. |
| **Major** | React 18 → 19, EF Core 9 → 10 | Its own work item. Nothing else in the change. Follow §5. | Explicit decision; ADR if it changes how code is written. |
| **Prerelease** | any `-preview`, `-rc`, `-beta` | **Not permitted on `main`.** Allowed on a `work/<id>` branch for evaluation only. | Never merged to `main`. |
| **SDK band** | .NET 10.0.1xx → 10.0.2xx | Coordinated across all three repositories in one change. | 08 DELIVERY. |

---

## 4. Upgrade frequency

| Cadence | Scope | Owner |
|---|---|---|
| **Every milestone boundary** | Patch-level updates across all three repositories. Milestone boundaries are already the natural checkpoint — the 2026-08-20 recovery produced the lesson *push at every stage boundary, not every milestone*, and version hygiene attaches to the same rhythm. | 08 DELIVERY |
| **Every phase boundary** | Minor updates; review whether any *unpinned* entry in TECHNOLOGY_STACK.md has become pinnable. | 08 DELIVERY |
| **On demand** | Security patches — see §6. No waiting for a cadence. | 08 DELIVERY |
| **Deliberately scheduled** | Majors. Planned into a milestone with capacity, never absorbed. | Milestone owner |
| **Never** | "Update everything to latest" as a routine action. It converts a diagnosable single failure into an undiagnosable compound one. | — |

**The rule that outranks the cadence:** never upgrade a package inside a work item that also changes behaviour. When the build breaks, the cost of separating cause from cause exceeds anything the convenience saved.

---

## 5. Breaking upgrades

A breaking upgrade is any upgrade that changes an API you call, changes a default that changes behaviour, or changes generated output. Majors usually qualify; minors sometimes do.

**Required, in order:**

1. **Its own branch and its own work item.** `work/<id>` per NAMING_STANDARDS.md §34. Nothing else in the change.
2. **A written blast radius.** Which projects, which files, which endpoints. For `Nexus.Web`, the client and the API are separate blast radii and must be listed separately.
3. **Boundary tests must pass unchanged.** `PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs` and `BoundaryTests.cs` are the only automated defence the architecture has. An upgrade that requires editing them is not an upgrade, it is an architecture change and needs an ADR.
4. **Manual verification, because two behaviour tests will not catch it.** The system has exactly two: `Ranking/KeywordContextRankerTests.cs` and `Chat/ChatContextBundleMapperTests.cs`. Until **M-09-3.1 Test plans and test cases**, the verification list is written by hand in the pull request. At minimum: the Chat API starts on `http://localhost:5299`, `HealthEndpoint` answers, and a `Workspace` insert returns a server-generated `Ref` and `Seq`.
5. **Push at the stage boundary.** The 2026-08-20 incident cost uncommitted work; SQL Stage 1b was complete, proven and uncommitted on `feat/azure-sql` at `29ac2f4`. A long-running upgrade branch is exactly the shape of work that gets lost.
6. **Record the outcome** in TECHNOLOGY_STACK.md in the same pull request.

**Upgrade-specific rules:**

| Upgrade | Extra requirement |
|---|---|
| **EF Core major** | Never in the same change as a migration. A model-snapshot difference plus a provider difference is not diagnosable. Verify a real insert against LocalDB and confirm the `Ref` computed column still materialises. |
| **`Microsoft.Data.SqlClient` major** | Historically changes TLS and certificate-trust defaults. Verify against **both** LocalDB and Azure SQL before merge. |
| **.NET SDK band** | All three repositories together. Nexus.Web consumes packages built by the other two. |
| **React major** | One change across the whole client — `App.tsx`, `main.tsx`, `layouts/AppLayout.tsx`, `routes/AppRoutes.tsx`, every page, every feature folder. There are **zero** frontend tests, so verification is manual and the list goes in the pull request. |
| **TanStack Query major** | Touches every `use*` hook — `useConversations.ts` through `useSystemHealth.ts` — plus `app/queryClient.ts`. Treat as one atomic change. |
| **OpenAI SDK** | Must stay behind `IModelGateway`. If an upgrade would surface a provider type in `Nexus.Platform.Contracts`, the upgrade is wrong, not the boundary. |

---

## 6. Security patches

| Rule | Detail |
|---|---|
| **Precedence** | A security patch overrides every cadence in §4 and may ship alone. |
| **Timescale** | Critical or high with a known exploit path into Nexus: immediately, ahead of feature work. Moderate: next milestone boundary. Low: next phase boundary. |
| **Transitive dependencies** | Patch the parent package first. Pin the transitive dependency directly only when no parent version resolves it, and add a comment naming the advisory and the parent version that will retire the pin. `System.Security.Cryptography.Xml` is exactly this shape today — a pin held only for the Dataverse client, which leaves at **M-02-1.4 Delete Dataverse**. |
| **Detection** | **TARGET.** No automated scanning exists — there is no CI to run it in. Until **M-08-1.2**, detection is manual: `dotnet list package --vulnerable --include-transitive` and `npm audit`, run at each milestone boundary and recorded. |
| **Secrets are the adjacent risk** | The OpenAI key is handled by `set-openai-key.ps1`. **TARGET: `ISecretResolver` — M-01-5.1 Real secret resolver**, whose stated outcome is that no secret is committed to any repository and CI can obtain what it needs without one. A dependency upgrade never justifies a temporary hardcoded credential. |
| **The unclosed one** | All three repositories lost `.git\objects` simultaneously on 2026-08-20, consistent with antivirus quarantine of extensionless zlib blobs. The recommended antivirus exclusion for `C:\Personal` **has never been confirmed**. That is an open security-adjacent risk to the source itself — **M-08-2.1 Close the 2026-08-20 recovery**. |

---

## 7. Deprecation

A technology moves through four states. Each transition is recorded in TECHNOLOGY_STACK.md.

| State | Meaning | Rule for developers |
|---|---|---|
| **Approved** | In the stack, use freely. | — |
| **Discouraged** | Still works; a better option exists. | No new usage. Existing usage may stay. |
| **Deprecated** | Scheduled for removal, with a named milestone. | No new usage, and touching a file that uses it obliges you to migrate that usage or record why you did not. |
| **Removed** | Gone from every project file. | Any reintroduction requires an ADR. |

**The live example, and the template for every future one.**

| Field | Value |
|---|---|
| Technologies | `Microsoft.PowerPlatform.Dataverse.Client`, `Microsoft.Xrm.Sdk`, `Microsoft.Crm.Sdk.Proxy`, and the `System.Security.Cryptography.Xml` pin held for them |
| State | **Deprecated** |
| Decision | ADR-014, Stage 3 |
| Removal milestone | **M-02-1.4 Delete Dataverse** |
| Size | ~7.2 MB of assemblies |
| Replacement | EF Core against Azure SQL. Reference implementations: `SqlWorkspaceRepository.cs`, `WorkspaceConfiguration.cs`. |
| Migration state | 1 of 11 aggregates migrated (`Workspace`). The other ten — `Adr`, `Artifact`, `Branch`, `Conversation`, `ConversationMessage`, `Knowledge`, `Project`, `Session`, `Snapshot`, `WorkItem` — still resolve to Dataverse implementations. |
| Rule while deprecated | Do not extend a Dataverse implementation. Migrate the aggregate instead. |

**The ambiguous case, handled explicitly.** `Azure.Identity` and `Azure.Core` are present but arrived transitively with Dataverse. They are neither approved nor deprecated: they are **unclassified**, which is the one state that must not persist. Before **M-02-1.4** completes, either adopt them deliberately — the plausible reason being `ISecretResolver` at **M-01-5.1** — or let them leave with Dataverse. An unclassified dependency surviving the removal of the thing that brought it in is how stacks accumulate.

---

## 8. Detecting obsolete stack use

Six checks. Three are automatable today; three are TARGET.

| # | Check | How | State |
|---|---|---|---|
| 1 | Package not listed in TECHNOLOGY_STACK.md | Compare the union of all `PackageReference` entries and `package.json` dependencies against §2 of TECHNOLOGY_STACK.md. | **TARGET** — a build step at **M-08-1.2**; manual at milestone boundaries until then. |
| 2 | Version disagreement between projects | The same package at two versions across `.csproj` files. Central package management makes this structurally impossible. | **TARGET** — closed by introducing `Directory.Packages.props`. |
| 3 | Deprecated technology referenced | Any assembly reference to `Microsoft.Xrm.Sdk`, `Microsoft.Crm.Sdk.Proxy` or `Microsoft.PowerPlatform.Dataverse.Client`. | **CURRENT** — greppable now; count decreases as aggregates migrate, and reaches zero at **M-02-1.4**. |
| 4 | Boundary violation introduced by a dependency | `PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs`, `BoundaryTests.cs`. A provider SDK type reaching a Contracts project is the failure these exist to catch. | **CURRENT** locally; blocking at **M-08-1.4**. |
| 5 | Vulnerable package | `dotnet list package --vulnerable --include-transitive`, `npm audit`. | **CURRENT** manually; automated at **M-08-1.2**. |
| 6 | Version out of support window | Compare against published end-of-life dates. | **TARGET** — this is precisely what **M-03-2.1 Technology catalogue** provides: technologies and versions registered with support windows and end-of-life dates. |

**Compiler-level detection, available now and underused.** `TreatWarningsAsErrors` in `Directory.Build.props` turns every `[Obsolete]` attribute in every dependency into a build failure. That is the cheapest obsolescence detector in the stack and it needs no pipeline. Confirm it is set during the §1 audit.

---

## 9. Connection to GOVERNANCE

This document is the interim register. It is prose, maintained by hand, and it will drift.

**M-03-2.1 Technology catalogue** — *technologies and their versions are registered with support windows and end-of-life dates* — replaces the register, not the policy. After it lands:

| Concern | Before M-03-2.1 | After M-03-2.1 |
|---|---|---|
| What versions are in use | This document, by hand | Technology Registry, from the project files |
| Support window / EOL | Not tracked | Registry field |
| Which product uses what | Not tracked | **M-03-2.2 Product technology usage** |
| Approved / deprecated state | §7 here | Registry lifecycle state |
| *Why* a version moves, and what a breaking upgrade costs | **This document** | **This document** — unchanged |
| Conformance checking | Manual | **M-03-6.2 Standards governance and conformance** |

The division is durable: GOVERNANCE holds the facts, this document holds the rules. Two other GOVERNANCE milestones connect — **M-03-6.1 Configuration registry** for configuration keys, and **M-03-5.1 Licence registry**, which will consume the same dependency inventory §8 builds for a different purpose. Build the inventory once.

---

## 10. The version audit — the first work this policy requires

Nothing in §§3–8 can be enforced against versions nobody has written down. This is the ordered list of what must happen first, and each step is small enough to be one work item.

| # | Action | Produces | Blocked by |
|---|---|---|---|
| 1 | Read the SDK version out of all three `global.json` files and compare them | Either confirmation that the three repositories agree, or the first defect this policy finds | nothing |
| 2 | Read the contents of all three `Directory.Build.props` files | Confirmation of whether `Nullable`, `TreatWarningsAsErrors` and `TargetFramework` are actually centralised, or a list of what is missing | nothing |
| 3 | List every `PackageReference` and its version across every `.csproj` in all three repositories | The real dependency inventory — the thing TECHNOLOGY_STACK.md currently records as *unpinned* | nothing |
| 4 | Record the exact versions in TECHNOLOGY_STACK.md §2 | A stack document that states facts rather than absences | 3 |
| 5 | Introduce `Directory.Packages.props` per repository and move every version into it | Structural impossibility of two projects disagreeing | 3 |
| 6 | Read `package.json` and confirm the lock file is committed | The frontend pin, confirmed rather than assumed | nothing |
| 7 | Record the SQL Server / LocalDB engine version | The untracked dependency the `Ref` computed column depends on | nothing |
| 8 | Run `dotnet list package --vulnerable --include-transitive` and `npm audit`, and record the output | The first security baseline | 3 |

**Ordering note that matters:** step 5 is a repository-wide edit touching every project file. Under **M-07-2.2 Parallel-safety rules** it overlaps the file scope of essentially every other work item in that repository, so it is *Must be sequential* — it cannot run alongside feature work in the same repository. Schedule it at a milestone boundary, and push immediately after, per the 2026-08-20 lesson.

**What this audit is not.** It is not an upgrade. Nothing changes version in steps 1–8; the only edits are moving declarations and writing things down. Upgrading and cataloguing in the same change is exactly the compound failure §4 forbids.

## 11. Related documents

| Document | Owns |
|---|---|
| TECHNOLOGY_STACK.md | Which technologies are approved, and the not-yet-selected list |
| NAMING_STANDARDS.md | Package, project and branch naming |
| DATABASE_STANDARDS.md | Migration rules, which constrain EF Core upgrades |
| CODE_CONVENTIONS.md, CSHARP_STANDARDS.md, TYPESCRIPT_REACT_STANDARDS.md | How code is written against these versions |
| ADR-014_AZURE_SQL_MIGRATION.md | The decision deprecating the Dataverse stack |
