# AGENTS.md — Nexus.Platform

**Repository**: C:\Personal\Nexus.Platform · github.com/prtcare/Nexus.Platform · solution Nexus.Platform.slnx
**Is**: The Platform layer — model gateways, provider neutrality, usage metering, quota policy, audit. Ships as NuGet packages, not a service. See README.md for the full is/is-not.
**This repo is also the documentation hub for all of Nexus** — `docs\` here is authoritative for all three repositories.

## Read before implementing (always)

1. This file.
2. `docs\DOCUMENTATION_INDEX.md` — the map of every authoritative document.
3. `docs\CURRENT_STATE.md` — what is actually built right now, including known temporary mechanisms and documentation gaps.
4. `README.md` (this repository) — is/is-not, local dev commands.
5. Whatever the active implementation prompt names as task-specific reading.

## Authoritative rules for this repository

Repository instructions in this file override a coding model's default conventions. Coding/naming/security/testing/git rules live in and are owned by the standards indexed in `docs\DOCUMENTATION_INDEX.md`. If this file and one of those disagree, report the conflict — do not silently pick one. The full model-independent development process is `docs\AI_DEVELOPMENT_GOVERNANCE.md`.

## Before changing anything

Inspect the existing implementation, folder structure, and naming already in use in the area you are touching. Reuse it. Confirm `git status` is clean and `git fsck` reports no corruption before starting — this repository has a documented history of git object loss (`docs\GIT_WORKFLOW.md` §2), and a `.git-broken\` folder still sits here pending `M-08-2.1`. Do not delete `.git-broken\` without architect approval — it is the only surviving record of some pre-recovery commits.

## What you may decide yourself

Method-level implementation, code organization consistent with existing patterns, naming per NAMING_STANDARDS.md, normal error handling, and fixing compilation/test failures caused by your own changes.

## What requires architect approval — stop and report, do not guess

Any change to public contracts in `Nexus.Platform.Contracts`, any new cross-repository dependency, any technology or major package addition, any change to the pack/publish mechanism's target architecture, any database/business-model decision, and anything the active implementation prompt does not explicitly authorize.

## Before declaring completion

Build the affected projects, run relevant tests, check the acceptance criteria in the active prompt one by one, and run `git status` / `git diff --stat` to account for every changed file.

## Known temporary mechanisms in this repository

See `docs\CURRENT_STATE.md` for full detail and evidence. As of 2026-08-23: `pack-local.ps1` packs to `C:\Personal\LocalNuGet` (closing at `M-08-1.1`) — note the consuming `nuget.config` lives in **Nexus.Int**, not here, this repository has none; `InMemoryUsageMeter`, `PermissiveQuotaPolicy`, `ConsoleAuditLog` live in `src\Nexus.Platform.Core\Governance\`, in-memory only; `Platform.Persistence`, `.Identity`, `.Tools` and the Anthropic provider are single-file stub scaffolds.
