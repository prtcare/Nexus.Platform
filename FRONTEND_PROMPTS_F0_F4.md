# Nexus.Web.Client — frontend build prompts F0 → F4

Roadmap item 2. `NEXUS_ARCHITECTURE_V2.md` §8 is the plan; this is how to execute it.

## Status — updated 2026-08-19

| Stage | State |
|---|---|
| **F0** | **Done.** Build ✓, lint ✓, diff scoped to `src/Nexus.Web.Client`, committed `267b4b7` |
| F1 | **Not started.** `IntelligencePage.tsx` and `features/platform/` still exist under their old names; nav still reads "Intelligence" / "AI Platform"; `.env.*` still carry two variables |
| F2 | **Not started.** No `features/chat/`, no `chat.types.ts`, no `ChatPanel.tsx`, no `MessageThread.tsx` |
| F3 | Refused — correctly. It renders citations "for the selected assistant turn"; F2 doesn't exist, so there is no turn and no selection state |
| F4 | Refused — correctly. Same reason: its lists are F2/F3 surfaces |

**Claude Code was right to refuse, and right to refuse the commits too.** It declined to run
`git commit -m "F2: chat UI"` over a tree containing no chat UI. A commit message that
describes work that does not exist is worse than no commit — it makes `git log` lie, and
`git log` is the only record that survives when memory doesn't.

## Run these ONE AT A TIME

All five were pasted in sequence. That defeats the design. Each stage ends in a **build,
a verification and a commit** precisely so that the next stage starts from something known
to work. Pasting F0–F4 back to back asks for four unverified stages stacked on one verified
one — which is the exact failure mode that killed the V2 migration three times.

Paste **one** prompt. Wait for its acceptance report. `/clear`. Commit. Then the next.

---

Same discipline as the SQL stages: PowerShell at `C:\Personal\Nexus.Web`, one prompt at a
time, `/clear` between, commit after each.

**These interleave safely with the SQL stages.** The frontend lives in
`src/Nexus.Web.Client`; the SQL work lives in `src/Nexus.Products.Chat.Infrastructure`. They
never touch the same file. Just don't start one with the other half-finished and the tree
dirty — that is what stalled the V2 migration repeatedly.

---

## Why this is the next thing

The chat UI is not a feature, it is **the measuring instrument.**

Every intelligence change from here — ranking weights, trust levels, prompt section order,
the `ContextBundle` mapper, `ProjectBrief` when it lands — changes the *quality* of an
answer, not its status code. Swagger tells you the endpoint returned 200. It cannot tell you
whether the assembled context produced a good answer. Without a chat UI, every one of those
changes is unfalsifiable, and unfalsifiable changes accumulate into a system nobody can
reason about.

It is also the only page that makes this a chatbot. Everything else you have built is admin.

---

## F0 — Baseline and the two HTTP paths — ✅ DONE (`267b4b7`)

Small on purpose. Nothing here is visible to a user; it stops later stages from building on
sand.

**Outcome:** `projectsApi.ts` converted to the `workspacesApi.ts` shape, `ApiClient` base
moved to `/api/v1`, the four 0-byte `products` files and their route deleted (the endpoint
did not exist — correct call, it did not invent one), `AppRoutes.tsx` and `AppLayout.tsx`
repaired after the deletion. Build and lint both clean; the three remaining lint warnings
in `WorkspaceContext.tsx` pre-date this work.

One finding carried into F1: `environment.ts` reads a **second** environment variable,
`VITE_NEXUS_ENVIRONMENT`. F1 below now says what to do about it.

