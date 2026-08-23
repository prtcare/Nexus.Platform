# Code Review Checklist

**Status:** Active
**Owner:** DEVELOPER (Layer 07)
**Last updated:** 2026-08-21
**Layer:** 07 DEVELOPER, applied to every layer
**Authoritative for:** what a reviewer checks, in what order, and which checks apply to which kinds
of change.

Not authoritative for: who may review and what a recorded review decision is — `GIT_WORKFLOW.md` §9
and `DEVELOPMENT_WORKFLOW.md` §2.1 state 13; whether the work item is finished —
`DEFINITION_OF_DONE.md`; what the rules themselves say — each sibling standard, cited per item.

**How to use this document.** Run §1 first, on every review, before opening a single implementation
file. Then run §2 — it applies to everything. Then run only the §3 groups the change actually
touches. Skipping groups is expected; skipping §1 and §2 is not.

---

## 1. The reviewer's first five minutes

**Before reading any implementation.** These checks catch more than the line-by-line pass, cost five
minutes, and every one of them is answerable from the branch name, the diff summary and the work
item.

- [ ] **Do I know what this change is supposed to do?** If the work item, the acceptance criterion
      and the PR description do not tell me, I cannot review it — I can only proofread it. Ask
      before reading further.
- [ ] **Is there an acceptance criterion, and could it fail?** A criterion that cannot fail is a
      description. `ASSURANCE_STANDARDS.md` §3.
- [ ] **Does the diff stay inside the work item's declared scope** — the projects, schemas and
      contracts it declared? A change that reaches outside its scope is not reviewable against its
      criterion, and it silently breaks the parallel-safety guarantee everything else in flight is
      relying on. `DEVELOPMENT_WORKFLOW.md` §4.
- [ ] **Does the branch name match the work item id?** It is the join key that attributes a build
      record to a work item. `GIT_WORKFLOW.md` §4, S-07-4.1.1.1.1.
- [ ] **What files changed, and does that list surprise me?** An unexpected file is the single
      highest-yield signal in review. A `Directory.Build.props`, a `.csproj`, a `nuget.config`, a
      migration or a `*.Contracts` file in a change that was described as a bug fix is worth more
      attention than the fix.
- [ ] **Is there a migration?** If yes, §3.4 is mandatory and the change conflicts with every other
      in-flight migration on the same `DbContext` — model-snapshot conflict, `DEVELOPMENT_WORKFLOW.md`
      §4.1. This is the rule most often got wrong.
- [ ] **Does it change a published contract** in `Nexus.Platform.Contracts`,
      `Nexus.Intelligence.Contracts` or a product's API DTOs? If yes, §3.13 and probably an ADR.
- [ ] **Is the risk tier right?** Anything touching identity, tenancy, secrets, audit, permissions or
      the deployment path is Security-critical, whatever the work item said when it was scoped.
      `DEFINITION_OF_DONE.md` §4.3.
- [ ] **Can I review this in one sitting?** A PR nobody can review does not get reviewed — it gets
      approved. Say so and ask for it to be split.
- [ ] **Am I the worker?** No self-approval, including agent work. `DEVELOPMENT_WORKFLOW.md` §2.2.

---

## 2. Always

Applies to every change, including one-line changes.

### 2.1 Layer boundaries

- [ ] Does every new dependency point **downward** only? A layer may depend only on layers below it;
      DELIVERY (08), ASSURANCE (09) and OPERATIONS (10) are cross-cutting.
- [ ] Do the architecture tests still pass — `PlatformBoundaryTests.cs`, `BoundaryRuleTests.cs`,
      `BoundaryTests.cs`? If the change required **editing** one of them, that is the finding, and it
      needs a stated reason, not a green build.
- [ ] Does `Nexus.Platform.Contracts` or `Nexus.Intelligence.Contracts` reference any product type?
      **No shared kernel.** This is currently true across all three repositories; keeping it true is
      cheaper than restoring it.
- [ ] Does any product reference another product?
- [ ] Does AI see product structure? It receives a `ContextBundle`; `ScopeRef` is **opaque** to it.
- [ ] Is there any `if (Product == X)` branching, in any form — a switch on product name, a type
      check, a lookup keyed by product identity? Capability packs are **declared, not coded**.
- [ ] Does the Domain know about persistence? No EF attributes, no navigation-property pollution, no
      base classes from Infrastructure. `DATABASE_STANDARDS.md`.

