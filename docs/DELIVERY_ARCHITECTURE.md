# Delivery Architecture

**Status:** TRANSITION — the source-control half exists and is in daily use; **the build,
environment, deployment and infrastructure halves do not exist at all.** Every gap names the
milestone that closes it
**Owner:** Durai
**Last updated:** 2026-08-21
**Layer:** 07 DELIVERY (v2.2, was 08 DELIVERY under v2.1 — see `LAYER_MODEL.md` §2.2) — contracts
and records in `Nexus.Platform`, pipelines per repository, schema
`delivery`
**Authoritative for:** the shape and boundaries of the DELIVERY layer — repositories, branches, tags
and commits as records; where pipelines live and why; how build and test execution produces results
another layer can consume; artifacts; the environment model and its orthogonality to release
maturity; infrastructure; deployment and promotion; backup, restore and disaster recovery.

**Not authoritative for:** branch naming, worktree commands, commit format, pull request rules or
merge strategy — `GIT_WORKFLOW.md`. What a build *means* for a work item — `DEVELOPER_ARCHITECTURE.md`.
Whether a requirement was satisfied — `ASSURANCE_ARCHITECTURE.md`. Runtime health after deployment —
`OPERATIONS_ARCHITECTURE.md`. Which technologies are approved — `TECHNOLOGY_STACK.md`. Configuration
hierarchy and what may never enter git — `CONFIGURATION_STANDARDS.md`.

---

## 1. Purpose

DELIVERY safely moves source into reproducible running systems, and preserves everything required to
reconstruct them.

Two halves, in very different states:

| Half | Concern | State |
|---|---|---|
| **Source** | Repositories, branches, tags, commits, backup | Exists, in daily use, and lost its object database once |
| **Everything after source** | Pipelines, builds, artifacts, environments, deployment, infrastructure | **Does not exist.** Nothing is deployed anywhere |

The layer's position in the roadmap was corrected once and the correction is worth stating, because
it explains why the first work in the entire programme is a build pipeline. The original brief placed
DELIVERY after the Foundation Gate. It cannot be: the gate's own acceptance test requires independent
build, independent test and result capture across three simultaneous workers, and none of that is
demonstrable without CI. A minimal DELIVERY slice therefore moves to P0 and is the first work in the
roadmap.

---

## 2. What this layer owns, and the one boundary that matters

`DATA_OWNERSHIP.md` §4 holds the full entity list. Twelve entities in four groups:

| Group | Entities |
|---|---|
| Source | `Repository`, `GitBranch`, `Tag`, `Commit` |
| Build | `Pipeline`, `PipelineRun`, `BuildArtifact` |
| Environments | `Environment`, `Deployment`, `InfrastructureResource` |
| Recovery | `BackupRecord`, `RestorePoint` |

`GitBranch` carries the `Git` prefix deliberately: `Branch` alone collides with the existing Chat
aggregate and with the branch concept in any product that has one.

**The boundary that matters is with DEVELOPER, and it is a boundary of meaning, not of mechanism.**

> DELIVERY produces a build. DEVELOPER decides whether that build satisfies a work item.

The same event lands as two rows in two layers. `PipelineRun` records what ran, on what commit, with
what outcome. `BuildRecord` records DEVELOPER's judgement that the run satisfies work item X. Neither
row is derivable from the other, because "the tests passed" and "the thing we asked for was built"
are different claims — the second one is ASSURANCE's whole reason for existing.

DELIVERY therefore never reads a work item, never knows a milestone, and never decides that anything
is done. It emits a structured result and stops.

---

## 3. Where the code lives, and why the pipelines do not

| Component | Repository |
|---|---|
| `Nexus.Delivery.Contracts` — the result artifact schema, records other layers reference | `Nexus.Platform` |
| `Nexus.Delivery.Core` — `Environment`, `BuildArtifact`, promotion workflow | `Nexus.Platform` |
| `Nexus.Delivery.Infrastructure` — provisioning, deployment, migration-on-deploy | `Nexus.Platform` |
| **The pipelines themselves** | **Inside each repository, as configuration** |

Contracts and records are centralised; pipeline definitions are not. A pipeline that is not next to
the code it builds drifts from it — the build file stops matching the solution it builds, and nothing
detects the divergence until a change to one breaks the other in a repository nobody was editing.
Pipeline definitions live in `.github/workflows/` in the repository they build, version alongside the
code, and are reviewed in the same pull request that changes what they build.

