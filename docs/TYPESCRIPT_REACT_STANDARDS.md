# TypeScript and React Standards

> **Status:** CURRENT for structure and data access; **testing is a total gap** and is marked TARGET
> **Owner:** Layer 10 EXPERIENCE (renumbered from 11, see LAYER_MODEL.md §2.2)
> **Last updated:** 2026-08-21
> **Layer:** 10 EXPERIENCE (renumbered from 11, see LAYER_MODEL.md §2.2)
> **Authoritative for:** how the Nexus web client is structured and written — folders, components, hooks, API access, types, state, forms, errors, loading, accessibility, routing, styling, testing, cancellation, contracts and telemetry

**Scope.** Cross-language rules are **CODE_CONVENTIONS.md**; names are **NAMING_STANDARDS.md**; the server side of every contract is **CSHARP_STANDARDS.md**. This document is the frontend form only.

The client is `Nexus.Experience.Client` — React, TypeScript and Vite, with TanStack Query for server state. See TECHNOLOGY_STACK.md. **Renamed (2026-08-24):** the project, formerly `Nexus.Web.Client`, became `Nexus.Experience.Client` when `Nexus.Web` became `Nexus.Experience`.

---

## 1. Folder structure

```
Nexus.Experience.Client/src/
  api/          ApiClient.ts, ApiError.ts
  app/          AppProviders.tsx, queryClient.ts
  components/   Card.tsx, MetricCard.tsx, RouteErrorBoundary.tsx
  config/       environment.ts
  features/     chat/ projects/ workspaces/ system/
  layouts/      AppLayout.tsx
  pages/        ChatPage … WorkspacesPage
  routes/       AppRoutes.tsx
  types/        SystemHealth.ts
  App.tsx  main.tsx  index.css
```

| Folder | Holds | Rule |
|---|---|---|
| `api/` | The single HTTP path and its error type | Nothing feature-specific. §5. |
| `app/` | Composition — providers, the query client | No UI |
| `components/` | Components used by **two or more** features | A component used by one feature lives in that feature |
| `config/` | `environment.ts`, the only reader of `import.meta.env` | §4 |
| `features/<name>/` | Everything for one feature: components, hooks, API module, types | §2 |
| `layouts/` | Application shell | `AppLayout.tsx` |
| `pages/` | One component per route, composition only | §12 |
| `routes/` | The routing table | `AppRoutes.tsx` |
| `types/` | Types genuinely shared across features | `SystemHealth.ts`. A type used by one feature belongs to it. |

Folder names are lowercase; file naming is NAMING_STANDARDS.md §31.

**The direction of dependency is fixed:** `pages` → `features` → `api`/`config`. A feature never imports from a page. Two features never import from each other — if `chat` needs something from `workspaces`, it is promoted to `components/` or `types/`. This is the frontend expression of the layering invariant and, unlike the backend, **nothing enforces it automatically** — there is no NetArchTest equivalent here.

## 2. Feature folders

Four exist: `chat`, `projects`, `workspaces`, `system`. A feature folder is self-contained and holds five kinds of file.

| Kind | Pattern | In `features/workspaces/` |
|---|---|---|
| Components | `PascalCase.tsx` | `CreateWorkspaceForm.tsx`, `UpdateWorkspaceForm.tsx`, `WorkspaceSelector.tsx` |
| Hooks | `use<Thing>.ts` | `useWorkspaces.ts`, `useWorkspace.ts`, `useCreateWorkspace.ts`, `useUpdateWorkspace.ts` |
| API module | `<feature>Api.ts` | `workspacesApi.ts` |
| Types | `<Entity>.ts` or `<feature>.types.ts` | `Workspace.ts` |
| Context | `<Name>Context.tsx` | `WorkspaceContext.tsx` |

