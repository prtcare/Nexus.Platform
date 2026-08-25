# Security Architecture

**Status:** **TARGET, almost entirely — there is no authentication and no authorization in Nexus
today**, and the only access control that has ever existed leaves with Dataverse at `M-02-1.4`. Every
boundary below names the milestone that makes it real
**Owner:** CORE (Layer 01)
**Last updated:** 2026-08-21
**Layer:** 01 CORE — binding on every layer
**Authoritative for:** the **architecture** of security — where the trust boundaries are, how identity
flows end to end, where authentication and authorization decisions are made, how tenant isolation is
structured, where secrets live, how worker, AI and tool authority is bounded structurally, and the
environment, repository, deployment, product, data and audit boundaries.

**Not authoritative for the rules themselves.** `SECURITY_STANDARDS.md` owns every requirement, every
prohibition and every coding rule: token shape and claims, permission granularity, `403` versus `404`,
the secret resolution order, encryption standards, PII handling, the logging prohibitions, the worker
permission matrix, repository and environment permission tables, supply chain rules and backup
security. **This document says where a control sits and why; that one says what the control must do.**
Where they meet, it wins.

Also not authoritative for: how a security rule is proven — `ASSURANCE_STANDARDS.md`; HTTP error
shapes — `API_STANDARDS.md`; git mechanics — `GIT_WORKFLOW.md`; what may reference what —
`DEPENDENCY_RULES.md`.

---

## 1. The position, architecturally

**CURRENT.** The only access control Nexus has ever had is **Dataverse row-level security** —
inherited, not written, not tested, not portable. It leaves with `M-02-1.4 Delete Dataverse`.

```
today                    M-02-1.4                M-01-1.2 / M-01-2.1 / M-01-3.1
  |                          |                                |
  Dataverse RLS ------------>| Dataverse removed              |
                             |----- NO ACCESS CONTROL AT ALL -| real identity, tenancy, authz
```

That window is not a schedule risk; it is an architectural one. **`M-02-1.4` must not land far ahead
of `M-01-2.1`.** Nothing carrying real data may be exposed to a real user in between.

The interfaces exist and the implementations do not: `IIdentityService`, `ITenantResolver` and
`ResolvedIdentity` are in `Nexus.Platform.Contracts/Identity/`;
`Nexus.Platform.Identity/IdentityProvider.cs` is a **240-byte stub** that `M-01-1.1` deletes rather
than extends.

**Why the architecture is worth writing before the implementation.** Every boundary below is cheap to
place now and expensive to move later. A tenant filter added to a schema that already has data is a
migration; a tenant filter designed into the schema is a column. The same is true of every other line
in this document.

---

## 2. The trust boundaries

