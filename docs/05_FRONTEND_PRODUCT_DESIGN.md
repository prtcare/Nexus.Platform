# Frontend Product Design

## Goal

The interface should make a powerful underlying structure feel simple. Users should usually see their current work and recommended next action, while advanced structure remains available without crowding the screen.

## Recommended frontend path

Build the public NexusAI frontend as a responsive React + TypeScript web application calling `NexusAI.Api`. It can later support installable/PWA packaging or dedicated mobile clients. Blazor is a valid alternative when a single .NET stack is more important than broader frontend ecosystem support.

Power Apps is appropriate for internal PRT operational clients, but it should not be the main public NexusAI product UI.

## Information architecture

Primary hierarchy:

`Workspace → Project → Milestone → Conversation / Work Item → Artifact`

Supporting context:

- Workspace Knowledge
- Project/Conversation ADRs
- Conversation Branches and Snapshots
- Conversation Sessions
- Results/outcomes and activity history

The URL should carry the selected IDs so refresh, deep linking, and browser navigation work correctly.

## Global app shell

### Desktop

- Left navigation: Home, Workspaces, Knowledge, Work, Agents, Settings.
- Top bar: current location breadcrumb, search/command entry, notifications, profile.
- Main content area: current screen.
- Optional right context panel: milestone, decisions, knowledge, related work.

### Mobile

- Top bar with context title and menu.
- Bottom navigation for Home, Workspaces, Chat/Action, Work, Profile.
- Drawers/sheets for secondary navigation and context.
- No permanent three-column layout.

## Core screens

### Home

Show active projects, next actions, recent conversations, milestone progress, waiting items, and important results. Avoid forcing the user to browse the full hierarchy for daily work.

### Workspace

Show projects, workspace knowledge, members/access when available, recent activity, and a clear Create Project action.

### Project

Show project brief, active milestone, milestone timeline, conversations, work items, artifacts, decisions, and progress. The active milestone should dominate; completed/archived structure should collapse.

### Conversation

Show messages and composer as the main surface. Include:

- breadcrumb to workspace/project/milestone;
- branch/sub-conversation navigation;
- related work items and artifacts;
- current knowledge/context indicator;
- session/snapshot history;
- actions to convert a discussion into a decision, knowledge item, work item, or artifact.

### Knowledge

Provide list/search/filter, detail, scope, source, status, and usage context. Make the distinction between trusted Knowledge and informal Memory visible.

### Work and Artifacts

Provide project/milestone grouping, status, priority, details, and related outputs. Artifact list views show metadata; full content loads only on demand.

### Branches and Snapshots

Treat a branch as an alternate line of work, not a duplicate project. A snapshot is a restorable/inspectable captured state. Make the current branch clear and prevent accidental history loss.

## Milestone UX

Milestones may contain many evolving sub-milestones or criteria. Keep this usable by showing:

- one active milestone prominently;
- a short progress summary;
- expandable criteria/sub-goals;
- AI-suggested refinements awaiting approval;
- archived/completed milestones collapsed;
- a clear distinction between editing wording and changing the agreed outcome.

The user should not manually maintain every internal relationship. NexusAI may suggest organization, but consequential milestone changes require approval.

## Frontend implementation slices

### Readiness gate

Before UI coding, build the solution locally; verify Workspace, Project, Conversation, Message, and Chat routes in Swagger against development Dataverse; confirm CORS; freeze enum values; and choose the canonical API host.

### Slice 1 — Core navigation and chat

1. App shell and routing.
2. Workspace list/create.
3. Project list/create.
4. Conversation list/create.
5. Message history.
6. Chat composer.

Definition of done: a user can create/select the hierarchy, send a prompt, refresh, and see persisted data.

### Slice 2 — Milestones

Implement the missing Milestone backend and then add active milestone, criteria, conversation/work association, and approval flow. Do this before heavily polishing Project navigation.

### Slice 3 — Project execution

Add Work Items, Artifact list/detail/create/edit, and project progress.

### Slice 4 — Intelligence context

Add Knowledge, context indicators, decision capture, and later search/filtering.

### Slice 5 — History and branching

Add Branches, Snapshots, Sessions, and activity/history views.

## State and component rules

- Use a server-state query/cache library; do not copy API data into multiple global stores.
- Separate API DTOs from UI view models.
- Centralize base URL, auth, JSON, error mapping, and retry rules.
- Every data surface needs loading, empty, error, retry, and permission states.
- Use optimistic updates only where rollback is safe.
- Design components responsively from the beginning.
- Meet keyboard, focus, contrast, and screen-reader accessibility requirements.
- Never connect the browser directly to Dataverse or expose model/provider credentials.

## Open product decisions

- Authentication provider and organization/tenant model.
- Public SaaS versus first internal pilot.
- Exact role/permission matrix.
- Whether milestone criteria allow nesting or use a flat ordered list.
- Whether conversation branches are displayed as a tree or a simpler main/sub-chat model.
- Artifact editing formats and binary file storage.
- Notification and activity feed scope.