**CURRENT — TARGET at `M-08-1.2`.** `C:\Personal\NexusAI\.github\workflows\` exists and is empty.
`Nexus.Web` and `Nexus.Int` have no `.github` directory at all.

---

## 4. Source control

### 4.1 Repositories — CURRENT

| Path | Remote | Solution |
|---|---|---|
| `C:\Personal\NexusAI` | `github.com/prtcare/NexusAI` | `Nexus.AI.slnx` |
| `C:\Personal\Nexus.Int` | `github.com/prtcare/Nexus-Int` | `Nexus.Int.slnx` |
| `C:\Personal\Nexus.Web` | `github.com/prtcare/Nexus-web` | `Nexus.Web.slnx` |
| `C:\Personal\LocalNuGet` | — | Local package feed, **not a git repository** |

DONE (2026-08-24) — `Nexus.Platform` (NexusAI renamed), `Nexus.Intelligence` (Nexus.Int renamed),
`Nexus.Experience` (Nexus.Web renamed); `Nexus.Developer` and `Nexus.Products.<Name>` remain to be created.

### 4.2 Branches, worktrees and the DEVELOPER relationship

The branch model is `GIT_WORKFLOW.md` §3 and §4. What belongs here is the **relationship**: branch
structure is the join between the two layers.

```
main                    protected, green-build-required, no direct commits
└── integration/<ms>    per-milestone integration branch
    ├── work/<id>-a     worker A, own worktree (sibling directory)
    ├── work/<id>-b     worker B, own worktree
    └── work/<id>-c     worker C, own worktree