```
╔═════════════════════════════════════════════════════════════════════════════╗
║ ZONE 0 — UNTRUSTED                                                          ║
║   Browser · Nexus.Experience.Client (React + Vite + TanStack Query)         ║
║   Holds a token. Renders what it is given. ENFORCES NOTHING.                ║
║   Every check here is a convenience; none of it is a control.               ║
╚══════════════════════════════╤══════════════════════════════════════════════╝
                               │   B1  THE EDGE          HTTPS, /api/v1
                               │       authenticate · resolve identity
                               │       resolve tenant · authorize · correlate
╔══════════════════════════════▼══════════════════════════════════════════════╗
║ ZONE 1 — TRUSTED APPLICATION                                                ║
║   Nexus.Products.Chat.Api          (today: the only user-facing host)       ║
║   Nexus.Developer.Api              (TARGET)                                 ║
║   Nexus.Experience.Api             (TARGET)                                 ║
║   Identity is resolved here and NEVER re-derived downstream.                ║
╚═══╤════════════════════════════════════════════════════╤════════════════════╝
    │  B2  HOST TO HOST                                  │  B4  PERSISTENCE
    │      HTTP /intelligence/v1                         │      tenant filter
    │      token revalidated · correlation propagated    │      schema ownership
╔═══▼════════════════════════════════════════════════╗ ╔═▼══════════════════════╗
║ ZONE 2 — REASONING                                 ║ ║ ZONE 4 — DATA          ║
║   Nexus.Intelligence.Api / .Core                   ║ ║  NexusPlatform         ║
║   Acts AS the user, never above the user.          ║ ║   core · data · ai ·   ║
║   Receives ContextBundle. ScopeRef is OPAQUE.      ║ ║   governance · …       ║
║   TrustLevel marks what is data, not instruction.  ║ ║  ─────────────────     ║
╚═══╤═══════════════════════════════╤════════════════╝ ║  Nexus<Product> DB     ║
    │  B3  TOOL GATEWAY             │  B5  PROVIDER    ║   one per product,     ║
    │      SideEffectClass gate     │      credential  ║   no shared FK possible║
    │      permission · audit       │      boundary    ╚════════════════════════╝
╔═══▼═════════════════════╗   ╔═════▼══════════════════════════════════════════╗
║ ZONE 3 — EFFECTS        ║   ║ ZONE 5 — CORE PROVIDER EDGE                    ║
║  IToolGateway           ║   ║  Nexus.Platform.Providers.OpenAI               ║
║  irreversible/external  ║   ║  THE ONLY PLACE A PROVIDER CREDENTIAL EXISTS.  ║
║  ⇒ HUMAN APPROVAL       ║   ║  No API key anywhere in Nexus.Intelligence.    ║
╚═══╤═════════════════════╝   ╚═════╤══════════════════════════════════════════╝
    │  B6  MACHINE SAFETY BOUNDARY  │  B7  EXTERNAL
    │      ONE WAY ONLY             ▼      OpenAI · future providers
╔═══▼═════════════════════════════════════════════════════════════════════════╗
║ ZONE 6 — DETERMINISTIC CONTROL          (TARGET — no machine domain exists) ║
║   PLC / real-time motion controller.                                        ║
║   Owns motion, interlocks, E-stop, hard limits, guarding.                    ║
║   NEXUS IS NEVER INSIDE THIS ZONE. Telemetry flows out; only human-approved ║
║   proposals flow in, and the controller enforces its own limits regardless. ║
╚═════════════════════════════════════════════════════════════════════════════╝

Cross-cutting, not a zone:  B8 REPOSITORY · B9 ENVIRONMENT · B10 DEPLOYMENT
```

| # | Boundary | What crosses | What is enforced | State |
|---|---|---|---|---|
| **B1** | The edge | An HTTP request with a bearer token | Authenticate, resolve identity and tenant, authorize, assign correlation id | **TARGET** — `M-01-1.2`, `M-01-2.1`, `M-01-3.1`, `M-10-1.1` |
| **B2** | Host to host | `IntelligenceTurnRequest` over `/intelligence/v1` | **Revalidation**, not trust. Correlation propagates | Integration works; **no validation exists** |
| **B3** | Tool gateway | `ToolInvocation` | `SideEffectClass` gate, permission check, audit, approval | **TARGET** — `M-01-7.1`, `M-05-5.1` |
| **B4** | Persistence | An EF query | Tenant filter, schema ownership | **TARGET** — `M-01-2.1`, `M-02-1.5` |
| **B5** | Provider edge | A model invocation | The credential is resolved here and nowhere else | Works; credential path is `set-openai-key.ps1` until `M-01-5.1` |
| **B6** | Machine safety | Telemetry out; approved proposals in | **One-way. No Nexus component is ever in the loop** | TARGET — no machine domain exists |
| **B7** | External | An outbound HTTP call | Timeout, bounded retry, adapter mapping, `TrustLevel` on anything returned | One integration exists — OpenAI |
| **B8** | Repository | A push | Branch protection, review, green build | **TARGET — `M-08-1.4`.** `main` is pushable in all three repositories today |
| **B9** | Environment | A deployment or an operator | Environment-scoped credentials, named access, **no production data downward** | **TARGET — `M-08-4.1`.** No environment exists |
| **B10** | Deployment | An artifact | Approval for production, audited, reversible | **TARGET — `M-08-5.1`, `M-08-5.2`** |

**Zone 0 is the row people misread.** The frontend holds a token and displays what it is allowed to
display. It is not a control. A capability the member is not entitled to must be **absent, not
disabled** (`M-11-6.2`) — but that is user experience, not enforcement, and the API must reject the
request regardless of what the client rendered. A feature hidden on the free plan is inaccessible via
API as well as UI (`M-06-3.2`), and the API half is the half that matters.

---

## 3. Identity flow, end to end

One resolution, propagated. **Identity is established once at B1 and never re-derived.**