### 2.2 Naming

- [ ] Does the namespace mirror the folder path? `Nexus.Products.Chat.Domain.Workspace`.
- [ ] Do new files follow the pattern for their kind — `<Name>Endpoint.cs`, `Sql<Name>Repository`,
      `<Name>Configuration`, `use<Thing>.ts`, `<Thing>Api.ts`, PascalCase `.tsx`?
- [ ] Is an aggregate folder complete — `<Name>.cs`, `<Name>Id.cs`, `<Name>Status.cs`,
      `I<Name>Repository.cs`?
- [ ] Do the request and response records follow `Create<Name>Request` … `Update<Name>Response`?
- [ ] Does the name say what the thing **is**, without abbreviation, without a type suffix that adds
      nothing, and without a Dataverse-era `T_nnn_` prefix?
- [ ] `NAMING_STANDARDS.md` — is there any rule this change quietly departs from?

### 2.3 Error handling

- [ ] Is each failure the right **category**: expected outcome (a value), caller error (rejected at
      the boundary), or genuine fault (an exception)? `ERROR_HANDLING.md` §2.
- [ ] Is a **refusal** returned as a value — `QuotaVerdict`, `ToolResult`, `TurnError`,
      `ResultOutcome` — rather than thrown?
- [ ] Is there a `catch (Exception)` anywhere other than a host boundary?
- [ ] Is there `throw ex;` where `throw;` was meant? It erases the stack trace.
- [ ] Is there an empty or silent `catch`? The one legitimate exception is a failed telemetry call in
      the client.
- [ ] Does every error message name **what failed and what was attempted**? Never "Error", never
      "Something went wrong".
- [ ] Does the status code match the error class? `ERROR_HANDLING.md` §4.
- [ ] Is a cross-tenant resource returning `404` rather than `403`?

### 2.4 Logging and secrets

- [ ] **Does any log line, metric dimension, telemetry event or error message contain a secret, a
      token, a full prompt or completion body, a message body, or personal data beyond an
      identifier?** `SECURITY_STANDARDS.md` §11. This is the one check worth running twice.
- [ ] Are log calls structured — a message template with named placeholders, never an interpolated
      string? `"Workspace {WorkspaceId} created"`, not `$"Workspace {id} created"`.
- [ ] Is the level right? A handled failure is `Warning`; an unhandled fault is `Error`; a refusal is
      `Information`. `OBSERVABILITY_STANDARDS.md` §4.
- [ ] Is logging at a **boundary** rather than inside a loop? A per-item line in `ToolLoop` is volume
      without information.
- [ ] Is an audit record going through `IAuditLog`, and a diagnostic line through `ILogger`, with
      neither routed through the other?
- [ ] Any `Console.WriteLine` or `console.log`?
- [ ] Any secret, key, connection string or token in source, `appsettings`, a script, a test, or a
      commit message? `CONFIGURATION_STANDARDS.md` §13.

### 2.5 Documentation

- [ ] Did this change make a **TARGET** real anywhere in `docs/`, and is the marker updated in this
      change? `DOCUMENTATION_STANDARDS.md` §11.
- [ ] Did it change a convention, add a technology, or make an architectural decision? Then the
      owning standard is updated and — for a decision — an ADR exists at the next number, ADR-016.
      `ADR_STANDARD.md` §2.
- [ ] Does any comment or document now contradict the code?
- [ ] Does a comment explain **why**, rather than restate the code?
- [ ] Was anything copied from another document rather than linked to it?

### 2.6 Tests and assurance

- [ ] Is there a test proving the behaviour this change claims? With **exactly two behaviour tests
      in the entire system**, this question is nearly always answered "no" — the point of asking is
      to make the gap a decision rather than an oversight.
- [ ] Does the test assert an **outcome**, not an implementation detail?
- [ ] If this fixes a defect, is there a test that fails without the fix?
- [ ] Does the change **delete or weaken** an existing test? That is a finding, always, and it needs
      a stated reason.
- [ ] Is there evidence a criterion was satisfied, or only a claim that it was?
      `ASSURANCE_STANDARDS.md` §10.

---

## 3. Conditional groups

Run only the groups the change touches.

### 3.1 Security — anything touching identity, tenancy, secrets, permissions, personal data

- [ ] Is every query that reads tenant-owned data **tenant-filtered**, with the filter applied where
      it cannot be forgotten rather than per call site?
