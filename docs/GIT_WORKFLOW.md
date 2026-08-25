# Git Workflow

**Status:** Active
**Owner:** DELIVERY (Layer 08)
**Last updated:** 2026-08-21
**Layer:** 08 DELIVERY
**Authoritative for:** repository rules, main protection, integration branches, work branches,
worktrees, branch naming, commit standard, push rules, pull requests, reviews, merge strategy,
conflict resolution, tags, release branches, hotfixes, cleanup, backup, recovery, and parallel
worker isolation.

Not authoritative for: what state a work item is in and when it may advance — that is
`DEVELOPMENT_WORKFLOW.md`; whether the code is correct — `ASSURANCE_STANDARDS.md`; who may push
where — `SECURITY_STANDARDS.md` §repository permissions.

---

## 1. Repositories

### 1.1 What exists

| Local path | Remote | Solution |
|---|---|---|
| `C:\Personal\Nexus.Platform` | `github.com/prtcare/Nexus.Platform` | `Nexus.Platform.slnx` |
| `C:\Personal\Nexus.Intelligence` | `github.com/prtcare/Nexus.Intelligence` | `Nexus.Intelligence.slnx` |
| `C:\Personal\Nexus.Experience` | `github.com/prtcare/Nexus.Experience` | `Nexus.Experience.slnx` |

`C:\Personal\LocalNuGet` is a package feed on disk. **It is not a git repository** and must never be
made one — it holds build output, and build output does not belong in version control.

### 1.2 What does not exist yet

`Nexus.Platform` (NexusAI renamed), `Nexus.Experience` (Nexus.Web renamed), `Nexus.Developer` (new)
and `Nexus.Products.<Name>` (new) are **TARGET** repositories. They do not exist. Do not write
scripts, documentation or configuration that assumes them.

### 1.3 Rules

| Rule | Statement |
|---|---|
| One solution per repository | `.slnx`, at the repository root |
| One remote per repository | `origin`, on GitHub, under `prtcare` |
| Repository-level build config | `Directory.Build.props` and `global.json` per repository |
| Package sources | `nuget.config` per repository |
| No binaries in git | No build output, no packages, no `.dll`, no `bin`/`obj` |
| No secrets in git | See `SECURITY_STANDARDS.md` and `CONFIGURATION_STANDARDS.md` |
| No nested repositories | A repository never contains another repository's `.git` |

---

## 2. The 2026-08-20 incident

This section is the incident record. The risk it describes — an unconfirmed antivirus cause, no
backup, `.git-broken` residue in all three repositories — was **closed by M-08-2.1 on 2026-08-25**;
see §2.4 for the closure and evidence. §2.1–2.3 and §2.5 remain the historical record.

### 2.1 What happened

On **2026-08-20**, all three repositories lost `.git\objects` **simultaneously**. Not one repository,
not one branch — the object database of every repository on the machine, at the same time.

Everything else survived. `HEAD`, `config`, `index`, `refs`, `logs` and `hooks` were all intact in
all three repositories. Only the object store was gone.

### 2.2 The cause

The evidence is consistent with **antivirus quarantine of extensionless zlib-compressed blobs**.
Loose git objects are extensionless binary files with a compressed header; a heuristic scanner can
classify them as suspicious and quarantine them in bulk. The simultaneity across three unrelated
repositories, and the survival of every file that *does* have a recognisable extension, both point
the same way.

It has not been proven conclusively, because the quarantine log was not captured at the time. The
lesson there is its own: **when a mass file loss happens, capture the security product's log before
doing anything else.**

### 2.3 The recovery

What was done, and what to do again:

1. Renamed the damaged `.git` to `.git-broken` — **do not delete it**, it holds `refs`, `logs` and
   `config` that tell you what you had.
2. Recorded every local ref and its commit hash from `.git-broken\refs` and `.git-broken\logs\HEAD`.
3. Cloned the repository fresh from `origin` into a temporary directory.
4. Swapped the fresh `.git` into the original working directory in place, leaving the working tree
   files untouched.
5. Re-pointed `HEAD` at the branch that had been checked out, and compared the working tree against
   the recovered ref.
6. Verified: `git status`, `git log`, `git fsck`, and a full build.

