# Current State

> **Status** Authoritative · **Owner** Durai · **Last updated** 2026-08-23 · **Architecture version** v2.2
> **Authoritative for** what is actually built and running right now, separate from what the roadmap plans. Facts below were verified against the live repositories on 2026-08-23 — see `_incoming\landing-report.md` for full evidence per item.

## Completed

- V2.1 three-solution restructure (NexusAI / Nexus.Int / Nexus.Web split).
- Frontend F0 — single HTTP path through `ApiClient`, dead `products` feature removed. Verified live as commit `79d42ed` in Nexus.Web ("F0: single HTTP path, /api/v1 base, dead products feature removed"). An earlier reference to this work cited commit `267b4b7` — that object was lost in the 2026-08-20 incident and survives only in `.git-broken\logs\HEAD`; `79d42ed` is the live equivalent and the one to cite going forward.
- v2.2 documentation bundle landed into NexusAI (2026-08-23), non-destructively, alongside the pre-existing documentation set. See `docs\DOCUMENTATION_INDEX.md` for what's reconciled and what's still gapped.

## Current

Documentation baseline established (this pass). Next: resume roadmap implementation at `M-08-1.1`, package feed reachable from CI (GATE A, P0) — but re-scope it first. The LocalNuGet-referencing `nuget.config` lives in **Nexus.Int**, not NexusAI; NexusAI itself has no `nuget.config`. The milestone's original NexusAI-only framing needs correcting before that work resumes.

## Temporary mechanisms

| Mechanism | Where (verified 2026-08-23) | Closes at |
|---|---|---|
| `pack-local.ps1` packs to `C:\Personal\LocalNuGet` | NexusAI (repo root) | `M-08-1.1` |
| LocalNuGet-referencing `nuget.config` | **Nexus.Int** (not NexusAI) | `M-08-1.1` |
| `InMemoryUsageMeter` (`src\Nexus.Platform.Core\Models\`, moved from `Governance\` in Batch 06 -- see architecture/NEXUS_V2_EXECUTION_BATCH_06_REPORT.md), `PermissiveQuotaPolicy` (`src\Nexus.Platform.Core\ProductCore\`, moved from `Governance\` in Batch 06), `ConsoleAuditLog` (`src\Nexus.Platform.Core\`, moved from `Governance\` in Batch 07 -- audit reclassified CORE-owned, not Governance-owned -- see architecture/NEXUS_V2_EXECUTION_BATCH_07_REPORT.md) | **NexusAI** | Later persistence milestones |
| `InMemoryMemoryStore` | Nexus.Int | Later persistence milestones |
| `ChatTurnIdentity` hardcoded tenant (`nexus-dev`) + fixed permissions (`chat:send-message`), no auth on either API | Nexus.Web | Identity work (decision D-1, per the code's own TODO) |
| `Nexus.Platform.Persistence`, `.Identity`, `.Tools`, `.Providers.Anthropic` are single-file, ~7–8 line stub scaffolds | NexusAI | Later GATE A/B milestones per layer |
| `.git-broken\` folders (missing `objects` dir) present near all three repos, from the 2026-08-20 incident | NexusAI, Nexus.Int, Nexus.Web | `M-08-2.1` (not yet closed) |

## Not yet implemented (near-term)

- GitHub Packages feed (`M-08-1.1`) — needs re-scoping, see Current above.
- CI pipelines on any repository (`M-08-1.2`).
- Real secret resolver `ISecretResolver` (`M-01-5.1`) — `set-openai-key.ps1` is documented as today's interim mechanism; not independently re-verified in this pass.
- `M-08-2.1` — close out the 2026-08-20 git incident across all three repositories.

## Known documentation gaps

Surfaced by the 2026-08-23 reconciliation. Real, non-blocking, intentionally deferred rather than fixed in this pass:

1. Coding-agent prompt discipline — addressed by `docs\AI_DEVELOPMENT_GOVERNANCE.md` (landed alongside this document).
2. PowerShell `Invoke-Native` / `$ErrorActionPreference='Stop'` convention — not written down in any current doc.
3. No consolidated technical-debt table — the old numbered set had one; nothing replaces it yet.
4. No changelog — root `CHANGELOG.md` is empty with no successor.
5. Frontend UX/screen design (app shell, navigation, core screens) has no owning document in the current set.
6. `DATABASE_STANDARDS.md` §3.6's Ref-prefix registry lists only `WKS-`; `PRJ-`, `CON-`, `MSG-`, `KNW-`, `ADR-`, `WRK-`, `ART-`, `SES-`, `BRN-`, `SNP-` are used elsewhere but unregistered there.

## Known blockers

| Item | Status |
|---|---|
| SQL Stage 1b commit | Not independently re-verified in this pass |
| Antivirus exclusion on `C:\Personal` | Not independently re-verified in this pass |
| OpenAI billing | Blocks end-to-end verification of citations/usage/cost fields |
| `.git-broken\` cleanup (`M-08-2.1`) | Present in all three repos as of 2026-08-23, unclosed |

> Update this document when something materially changes — a milestone completes, a temporary mechanism closes, a gap closes, a blocker appears or clears. Do not update it for every commit.