`features/chat/` is the fullest example and shows what a mature feature looks like: components (`ChatPanel.tsx`, `MessageThread.tsx`, `ConversationList.tsx`, `CitationsPanel.tsx`, `CreateConversationForm.tsx`), hooks (`useConversation.ts`, `useConversations.ts`, `useConversationMessages.ts`, `useCreateConversation.ts`, `useSendChat.ts`, `useCitationTarget.ts`), transport (`chatApi.ts`), types (`chat.types.ts`), a helper (`citationTargets.ts`) and a context (`ChatTelemetryContext.tsx`).

`features/system/` is the minimal example: `systemApi.ts` and `useSystemHealth.ts`, no components at all. That is a legitimate feature.

**A new feature starts with `<feature>Api.ts` and one hook**, not with a component. The data path is the part that is hard to change later.

## 3. Components

| Rule | Detail |
|---|---|
| Function components only | No classes, with one exception: an error boundary must be a class, which is why `RouteErrorBoundary.tsx` is one |
| One component per file, named export matching the file | — |
| Props are a named `type`, defined immediately above the component | Not inlined into the signature when there is more than one |
| A component either **fetches and composes** or **receives and renders** | `ChatPanel` fetches through hooks; `MessageThread` and `MetricCard` receive props. Mixing the two is what makes a component untestable and un-reusable. |
| No business logic in a component | Derivation goes in a hook — `useCitationTarget.ts` exists because that derivation did not belong in `CitationsPanel.tsx` |
| No direct `fetch` | §5 |
| Render is pure | Effects belong in `useEffect` with cleanup, or in a query hook. CODE_CONVENTIONS.md §8. |
| No index-as-key in a list | Key by identity — the message or conversation id |
| Length | ≤ 150 lines, hard limit 200. CODE_CONVENTIONS.md §2. |

Naming vocabulary — `Page`, `Panel`, `List`, `Thread`, `Form`, `Selector`, `Card`, `Context`, `ErrorBoundary`, `Layout` — is NAMING_STANDARDS.md §29.

## 4. Configuration and environment

`config/environment.ts` is **the only file in the client that reads `import.meta.env`.** Nothing else may.

| Rule | Detail |
|---|---|
| Only `VITE_`-prefixed variables reach the bundle | This is a security boundary. Anything without the prefix is invisible to the browser, which is why nothing secret may ever carry it. |
| Validate on load | A missing base URL should fail loudly at startup, not as a confusing 404 on the first request |
| Export typed values, not raw strings | The rest of the client imports a typed configuration object |
| **A secret never reaches the client** | No API key, no connection string, no token that is not user-scoped. Server-side secrets are `ISecretResolver` — **M-01-5.1**. |

## 5. The API client — the single-HTTP-path rule

**`api/ApiClient.ts` is the only place in the client that calls `fetch`. `api/ApiError.ts` is the only error type it throws.** This is the most important structural rule in the frontend.

```
component  →  use*.ts hook  →  <feature>Api.ts  →  ApiClient.ts  →  fetch
```

| Layer | Responsibility | Must not |
|---|---|---|
| `ApiClient.ts` | Base URL from `environment.ts`, headers, JSON encode/decode, `AbortSignal`, non-2xx → `ApiError` | Know any endpoint path |
| `<feature>Api.ts` | The endpoint paths and request/response shapes for one feature | Call `fetch`, or catch errors it cannot resolve |
| `use<Thing>.ts` | Query keys, caching, invalidation, loading and error state | Know a URL |
| Component | Render what the hook returns | Know anything about transport |

**Why the rule earns its keep.** Every cross-cutting HTTP concern — a correlation header for **M-10-1.1**, an auth token for **M-01-1.2 Authentication flow**, a timeout, a retry policy — is one edit in `ApiClient.ts` instead of an audit of every feature. A single stray `fetch` in a component silently opts out of all of them.

`chatApi.ts`, `projectsApi.ts`, `workspacesApi.ts` and `systemApi.ts` are the four feature modules and they are exhaustive. A fifth appears only with a fifth feature.