```

DEVELOPER allocates the branch name from the work item id and allocates the worktree path.
DELIVERY's pipeline runs on the branch and emits a result stamped with the branch name.
**The branch name is the join key** — it is what lets `M-07-4.1` match a CI result to the correct
work item mechanically, with no lookup table and no human transcription. A result whose branch
matches no active assignment is rejected rather than stored.

Worktrees are allocated as **siblings** of the repository, never nested. That rule is DEVELOPER's to
enforce at allocation time and `GIT_WORKFLOW.md` §5.1 owns the reasoning; it appears here only
because it is the constraint that makes three simultaneous pipeline runs possible from one
repository.

### 4.3 Tags and commits

`Tag` and `Commit` are records, not mechanisms — DELIVERY stores what git already knows so that an
artifact, a deployment and a release qualification can all reference the same immutable point.
`BuildArtifact` and `Tag` arrive together at `M-08-3.1`, because an artifact that cannot be addressed
by tag cannot be promoted.

### 4.4 The open non-conformance from 2026-08-20

All three repositories lost `.git\objects` simultaneously on 2026-08-20. Everything else survived —
HEAD, config, index, refs, logs, hooks. The cause is consistent with antivirus quarantine of
extensionless zlib blobs. Recovery was a fresh clone with an in-place `.git` swap, and `.git-broken\`
still sits in all three repositories.

**The antivirus exclusion for `C:\Personal` was recommended and has never been confirmed.** The
condition that destroyed three repositories is therefore still live. `M-08-2.1` closes it, and its
acceptance criteria are unusually concrete for a P0 item: confirm the exclusion is present in Windows
Security with evidence recorded in the runbook; a documented, tested backup for all three
repositories; `.git-broken` removed only after verifying every branch is pushed to origin; and the
written rule that pushing happens at every stage boundary, not every milestone.

The narrative is `GIT_RECOVERY_2026-08-20.md`. The live rules are `GIT_WORKFLOW.md` §2.

---

## 5. Continuous integration

### 5.1 Current state — there is none

`.github/workflows/` in NexusAI exists and is empty. `Nexus.Web` and `Nexus.Int` have no `.github`
directory. There is no deployment pipeline, no infrastructure-as-code, and no environment definition
anywhere in any repository.

Build and test results are, today, a human's assertion on one laptop. That assertion does not survive
a single parallel worker and it produces no evidence trail, which is why every one of the four
CI milestones sits in P0 and gates GATE A.

### 5.2 The four P0 milestones

| Milestone | Delivers | Why it is first |
|---|---|---|
| `M-08-1.1` Package feed reachable from CI | `Nexus.Platform.*` and `Nexus.Intelligence.*` publish to GitHub Packages; `nuget.config` points at a reachable feed | **`C:\Personal\LocalNuGet` is unreachable from a build agent.** No pipeline can restore until this moves |
| `M-08-1.2` Pipelines on every repository | Restore, build and test on every push, in all three repositories | Establishes the pattern; Nexus.Platform's pipeline is the template the others copy |
| `M-08-1.3` Machine-readable results | A versioned JSON artifact per run — branch, commit, outcome, test counts — retrievable by branch | This is the DEVELOPER interface. Without it, ingestion means parsing logs |
| `M-08-1.4` Branch protection and architecture gate | `main` unpushable directly; no merge without a green build; NetArchTest as a hard gate | Boundaries enforced by the pipeline rather than by whoever remembers them |

Two details in `M-08-1.2` are easy to skip and both are acceptance criteria. The **frontend build and
typecheck** run in the `Nexus.Experience` pipeline — a .NET-only pipeline in a repository containing
`Nexus.Experience.Client` is a false green. And `Nexus.Platform.Tests` — a `.csproj` with zero `.cs` files —
either gets a real test or is deleted, because **an empty test project passing in CI is worse than no
project**: it reports success for work that was never done.

`M-08-1.4` is the milestone that makes parallel workers safe at all. Four architecture test files
exist today — `PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs`, `BoundaryTests.cs` — and they are
run by whoever remembers to run them. Enforced by a pipeline, they become the mechanism that stops
three simultaneous workers from each breaking a different boundary. The milestone requires proving it
once with a deliberate violation, then reverting.

### 5.3 The result artifact — the layer's most important contract

`M-08-1.3` defines the seam between DELIVERY and DEVELOPER, and its design decisions are all about
making the seam mechanical:

| Decision | Reason |
|---|---|
| A **structured artifact**, not a log | A log is parsed; an artifact is read. Parsing breaks when the log format changes |
| Keyed by **branch name** | The branch derives from the work item id, so the join needs no lookup |
| **Versioned schema** | DEVELOPER's ingestion evolves without every pipeline changing on the same day |
| Retrievable **by branch** | Three concurrent runs mean "the latest result" is meaningless |
| Emitted by **every** pipeline | A repository whose pipeline does not emit one is invisible to DEVELOPER |

---

## 6. Test execution — what DELIVERY does and does not do

The four-layer split is `DEVELOPMENT_WORKFLOW.md` §3. DELIVERY's share is narrow and mechanical:

| DELIVERY does | DELIVERY does not |
|---|---|
| Execute unit and integration tests repeatably | Decide what needs testing — DEVELOPER |
| Report counts, durations and outcomes | Decide whether the requirement was satisfied — ASSURANCE |
| Fail the pipeline on a failing test or a boundary violation | Decide whether a failure is acceptable — ASSURANCE, via `Deviation` |
| Publish the result where others can read it | Interpret the result — DEVELOPER |

**CURRENT reality:** five test files exist across three repositories, of which three are architecture
tests and **two are behaviour tests** — `KeywordContextRankerTests.cs` and
`ChatContextBundleMapperTests.cs`. Repeatable execution of two behaviour tests is still worth having,
because the pipeline is the thing that has to exist before the tests are worth writing; but nobody
should read a green pipeline in P0 as coverage.

A pipeline run becomes ASSURANCE `Evidence` at `M-09-1.2`, where it is mapped to the acceptance
criteria it covers — and a pipeline covering no declared criterion is recorded but proves nothing.

---

## 7. Artifacts — `M-08-3.1`, P2 / GATE B

Every green build produces a retrievable, immutable artifact, addressable by commit and by tag, under
a retention policy that prevents unbounded growth.

Immutability is the load-bearing property. Promotion (§10) moves the *same* artifact up through
environments rather than rebuilding per environment, and that is only a meaningful guarantee if the
artifact cannot change between promotions. A rebuilt artifact is a different artifact, whatever the
version label says, and everything qualified about the first one has to be re-established for the
second.

---

## 8. Environments and the orthogonality rule

### 8.1 The rule

> **Release maturity and environment are two independent axes. Neither is expressible in the other's
> vocabulary.**

| Axis | Values | Owner |
|---|---|---|
| **Release maturity** | Idea → … → End of Life | 07 DEVELOPER, `M-07-7.2` |
| **Environment** | Local, Development, Integration, Staging, Pre-Production, Production | 08 DELIVERY, `M-08-4.1` |

`Environment` carries no maturity field. Maturity carries no environment field. **`Dev`/`Test`/`Prod`
appears nowhere as maturity terminology** — it is an environment vocabulary and using it for maturity
collapses two facts into one misleading status.

The test of the model is a single sentence: *a Beta release is running in Production.* That must be
expressible without contradiction, because it is the normal state of any product with an early-access
programme. A model that cannot say it will force somebody to lie in one field or the other, and the
field they lie in will be the one a promotion decision reads.

The maturity half of this pair is `DEVELOPER_ARCHITECTURE.md` §14.2. This document owns the
environment half.

### 8.2 Environments

`M-08-4.1`, P2 / GATE B. **CURRENT: no environment definition exists anywhere.** The only running
system is a developer's laptop — Chat API on `http://localhost:5299`, SQL Server LocalDB, the
Intelligence API on a port that is not recorded. `LOCAL_DEVELOPMENT.md` owns that topology.