| Step | Where | Carries |
|---|---|---|
| 1 | Browser | A signed, short-lived access token |
| 2 | **B1 — host middleware, every host** | Validates signature, expiry and revocation. Produces `ResolvedIdentity` |
| 3 | `ITenantResolver` | The tenant **from the token, never a constant** |
| 4 | `IAuthorizationService` | `principal × permission × scope` — never `principal × permission` alone |
| 5 | Correlation | `X-Correlation-Id` accepted or generated, attached to everything downstream |
| 6 | **B2 — Intelligence** | The turn carries `ActorRef`. The token is **revalidated**, not assumed |
| 7 | Context assembly | `ContextSelector` and `PromptAssembler` operate on **already-permitted** content |
| 8 | **B3 — tool invocation** | The invoking principal's permissions, evaluated per invocation |
| 9 | Persistence | Tenant set from the resolved identity on insert, **never from the request body** |
| 10 | Audit | `AuditEntry` carrying actor, tenant, action, timestamp and the correlation id |

**Two properties make the chain load-bearing rather than decorative.**

**Every host validates, or none of them mean anything.** Chat API, Intelligence API and every future
host run the same validation middleware. A host that trusts an upstream host's assertion is a host
whose security depends on nobody ever calling it directly — and `/intelligence/v1` is reachable.

**The AI acts as the user and never above the user.** If the user cannot read a document, no prompt
makes it visible. Filtering happens *before* context assembly, not by asking the model to be discreet.
`TrustLevel` on `ContextItem` marks retrieved content as **data to be reasoned about, never
instructions to be followed** — the architectural defence against prompt injection, and it only works
because the filtering already happened upstream of it.

**CURRENT.** No step above exists. `ChatTurnIdentity` returns a hardcoded tenant and placeholder
permissions; `M-01-3.1`'s acceptance criterion replaces it explicitly.

---

## 4. Authentication architecture — where it sits

**TARGET — `M-01-1.2`.** `SECURITY_STANDARDS.md` §2 owns the requirements. The architectural
placement:

| Decision | Placement | Why here |
|---|---|---|
| Credential storage and verification | CORE context 1.1 | The only layer that may hold `Credential` |
| Token issue | CORE context 1.1 | One issuer. Two issuers is two revocation stories |
| Token **validation** | **Shared middleware in every host** | A host that does not validate is an open door with a closed door beside it |
| Signing key | Resolved through `ISecretResolver` | Never appsettings. `M-01-5.1` |
| Permission set version claim | In the token | Lets a revocation take effect before expiry without a per-request revocation lookup |

**Non-human principals are principals.** An agent worker, a pipeline and a scheduled job each
authenticate as a **distinct principal** with its own credential and its own permission set. They
never share a human's credential and never share each other's. An agent's action is attributable both
to the agent and, separately, to the human who dispatched it — which is why `DevelopmentRun` carries a
worker and `AuditEntry` carries both.

---

## 5. Authorization architecture — where the decision is made

**TARGET — `M-01-3.1`.** `SECURITY_STANDARDS.md` §3 owns granularity and status codes.

| Property | Architectural consequence |
|---|---|
| **Deny by default** | The filter applies to every endpoint; opt-out is explicit and per-endpoint. Allow-by-default fails open, silently, exactly once |
| Evaluated at the **API boundary**, in the host | Not in the domain, not in a repository. A domain type that authorizes is a domain type that cannot be reused in a context with different rules |
| Scope-aware | Platform roles combine with **product-scoped** roles — `M-06-2.2`. Layer 06 evaluates the product half; CORE evaluates the platform half; the composition is CORE's |
| Never in the client | Zone 0 enforces nothing |
| Never in AI | AI receives already-permitted content. It does not decide access; it cannot, because it never sees the structure |

**Where the two authorization layers meet.** CORE owns *who you are* and *what you may do in Nexus*;
PRODUCT CORE owns *who you are within a product* and *what you may do there*. Removing a product
membership must not affect the Nexus identity — `M-06-2.1`'s acceptance criterion is the test that the
split held.

---

## 6. Tenant isolation — the single most important control

> **Every query is tenant-scoped by construction, and cross-tenant access is impossible rather than
> merely discouraged.**

This is the most important control in the system for one reason: **it is the control the system is
about to lose.** Dataverse row-level security is the only isolation Nexus has ever had, it was never
written by anyone here, and `M-02-1.4` removes it. `M-01-2.1` must replace it.

`SECURITY_STANDARDS.md` §4 owns the mechanisms. The architectural properties:

| Property | Why it is architecture, not implementation |
|---|---|
| The tenant comes **from the token** | An architecture in which any caller can name a tenant has no isolation, only a naming convention |
| A **global query filter** on every tenant-owned entity | Isolation applied per-query is isolation that will be forgotten per-query |
| **A missing filter throws; it never returns all tenants** | A missing filter that returns everything is a breach that looks like a working feature. One that throws is a bug found in the first test run |
| The tenant column is **indexed on every tenant-owned entity** | An unindexed filter is a filter someone will be tempted to remove |
| The tenant is set on insert from the resolved identity | Accepting it from a request body makes the client the authority on its own isolation |
| Cross-tenant reads return `404`, not `403` | A `403` confirms the resource exists, which is itself a cross-tenant leak |

### 6.1 The test is written before the implementation

> **A user in tenant A cannot read tenant B data — proven by an integration test written BEFORE the
> implementation.**

That is `M-01-2.1`'s acceptance criterion, stated in that order deliberately. A test written after an
implementation proves the implementation agrees with itself. A test written first, watched to fail,
then made to pass, proves the isolation actually engages. **This is the one place in the whole
document where the ordering of two activities is itself the control.**

`M-01-2.1` is `parallel_safe: false` — it touches every tenant-owned entity and runs alone.

### 6.2 Cross-tenant by design

Platform administration, aggregate operational metrics and billing are legitimately cross-tenant. Each
such operation **names itself explicitly, carries its own permission, escapes the filter only through
an explicit and auditable path, and writes an audit entry every time.** `IgnoreQueryFilters()`
appearing anywhere without those four properties is a defect.

### 6.3 Isolation is layered, not single

| Layer of defence | Mechanism |
|---|---|
| Token | The tenant is a claim, not a parameter |
| Query | Global filter, enforced, failing loudly |
| Schema | Layer schemas separate layers — `M-02-1.5` |
| **Database** | **One database per product.** Two products cannot share a foreign key because they cannot share a database |
| Detection | Repeated cross-tenant attempts raise an alert naming the actor — `M-10-6.1` |

The product row is the strongest because it is physical. Every other row is a rule that code obeys;
that one is a fact code cannot disobey.

---

## 7. Secrets architecture

**TARGET — `M-01-5.1`.** `SECURITY_STANDARDS.md` §5 owns the resolution order and the rules.

| Architectural property | |
|---|---|
| **One resolver, in CORE** | `ISecretResolver` in `Nexus.Platform.Contracts/Secrets/`. Every layer asks; no layer holds |
| **Resolved, never passed** | A secret is never a constructor parameter carried through layers. Carrying it makes every intermediate layer part of the credential's blast radius |
| The provider credential resolves **at B5 and nowhere else** | `Nexus.Platform.Providers.<Vendor>` is the only place. This is what makes "no API key anywhere in `Nexus.Intelligence`" a structural fact rather than a habit |
| The local fallback is **structurally incapable** of use elsewhere | Not merely configured off. A fallback that can be switched on in production is a production secret store with a misleading name |
| Registries hold **references**, never material | `M-03-3.3` stores a thumbprint and a secret reference; `M-03-6.1` rejects a value written to an `IsSecret` entry |
| Resolution is audited; **the value never is** | An audit trail that leaks what it audits is worse than none |

**CURRENT.** `set-openai-key.ps1` is the entirety of secret management. It works for one developer on
one machine and for one secret. There is no secret scanning; `M-01-5.1` adds a CI scan that **fails
the build on a match**, which requires CI, which is `M-08-1.2`.

---

## 8. AI and tool authority — `SideEffectClass` is the gate

The architecture's answer to "what may an AI do" is not a permission list. It is a **classification of
consequence applied before execution**.

| `SideEffectClass` | Requirement |
|---|---|
| Read-only | Permission check |
| Reversible write | Permission check, audited |
| **Irreversible write** | Permission check, audited, **explicit approval** |
| **External effect** | Permission check, audited, **explicit human approval** |

Four architectural properties follow, and each is a boundary rather than a rule:

1. **A tool is reachable only through `IToolGateway`, resolved from `IToolCatalog`.** There is no
   second path, so there is no second gate to forget.
2. **The class is declared before registration.** A tool that has not classified itself cannot be
   registered, so it cannot be invoked.
3. **AI produces a `ProposedAction`; something else executes it under policy.** The separation between
   proposing and executing is the whole safety argument, and it is already in
   `Nexus.Intelligence.Contracts/Turns/`.
4. **The AI cannot exceed the user.** A turn carries the user's identity and permissions, never
   elevated ones.