## 6. Types and interfaces

| Rule | Detail |
|---|---|
| `type` for unions, aliases and props; `interface` only for a shape genuinely meant to be extended | The codebase is alias-dominant — `Workspace.ts`, `Project.ts`, `SystemHealth.ts`, `chat.types.ts` |
| **No `any`. Ever.** | `unknown` plus narrowing, which forces the check that `any` skips |
| `strict` on, and no `@ts-ignore` | `@ts-expect-error` with a comment naming the reason, if there is no alternative |
| Union of string literals, not a TypeScript `enum` | `type ConversationStatus = 'Active' \| 'Archived'`. It matches the C# side, which serialises enums as strings — CSHARP_STANDARDS.md §18. |
| An id is a `string` | Never parsed, never ordered, only compared and passed |
| A `DateTimeOffset` arrives as an ISO 8601 `string` | Parse at the edge, format at render; never compare formatted strings — CODE_CONVENTIONS.md §18 |
| A response type mirrors the server DTO exactly | §16 |
| Types are exported from the feature that owns them | Only a genuinely cross-feature type goes in `types/` |

## 7. State management

**There is no state-management library, and one must not be added.** Four kinds of state, four homes.

| Kind | Home | Example |
|---|---|---|
| **Server state** | TanStack Query | conversations, projects, workspaces, health — every `use*` hook |
| **Local UI state** | `useState` in the component that owns it | an open panel, a form field before submit |
| **Ambient application state** | React context, sparingly | `WorkspaceContext.tsx` (the selected workspace), `ChatTelemetryContext.tsx` (§17) |
| **URL state** | the route | the selected conversation, the current page |

| Rule | Detail |
|---|---|
| **Server data never goes into `useState` or context** | Copying query data into local state creates a second source of truth that goes stale silently. This is the single most common React data bug and TanStack Query exists to prevent it. |
| **State lives at the lowest component that needs it** | Lift only when a sibling needs it too |
| **Context is for ambient values, not a dependency container** | Two contexts exist; a third needs a reason |
| **Anything shareable as a link belongs in the URL** | A conversation selected only in memory cannot be linked, refreshed or bookmarked |

## 8. Server state — TanStack Query

`app/queryClient.ts` constructs the client; `app/AppProviders.tsx` installs it. Every `use*` hook consumes it.

| Rule | Detail |
|---|---|
| **A query key is an array, ordered general → specific** | `['workspaces']`, `['workspaces', workspaceId]`, `['conversations', workspaceId]`, `['conversation', conversationId, 'messages']`. The prefix relationship is what makes targeted invalidation possible. |
| **Query keys are built by a helper per feature, never inlined** | Two hooks that disagree about a key produce a cache that never invalidates, and nothing errors |
| **Queries read; mutations write** | `useConversations` and `useWorkspaces` are queries; `useCreateWorkspace`, `useUpdateProject`, `useSendChat` are mutations |
| **A mutation invalidates precisely** | `useCreateWorkspace` invalidates `['workspaces']`, not everything. Blanket invalidation refetches the whole client on every write. |
| **Configure `staleTime` deliberately per query** | `useSystemHealth` is short-lived; a workspace list is not. Accepting the default everywhere means either refetching constantly or showing stale data. |
| **Retry is configured, not accepted** | The default retries queries. A failed *mutation* — `useCreateWorkspace` — usually must not retry, because it may not be idempotent. CODE_CONVENTIONS.md §§14, 16. |
| **Use the `signal` the query function is given** | §14 |
| **`undefined` is loading; an empty array is empty** | Conflating them shows "no conversations" during the first load, which reads as data loss |
| **One hook per query** | `useConversation` (one) and `useConversations` (many) are separate files for a reason |

## 9. Forms