An `Environment` is a named place a deployment can land, carrying its configuration binding. Six of
them, and the list is closed at `M-08-4.1` rather than open, because an environment vocabulary that
grows per product stops being comparable across products.

---

## 9. Infrastructure

`InfrastructureResource` arrives at `M-08-4.2` — an environment's databases, storage and networking
created from a definition rather than from portal clicks. Its acceptance criterion is behavioural: a
new environment is stood up without manual portal clicks.

Infrastructure-as-code proper is P3, and it is two milestones rather than one:

| Milestone | Delivers |
|---|---|
| `M-08-6.1` Environments defined in source | An environment can be destroyed and rebuilt from the repository, verified functionally identical by smoke test |
| `M-08-6.2` Drift detection | A manual change to a provisioned resource raises a drift finding |

Drift detection is separate because declaring infrastructure in source does not prevent anyone from
changing it by hand; it only makes the divergence detectable. Without `M-08-6.2`, an
infrastructure-as-code repository becomes a description of what someone once intended.

**CURRENT: no IaC of any kind exists.** No container tooling has been selected, and no cloud provider
beyond Azure SQL has been chosen. `TECHNOLOGY_STACK.md` records both as NOT SELECTED, and neither
should be presumed by anything written here.

---

## 10. Deployment and promotion — `M-08-5.1`, `M-08-5.2`, P2 / GATE B

**CURRENT: nothing is deployed anywhere.** There is no path from a green build to a running system.

| Milestone | Delivers |
|---|---|
| `M-08-5.1` Automated deployment | A merge to `main` deploys to Development automatically. A `Deployment` records commit, artifact, environment, actor and outcome. A failed deployment rolls back and records the reason |
| `M-08-5.2` Release promotion | The same artifact moves up environments. Promotion to Production requires a recorded human approval |

Database migration on deploy is its own work item (`WI-08-5.1.2`) rather than a step inside the
deployment pipeline, because schema change is the one part of a deployment that is not trivially
reversible. Rolling back an application artifact restores the previous behaviour; rolling back a
migration may not restore the previous data.

**Promotion consults ASSURANCE.** `M-09-5.1` makes promotion to Production blocked by an unqualified
release, and the qualification names every unmet criterion. DELIVERY provides the mechanism;
ASSURANCE provides the verdict; DEVELOPER provides the release the verdict is about. No layer decides
alone that something may ship.

---

## 11. Backup, restore and disaster recovery

Three levels, arriving at three different times, and the ordering reflects what is actually at risk.

| Level | Milestone | Scope |
|---|---|---|
| **Source backup minimum** | `M-08-2.1` (P0, GATE A) | A documented, tested backup of all three repositories, plus the antivirus exclusion confirmed |
| **Automated backup** | `M-08-7.1` (P3) | Every database and artifact store on a schedule. `BackupRecord`, `RestorePoint`. Success *and* failure recorded and alertable |
| **Tested restore** | `M-08-7.2` (P3) | A scheduled restore drill with its result recorded, against stated recovery time and recovery point objectives |

The reasoning for the split is the 2026-08-20 loss itself. It was survivable **only because GitHub
held the history** — the local object databases were gone in all three repositories simultaneously.
Once production data exists, no equivalent third party holds a copy, and that is the moment
`M-08-7.1` stops being prudent and becomes mandatory.

`M-08-7.2` exists as a separate milestone because a backup that has never been restored is a belief.
A restore drill on a schedule, with recovery time measured against a stated objective, is the only
form of evidence that distinguishes the two. OPERATIONS runs the drills as an operational practice at
`M-10-7.1`; DELIVERY owns the mechanism and the records.

Backup **monitoring** — alerting when a backup fails rather than discovering it at restore time — is
OPERATIONS. See `OPERATIONS_ARCHITECTURE.md`.

---

## 12. GATE A minimum versus later maturity

