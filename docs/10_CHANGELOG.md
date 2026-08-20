# Changelog

This changelog consolidates the former root, Phase 1, Phase 2, roadmap, status, and review histories.

## Unreleased — Frontend foundation

Planned:

- Stabilize API errors, list envelopes, enum serialization, versioning, and CORS.
- Select canonical API/Host entry point.
- Add responsive React + TypeScript frontend.
- Deliver Workspace → Project → Conversation → Messages/Chat vertical slice.
- Implement Project Milestone and Criterion after the first slice.
- Add authentication/authorization before multi-user or public release.

## 2026-08-16 — Phase 2 persistence expansion

Repository history in the supplied source records:

- Artifact feature completed end-to-end against the live Dataverse schema.
- Session feature completed across layers.
- Live-Dataverse-only defects corrected.
- Conversation update route added.
- Full-table-scan list queries reduced with server-side filtering.
- Required input validation added for Branch, Snapshot, and Work Item paths.
- Mapper crashes on unmapped status values addressed.
- Conversation Message mapping made tolerant of missing `du_agenttype`.

## 2026-08-08 — Documentation and Phase 1 baseline

Added:

- Canonical Phase 1 vision, architecture, module, API, database, decision, standard, convention, milestone, and frontend-design documentation.
- AI handoff/context guidance.

Phase 1 foundation included:

- Clean Architecture solution projects.
- Domain entities and repository abstractions.
- Command/query handler pattern.
- Minimal APIs and Swagger foundation.
- Dataverse infrastructure pattern and in-memory development path.
- OpenAI provider behind `ILLMProvider`.
- Agent registry/runtime and Developer Agent foundation.
- Planning, execution, chat, and knowledge-retrieval foundations.

Known gaps at that baseline included real Dataverse completion, persistent memory, multi-provider support, milestone implementation, frontend, authentication, tests, and production deployment.

## Documentation consolidation — 2026-08-16

The earlier package contained 35 repository documentation artifacts and five review documents, with repeated READMEs, roadmaps, deployment/environment descriptions, Dataverse schema narratives, and nested ZIP archives.

They were reworked into this 11-file canonical set (`README.md` plus ten numbered subject files). Superseded versions, empty root placeholders, redundant registries, raw transcript-style notes, and nested archives are intentionally excluded. Unique current information is assigned to one subject file and future edits should update that file in place.