Anything that had been committed locally but never pushed was recoverable only from the reflog in
`.git-broken`, and only if the object it named still existed somewhere. In practice: **anything not
pushed was at risk of being gone.**

### 2.4 Closure — M-08-2.1 Close the 2026-08-20 recovery

**CLOSED as of 2026-08-25.** Every acceptance criterion for M-08-2.1 has been met, and the evidence
for the two environmental ones is recorded:

- **The `C:\Personal` exclusion is confirmed present in Windows Security.** The exclusion that was
  recommended on 2026-08-20 — the one whose absence left the cause of the object-store loss live —
  is now confirmed in place. This closes the residual risk described in §2.2.
- **A documented, tested backup exists for all three repositories.** On 2026-08-25, `git clone
  --mirror` backed up `Nexus.Platform`, `Nexus.Intelligence` and `Nexus.Experience` — every ref,
  branch and tag — from `origin` to `C:\Users\Dell\OneDrive - PRT\A1_Business\Nexus` (a
  OneDrive-synced off-machine copy). Each mirror passed `git fsck --strict` and a restore-verified
  `dotnet build`. Full record in §15.1.
- **The `.git-broken` directories are removed from all three repositories**, each only after a
  per-repository comparison confirmed every local ref exists on `origin` (matching, or `origin` a
  strict ancestor with nothing local-only beyond it) and that nothing was ahead of its upstream.

**Stale same-machine artifacts.** Two sets predate the real backup above and no longer serve a
purpose now that it exists: `C:\Personal\_backup\*-worktree` (the 2026-08-20 worktree snapshots)
and `C:\Personal\*-fresh` (the temporary fresh clones from the recovery). They are safe to delete
manually.

### 2.5 The rule this produced

> **Push at every stage boundary, not every milestone.**

The incident happened while `Nexus.Web` was on `feat/azure-sql` at `29ac2f4` with SQL Stage 1b
complete, proven, and **uncommitted**. Work that is proven and not pushed is work that exists in
exactly one place, on the one machine that has already demonstrated it can lose an object database
without warning.

A "stage boundary" is any point at which the work is coherent: a migration that applies, a test that
passes, an endpoint that responds. It is not the end of a milestone, not the end of a day, and not
when the work feels finished. If you can describe what you just achieved in a sentence, commit it
and push it.

---

## 3. Branch model

```
main                    protected, green-build-required, no direct commits
└── integration/<ms>    per-milestone integration branch
    ├── work/<id>-a     worker A, own worktree (sibling dir)
    ├── work/<id>-b     worker B, own worktree
    └── work/<id>-c     worker C, own worktree
```

Three levels and no more. A branch off a work branch is a signal that a work item was too large and
should have been two.

### 3.1 `main`

**TARGET — M-08-1.4 Branch protection and architecture gate.**

| Rule | Enforced by |
|---|---|
| No direct pushes | GitHub branch protection |
| Pull request required | GitHub branch protection |
| Green build required to merge | Required status check |
| NetArchTest passes as a hard gate | Required status check |
| Boundary violation blocks the merge, demonstrated once | Deliberate violation, then reverted |

