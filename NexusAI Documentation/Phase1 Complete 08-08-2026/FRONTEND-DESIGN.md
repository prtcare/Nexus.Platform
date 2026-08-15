# FRONTEND-DESIGN.md

Design for NexusAI's two planned front ends: a **Power Apps** client and a **Visual Studio-built desktop client**. Neither exists yet — this document captures the intended design so both are built consistently, from the same information architecture, rather than diverging. See [CONVENTIONS.md](./CONVENTIONS.md)'s "UI Standards" section for the condensed rules; this document is the fuller design behind them.

## Guiding Principles

1. **Both clients are thin.** All business logic, memory composition, and orchestration lives behind `NexusAI.Api`. Neither client holds state beyond what it fetches — this is why `NexusAI.Api` exists as its own project rather than being folded into `Host` (see [ARCHITECTURE.md](./ARCHITECTURE.md)).
2. **Both clients share one information architecture.** A user should be able to start a conversation on the desktop app and continue it from the Power Apps client without anything feeling different beyond the platform's native look and feel.
3. **Memory is visible, not hidden.** The whole point of NexusAI's memory model is that it doesn't silently forget — so the UI has to make memory *visible*: the milestone window is always on screen, branches are always clearly marked, not buried in a menu.
4. **Different clients, different jobs.** The desktop app is expected to be the primary tool for heavier work (development tasks, longer sessions, more screen real estate). The Power Apps client is expected to be the lightweight, anywhere-access companion (quick questions, checking status, approving a milestone from a phone). Design for that difference rather than forcing identical layouts onto both.

## Information Architecture

```
Workspace
  └── Project
        ├── Milestone Window   (persistent, approval-gated summary)
        └── Conversation
              ├── Main Chat     (the primary thread — always intact)
              └── Branch(es)    (side-threads spun off the main chat)
                    └── Snapshot(s)  (point-in-time records within a branch)
```

This mirrors the Domain model exactly (`Workspace → Project → Conversation → ConversationMessage`, plus `Branch`, `Snapshot`) — see [DATABASE.md](./DATABASE.md) for the underlying entities. The navigation hierarchy in both clients should never diverge from this shape.

## Screens

### 1. Workspace Selector
The entry point. Lists workspaces (currently only one exists in practice, but the model supports more). Selecting one drills into its Projects.
- **API dependency**: needs `GET /api/workspaces` (list) and `POST /api/workspaces` (create) — **neither exists yet**, see [DECISIONS.md](./DECISIONS.md) Known Issue #16. This screen can't be built until Milestone 3 closes this gap.

### 2. Project List
Within a workspace: a list of projects, each showing name, status, and (once available) a one-line preview of its current milestone summary. Selecting a project opens the Project view.
- **API dependency**: `GET /api/workspaces/{workspaceId}/projects` — exists today.

### 3. Project View
The home screen for a single project. Two persistent regions:
- **Milestone panel** (left or top, depending on platform — see wireframe below): the current approved milestone summary, plus a visible "last approved" timestamp and a way to review/approve pending updates.
- **Conversation list**: existing conversations under this project, plus a "New Conversation" action.
- **API dependency**: `GET /api/projects/{id}` (exists) + the not-yet-built Milestone endpoints (Milestone 3) + `GET /api/projects/{projectId}/conversations` (exists, but currently leaks a wrapped ID — see [API.md](./API.md) — fix before building against it).