Four forms exist: `CreateConversationForm`, `CreateProjectForm`, `CreateWorkspaceForm`, `UpdateWorkspaceForm`. **No form library is in use** and none should be added without an ADR and a TECHNOLOGY_STACK.md entry.

| Rule | Detail |
|---|---|
| Controlled inputs, state in the form component | — |
| Submission goes through the feature's mutation hook | `CreateWorkspaceForm` → `useCreateWorkspace` → `workspacesApi.ts` → `ApiClient` |
| Client validation is a courtesy, never a control | The server validates; the client only saves a round trip. CODE_CONVENTIONS.md §9. |
| Disable submit while the mutation is pending, and say so | Otherwise a double-click creates two workspaces — a real risk while §16's idempotency is TARGET |
| Server validation errors render **on the field**, not in a toast | The `ApiError` carries the detail; put it where the user is looking |
| Reset only after confirmed success | Clearing a form on a failed submit destroys the user's input |
| Every input has a `<label>` with `htmlFor` | §11 |
| `Create<Name>Form` / `Update<Name>Form` mirror `Create<Name>Request` / `Update<Name>Request` | The verb is identical on both sides of the wire — NAMING_STANDARDS.md §29 |

## 10. Error handling

Three distinct mechanisms for three distinct failures.

| Failure | Mechanism | Where |
|---|---|---|
| A request failed | `ApiError` thrown by `ApiClient.ts`, surfaced as the hook's `isError` and rendered inline | the component that owns the query |
| A render threw | `RouteErrorBoundary.tsx` | mounted around routed content |
| A route does not exist | `NotFoundPage.tsx` | the routing table |

| Rule | Detail |
|---|---|
| **`ApiError` is the only error type crossing the transport boundary** | Nothing else escapes `ApiClient.ts` |
| **A query error is not an error-boundary case** | It is an expected state with a UI: a message and a retry affordance. Sending every failed fetch to the boundary blanks the screen for a recoverable problem. |
| **An error message says what failed and what to do** | Never "Something went wrong". Never a raw status code alone. |
| **Never render a server exception message verbatim** | It can leak internals |
| **`RouteErrorBoundary` is a last resort, not a strategy** | Reaching it means a bug |
| **Never `catch` and continue silently** | An unawaited or unhandled promise rejection loses the failure entirely |

## 11. Loading state

| Rule | Detail |
|---|---|
| Every query hook's consumer handles three states: loading, error, ready | Two-state handling is the most common defect shape in this codebase |
| Distinguish first load from background refetch | TanStack Query separates `isPending` from `isFetching`; a spinner over data the user is already reading is worse than no spinner |
| A mutation's pending state disables and labels its own control | Not a global overlay |
| Loading UI holds the same space as the loaded UI | Layout that jumps on arrival reads as breakage |
| A loading state has an end | An indefinite spinner with no timeout and no error path is the worst outcome — bounded by the `AbortSignal` in §14 |
| Announce state changes to assistive technology | §12 |

## 12. Accessibility

**Baseline, not aspiration. Every rule below applies to code being written now.**

| Rule | Detail |
|---|---|
| Semantic HTML first | `<button>`, `<nav>`, `<main>`, `<ul>`. A `<div>` with an `onClick` is not a button — it has no keyboard, no focus and no role |
| Every interactive element is keyboard reachable and operable | Tab, Enter, Escape. `WorkspaceSelector` and `ConversationList` are the two that most need this. |
| Visible focus indicator | Never removed without a replacement |
| Every input has an associated `<label>` | §9 |
| ARIA only where semantics cannot express it | Wrong ARIA is worse than none |
| Live regions for async change | A message appended to `MessageThread`, or an error appearing after submit, must be announced |
| Colour is never the only signal | Status in `MetricCard` needs text or shape too |
| Images and icon-only buttons have accessible names | An icon button with no name is unusable by a screen reader |
| Heading order is not skipped | Headings are structure, not size |

## 13. Routing