- [ ] Is there a test proving cross-tenant access **fails**? `SECURITY_STANDARDS.md` §4.3 — the test
      comes first.
- [ ] Does an authorization decision happen at the boundary, on a real principal, rather than being
      assumed from a caller-supplied id?
- [ ] Is a secret resolved through configuration or `ISecretResolver`, never read from a file path or
      a script inside application code? `CONFIGURATION_STANDARDS.md` §6.
- [ ] Does an error response distinguish "does not exist" from "you may not see it"? It must not.
- [ ] Is personal data minimised, tenant-scoped, deletable and exportable?
      `SECURITY_STANDARDS.md` §7.
- [ ] Does this change widen what any principal, worker or agent can reach? If yes, is that
      deliberate and recorded?
- [ ] **In the current window:** does this expose real data to a real user while there is no access
      control at all? `SECURITY_STANDARDS.md` §1.

### 3.2 Database — anything touching an entity, a configuration or a query

- [ ] Does a new entity have `Id` (uniqueidentifier PK), `Seq` (int IDENTITY shadow property) and a
      **computed PERSISTED** `Ref`? `DATABASE_STANDARDS.md` §3.
- [ ] Is `Ref` computed **in the database**? Only the database guarantees uniqueness under concurrent
      insert.
- [ ] Is the table name the C# class name verbatim, in the owning layer's schema, with no `T_nnn_`
      prefix?
- [ ] Are strongly-typed ID and enum converters **only** in Infrastructure
      (`StronglyTypedIdConverters.cs`)?
- [ ] **Cascade:** does only the owning parent cascade, with reference FKs `Restrict` and
      self-references `NoAction`? SQL Server error 1785 is a design error caught at migration time,
      not a runtime surprise. `DATABASE_STANDARDS.md` §5.3.
- [ ] Is there a cross-schema foreign key? There must not be — use a polymorphic reference by layer,
      type and id.
- [ ] Does a new filter, sort or lookup column have an index?
- [ ] Do date, money and unit columns follow §§11.1–11.4? **Does a quantity column name its unit?**
- [ ] Is rehydration via `public static <Name> Restore(...)` with a private constructor?
- [ ] Is there raw SQL, and if so is it parameterised and justified? `DATABASE_STANDARDS.md` §10.5.

### 3.3 API — anything adding or changing an endpoint

- [ ] Is the route plural, lowercase and versioned — `/api/v1/workspaces`,
      `/api/v1/workspaces/{id:guid}`?
- [ ] Is it in a `<Name>Endpoint.cs` file exposing one `Map<Name>Endpoints` extension method?
- [ ] Does the endpoint body do only routing, binding, validation and result translation? More than
      about twenty lines means logic has leaked into it.
- [ ] Does `GET` change state? It must never.
- [ ] Does `201` carry a `Location` header and the created representation?
- [ ] Is a **DTO** returned rather than a domain aggregate? Serialising an aggregate turns every
      private field into a public contract.
- [ ] Are enums serialised as **names**, not integers?
- [ ] Does validation run at the edge, before any domain object is constructed?
- [ ] Are collections wrapped with pagination metadata, and single resources returned bare?
- [ ] Does the error response follow Problem Details, with **no** stack trace, SQL, connection
      string, file path or internal type name in `detail`? `API_STANDARDS.md` §7.

### 3.4 Migrations

- [ ] Is the migration named `<timestamp>_<PascalCaseName>.cs`?
- [ ] Has the generated SQL been **read**, not just the C#?
- [ ] Is it additive, or does it drop or rename a column? A destructive migration needs an explicit
      decision and a stated data plan.
- [ ] Does it collide with another in-flight migration on the same `DbContext`? Two migrations
      conflict on the **model snapshot** even when they touch different tables.
- [ ] Is the model snapshot committed with the migration, in the same commit?
      `GIT_WORKFLOW.md` §6.2.
- [ ] Has the migration been **applied against a real database** and the result observed — not just
      generated?
- [ ] Is it reversible, or is a one-way migration deliberate and recorded?
- [ ] **Has this migration already been pushed?** A pushed migration is never edited; it is replaced
      by a new one. `DATABASE_STANDARDS.md` §9.

### 3.5 Frontend

- [ ] Does every request go through `ApiClient.ts`? No component calls `fetch` directly.
- [ ] Does every failure surface as `ApiError`, and is it **branched on** rather than stringified?
- [ ] Does the consumer of every query hook handle **three** states — loading, error, ready? Two-state
      handling is the most common defect shape in this codebase.