```
Frontend stage F0 for Nexus.Web.Client. Hygiene only - no new features, no new pages.

Work in src/Nexus.Web.Client.

1. npm install, then npm run build. Report whether the baseline builds BEFORE you change
   anything. If it does not, fix only what blocks the build and report what was broken.

2. THE REAL PROBLEM - there are two different HTTP paths in this codebase:
     workspacesApi.ts  goes through nexusApi/ApiClient - error handling, ApiError, a place
                       to put an auth header later
     projectsApi.ts    uses raw fetch with import.meta.env directly - no ApiError, no auth
                       seam, duplicated error strings
   Convert projectsApi.ts to the workspacesApi.ts shape. Then grep the whole client for
   `fetch(` and `import.meta.env` and report EVERY remaining hit outside ApiClient itself.
   There should be exactly one place that knows the base URL.

3. ApiClient base path becomes /api/v1 - the product API is versioned.

4. Delete src/features/workspaces/WorkspaceContext.tsx (the 0-byte one OUTSIDE
   Nexus.Web.Client - the real one is inside). Confirm you deleted the right one by
   printing both paths and their sizes first.

5. Four 0-byte files in features/products/ (Product.ts, ProductCard.tsx, productsApi.ts,
   useProducts.ts). ProductsPage links to them. Implement them minimally against
   /api/v1/products if that endpoint exists - check the API first. If the endpoint does
   NOT exist, delete the four files and the ProductsPage link, and say so. Do not invent
   an endpoint.

6. Empty folders src/hooks, src/styles, src/utils - leave them if F1-F4 will fill them,
   delete them if not. Your call, but say which you did.

ACCEPTANCE:
  1. npm run build succeeds
  2. npm run lint succeeds (or report that no lint script exists)
  3. exactly one module reads import.meta.env - name it
  4. git diff --stat touches only src/Nexus.Web.Client
```

```powershell
git add -A; git commit -m "F0: single HTTP path, /api/v1 base, dead file cleanup"
```

---

## F1 — Vocabulary and the boundary

The frontend currently has a page named after a layer it is architecturally forbidden to
know about. That is not a cosmetic problem — a name is a claim about what the code may
depend on, and this one is false.

```
Frontend stage F1 for Nexus.Web.Client. Renames and boundary enforcement. No new features.

THE PRINCIPLE - the frontend talks to the product API and nothing else. It does not know
Intelligence exists. It does not know Platform exists. Citations, decisions and usage reach
it THROUGH the product API, already flattened. A page named after an internal layer is a
claim the architecture does not permit.

1. IntelligencePage.tsx -> InsightsPage.tsx, and its route.
2. features/platform/ -> features/system/ (it holds a health check; "platform" now means
   something specific and this is not it). Keep the /health path itself unchanged - under
   V2 that endpoint belongs to the host.
3. AppLayout nav: "Intelligence" -> "Insights". Subtitle "AI Platform" -> "Chat".
   The user is in a chatbot, not a platform.
4. .env.* files keep exactly ONE variable: VITE_NEXUS_API_URL.
   Add this comment above it, verbatim:
       # The only URL the frontend may know. Intelligence and Platform URLs must never
       # appear here - the product API is the frontend's entire world.

   F0 found a second variable, VITE_NEXUS_ENVIRONMENT, read by environment.ts. DELETE it
   and rewrite environment.ts to use Vite's built-in import.meta.env.MODE / .DEV / .PROD.
   Vite already knows which mode it built in; a hand-maintained variable that duplicates
   it can only ever disagree with it. This is not the boundary rule - a mode flag leaks
   nothing - it is just one less thing to configure wrongly.
5. Then prove it: grep the whole client for "intelligence" and "platform",
   case-insensitive, and report every remaining hit with its file and line. Some will be
   legitimate (a comment, a DTO field name that arrives from the API). Judge each one and
   say why it stays or goes. Do NOT blind-rename - a blunt grep false-positives, and
   renaming a good identifier to satisfy a check is worse than the check failing.

ACCEPTANCE:
  1. npm run build succeeds
  2. npm run lint succeeds
  3. paste the grep output with your verdict per line
  4. .env files contain exactly one variable, and environment.ts reads import.meta.env.MODE
  5. git diff --stat touches only src/Nexus.Web.Client

Do F1 ONLY. Do not start F2 in the same session - it needs the backend contract read first,
and that deserves a clear context window.
```

```powershell
git add -A; git commit -m "F1: Insights not Intelligence, system not platform, one URL"
```

---

## F2 — The chat UI

The stage that matters. Everything before it was clearing the runway.

```
Frontend stage F2 for Nexus.Web.Client. Build the chat UI. This is the product.

DO NOT GUESS THE CONTRACT. Before writing a single line of TypeScript, read the actual
product API - the chat controller/endpoint in Nexus.Web and the request/response contract
types it uses. Report the exact shape of:
  - the POST /api/v1/chat request body
  - its response, including citations, decisions and usage if present
  - however conversation history is fetched
Then write TypeScript types that match what you read, field for field. If a field name in
the API is awkward, keep it - the frontend mirrors the contract, it does not improve it.

BUILD, under src/features/chat/:
  chat.types.ts       types derived from the real contract above
  chatApi.ts          through ApiClient - never raw fetch
  useSendChat.ts      send a turn, track pending/error, append to the thread
  MessageThread.tsx   the conversation, user and assistant turns visually distinct
  ChatPanel.tsx       thread + composer
  ConversationList.tsx  conversations for a project

  pages/ChatPage.tsx  route /projects/:projectId/conversations/:conversationId

BEHAVIOUR THAT IS NOT OPTIONAL:
  - the composer disables while a turn is in flight, and shows that a turn is in flight.
    A chat UI that looks idle while waiting is a chat UI people click twice.
  - an API error renders as a message in the thread, not a toast that disappears. When a
    turn fails you need to see WHICH turn failed and what the error was.
  - the thread scrolls to the newest message on arrival, but does NOT yank the view if the
    user has scrolled up to read.
  - no streaming yet. The API does not stream; do not fake it.

REFERENCES: the API now returns a human-readable Reference on conversations and projects
(CON-00000005, PRJ-00000007 style). Show it - small, muted, next to the title, selectable.
It is what a person quotes when something goes wrong. If the field is not in the response
yet, say so and skip it - it arrives with SQL stage 2a.

ACCEPTANCE:
  1. npm run build succeeds
  2. paste the contract shapes you read from the API, before your types
  3. with the API running, send a real turn and describe what rendered
  4. git diff --stat touches only src/Nexus.Web.Client

If you are running low on context, stop after chatApi.ts + useSendChat.ts and say so -
the components are the easy half.
```

```powershell
git add -A; git commit -m "F2: chat UI"
```

---

## F3 — The instrument

F2 makes the product usable. F3 makes it *measurable* — this is the stage that pays for the
whole frontend effort.

```
Frontend stage F3 for Nexus.Web.Client. Surface citations, decisions and usage. This is the
instrument that makes intelligence changes falsifiable.

1. CitationsPanel.tsx in features/chat/ - for the selected assistant turn, list what
   context the answer was built from. Each citation should show enough to judge it:
   what kind of thing it was, its reference, and its trust level if the contract carries
   one. A citation the user cannot trace is decoration.

2. Make citations clickable where the id maps to something the frontend can route to -
   a knowledge item, a decision, a work item. Where it does not map yet, render it plainly
   rather than as a dead link.

3. InsightsPage.tsx (renamed in F1, currently a stub): render usage per turn -
   model used, estimated cost, token counts - and the decision trace if the product API
   exposes one. Read the contract first; do not invent fields.

4. THE POINT, and say this back to me in your report: after this stage it should be
   possible to send the same prompt twice, change one ranking weight or trust level in
   Intelligence between them, and SEE the difference in which context was selected.
   If your implementation does not make that comparison possible, it is not finished.

ACCEPTANCE:
  1. npm run build succeeds
  2. send a real turn; paste what the citations panel rendered
  3. confirm point 4 in your own words, concretely
```

```powershell
git add -A; git commit -m "F3: citations, usage and decision trace"
```

---

## F4 — Make it survivable

```
Frontend stage F4 for Nexus.Web.Client. The unglamorous stage. No new surfaces.

1. Loading and empty states for every list already built: workspaces, projects,
   conversations, messages, citations. An empty list and a failed fetch must not look
   the same - that ambiguity has cost debugging time on every project ever built.
2. One error boundary at the route level so a render crash does not blank the whole app.
3. ApiError surfaced with its status and message, consistently, everywhere.
4. Keyboard: Enter sends, Shift+Enter newlines in the composer.
5. Then walk the app as a new user with an empty database and report every dead end you
   hit - a page with no way forward, a button that does nothing, a list with no create
   action. Do not fix them yet. List them, with the page and what you expected.

ACCEPTANCE:
  1. npm run build succeeds
  2. the dead-end list from step 5 - this is the real deliverable of this stage
```

```powershell
git add -A; git commit -m "F4: states, error boundary, keyboard, dead-end audit"
```

---

## What F4's audit feeds

The dead-end list is the input to the next planning conversation, not busywork. It will
mostly point at the same gap the backend has: the ten unmodelled tables. A project with no
brief, no milestones and no members has fewer places to go, and the UI is where that becomes
obvious rather than theoretical.

That is also the argument for doing the frontend before `ProjectBrief` rather than after —
you will be able to *see* what a brief adds to an answer, instead of asserting it.