**CURRENT: none of this is enforced.** `.github\workflows\` in NexusAI exists and is **empty**.
`Nexus.Web` and `Nexus.Int` have **no `.github` directory at all**. There is no CI, so there is no
status check to require, so branch protection has nothing to protect with. Protection is not
configured ahead of CI because a required check that never runs blocks every merge forever.

The order is fixed: **M-08-1.2 Pipelines on every repository → M-08-1.4 branch protection.** Until
then, `main` is protected by discipline alone, and the discipline is: no direct commits to `main`,
ever, even though nothing currently stops you.

`main` is always releasable in principle. It is never a place where work is parked.

### 3.2 Integration branches

One integration branch per milestone: `integration/M-02-1.5`.

| Rule | Statement |
|---|---|
| Created from | `main`, at the point the milestone starts |
| Receives | Work branches for that milestone only |
| Merge order | Sequential, one at a time, each verified green before the next |
| On red | The batch halts; the offending merge is reverted or fixed before another lands |
| Merged to `main` | Once, when the milestone is complete and qualified |
| Lifetime | Deleted after the merge to `main` |

Sequential verified merging is the point of the integration branch. Merging three work branches
simultaneously and testing the result tells you the combination is broken; it does not tell you
which merge broke it. This is **M-07-5.1 Review and controlled integration**, whose acceptance
criterion is explicit: integration into the milestone branch is sequential and each merge is
verified green.

### 3.3 Work branches

One branch per work item, one worker, one worktree.

| Rule | Statement |
|---|---|
| Created from | The integration branch, never from `main` |
| Owned by | Exactly one worker, human or agent |
| Scope | Exactly one work item |
| Lifetime | Hours to a few days; a work branch older than a week is a planning failure |
| Rebased onto | Its integration branch, before review |
| Deleted | After merge, locally and on `origin` |

The branch name is the join key between git and the work graph. **M-07-4.1 Build and test records**
matches CI results to work items by branch name, and rejects a result whose branch matches no active
assignment. A misnamed branch produces an unattributable build result.

---

## 4. Branch naming

```
integration/<milestone-id>          integration/M-02-1.5
work/<work-item-id>-<worker>        work/WI-02-1.5.1-a
feat/<short-description>            feat/azure-sql
fix/<short-description>             fix/workspace-ref-collision
hotfix/<version>-<description>      hotfix/1.2.1-token-expiry
release/<version>                   release/1.2.0
```

| Rule | Statement |
|---|---|
| Lowercase | Except milestone and work item ids, which keep their canonical form |
| Hyphens | Never underscores, never spaces, never camelCase |
| Worker suffix | Single letter — `-a`, `-b`, `-c` — matching the worktree |
| No personal names | The worker slot is a role, not a person |
| No ticket-only names | `work/WI-02-1.5.1-a` is fine; `WI-2151` alone is not |

`feat/azure-sql` predates this convention and is the branch that was in flight during the incident.
It stays as it is; new branches use the forms above.

---

## 5. Worktrees

Parallel work uses `git worktree`, one worktree per work branch. Each worker gets a real directory
with a real checkout and its own build output, so two workers never share a `bin`, an `obj`, a
`NuGet` restore or a LocalDB migration run.

### 5.1 The Windows caveat — read this before creating one

> **A git worktree nested inside a folder that an agent has as its working directory cannot be
> renamed while that agent runs.**

Windows holds a lock on the working directory of a running process. If a worktree lives inside a
directory an agent is operating from, cleanup, rename and removal all fail, and they fail in a way
that looks like a permissions problem rather than a lock.

**Worktrees go in a SIBLING directory.** Never inside the repository, never inside another
worktree, never inside an agent's working directory.

```
C:\Personal\Nexus.Experience\                  the repository
C:\Personal\Nexus.Experience.work\WI-02-1.5.1-a\    worker A worktree
C:\Personal\Nexus.Experience.work\WI-02-1.5.1-b\    worker B worktree
```

### 5.2 Lifecycle

```
git worktree add ../Nexus.Experience.work/WI-02-1.5.1-a -b work/WI-02-1.5.1-a integration/M-02-1.5
... work, commit, push ...
git worktree remove ../Nexus.Experience.work/WI-02-1.5.1-a
git worktree prune
```

| Rule | Statement |
|---|---|
| One worktree per work branch | Never two branches in one worktree |
| Sibling directory | Per §5.1 |
| Removed after merge | With `git worktree remove`, not by deleting the folder |
| Pruned | `git worktree prune` after any manual folder removal |
| Never shared | Two workers never operate in one worktree, ever |
| Own build output | `bin`/`obj` are per-worktree and gitignored |

### 5.3 Parallel worker isolation

A worker is isolated when all of these hold:

| Dimension | Isolation |
|---|---|
| Branch | Own work branch |
| Filesystem | Own worktree in a sibling directory |
| Build output | Own `bin`/`obj` inside its worktree |
| Database | Own LocalDB database, or a serialised turn at the shared one |
| Package cache | Shared, and read-only during the work — nobody runs `pack-local.ps1` mid-flight |
| Configuration | Own local settings, never committed |

The database is the isolation dimension that is most often missed. Two workers running
`dotnet ef database update` against the same LocalDB instance will interleave, and the second one's
migration history table will disagree with its model snapshot. Where work items touch schema at all,
they are scheduled apart — see `DEVELOPMENT_WORKFLOW.md` §parallel safety, rule 3.

`pack-local.ps1` writes to the shared `C:\Personal\LocalNuGet` feed. Two workers packing the same
package version at once will race. Until **M-08-1.1 Package feed reachable from CI** replaces the
local feed with GitHub Packages, packing is a serialised operation, announced before it happens.

---

## 6. Commits

### 6.1 The standard

```
<type>(<scope>): <subject>