`routes/AppRoutes.tsx` holds the routing table; `layouts/AppLayout.tsx` is the shell; `pages/` holds one component per route.

Eleven pages: `ChatPage`, `CreateWorkspacePage`, `DashboardPage`, `InsightsPage`, `KnowledgeItemPage`, `NotFoundPage`, `ProjectDetailsPage`, `SettingsPage`, `WorkItemPage`, `WorkspaceSettingsPage`, `WorkspacesPage`.

| Rule | Detail |
|---|---|
| Routes are declared in one place | `AppRoutes.tsx`. A route declared elsewhere is invisible to anyone reading the table. |
| Paths are lowercase and plural for collections | Mirrors the API — NAMING_STANDARDS.md §24 |
| A page composes; it does not fetch directly or hold business logic | It reads route parameters and renders feature components |
| Route parameters are validated before use | An id from the URL is untrusted input; a malformed one renders a not-found state, not a crash |
| `NotFoundPage` is the catch-all | — |
| Identity that should be linkable is in the URL, not in context | §7 |
| Navigating away cancels in-flight work | §14 |

## 14. Async calls and cancellation

| Rule | Detail |
|---|---|
| `async`/`await`, never `.then()` chains | CODE_CONVENTIONS.md §5 |
| **No `Async` suffix on names** | `sendChat`, not `sendChatAsync`. The C# rule does not cross over. |
| A component is never `async` | Async belongs to a hook |
| **No `useEffect` that fetches** | That is what the query hooks are for. A bare fetching effect has no cache, no dedupe, no cancellation and no error state, and it will race. |
| `ApiClient.ts` accepts an `AbortSignal` and passes it to `fetch` | §5 |
| Query functions use the signal TanStack Query provides | Navigating away from `ChatPage` mid-request aborts it instead of resolving into an unmounted component |
| Every `useEffect` that starts something returns a cleanup | Subscriptions, timers, listeners in `ChatTelemetryContext.tsx` and `WorkspaceContext.tsx` included |
| Never fire and forget | An unawaited promise loses its rejection |
| Parallel only where genuinely independent | `Promise.all` for independent reads; sequential where one result feeds the next |

## 15. Reusable components

`components/` holds three: `Card.tsx`, `MetricCard.tsx`, `RouteErrorBoundary.tsx`.

| Rule | Detail |
|---|---|
| **Promote on the second use, not the first** | A component abstracted for one caller is guesswork; the second caller shows what actually varies |
| A shared component takes props and holds no feature knowledge | `MetricCard` renders a metric; it does not know about workspaces |
| A shared component never calls a hook that fetches | It receives data |
| Composition over configuration | Children and slots beat a growing list of boolean flags |
| A shared component owns its accessibility | §12 — get it right once |

`MetricCard` building on `Card` is the pattern: a general primitive, then a specialised use. **M-11-6.1 Design tokens and primitives** and **M-11-3.1 Reusable chat components** are where this becomes a real library.

## 16. API contracts

| Rule | Detail |
|---|---|
| The server defines the contract; the client mirrors it | Types in `Workspace.ts`, `Project.ts`, `chat.types.ts`, `SystemHealth.ts` mirror `GetWorkspaceResponse`, `ListConversationsResponse` and their siblings |
| **The mirroring is manual and unverified — this is a known risk** | Nothing checks that the TypeScript type still matches the C# record. A renamed field compiles on both sides and fails at runtime. Generating client types from the Swashbuckle OpenAPI document would close it; that is not done today. |
| Paths live in `<feature>Api.ts` only | One place per feature to change when the API versions |
| Version bumps are handled in one edit | `/api/v1` → `/api/v2` in the feature module |
| Enums are string unions matching the server's string serialisation | §6 |
| The client tolerates unknown fields | An added server field must not break an older client |
| Removing or renaming a field is a breaking change requiring a new API version | CSHARP_STANDARDS.md §17, NAMING_STANDARDS.md §23 |
| The Intelligence API is a different base path | `/intelligence/v1` versus `/api/v1`. Never hardcode either outside `<feature>Api.ts` and `environment.ts`. |