**CURRENT: no tool can be invoked at all.** `ToolProvider.cs` is a 231-byte stub;
`Nexus.Intelligence.Api/Tooling/` holds `EmptyToolCatalog` and `EmptyToolGateway`. That absence is the
only reason the absence of tool permissions is not an exposure today. `M-01-7.1` is the milestone, and
**its permission model must land with it, not after** — a tool registry shipped without its gate is
the gate never arriving.

**The machine case needs no new mechanism.** A machine command is an irreversible external effect in
the most literal sense available, which the existing classification already covers and already
requires human approval for. **No agent may create, modify or waive a safety-critical acceptance
criterion** — `M-09-7.2`, absolute, no exception path. `INTEGRATION_ARCHITECTURE.md` §10 owns B6.

---

## 9. Worker permissions — bounded by structure

`SECURITY_STANDARDS.md` §12 owns the full human-versus-agent matrix. Three of its rows are
architectural, and together they are what makes running several agents at once safe:

> **An agent never approves work, never merges, and never modifies the controls that constrain it.**

| Structural control | Mechanism |
|---|---|
| Distinct principal per worker | Never a shared service account. Attribution survives |
| **Physical isolation** | Each assignment holds its own worktree, and **a worktree is a sibling of the repository, never nested inside it** — `M-07-3.1`. Two assignments cannot claim the same path |
| Scope declaration | A worker operates only within its work item's declared projects, schemas and contracts. A change outside is a scope violation caught at review — and for an agent, a signal to stop rather than to widen scope |
| Branch protection | `main` and integration branches are unpushable by any worker — `M-08-1.4` |
| Human decision required to integrate | *A run cannot integrate without a recorded human decision* — `M-07-5.1` |

The worktree rule has a Windows-specific origin worth keeping: a git worktree nested inside a folder
an agent has as its working directory cannot be renamed while that agent runs. The sibling placement
is an acceptance criterion, not a preference.

---

## 10. Environment, repository and deployment boundaries

**All three are TARGET. None exists.** `SECURITY_STANDARDS.md` §13 owns the permission tables; the
boundaries themselves:

| Boundary | The architectural rule |
|---|---|
| **B8 Repository** | `main` is unpushable; merge requires a reviewed PR with a green build; **a PR containing a boundary violation cannot merge** — `M-08-1.4` puts NetArchTest in every pipeline as a hard gate |
| **B9 Environment** | Environments are `Local`, `Development`, `Integration`, `Staging`, `Pre-Production`, `Production` — `M-08-4.1`. **Environment carries no maturity field and maturity carries no environment field** |
| **Production data** | **Never copied to a lower environment.** Not for debugging, not once, not anonymised in a hurry. Where production-like data is needed, it is generated |
| Credentials | Environment-scoped. A development credential cannot reach production and a production credential is never present below production |
| **B10 Deployment** | Deployment is a pipeline, not a person with a publish profile. The artifact is **built once and promoted**, never rebuilt per environment. Production promotion requires a recorded human approval |

The production-data rule is written now, before any environment exists, because it is the rule most
often broken the first time something is hard to reproduce — and once broken it cannot be unbroken.