### 4. Conversation / Chat View
The main working screen. Contains:
- The **main chat thread** — the primary conversation, which branching is specifically designed to keep intact (see below).
- A **composer** at the bottom for sending a new message.
- A visible indicator whenever a message triggered (or could trigger) a branch, with a way to jump into that branch.
- **API dependency**: `POST /api/chat` (exists, but has the wrapped-`ConversationId` serialization issue — see [DECISIONS.md](./DECISIONS.md) #15, fix before building against it), `GET /api/conversations/{id}/messages` (exists).

### 5. Branch View
Opened from an indicator in the main chat. Shows the branch's own message thread, visually distinguished from the main conversation (see "Branching" below), with a clear "back to main conversation" action and, once resolved, a visible marker showing what conclusion was folded back.
- **API dependency**: Branch endpoints don't exist yet — Milestone 3.

## The Milestone Window

This is the feature most central to NexusAI's whole memory philosophy, so it gets a dedicated design note rather than being treated as "just another panel."

- **Always visible** alongside the active project's conversations — not a modal, not a collapsed drawer by default. If it's hidden, the point of it being a trusted, constant reference is undermined.
- **Read-only by default.** The milestone content only changes through an explicit approval action — see [DECISIONS.md](./DECISIONS.md) ADR-005. The UI must make this distinction obvious: there should be no way to casually edit milestone text the way you'd edit a normal note.
- **Pending vs. Approved state.** When the system proposes an update to the milestone (from conversation content, once Milestone 2's extraction pipeline exists), it appears as a clearly separate "proposed update" — diffed against the current approved version if practical — with explicit Approve/Reject actions. Until approved, the *displayed* milestone content does not change.
- **Timestamped.** Always show when the milestone was last approved, and by extension, how "fresh" it is relative to the conversation happening around it.

## The Branching Model

The second core differentiator, and the reason `Branch` exists as its own entity rather than just being a message thread.

- **Trigger**: when a side-question comes up in the main chat that isn't the main thread's direct focus, it should branch rather than derail the main conversation. (The precise trigger logic — automatic detection vs. explicit user action — is an Application-layer decision tracked in [ROADMAP.md](./ROADMAP.md) Milestone 0, not a front-end concern; the front end just needs to render whatever the API reports.)
- **Visual distinction**: a branch is never rendered as if it were part of the main thread. Use a different background tint, an indentation/breadcrumb ("branched from: '...'"), or a distinct panel — the specific treatment can differ between Power Apps and desktop (native controls differ), but the *rule* — branches are never visually confusable with the main thread — applies to both.
- **Folding back**: only the branch's **resolved conclusion** appears in the main conversation view by default, rendered distinctly from a normal chat message (e.g., a labeled "Branch resolved:" summary card) — never the full branch transcript. The full transcript remains one click away via the branch indicator.
- **Reversibility**: a user should always be able to open a resolved branch and see its full history, even though the main thread only shows the summary.

## Wireframe — Conversation / Chat View (desktop layout)

```
┌─────────────────────────────────────────────────────────────────┐
│ Workspace: Default        Project: NexusAI                       │
├───────────────────┬───────────────────────────────────────────────┤
│  MILESTONE          │  Architecture Discussion            [≡]     │
│  (approved)          │───────────────────────────────────────────│
│  Last approved:      │  User: How should we structure the        │
│  2026-08-01          │  Dataverse schema?                        │
│                       │                                            │
│  "Dataverse chosen    │  Assistant: I'd suggest starting with...  │
│  as backend; agent    │                                            │
│  framework uses a     │  ┌ Branch resolved: "Naming convention   │
│  registry pattern..."  │  │ for lookup columns" → nexus_ prefix,  │
│                       │  └ confirmed. [View branch]               │
│  [Review pending      │                                            │
│   update: 1]          │  User: What about status fields?         │
│                       │                                            │
│                       │  Assistant: ...                           │
│                       │                                            │
│                       │───────────────────────────────────────────│
│                       │  [ Type a message...              ] [Send]│
└───────────────────────┴───────────────────────────────────────────┘
```
On the Power Apps client, the same three regions (milestone / main thread / composer) likely stack vertically rather than side-by-side, with the milestone panel collapsible to a summary strip given typical phone/tablet screen constraints — but still one tap away, never buried in a settings menu.

## Platform-Specific Considerations

**Power Apps (Canvas App)**
- Best suited for: checking milestone status, quick chat turns, approving/rejecting a pending milestone update from a phone.
- Constraints to design around: canvas apps are more limited for rich, long-scrolling chat UIs than a native or desktop app — favor a simpler, more list-like message rendering over anything highly custom.
- Data source: calls `NexusAI.Api` directly (custom connector or HTTP connector against the REST endpoints) — does not talk to Dataverse directly, to keep all business logic in one place rather than duplicated between the app's formulas and the API.

**Visual Studio Desktop App**
- Best suited for: longer working sessions, reviewing/approving larger milestone diffs, viewing full branch transcripts, anything involving the Developer agent's output (code diffs, file changes).
- More screen real estate allows the side-by-side milestone/chat layout shown above without needing to collapse anything.
- Likely candidate for surfacing agent activity in more detail (e.g., showing the Developer agent's proposed file changes inline) once Milestone 4 exists — the Power Apps client is not expected to need this level of detail.

## Open Design Questions (not yet decided)

- Exact interaction for reviewing a "pending milestone update" — full diff view, or a simpler before/after summary?
- Whether branch-trigger detection is fully automatic or requires an explicit "branch this" user action, at least initially — affects whether the front end needs a "branch this message" control.
- Visual/branding direction (colors, typography) — deliberately left open until the API surface (Milestone 3) is stable enough to build screens against.
- Notification model for a proposed milestone update — does the user need to be pinged, or is "visible next time they open the project" sufficient?

This document should be revisited once Milestone 3 (API + Front-End Contract) is underway — some of the above will be resolved by what turns out to be practical to build against the real API rather than decided purely in the abstract here.