## 17. Telemetry

`features/chat/ChatTelemetryContext.tsx` is the frontend telemetry seam and the only one.

| Rule | Detail |
|---|---|
| Telemetry goes through the context, never `console.log` | The browser console is not telemetry |
| **No secret, token, full prompt or full message body in a telemetry event** | Same rule as the backend — CODE_CONVENTIONS.md §11, acceptance criterion of **M-10-1.1 Correlation across hosts** |
| An event names what happened, not how the code is structured | `conversation_created`, not `handleSubmit_called` |
| Telemetry never blocks or breaks the UI | A failed telemetry call is swallowed deliberately, and that is the one legitimate silent catch in the client |
| No personal data without a classification | **M-02-5.1 Classification and retention** |
| The correlation id, once it exists, is attached here and in `ApiClient.ts` | **TARGET — M-10-1.1.** This is why both seams are single. |

## 18. Styling

**CURRENT: a single `index.css`. There is no CSS framework, no CSS-in-JS, no CSS modules, no component library** — TECHNOLOGY_STACK.md §7.

| Rule | Detail |
|---|---|
| Do not introduce a second styling mechanism | Two conventions in one client is worse than one imperfect one |
| Class names `kebab-case`, prefixed by the component | `chat-panel__message` |
| No inline styles except a genuinely dynamic value | A computed width, not a colour |
| Custom properties for anything reused | `--nexus-<category>-<name>` |
| Never `!important` | It means the selector is wrong |
| Responsive by default | Not a later pass |
| Respect `prefers-reduced-motion` | §12 |

**TARGET — M-11-6.1 Design tokens and primitives** decides the approach. Until it does, `index.css` grows and the discipline above keeps it navigable.

## 19. Testing

> **CURRENT: there are ZERO frontend tests. No test file, no test framework, no test configuration, anywhere in `Nexus.Experience.Client`. This is the largest single quality gap in Nexus.**

For context, the backend has exactly two behaviour tests — the frontend has none, and the frontend is where the most-changed code is.

| Consequence | Detail |
|---|---|
| Every React or TanStack Query major upgrade is verified by hand | STACK_VERSION_POLICY.md §5 |
| Every contract drift in §16 is found by a user | — |
| Every refactor of `ChatPanel` or `MessageThread` is unverified | — |

**TARGET.** No framework is selected — TECHNOLOGY_STACK.md §7 — and the testing standard belongs to **M-09-3.1 Test plans and test cases**. When it is chosen, the order that buys the most first:

| Priority | Target | Why |
|---|---|---|
| 1 | `ApiClient.ts` and `ApiError.ts` | Every request passes through them |
| 2 | `citationTargets.ts` and `useCitationTarget.ts` | Pure derivation, cheapest to test, and the same reason `KeywordContextRankerTests.cs` is one of the backend's two tests |
| 3 | The four forms | Where user input meets validation |
| 4 | Query-key and invalidation behaviour in the `use*` hooks | Where the silent bugs live |
| 5 | Accessibility assertions on `components/` | §12 becomes verifiable |

Until a framework exists, **do not write tests in an ad-hoc runner**. Two frontend test approaches would be worse than none. Record the gap, and close it at the milestone.

## 20. Related documents

| Document | Owns |
|---|---|
| CODE_CONVENTIONS.md | Cross-language rules — async, errors, cancellation, pagination, dates |
| NAMING_STANDARDS.md | File, component, hook and route naming |
| CSHARP_STANDARDS.md | The server side of every contract in §16 |
| TECHNOLOGY_STACK.md | React, TypeScript, Vite, TanStack Query; what is not selected |
| STACK_VERSION_POLICY.md | Frontend package pinning and upgrades |
