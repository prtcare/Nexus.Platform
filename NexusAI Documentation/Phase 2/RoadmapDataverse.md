\# NexusAI



\## Vision



NexusAI is an enterprise AI software engineering platform that provides

persistent, structured engineering memory across conversations, projects,

knowledge, decisions, artifacts and development work.



\---



\## Architecture



NexusAI uses:



\- Clean Architecture

\- .NET

\- C#

\- Minimal APIs

\- Dependency Injection

\- CQRS-style Commands and Queries

\- Repository Pattern

\- Microsoft Dataverse

\- OpenAI

\- Persistent AI Engineering Memory



\---



\## Technology Stack



\- .NET 10

\- C#

\- Microsoft Dataverse

\- OpenAI

\- Minimal APIs

\- Swagger

\- Dependency Injection

\- Repository Pattern

\- CQRS-style Application layer



\---



\## Current Architecture



\### Workspace



Top-level organizational boundary.



\### Project



Optional engineering container within a Workspace.



\### Conversation



Unified conversation model supporting:



\- Standalone conversations

\- Main project conversations

\- Sub-chats

\- Workspace conversations



\### Knowledge



Durable reusable engineering knowledge.



\### ADR



Architectural decision record.



\### Project Brief



Current project state.



\### Project Milestone



Optional project planning and progress tracking.



\---



\# Completed Milestones



\## Phase 1 — AI Foundation



Status: COMPLETE



Phase 1 delivered:



\- Workspace CRUD

\- Project CRUD

\- Conversation CRUD

\- Conversation Messages

\- Knowledge CRUD

\- WorkItem CRUD

\- Chat

\- Prompt Builder

\- Knowledge Retrieval

\- Keyword Ranking

\- OpenAI Integration

\- Dataverse Repository Layer

\- Sessions

\- Branches

\- Snapshots

\- Artifacts

\- ADRs

\- Planning

\- Execution

\- Agent foundation



Git Tag:



`v1.0.0-phase1`



\---



\# Current Phase



\# Phase 2 — Persistent AI Engineering Memory



Status: IN PROGRESS



Objective:



Build persistent engineering memory, project state, structured conversations,

collaboration, permission-aware retrieval and analytics.



\---



\## Phase 2 Milestones



\### 2.1 Dataverse Architecture \& Schema



Status: IN PROGRESS



Define and freeze:



\- Workspace

\- WorkspaceMember

\- Team

\- TeamMember

\- Project

\- ProjectMember

\- ProjectBrief

\- ProjectMilestone

\- MilestoneCriterion

\- Conversation

\- ConversationMessage

\- ConversationSummary

\- ConversationLink

\- Session

\- Branch

\- Snapshot

\- Knowledge

\- ADR

\- WorkItem

\- Artifact

\- AccessGrant



Deliverables:



\- DataverseSchema.md

\- ConversationArchitecture.md

\- MemoryArchitecture.md

\- SecurityModel.md



\---



\### 2.2 Dataverse Solution \& Environment



Status: PLANNED



DEV → TEST → PROD deployment model.



\---



\### 2.3 Domain Model Rework



Status: PLANNED



Add the Phase 2 domain entities without changing Clean Architecture.



\---



\### 2.4 Dataverse Entities \& Mappers



Status: PLANNED



Implement Infrastructure mappings.



\---



\### 2.5 Repository Layer



Status: PLANNED



Implement repositories using the existing repository architecture.



\---



\### 2.6 Project Brief



Status: PLANNED



Persist current project state.



\---



\### 2.7 Project Milestones



Status: PLANNED



Implement optional project milestones and acceptance criteria.



\---



\### 2.8 Conversation Architecture



Status: PLANNED



Implement:



\- Standalone conversations

\- Main conversations

\- Sub-chats

\- Conversation hierarchy



\---



\### 2.9 Conversation Summaries



Status: PLANNED



Persist compressed conversation state.



\---



\### 2.10 Conversation Links