<body — what changed and why, wrapped at 72 columns>

Refs: <work item id>
```

| Type | Use |
|---|---|
| `feat` | New behaviour |
| `fix` | Corrected behaviour |
| `refactor` | Changed structure, identical behaviour |
| `test` | Test only |
| `docs` | Documentation only |
| `build` | Build, packaging, dependencies |
| `chore` | Everything else, and it should be rare |

Scope is the project or area: `feat(persistence): add Ref computed column to Workspace`.

| Rule | Statement |
|---|---|
| Subject | Imperative mood, lowercase, no trailing period, ≤ 72 characters |
| Body | Explains *why*; the diff already shows *what* |
| Work item reference | `Refs:` line on every commit that belongs to a work item |
| Atomic | One logical change per commit |
| Builds | Every commit builds — a commit that does not build cannot be bisected across |
| No `wip` | Not as a message, not as a habit |
| No secrets | A secret committed and then removed is still in history — see §12 |

### 6.2 Migrations in commits

A commit containing an EF Core migration contains **that migration and nothing else** except the
model change that produced it. Migrations are the highest-conflict artefact in the repository
(`DATABASE_STANDARDS.md` §9.3), and isolating them makes the conflict resolution — drop, regenerate,
re-verify — mechanical rather than surgical.

---

## 7. Push rules

| Rule | Statement |
|---|---|
| **Push at every stage boundary** | The rule from §2.5. Not every milestone |
| Push before stopping | Never end a working session with unpushed commits |
| Push before switching context | Never leave one branch unpushed to start another |
| Push before a long-running operation | Restructure scripts, bulk renames, tool upgrades |
| Never force-push a shared branch | `main`, any `integration/*`, any branch someone else reads |
| Force-push only your own work branch | And only with `--force-with-lease`, never bare `--force` |

`--force-with-lease` refuses when the remote moved since you last fetched. Bare `--force` does not,
and will discard a colleague's commit without telling you.

The push rules are the operational lesson of the incident. Every one of them exists because the
alternative was demonstrated to lose work.

---

## 8. Pull requests

**TARGET — M-08-1.4.** Pull requests are the only path into `main` and the only path into an
integration branch.

| Element | Requirement |
|---|---|
| Title | The commit subject convention, describing the whole change |
| Description | What changed, why, what was verified, what evidence exists |
| Work item link | The work item id, matching the branch name |
| Size | Reviewable in one sitting; a PR nobody can review does not get reviewed |
| Green build | Required — once CI exists |
| Architecture tests | Green — NetArchTest is a hard gate |
| Acceptance evidence | Per `ASSURANCE_STANDARDS.md`; a claim of "tested" is not evidence |
| Draft while in progress | A PR that is not ready is a draft, not a comment thread |

**CURRENT: there is no CI, so there is no green build to require.** Pull requests today carry the
author's assertion. That is precisely the weakness **M-07-4.1 Build and test records** exists to
close: "every run carries evidence from CI rather than a human's assertion that it built."

---

## 9. Reviews

| Rule | Statement |
|---|---|
| Every merge is reviewed | Including agent-produced work. Especially agent-produced work |
| The reviewer is a person | **M-07-5.1**: the reviewer is a Layer 01 User, not a string |
| A run cannot integrate without a recorded human decision | M-07-5.1 acceptance criterion |
| A rejection returns the item to its worker with the reason recorded | M-07-5.1 acceptance criterion |
| Approval is on evidence | Not on the description of the change |

What a reviewer checks, in order of how often it catches something:

1. Does the change match the work item's scope, and only that scope?
2. Do the layer boundaries hold? Would NetArchTest pass?
3. Is there a migration, and does it follow `DATABASE_STANDARDS.md`?
4. Does anything log a secret, a token or a prompt body?
5. Is there an acceptance criterion, and does the evidence actually prove it?
6. Would this break an existing API caller? (`API_STANDARDS.md` §15)

Review comments are specific and actionable. "This is wrong" is not a review comment; "this
relationship needs `DeleteBehavior.Restrict` or it will trip error 1785 when `Project` is added" is.

---

## 10. Merge strategy

| Merge | Strategy | Reason |
|---|---|---|
| Work branch → integration | **Squash** | One work item, one commit, one revertible unit |
| Integration → `main` | **Merge commit** | Preserves the milestone as a visible unit |
| `main` → integration (sync) | **Rebase** the integration branch | Keeps history linear |
| Hotfix → `main` | **Merge commit** | The hotfix is a distinguishable event |

Squashing at the work-branch boundary means a work item can be reverted with one command. It also
means the noisy intermediate commits — which are *encouraged*, because pushing often is the rule —
do not clutter the integration history.

Rebasing an integration branch is safe because it is force-pushed only by its owner and only with
`--force-with-lease`. Rebasing `main` is never safe and never done.

---

## 11. Conflict resolution

### 11.1 General

Rebase your work branch onto its integration branch before requesting review. Resolve conflicts in
your own branch, never in the integration branch, and never during the merge itself.

After any non-trivial resolution: build, run the tests, and re-verify the specific behaviour the
work item claims. A conflict resolution that compiles is not a conflict resolution that is correct.

### 11.2 Migration and model-snapshot conflicts

This one has a specific procedure because the general one produces a broken model:

1. **Do not hand-merge the model snapshot.** It is generated; a merged snapshot describes a model
   that no code produces.
2. Rebase onto the integration branch.
3. Delete your migration file *and* revert the snapshot to the integration branch's version.
4. Regenerate the migration against the updated snapshot.
5. Apply it to a clean database and verify.
6. Amend the commit.

Better than resolving this conflict is not having it: parallel-safety rule 3 in
`DEVELOPMENT_WORKFLOW.md` exists to keep two migrations off one DbContext at the same time.

### 11.3 Contract conflicts

A conflict in `Nexus.Platform.Contracts` or `Nexus.Intelligence.Contracts` is a scheduling failure,
not a merge problem. Two work items changed a shared boundary simultaneously, which parallel-safety
rule 4 forbids. Resolve the merge, then fix the schedule so it does not recur.

---

## 12. Secrets in history

If a secret reaches any branch:

1. **Rotate the secret first.** It is compromised the moment it is pushed; history rewriting does
   not un-compromise it.
2. Remove it from the working tree and push the removal.
3. Rewrite history only if the branch has not been widely fetched, and coordinate the force-push.
4. Record the incident.

The prevention is in `CONFIGURATION_STANDARDS.md` §what must never enter Git, and the durable fix is
**M-01-5.1 Real secret resolver**, whose acceptance criteria include a secret scan running in CI
that fails the build on a match.

---

## 13. Tags, releases and hotfixes

### 13.1 Tags

| Rule | Statement |
|---|---|
| Format | `v<major>.<minor>.<patch>` — `v1.2.0` |
| Annotated | Always `git tag -a`, never lightweight |
| On `main` only | A tag never points at an integration or work branch |
| Immutable | A tag is never moved, never deleted, never reused |
| Pushed explicitly | `git push origin v1.2.0` |

Nothing is tagged today. Nexus has not released.

### 13.2 Release branches

A release branch is created only when work must continue on `main` while a release stabilises:

```
release/1.2.0    from main, fixes only, merged back to main, tagged
```

If there is no parallel work to protect, tag `main` and skip the branch. A release branch that
exists out of ceremony is a branch that will diverge.

### 13.3 Hotfixes

```
hotfix/1.2.1-token-expiry    from the release tag, not from main
```

| Step | Requirement |
|---|---|
| Branch from | The tag being fixed |
| Scope | The single fault. Nothing else travels with a hotfix |
| Verified | The same gates as any other merge. A hotfix is not exempt |
| Merged to | `main`, and to any active release branch |
| Tagged | Patch increment |
| Followed by | A recorded reason the fault reached a release |

A hotfix that skips review is how the second incident happens.

---

## 14. Cleanup

| What | When | How |
|---|---|---|
| Work branch, local | After merge | `git branch -d` |
| Work branch, remote | After merge | `git push origin --delete` |
| Worktree | After merge | `git worktree remove`, then `git worktree prune` |
| Integration branch | After merge to `main` | Deleted locally and remotely |
| `.git-broken` | **Only after M-08-2.1** | After confirming every local ref exists on `origin` |
| Stale remote refs | Routinely | `git fetch --prune` |

Nothing is deleted before it is confirmed present somewhere else. That applies to a branch and it
applies emphatically to `.git-broken`.

---

## 15. Backup and recovery

### 15.1 Current state

**CURRENT: `origin` on GitHub is the only backup, and it is not a backup.** A remote protects against
local loss — which is exactly what happened, and it worked. It does not protect against a bad force
push, an accidental repository deletion, an account compromise, or a provider outage.

**A documented, tested backup now exists.** On **2026-08-25**, `git clone --mirror` was used to back
up all three repositories (`Nexus.Platform`, `Nexus.Intelligence`, `Nexus.Experience`) — every ref,
branch, and tag — from `origin` on GitHub to `C:\Users\Dell\OneDrive - PRT\A1_Business\Nexus`
(OneDrive-synced off-machine copy). Each mirror passed `git fsck --strict`, and each was
restore-verified: a fresh clone of the mirror into a temp folder was built with `dotnet build`,
succeeding with **0 errors, 0 warnings** for all three. This satisfies the acceptance criterion of
**M-08-2.1**; automated backup with tested restore remains **M-08-7.1 Automated backup** and
**M-08-7.2 Tested restore**.

### 15.2 What a real backup requires

| Property | Requirement |
|---|---|
| Independent | Not on the development machine, not solely on GitHub |
| Complete | All refs, all tags, all branches — `git clone --mirror` |
| Scheduled | Automatic, not remembered |
| Verified | A restore is performed and the result builds |
| Recorded | The restore test has a date and an outcome |

**An untested backup is a hypothesis.** M-08-7.2 exists as a separate milestone from M-08-7.1
precisely because taking a backup and being able to restore one are different achievements.

### 15.3 Recovery procedure

For loss of the object database, follow §2.3 exactly. For loss of a single branch, recover from
`origin`; for loss of an unpushed commit, `git reflog` while the reflog still exists — it expires,
by default, in 90 days for reachable objects and 30 for unreachable ones.

**Before any recovery attempt: preserve the damaged state.** Rename, do not delete. The damaged
`.git` is evidence and is frequently the only record of what the refs used to be.

---

## 16. Current gaps

| Gap | State | Closed by |
|---|---|---|
| No CI on any repository | `.github\workflows\` empty in NexusAI; absent in the other two | M-08-1.2 |
| `main` not protected | Direct pushes are possible in all three repositories | M-08-1.4 |
| No architecture gate | NetArchTest exists but runs only when someone runs it | M-08-1.4 |
| Antivirus exclusion unconfirmed | The 2026-08-20 cause is still live | M-08-2.1 |
| `.git-broken` still present | In all three repositories | M-08-2.1 |
| No automated backup | Manual `git clone --mirror` backup with tested restore exists (2026-08-25); automation pending | M-08-7.1, M-08-7.2 |
| Local package feed | `C:\Personal\LocalNuGet` is unreachable from CI | M-08-1.1 |
| Uncommitted proven work | `Nexus.Web` `feat/azure-sql` at `29ac2f4`, Stage 1b uncommitted | M-02-1.1 Commit Stage 1b |

The last row is the most urgent line in this document. Proven, working, uncommitted code on a
machine that has already lost its git objects once is the exact condition that produced the
incident.

---

## 17. References

- `DEVELOPMENT_WORKFLOW.md` — work item states, parallel-safety rules, six-way classification.
- `ASSURANCE_STANDARDS.md` — what evidence a pull request must carry.
- `SECURITY_STANDARDS.md` — repository permissions, worker permissions, secret handling.
- `CONFIGURATION_STANDARDS.md` — what may and may not enter git.
- `DATABASE_STANDARDS.md` — migration ownership and the model snapshot.
- GIT_RECOVERY_2026-08-20.md — the original incident record in the Nexus.Platform docs set.