- [ ] Is `undefined` before the first fetch treated as *loading*, not as *empty*?
- [ ] Is a query failure rendered inline rather than thrown to `RouteErrorBoundary.tsx`? The boundary
      is a last resort; reaching it means a bug.
- [ ] Is a server exception message rendered verbatim anywhere? It leaks internals.
- [ ] Does a mutation retry automatically? It must not, until idempotency exists.
- [ ] Does each `useEffect` that starts something return a function that stops it, and is the
      `AbortSignal` from the query function actually passed to `fetch`?
- [ ] Does the change add a second styling mechanism? There is one `index.css` and no framework.
- [ ] Is telemetry going through `ChatTelemetryContext.tsx` rather than the console?
- [ ] Keyboard reachable, labelled, and are state changes announced to assistive technology?
      `TYPESCRIPT_REACT_STANDARDS.md` §12.

### 3.6 Async, concurrency and cancellation

- [ ] Is `CancellationToken` threaded through **every** call that accepts one? A token that stops
      being passed halfway down is worse than none, because it looks like cancellation works.
- [ ] Is it the last parameter, named `cancellationToken`, with no default on an interface method?
- [ ] Any `.Result`, `.Wait()` or `async void` outside an event handler?
- [ ] Is a shared mutable object being mutated from more than one task?
- [ ] Is a concurrency token used where two callers can edit one aggregate?
- [ ] Is the transaction boundary one aggregate per request?
- [ ] Is a cancellation being converted into a `500`? It is not a fault.
- [ ] Does a cancelled operation leave state consistent?

### 3.7 Timeouts, retries and idempotency

- [ ] Does a new outbound call have an explicit timeout, expressed as a `TimeSpan`, from
      configuration rather than a literal?
- [ ] Is the **inner** timeout shorter than the outer one? Otherwise the client gives up while the
      server keeps burning provider cost, and both look like success.
- [ ] Does a retry apply only to something **transient** and **idempotent**? Never a `4xx`, never a
      validation failure, never a policy refusal.
- [ ] Is there a retry at more than one layer? Three layers of three attempts is twenty-seven calls.
- [ ] Is there backoff with jitter, a bounded attempt count, and a dead-letter path?
- [ ] Is a retried `POST` protected by an idempotency key, or will it create duplicates?

### 3.8 Performance

- [ ] Is there a query inside a loop that should be one query?
- [ ] Is there an unbounded result set — a list endpoint with no pagination, a `ToList()` on a whole
      table?
- [ ] Is a filter applied in memory that the database could apply?
- [ ] Is a **new** filter or sort column indexed?
- [ ] Does a metric dimension carry unbounded cardinality — a workspace id, a conversation id, a
      concrete path instead of a route template? `OBSERVABILITY_STANDARDS.md` §7.2.
- [ ] Is a longer timeout being used where an index is the actual fix?
- [ ] Does the change make an AI turn call the model more times than before, and is that deliberate?

### 3.9 AI and tool permissions

- [ ] Does the change let AI see product structure, or does it still receive an opaque `ScopeRef`?
- [ ] Does a tool declare its `SideEffectClass`, and is that class honoured at the gateway rather
      than trusted from the tool?
- [ ] Can an agent invoke something it has no permission for? `SECURITY_STANDARDS.md` §§9, 10.
- [ ] Does a prompt, a completion or a `ContextItem` body reach a log, a trace or a telemetry event?
- [ ] Does a turn failure produce a `TurnError` that **names the pipeline step**, rather than a bare
      `500`?
- [ ] Is turn state being written somewhere that survives a restart, or into
      `InMemoryTurnTraceStore`, which does not?
- [ ] Are token counts and the model recorded, even for a failed invocation? It cost money either
      way.
- [ ] Could this change let an agent create, modify or waive its own acceptance criteria? It must
      not.

### 3.10 Configuration

- [ ] Is a new setting bound to a typed options class and validated at startup?
- [ ] Is a value read from configuration rather than hardcoded — a URL, a port, a timeout, a limit?
- [ ] Does a frontend variable use the `VITE_` prefix, and is the author aware it is **inlined at
      build time** and therefore public?
- [ ] Does a default in `appsettings.json` differ from what a developer needs locally, in a way that
      will silently do the wrong thing?

### 3.11 Dependencies