Status: PLANNED



Support relationships between conversations.



\---



\### 2.11 Knowledge \& ADR Promotion



Status: PLANNED



Promote finalized sub-chat conclusions into durable knowledge and decisions.



\---



\### 2.12 Context Selection



Status: PLANNED



Allow users to explicitly select previous conversations and knowledge.



\---



\### 2.13 Persistent Retrieval



Status: PLANNED



Implement hot/warm/cold retrieval.



\---



\### 2.14 AI Context Builder



Status: PLANNED



Build controlled AI context from authorized persistent memory.



\---



\### 2.15 Memory-Aware Agents



Status: PLANNED



Connect existing agents to persistent context.



\---



\### 2.16 Collaboration \& Sharing



Status: PLANNED



Implement workspace, project, conversation and team sharing.



\---



\### 2.17 Permission-Aware Retrieval



Status: PLANNED



Enforce authorization before AI retrieval.



\---



\### 2.18 Milestone Automation



Status: PLANNED



Use acceptance criteria and evidence to update milestone status.



\---



\### 2.19 Workspace / Cross-Project Knowledge



Status: PLANNED



Allow approved knowledge to become reusable across projects.



\---



\### 2.20 Data Warehouse Foundation



Status: PLANNED



Define and implement the analytics model.



\---



\### 2.21 AI \& Engineering Analytics



Status: PLANNED



Track AI usage, retrieval, memory effectiveness and engineering metrics.



\---



\### 2.22 Integration Testing



Status: PLANNED



Validate the complete Phase 2 workflow.



\---



\### 2.23 TEST Environment Validation



Status: PLANNED



Promote and validate the Dataverse solution in TEST.



\---



\### 2.24 Production Deployment



Status: PLANNED



Promote validated Phase 2 to Production.



\---



\### 2.25 Phase 2 Completion



Status: PLANNED



Target Git tag:



`v2.0.0-phase2`



\---



\# Future Phases



Future phases may include:



\- Advanced semantic/vector retrieval

\- Advanced agent orchestration

\- Automated engineering workflows

\- Advanced collaboration

\- External integrations

\- Advanced analytics

\- Enterprise governance

\- Additional AI providers

\- Production-scale optimization



These are intentionally outside Phase 2.



\---



\# Coding Standards



\- Clean Architecture

\- SOLID principles

\- Dependency Injection

\- Repository Pattern

\- CQRS-style Commands and Queries

\- Small focused classes

\- Explicit domain boundaries

\- No business logic in Infrastructure

\- No Infrastructure dependencies in Domain

\- Async APIs

\- CancellationToken support

\- Nullable reference types enabled



\---



\# Naming Standards



Domain:



`PascalCase`



Interfaces:



`IName`



Commands:



`CreateXCommand`



Queries:



`GetXQuery`



Handlers:



`CreateXHandler`



Dataverse entities:



`XEntity`



Repositories:



`XDataverseRepository`



\---



\# Git Convention



Milestone commits use:



`Implement <milestone>`



Examples:



`Implement persistent project brief`



Phase tags:



`v1.0.0-phase1`



`v2.0.0-phase2`



\---



\# Development Rules



\- Small incremental changes.

\- Build after each significant change.

\- Test before milestone completion.

\- Git checkpoint before every milestone.

\- Do not rewrite completed Phase 1 functionality without necessity.

\- Do not introduce unnecessary technologies.

\- Dataverse is the operational source of truth.

\- Data Warehouse is analytical.

\- DEV must never depend on PROD.

\- Authorization must occur before AI retrieval.

\- Raw conversation history remains available.

\- Historical conversations are not automatically included in every AI context.

\- Finalized knowledge and decisions are preferred over raw historical discussion.

\- Projects may have zero or more milestones.

\- Standalone conversations do not require a Project.

\- Main Chat and Sub-Chats use the same Conversation model.

\- Generic Memory is not the primary persistent-memory model.