**CURRENT.** `main` is directly pushable in all three repositories. `Nexus.Platform\.github\workflows\` is
empty; `Nexus.Experience` and `Nexus.Intelligence` have no `.github` directory at all. There is no deployment pipeline,
no IaC and no environment definition anywhere.

---

## 11. Product and data boundaries

| Boundary | Enforcement |
|---|---|
| Product ↔ product | **Physical.** One database per product; a shared foreign key is impossible. No `Nexus.Products.*` assembly may reference another |
| Product ↔ platform schema | A platform table inside a product database, or a product table inside `NexusPlatform`, is the boundary being broken — not an optimisation |
| Layer ↔ layer, in one database | A schema boundary is a real boundary though the tables are adjacent. **Adjacency is not permission** |
| Where a reference is forbidden | Polymorphic and constraint-free: layer, type, id, no FK. Integrity in application code, proven by test |
| Highest-sensitivity data | **Conversation content and prompt bodies.** A user talking to an assistant discloses what they would not put in a form |
| Log store versus content store | Different access control, different retention, different audience. **A prompt body in a log copies the most sensitive data in the system into the least protected place** — an `M-10-1.1` acceptance criterion forbids it |

**The classification and residency layer is GOVERNANCE, not CORE.** `M-02-5.1` classifies and retains;
`M-03-4.2` declares residency and rejects a region outside the allowed set. Until both exist, build for
**deletability and exportability**, because retrofitting either into a system that assumed neither is
expensive — and a deletion that clears the live database and leaves the data in six months of backups
has not been satisfied.

---

## 12. Audit boundaries

| Property | Architectural consequence |
|---|---|
| **Append-only and immutable** | Never updated, never deleted within retention |
| **Written where the action happens**, synchronously | Not in an event handler. An audit record whose existence depends on a best-effort notification is not an audit record |
| **Write access separate from everything else** | An audit log editable by the principal whose actions it records is a log with an asterisk |
| Carries the correlation id | So an audit entry and a log line describe the same request |
| Distinct from a row-audit column | `CreatedBy`/`ModifiedBy` answers *who last touched this*; `AuditEntry` answers *what happened, in order* |
| Distinct from a telemetry event | Different retention, different access control, different audience — `EVENT_ARCHITECTURE.md` §2 |

**CURRENT.** `IAuditLog` and `AuditEntry` exist in `Nexus.Platform.Contracts/Governance/`. The only
implementation is `ConsoleAuditLog` — **entries are written to the console and then lost.**
`M-01-4.1` makes it durable and queryable by user, tenant, action and date range.

---

## 13. Where the architecture is weakest today

Ranked by consequence, not effort. `SECURITY_STANDARDS.md` §16 carries the full list; these are the
five that are *structural* rather than procedural.

| # | Weakness | Closed by |
|---|---|---|
| 1 | **B1 does not exist.** No authentication, no authorization, and the only isolation leaves with Dataverse | `M-01-1.2`, `M-01-2.1`, `M-01-3.1`, sequenced against `M-02-1.4` |
| 2 | **B2 is not validated.** `/intelligence/v1` is reachable and trusts its caller | `M-01-1.2` — every host validates |
| 3 | **No correlation id anywhere.** A security event cannot be traced across three hosts | `M-10-1.1` |
| 4 | **B8 is open.** `main` is pushable in all three repositories; three architecture test files exist and no pipeline runs them | `M-08-1.4` |
| 5 | Audit goes to the console and is lost | `M-01-4.1` |

**The sequencing constraint that matters most, restated because it is the one that can go wrong
quietly:** removing Dataverse removes the only access control that has ever existed. If identity is
not ready, the window between `M-02-1.4` and `M-01-2.1` is a system with real data and no gate on it.

---

## 14. Open decisions

| Question | What would decide it | State |
|---|---|---|
| Which privacy regimes Nexus is subject to | `M-03-4.1` Compliance obligation catalogue, plus where the business operates | **Not yet decided** |
| Whether `Azure.Identity` becomes the key-vault path | It arrived with Dataverse; nothing selected it | Not yet decided |
| Which key vault | `M-01-5.1`; no cloud provider beyond Azure SQL is selected | Not yet decided |
| Whether a machine domain ever exists, and under whose sign-off | A business decision; B6 is written regardless | Not yet decided |
| Whether the `operations` schema leaves `NexusPlatform` | Volume after `M-10-2.2`. It changes B4's shape, not its rules | Predicted, not decided |
| Whether the antivirus exclusion for `C:\Personal` is in place | **Still unconfirmed.** The cause of the 2026-08-20 loss is live | Open nonconformance — `M-08-2.1` |

---

## 15. References

- **`SECURITY_STANDARDS.md`** — every rule, requirement and prohibition. This document places controls;
  that one specifies them.
- `DEPENDENCY_RULES.md` — the reference direction, which is a security property as much as a design one.
- `INTEGRATION_ARCHITECTURE.md` — what crosses each boundary, and §10 for the machine boundary in full.
- `EVENT_ARCHITECTURE.md` §2, §11 — audit versus event versus telemetry.
- `DATABASE_ARCHITECTURE.md` — schema and database separation as the physical half of isolation.
- `BOUNDED_CONTEXTS.md` — CORE contexts 1.1 to 1.5, which own the controls named here.
- `ASSURANCE_STANDARDS.md` — how a security control is proven, and the cross-tenant denial test.
- `MACHINE_DEVELOPMENT_GUIDE.md` §1 — the division of authority at B6.
- `GIT_WORKFLOW.md` — B8 mechanics and the 2026-08-20 incident.
- `../nexus-roadmap.yaml` — `M-01-1.1`, `M-01-1.2`, `M-01-2.1`, `M-01-3.1`, `M-01-4.1`, `M-01-5.1`,
  `M-01-7.1`, `M-02-1.4`, `M-08-1.4`, `M-09-7.2`, `M-10-1.1`, `M-10-6.1`.
