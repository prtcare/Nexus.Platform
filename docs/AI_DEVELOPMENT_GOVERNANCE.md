# AI Development Governance

> **Status** Authoritative · **Owner** Durai · **Last updated** 2026-08-23 · **Architecture version** v2.2
> **Authoritative for** the responsibility boundary between the architect and any AI coding model used to implement Nexus, the development loop, and the contract every repository's AGENTS.md must satisfy.

## 1. Roles

**Architect** — currently Claude, operating outside any single repository. Owns architecture, planning, task decomposition, implementation specifications, review, and approval. Never delegates an architectural, cross-repository, technology-stack, database/business-model, or security-architecture decision to a coding model.

**Implementation model** — currently DeepSeek, running through Claude Code inside a single repository at a time. Interchangeable: GLM, Codex, or another approved coding model may fill this role later without changing this process. Owns repository inspection, bounded implementation, tests, build, and reporting.

**Owner** — Durai. Owns business decisions, credentials and other manual actions, and the decision of when development proceeds.

The process must not depend on any model remembering a previous terminal session or conversation. The repository and the documentation set are the persistent engineering record; a model's chat memory is not.

## 2. The loop

Discuss/design → select the smallest useful dependency-aware step → check prerequisites → generate an implementation prompt → implementation model implements → build/test/inspect → architect reviews the actual change → correction or approval → commit/checkpoint → next step.

An implementation model is never given an open-ended instruction such as "continue developing Nexus." Every task has a bounded scope and explicit acceptance criteria, decided by the architect before the prompt is written.

## 3. What the implementation model may decide

Method-level implementation; code organization consistent with an existing pattern; naming that follows NAMING_STANDARDS.md; normal error handling; tests; fixing a build or test failure its own change caused.

## 4. What it must not decide — stop and report instead

Application or cross-repository architecture; technology-stack changes; database or business-model changes; new business rules; new major abstractions; a major third-party dependency; security-architecture changes; roadmap sequencing; scope expansion beyond the active prompt. If an approved implementation step turns out to need one of these, the model stops and reports the conflict rather than guessing — the architect resolves it before implementation continues.

## 5. Existing code is evidence

Documentation states intended architecture; the real repository states what is actually built. When they disagree, the model does not silently pick one — it reports the mismatch, and the architect determines whether the documentation is stale, the implementation is stale, the difference is intentional, or a migration is already underway.

## 6. The AGENTS.md contract

Every primary repository (currently NexusAI, Nexus.Int, Nexus.Web) carries an AGENTS.md at its root. It states: what the repository is; the small mandatory reading set for any task (never the entire documentation set); that repository instructions override a coding model's default conventions; that existing implementation, naming and structure must be inspected and reused before anything new is created; what the model may decide itself; what requires architect approval; what to do before changing files; what to do before declaring completion; and the repository's currently known temporary mechanisms. It links to this document and to DOCUMENTATION_INDEX.md rather than restating their content.

## 7. Documentation discipline

One authoritative document per subject — DOCUMENTATION_INDEX.md is the map and the enforcement point. A new standards document is created only when no existing authoritative document already owns the subject; otherwise the new material is added to, or linked from, the existing owner. Mechanical documentation synchronization (for example, flipping a CURRENT→TARGET marker once a milestone closes) may be its own small step rather than folded into an unrelated implementation task.

## 8. Secrets

No credential, token, or password is ever committed, hardcoded, logged, or fabricated by a coding model. Configuration holds a reference; the value is resolved through the mechanism CONFIGURATION_STANDARDS.md and SECURITY_STANDARDS.md name for the current state of the system (today: environment variables and the documented interim scripts; going forward: ISecretResolver, M-01-5.1). If a credential must be created manually by the Owner, the implementation prompt states that as a separate manual pre-step, never as something the model can substitute or invent.

## 9. Commit and checkpoint discipline

Followed per GIT_WORKFLOW.md. A repository is confirmed in a known, healthy state (git status, git fsck) before a migration-shaped change begins. A meaningful, approved implementation step gets its own commit rather than being mixed with an unrelated one, so rollback and audit stay simple.

## 10. Review gate

After an implementation task completes, the architect inspects the actual change surface (git status, git diff --stat, git diff) rather than relying only on the model's own summary, and returns one of three outcomes: **Approved** — satisfies architecture, requirements and acceptance criteria; state documentation is updated if material, a checkpoint is confirmed, and the next step is chosen. **Correction required** — the approach is right but specific problems need fixing; a small, focused correction prompt is issued rather than repeating the whole task. **Rework required** — the change materially violates architecture, scope, security or requirements; the problem is explained and a bounded rework prompt is issued. Development does not advance to the next roadmap item until the current one is approved.

## 11. Fresh-terminal behavior

Every implementation prompt assumes zero conversational memory. The model starts in the named repository, reads its AGENTS.md and the mandatory files it names, inspects current repository status and the relevant existing code, reads whatever task-specific documentation the prompt names, and only then begins. This is a small mandatory core plus task-specific context — never a re-read of the entire documentation set per task.

## 12. Roadmap interpretation

Nexus is not built one layer to completion before the next begins. Platform, Intelligence, Developer and Delivery are built only to the depth a real capability needs, then extended. Sequencing follows the roadmap's declared dependencies (nexus-roadmap.yaml) and the two-gate structure in NEXUS_MASTER_ARCHITECTURE.md §12 — not a document's position in a numbered list.