- [ ] Does this add a package? Is it in `TECHNOLOGY_STACK.md`, and if not, is there an ADR?
- [ ] Is the version pinned per `STACK_VERSION_POLICY.md`?
- [ ] Does it come from a feed CI can reach? `C:\Personal\LocalNuGet` **is not reachable from CI** —
      M-08-1.1.
- [ ] Does it duplicate something already in the stack?
- [ ] Does it drag in a transitive dependency nobody chose?

### 3.12 Backward compatibility

- [ ] Does this change or remove a field, a route, a status code or an enum value a caller already
      depends on?
- [ ] Is an added field optional, so existing callers keep working?
- [ ] Is a removed endpoint deprecated with a date before it returns `410`?
- [ ] Does an enum gain a value the frontend does not handle?
- [ ] Would the currently deployed frontend still work against this API? **CURRENT:** nothing is
      deployed, so this is about the local client — the habit is worth having before it matters.

### 3.13 Contract changes

- [ ] Does the change modify a type in `Nexus.Platform.Contracts` or
      `Nexus.Intelligence.Contracts`?
- [ ] If so: is it additive, or does it break every consumer? A contract mutation on a shared
      boundary makes this work item unsafe to run in parallel with anything else touching it —
      `DEVELOPMENT_WORKFLOW.md` §4.
- [ ] Does the contract now reference a product type? It must not.
- [ ] Is there an ADR? A contract change is normally a durable architectural decision.

---

## 4. Findings

| Rule | Statement |
|---|---|
| A finding is specific and actionable | *"this relationship needs `DeleteBehavior.Restrict` or it will trip error 1785 when `Project` is added"* — not *"this is wrong"* |
| A finding cites the rule | A standard and a section, so the discussion is about the rule rather than about taste |
| Severity is stated | **Blocking**, **Should fix**, or **Consider**. An unlabelled comment is treated as blocking by a cautious author and ignored by a confident one |
| Preference is labelled as preference | "Consider" means the author may decline, and that is the end of it |
| Approval is on evidence, not on the description of the change | `GIT_WORKFLOW.md` §9 |
| A rejection records the reason | M-07-5.1 acceptance criterion |

Reviewing agent-produced work: **the checklist does not relax.** Agent output is confident,
consistent and plausible, which removes the surface cues a reviewer normally uses. §1 — scope,
unexpected files, migrations, contract changes — catches more agent errors than the line-by-line
pass, because the characteristic agent failure is doing something reasonable that was not asked for.

---

## 5. What this checklist cannot do today

| Check | Should be automatic | State |
|---|---|---|
| Layer boundaries | NetArchTest in CI | **Manual — no CI. M-08-1.4** |
| Build green | Pipeline | **Manual — `.github/workflows/` is empty. M-08-1.2** |
| Secret scanning | Pre-commit and CI | **Manual. `CONFIGURATION_STANDARDS.md` §13.1** |
| No secret in a log line | A redaction test | **Manual. S-10-1.1.1.2.1** |
| Scope conformance | Scope declaration versus diff | **Manual. M-07-1.1** |
| Criterion exists | Traceability gap report | **Manual. M-09-1.1** |
| Evidence exists | Quality gate | **Manual. M-09-1.3** |
| Migration conflict | Conflict group analysis | **Manual. M-07-2.2** |

Every row is a check a human currently performs from memory and will eventually forget. The purpose
of writing them here is not to keep them manual — it is to make each one specific enough to be
automated by the milestone named beside it.

---

## 6. References

- `GIT_WORKFLOW.md` §§8, 9 — pull requests, who reviews, and what a review decision is.
- `DEFINITION_OF_DONE.md` — the conditions this review is one of.
- `ERROR_HANDLING.md`, `OBSERVABILITY_STANDARDS.md` — §§2.3 and 2.4 in full.
- `SECURITY_STANDARDS.md` — §§2.4 and 3.1 in full; §11 is absolute.
- `DATABASE_STANDARDS.md`, `API_STANDARDS.md`, `NAMING_STANDARDS.md`,
  `TYPESCRIPT_REACT_STANDARDS.md`, `CSHARP_STANDARDS.md`, `CODE_CONVENTIONS.md`,
  `CONFIGURATION_STANDARDS.md`, `STACK_VERSION_POLICY.md` — the rules each group checks.
- `DEVELOPMENT_WORKFLOW.md` §4 — parallel safety, which §1 protects.