GATE A takes only what the gate's own acceptance test needs. Everything else waits, and waiting does
not block business development.

### 12.1 In GATE A

| Capability | Milestone | Phase |
|---|---|---|
| Package feed reachable from a build agent | `M-08-1.1` | P0 |
| Git integration, branch and worktree rules | `M-08-1.1`, `GIT_WORKFLOW.md` | P0 |
| CI build on every repository | `M-08-1.2` | P0 |
| Automated test execution | `M-08-1.2` | P0 |
| Build/test results available to DEVELOPER | `M-08-1.3` | P0 |
| Branch protection and the architecture gate | `M-08-1.4` | P0 |
| Source backup and recovery minimum | `M-08-2.1` | P0 |

Seven capabilities, all P0, all mechanical. Note what is *not* there: nothing is deployed, no
artifact is retained, no environment is defined. **GATE A does not require anything to run anywhere.**
It requires that three workers can build and test independently and that the results are structured.

### 12.2 After GATE A

| Capability | Milestone | Phase / Gate |
|---|---|---|
| Artifact publication and retention | `M-08-3.1` | P2 / GATE B |
| Environment model | `M-08-4.1` | P2 / GATE B |
| Provisioning | `M-08-4.2` | P2 / GATE B |
| Automated deployment | `M-08-5.1` | P2 / GATE B |
| Release promotion with approval | `M-08-5.2` | P2 / GATE B |
| Environments defined in source | `M-08-6.1` | P3 |
| Drift detection | `M-08-6.2` | P3 |
| Automated backup | `M-08-7.1` | P3 |
| Tested restore and DR objectives | `M-08-7.2` | P3 |

`M-08-5.1` is one of the seven milestones that close GATE B. The rule that governs all of P2 applies
here in full: **this work runs in parallel with business development and must never pause or block
it.** A business system waiting for automated deployment is a scheduling error, not a dependency —
the system can be deployed by hand while `M-08-5.1` is built.

---

## 13. Boundaries with the sibling layers

| Layer | The seam |
|---|---|
| 07 DEVELOPER | DELIVERY emits `PipelineRun`; DEVELOPER interprets it as `BuildRecord`. Branch name is the join key |
| 09 ASSURANCE | A `PipelineRun` becomes `Evidence` at `M-09-1.2`. ASSURANCE blocks promotion via `M-09-5.1` |
| 10 OPERATIONS | DELIVERY ships it; OPERATIONS proves it stays healthy. `M-10-2.3` correlates a deployment with its effect on health |
| 03 GOVERNANCE | `Environment` and provisioning reference a `ProductId`. DELIVERY never names a product |
| 01 CORE | Every pipeline credential and feed token resolves through `ISecretResolver` (`M-01-5.1`) and is never committed |

---

## 14. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Container tooling | Nothing has selected one; `M-08-4.2` provisioning forces the question | **Not yet decided** — NOT SELECTED |
| Cloud provider beyond Azure SQL | `M-08-4.1` environment definitions | **Not yet decided** |
| Whether the pipeline platform stays GitHub Actions | It is presumed by `M-08-1.1`/`M-08-1.2` and never formally chosen | Not yet decided — worth an ADR before P2 |
| Artifact retention window | `M-08-3.1`; a function of storage cost and rollback depth | Not yet decided |
| Recovery time and recovery point objectives | `M-08-7.2` requires them to be *stated*, and they are not | **Not yet decided** |

---

## 15. References

- `GIT_WORKFLOW.md` — branches, worktrees, commits, push rules, pull requests, merges, tags,
  the 2026-08-20 incident and its live rules.
- `GIT_RECOVERY_2026-08-20.md` — the incident narrative.
- `DEVELOPER_ARCHITECTURE.md` — §10, how a `PipelineRun` becomes a `BuildRecord`; §14.2, release
  maturity, the other half of §8.1 here.
- `ASSURANCE_ARCHITECTURE.md` — pipeline results as evidence, release qualification.
- `OPERATIONS_ARCHITECTURE.md` — what happens after the deployment lands.
- `TECHNOLOGY_STACK.md` — approved technologies, and what is recorded as NOT SELECTED.
- `CONFIGURATION_STANDARDS.md` — configuration hierarchy, what may never enter git.
- `LOCAL_DEVELOPMENT.md` — the current local topology, ports, feeds and startup order.
- `DATA_OWNERSHIP.md` — §4, the DELIVERY entity list and the `GitBranch` naming rule.
