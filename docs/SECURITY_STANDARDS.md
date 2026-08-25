# Security Standards

**Status:** Active
**Owner:** CORE (Layer 01)
**Last updated:** 2026-08-21
**Layer:** 01 CORE — binding on every layer
**Authoritative for:** authentication, authorization, tenant isolation, secrets, encryption, PII,
audit, AI permissions, tool permissions, worker permissions, repository permissions, environment
permissions, deployment permissions, dependency security, supply chain, logging restrictions,
backup security and recovery.

Not authoritative for: HTTP error shapes — `API_STANDARDS.md`; where a configuration value lives —
`CONFIGURATION_STANDARDS.md`; how a security rule is proven — `ASSURANCE_STANDARDS.md`; git
mechanics — `GIT_WORKFLOW.md`.

---

## 1. The position, stated bluntly

**There is no authentication in Nexus today. There is no authorization in Nexus today.**

The only access control that has ever existed in this system is **Dataverse row-level security**,
inherited from the platform Nexus is migrating off. It is not code anyone wrote, it is not tested,
it is not portable, and — this is the critical part — **it leaves with M-02-1.4 Delete Dataverse.**

That produces a specific and dangerous window:

```
today                    M-02-1.4                M-01-1.2 / M-01-2.1 / M-01-3.1
  |                          |                                |
  Dataverse RLS ------------>| Dataverse removed              |
                             |----- NO ACCESS CONTROL AT ALL -| real identity, tenancy, authz
```

Between removing Dataverse and shipping identity, the system has **no access control of any kind**.
This is why identity is gate-critical and why it sits in P1 rather than being deferred behind
features. Nothing that carries real data may be exposed to a real user in that window.

`Nexus.Platform.Contracts/Identity/` already defines the shapes — `IIdentityService`,
`ITenantResolver`, `IProductRegistry`, `ResolvedIdentity`. `Nexus.Platform.Identity/
IdentityProvider.cs` is a **240-byte stub**. The interfaces exist; the implementation does not.

| Milestone | What it closes |
|---|---|
| M-01-1.1 Identity domain and schema | Users exist as records |
| **M-01-1.2 Authentication flow** | A user can prove who they are |
| **M-01-2.1 Organisation and tenant with enforced isolation** | Tenants cannot see each other |
| **M-01-3.1 Roles, permissions and evaluation** | Endpoints make real access decisions |
| M-01-3.2 Policy-based and attribute-based rules | Conditional access |
| M-01-4.1 Durable audit log | Attempts are recorded somewhere durable |
| **M-01-5.1 Real secret resolver** | Secrets stop living in scripts |

---

## 2. Authentication

**TARGET — M-01-1.2 Authentication flow.**

### 2.1 Requirements

| Requirement | Detail |
|---|---|
| Token | JWT, signed, short-lived access token with refresh |
| Claims | `UserId`, `TenantId`, permission set version |
| Validation | Shared middleware in **every** host — Chat API, Intelligence API, and any future host |
| Signing key | **Resolved through `ISecretResolver`, never from appsettings** |
| Revocation | An expired or revoked token is rejected by every host |
| Failure | Invalid credentials return `401` and record an audit entry |
| Lockout | Sign-in locks out after repeated failure |
| Rate limit | The sign-in endpoint is rate-limited — `API_STANDARDS.md` §13.1 |

Every authentication attempt is audited, success and failure alike. Auditing only failures loses the
ability to answer "when did this account last sign in successfully, and from where".

### 2.2 The permission set version claim

The token carries the version of the permission set it was issued against, not the permissions
themselves. A permission change increments the version; a token carrying a stale version is
re-evaluated against current state rather than trusted. This is what makes a permission revocation
take effect before the token expires without requiring a revocation list lookup on every request.

### 2.3 Machine and agent authentication

An agent worker, a pipeline and a scheduled job each authenticate as a **distinct principal** with
its own credential and its own permission set. They never share a human's credential and never
share each other's.

An agent's actions are attributable to the agent, and separately to the human who dispatched it.
`CreatedBy` records the acting principal (`DATABASE_STANDARDS.md` §6); the audit log records both.

### 2.4 What must not be done

| Never | Why |
|---|---|
| A hardcoded user, tenant or permission set | `ChatTurnIdentity`'s placeholder is explicitly replaced at M-01-3.1 |
| A shared service account for human users | Attribution is lost permanently |
| A long-lived token as a convenience | It is a credential with no revocation story |
| Authentication in one host and not another | Every host validates, or none of them mean anything |
| Passwords stored in any form other than a modern password hash | No exceptions |

---

## 3. Authorization

**TARGET — M-01-3.1 Roles, permissions and evaluation.**

### 3.1 Requirements

| Requirement | Detail |
|---|---|
| Model | `Role`, `Permission`, `RoleAssignment` with role inheritance |
| Evaluation | `IAuthorizationService` evaluates a real permission set — never a stub |
| Default | **Deny by default.** An authorization filter applies to every endpoint; opt-out is explicit and per-endpoint |
| Coverage | Every Experience and Developer endpoint rejects unauthenticated requests |
| Failure | A user without a permission receives `403` and an audit entry is recorded |
| Placeholder removal | `ChatTurnIdentity` returns real values — no hardcoded tenant, no placeholder permissions |

Deny-by-default with explicit opt-out is the only arrangement that survives a new endpoint being
added by someone who did not read this document. Allow-by-default with explicit protection fails
open, silently, exactly once.

### 3.2 Permission granularity

Permissions name a resource and an action: `workspace.read`, `workspace.create`,
`work-item.approve`, `secret.resolve`. Not `admin`. Not `user`.

State transitions get their own permissions, which is why they are sub-resource `POST` endpoints
rather than a `PATCH` on a status field (`API_STANDARDS.md` §3.3). `work-item.approve` is a
different permission from `work-item.update`, and a `PATCH` that can set status to `Approved`
collapses the two.

### 3.3 Product-scoped roles

**TARGET — M-06-2.2 Product-scoped roles.** A role may be scoped to a product, a workspace or a
project. The evaluation is `principal × permission × scope`, never `principal × permission` alone.

### 3.4 `403` versus `404`

| Situation | Response |
|---|---|
| Authenticated, lacks permission, resource is in the caller's tenant | `403 Forbidden` |
| Authenticated, resource is in **another tenant** | `404 Not Found` |
| Not authenticated | `401 Unauthorized` |

The second row is deliberate. A `403` confirms the resource exists, which is an information leak
across a tenant boundary. Cross-tenant requests are indistinguishable from requests for things that
do not exist, because from the caller's perspective they should be.

---

## 4. Tenant isolation

**TARGET — M-01-2.1. This milestone is `parallel_safe: false` — it touches every tenant-owned entity
and runs alone.**

### 4.1 The rule

> Every query is tenant-scoped **by construction**, and cross-tenant access is impossible rather
> than merely discouraged.

### 4.2 How

| Mechanism | Detail |
|---|---|
| Tenant from the token | `ITenantResolver` returns the tenant from the token, **never a constant** |
| Global query filter | An EF Core global query filter on every tenant-owned entity |
| Fail loudly | A query that omits the tenant filter **fails at build time or throws** — it never returns all tenants |
| Indexed | The tenant column is indexed on every tenant-owned entity — `DATABASE_STANDARDS.md` §5.2 |
| Writes too | The tenant is set from the resolved identity on insert, never accepted from the request body |

The third row is the one that distinguishes real isolation from hopeful isolation. A missing filter
that returns every tenant's rows is a breach that looks like a working feature. A missing filter that
throws is a bug found in the first test run.

### 4.3 The test comes first

> **A user in tenant A cannot read tenant B data — proven by an integration test written BEFORE the
> implementation.**

This is an acceptance criterion of M-01-2.1, stated in that order for a reason. A test written after
an implementation proves the implementation agrees with itself. A test written first, watched to
fail, then made to pass, proves the isolation actually engages.

The full list of security tests and their milestones is in `ASSURANCE_STANDARDS.md` §5.7.

### 4.4 Cross-tenant by design

Some operations are legitimately cross-tenant: platform administration, aggregate operational
metrics, billing. Each such operation names itself explicitly, carries its own permission, ignores
the filter only through an explicit and auditable escape, and writes an audit entry every time.
`IgnoreQueryFilters()` appearing anywhere without those four properties is a defect.

---

## 5. Secrets

### 5.1 Current state

**CURRENT.** `set-openai-key.ps1` in Nexus.Platform handles the OpenAI key. It is a PowerShell script that
puts the key where the application can read it. That is the entirety of secret management in Nexus.

It works for one developer on one machine. It does not work for a second developer, a build agent,
a deployed environment, or any secret other than the OpenAI key.

### 5.2 Target

**TARGET — M-01-5.1 Real secret resolver.** `ISecretResolver` already exists as a contract in
`Nexus.Platform.Contracts/Secrets/ISecretResolver`. The implementation does not.

Acceptance criteria, verbatim:

- `ISecretResolver` resolves from environment and key vault, with a local development fallback;
- **no provider key, connection string or signing key appears in any appsettings file in any
  repository**;
- a secret scan runs in CI and fails the build on a match.

Plus two tasks worth restating: **audit secret resolution without logging the value**, and move
`set-openai-key.ps1` handling to the resolver.

### 5.3 Resolution order

| Order | Source | Environment |
|---|---|---|
| 1 | Environment variable | All |
| 2 | Key vault | Deployed environments |
| 3 | Local development fallback | Development only, never present elsewhere |

The fallback is development-only and must be structurally incapable of being used elsewhere — not
merely configured off. A fallback that can be switched on in production is a production secret store
with a misleading name.

### 5.4 Rules

| Rule | Statement |
|---|---|
| Never in git | No secret in any file in any repository, ever |
| Never in appsettings | Including `appsettings.Development.json` |
| Never in a log | Not at any level, not in an exception message, not in a trace |
| Never in an error response | `API_STANDARDS.md` §7 — `detail` never carries internal state |
| Never in a commit message | It survives in history |
| Resolved, not passed | Code asks `ISecretResolver`; a secret is never a constructor parameter carried through layers |
| Audited | Resolution is audited; **the value never is** |
| Rotatable | Every secret has a rotation path, exercised at least once |

### 5.5 If a secret is exposed

1. **Rotate first.** Everything else is secondary. The secret is compromised the moment it is
   pushed, and rewriting history does not un-compromise it.
2. Remove it from the working tree and push the removal.
3. Rewrite history only if coordinated — `GIT_WORKFLOW.md` §12.
4. Record a nonconformance — `ASSURANCE_STANDARDS.md` §11.2.
5. Check whether the exposed secret was used in the interval.

---

## 6. Encryption

| Layer | Standard | State |
|---|---|---|
| In transit, external | TLS 1.2 minimum, 1.3 preferred | **TARGET** — development runs HTTP and warns "Failed to determine the https port for redirect" |
| In transit, to Azure SQL | `Encrypt=True` in the connection string | **TARGET** — verify before first deployed environment |
| At rest, database | Azure SQL Transparent Data Encryption | Default on Azure SQL; **not applicable to LocalDB** |
| At rest, column | Only where a specific obligation requires it | Not yet required |
| Backups | Encrypted at rest, keys held separately from the backup | **TARGET** — no backup exists (§14) |
| Passwords | Modern password hashing, never encryption | **TARGET** — M-01-1.2 |
| Tokens | Signed, not encrypted; contents are not secret, integrity is | **TARGET** — M-01-1.2 |

The development HTTPS warning is expected local noise, not a defect. It becomes a defect the moment
anything is deployed, because a deployed HTTP endpoint carrying a bearer token is a credential
broadcast.

Key custody: application keys through `ISecretResolver`; database encryption keys are the platform's
(Azure SQL); backup encryption keys are held **separately from the backups**, because a key stored
with the thing it protects protects nothing.

---

## 7. Personal data

### 7.1 What Nexus will hold

| Category | Where | Sensitivity |
|---|---|---|
| Account identity — name, email | Identity schema (M-01-1.1) | Personal |
| Conversation content | `Conversation`, `ConversationMessage` | **Potentially highly sensitive** |
| Prompt and completion bodies | AI layer traces (M-04-1.1) | **Potentially highly sensitive** |
| Documents and knowledge | DATA layer (M-02-2.1, M-02-3.1) | Whatever the user put in them |
| Audit records | `AuditEntry` | Identifiers and actions |
| Usage and cost records | `UsageRecord` | Identifiers and counts |

Conversation content is the highest-risk data in the system, and it is the data the product is
built around. A user talking to an AI assistant will disclose things they would not put in a form.

### 7.2 Rules

| Rule | Statement |
|---|---|
| Minimise | Do not collect what is not needed |
| Identifiers in logs | Log a `UserId`, never a name or an email address |
| **Never log a prompt body** | §11 — this is an M-10-1.1 acceptance criterion |
| Tenant-scoped | All personal data is tenant-owned and filtered (§4) |
| Deletable | A deletion request must be satisfiable — which requires knowing where it all is |
| Exportable | A subject access request must be satisfiable |
| Classified | **TARGET — M-02-5.1 Classification and retention** |
| Residency | **TARGET — M-03-4.2 Privacy requirements and data residency** |

**Not yet decided:** which privacy regimes Nexus is subject to. This is a GOVERNANCE determination
(M-03-4.1 Compliance obligation catalogue) and depends on where the business operates and who the
users are. Until it is decided, build for deletability and exportability, because retrofitting
either into a system that assumed neither is expensive.

---

## 8. Audit

### 8.1 Two distinct things

| Thing | What | Where |
|---|---|---|
| Row audit | Who last touched this record | `CreatedBy`/`ModifiedBy` — `DATABASE_STANDARDS.md` §6 |
| Audit log | What happened, in order, immutably | `IAuditLog`, `AuditEntry` |

### 8.2 The audit log

**CURRENT.** `IAuditLog` and `AuditEntry` exist in `Nexus.Platform.Contracts/Governance/`. The only
implementation is `ConsoleAuditLog` in `Nexus.Platform.Core/Governance/`. **Audit entries are
written to the console and then lost.**

**TARGET — M-01-4.1 Durable audit log.** Alongside it, `InMemoryUsageMeter` and
`PermissiveQuotaPolicy` are the current implementations of `IUsageMeter` and `IQuotaPolicy`.
`PermissiveQuotaPolicy` permits everything — its name is honest, and it must not be mistaken for a
quota control. Durable metering is **M-01-4.2**.

### 8.3 What must be audited

| Event | Required |
|---|---|
| Authentication success and failure | M-01-1.2 |
| Authorization denial | M-01-3.1 |
| Secret resolution — **not the value** | M-01-5.1 |
| Every cross-tenant operation | §4.4 |
| Permission and role changes | M-01-3.1 |
| A feature flag change | M-10-5.1 |
| Data deletion | Always |
| Model invocation — who, which model, cost, **not the prompt body** | M-04-4.1 |
| Agent and tool invocation with side effects | §9, §10 |

### 8.4 Properties

An audit entry is **append-only, immutable, tenant-attributed and time-stamped in UTC**. It is never
updated and never deleted within its retention period. It carries the correlation id
(`API_STANDARDS.md` §11) so that an audit entry and a log line describe the same request.

An audit log that can be edited by the same principal whose actions it records is a log with an
asterisk. Write access to the audit store is separate from write access to everything else.

---

## 9. AI permissions

AI is not exempt from authorization. It is the part of the system most in need of it, because it
acts on instructions that may have originated with someone other than the user.

| Rule | Statement |
|---|---|
| The AI acts as the user | A turn carries the user's identity and permissions, never elevated ones |
| The AI cannot exceed the user | If the user cannot read a document, no prompt makes it visible |
| **AI never sees product structure** | It receives a `ContextBundle`; `ScopeRef` is opaque to it |
| Context is filtered before assembly | `ContextSelector` and `PromptAssembler` operate on already-permitted content |
| Trust is explicit | `TrustLevel` on `ContextItem` — content pulled from an external source is not trusted content |
| Provenance is kept | `Citation` and `PersistenceHint` record where content came from and whether it may be retained |
| Cost is attributed | `UsageRecord`, `ModelUsage`, `UsageSummary` — M-04-4.1 |
| Quotas are enforced | `IQuotaPolicy` — currently `PermissiveQuotaPolicy`, which enforces nothing |

`TrustLevel` is the defence against prompt injection, and it only works if it is honoured. Content
retrieved from a document, a web source or another user's message is untrusted, and untrusted content
is data to be reasoned about — never instructions to be followed. An agent that treats retrieved text
as an instruction has been compromised by whoever wrote that text.

**Guardrails and output validation are M-04-5.2.** Nothing validates model output today.

---

## 10. Tool permissions

`Nexus.Platform.Contracts/Tools/` defines `IToolCatalog`, `IToolGateway`, `ToolDescriptor`,
`ToolInvocation`, `ToolResult` and — critically — **`SideEffectClass`**.

`SideEffectClass` exists to answer one question before a tool runs: *what can this do that cannot be
undone?*

| Class | Meaning | Requirement |
|---|---|---|
| Read-only | Observes; changes nothing | Permission check |
| Reversible write | Changes state that can be restored | Permission check, audited |
| Irreversible write | Cannot be undone | Permission check, audited, **explicit approval** |
| External effect | Reaches outside Nexus — sends, pays, publishes | Permission check, audited, **explicit human approval** |

| Rule | Statement |
|---|---|
| Registered | A tool is invoked only through `IToolGateway` from `IToolCatalog` — never called directly |
| Declared | Every tool declares its `SideEffectClass` before registration |
| Permission-checked | The invoking principal's permissions are evaluated per invocation |
| Bounded | Every invocation has a timeout and a bounded result size |
| Audited | Every invocation with side effects is audited |
| Approved | Irreversible and external effects require approval — M-05-5.1 Approval gates |

**CURRENT.** `Nexus.Platform.Tools/ToolProvider.cs` is a **231-byte stub**.
`Nexus.Intelligence.Api/Tooling/` contains `EmptyToolCatalog` and `EmptyToolGateway`. **No tool can
be invoked today, which is the only reason the absence of tool permissions is not currently an
exposure.** The registry and invocation path is **M-01-7.1**, and its permission model must land
with it, not after.

---

## 11. Logging restrictions

**Absolute prohibitions. No log line, at any level, in any environment, may contain:**

| Never logged | Includes |
|---|---|
| A secret | API keys, connection strings, signing keys, passwords, client secrets |
| A token | Access tokens, refresh tokens, session identifiers, cookies |
| **A full prompt body** | The prompt sent to a model, and the completion returned |
| Personal data beyond identifiers | Names, email addresses, addresses, phone numbers |
| Document or message content | Any user-authored content |
| A full request or response body | Log the shape and the size, not the payload |

The prompt-body prohibition is an acceptance criterion of **M-10-1.1**: *no log line contains a
secret, a token or a full prompt body.* It exists because a prompt routinely contains the user's
actual content, and the log store has different access controls, a different retention period and a
different audience from the conversation store. Logging prompts silently copies the most sensitive
data in the system into the least protected place.

What to log instead: the correlation id, the principal id, the tenant id, the model name, token
counts, duration in milliseconds, and the outcome. That is enough to diagnose almost anything, and
where it is not, the conversation store holds the content under its own access control.

**A unit test asserting that a known secret pattern is redacted** is a required task of M-10-1.1.
Redaction that is not tested is redaction that has already failed somewhere.

Logging conventions generally are in `API_STANDARDS.md` §12; **this section is authoritative on what
may never be written.**

---

## 12. Worker permissions

A worker — human or agent — operates under a bounded set of permissions. Agent workers are bounded
more tightly, because their failure mode is confident action rather than hesitation.

| Permission | Human worker | Agent worker |
|---|---|---|
| Read the repository | Yes | Yes |
| Commit to its own work branch | Yes | Yes |
| Push its own work branch | Yes | Yes |
| Force-push its own work branch | With `--force-with-lease` | With `--force-with-lease` |
| Push to `main` or an integration branch | **No** | **No** |
| Approve a review | Yes — never its own | **No** |
| Merge to an integration branch | Yes, after review | **No** |
| Change branch protection or CI configuration | Only with explicit authority | **No** |
| Read a production secret | **No** | **No** |
| Read a development secret | Through `ISecretResolver` | Through `ISecretResolver` |
| Access a deployed environment | Per environment (§13) | **No** |
| Create, modify or waive a safety-critical criterion | Named authority only | **Never** — no exception |

A worker operates **only within its work item's declared scope**
(`DEVELOPMENT_WORKFLOW.md` §6). A change outside the declared projects, schemas or contracts is a
scope violation caught at review — and for an agent worker, it is a signal to stop rather than to
widen the scope.

An agent never approves work, never merges, and never modifies the controls that constrain it. Those
three rules together are what make it safe to run several agents at once.

---

## 13. Repository, environment and deployment permissions

### 13.1 Repository

| Action | Who | State |
|---|---|---|
| Read | Repository members | **CURRENT** — private repositories under `prtcare` |
| Push to a work branch | Any member | **CURRENT** |
| Push to `main` | **Nobody** | **TARGET — M-08-1.4.** Currently possible in all three repositories |
| Merge to `main` | Via reviewed PR with a green build | **TARGET — M-08-1.4** |
| Change branch protection | Repository admin only | **TARGET** |
| Change CI configuration | Repository admin, reviewed | **TARGET** — no CI exists |
| Force-push `main` | **Nobody, ever** | **TARGET — M-08-1.4** |

### 13.2 Environments

**No environments exist.** No environment definitions, no IaC, no deployment pipeline exists
anywhere. The model below applies from **M-08-4.1 Environment model**:

| Environment | Data | Access | Secrets |
|---|---|---|---|
| Development | Synthetic only | The developer | Local fallback |
| Test | Synthetic only | Developers and CI | Environment-scoped |
| Staging | Anonymised or synthetic | Named individuals | Environment-scoped |
| Production | Real | Named individuals, audited, least privilege | Vault only |

**Production data is never copied to a lower environment.** Not for debugging, not once, not
anonymised-in-a-hurry. Where production-like data is needed, it is generated. This rule is written
now, before any environment exists, because it is the rule most often broken the first time
something is hard to reproduce.

### 13.3 Deployment

| Rule | Statement | State |
|---|---|---|
| Automated | Deployment is a pipeline, not a person with a publish profile | **TARGET — M-08-5.1** |
| From a qualified artefact | The artefact is built once and promoted, never rebuilt per environment | **TARGET — M-08-3.1, M-08-5.2** |
| Approved | Production deployment requires an approval | **TARGET — M-05-5.1** |
| Audited | Who deployed what, where, when | **TARGET** |
| Reversible | A rollback path exists and has been exercised | **TARGET — M-10-7.1** |
| Credentials | Deployment credentials are environment-scoped and vault-held | **TARGET — M-01-5.1** |

---

## 14. Dependency and supply chain security

### 14.1 Current exposure

| Item | State |
|---|---|
| Package feed | `C:\Personal\LocalNuGet` — a local file feed, **unreachable from any build agent** |
| Feed migration | **TARGET — M-08-1.1** GitHub Packages |
| Vulnerability scanning | **None** |
| Dependency pinning | `Directory.Build.props` + `global.json` per repository |
| Lock files | Not in use |
| Secret scanning | **None** — TARGET, M-01-5.1 |
| SBOM | **None** |

`pack-local.ps1` publishes to the local feed in Nexus.Platform and Nexus.Intelligence. The feed is a directory. It
has no integrity checking, no provenance and no access control beyond the filesystem. It is
adequate for one machine and is a blocker for CI, which is why M-08-1.1 is P0 with no dependencies —
nothing else in DELIVERY can proceed while packages resolve only from a path that exists on one
computer.

### 14.2 Dataverse removal is a supply chain improvement

**TRANSITION — ADR-014 Stage 3, M-02-1.4.** Still present and being removed:
`Microsoft.PowerPlatform.Dataverse.Client`, `Microsoft.Xrm.Sdk`, `Microsoft.Crm.Sdk.Proxy`, and a
`System.Security.Cryptography.Xml` pin. Roughly **7.2 MB** of dependency surface that will no longer
need patching, scanning or trusting.

The `System.Security.Cryptography.Xml` pin deserves a note: a pinned cryptography package is a pin
that must be re-examined whenever it is advisory-affected. Removing the reason for the pin removes
the obligation.

**`Azure.Identity` and `Azure.Core` are present but arrived with Dataverse.** They have not been
chosen. Before relying on them for key vault access at M-01-5.1, confirm they are wanted rather than
merely present — a dependency that survives a removal by accident is a dependency nobody owns.

### 14.3 Rules

| Rule | Statement |
|---|---|
| Explicit versions | No floating version ranges, no wildcards |
| Central management | Versions in `Directory.Build.props`, not scattered across `.csproj` files |
| Reviewed additions | A new dependency is a reviewed decision, not an incidental commit |
| Trusted sources | Only NuGet.org and the Nexus feed. Never an arbitrary URL |
| Removed when unused | An unused dependency is attack surface for no benefit |
| Scanned | **TARGET** — vulnerability scanning in CI, once CI exists |

---

## 15. Backup security and recovery

### 15.1 Source

The 2026-08-20 incident — all three repositories losing `.git\objects` simultaneously, consistent
with antivirus quarantine of extensionless zlib blobs — is documented in full in
`GIT_WORKFLOW.md` §2. Its security-relevant residue:

| Item | State |
|---|---|
| **The antivirus exclusion for `C:\Personal` has never been confirmed** | **Open.** The cause is still live |
| `.git-broken\` remains in all three repositories | Open — removal is gated on ref comparison |
| No documented, tested backup of any repository | Open |
| The only copy is `origin` on GitHub | A remote is not a backup |

This is an open nonconformance (`ASSURANCE_STANDARDS.md` §11.2), and its corrective action is
**M-08-2.1 Close the 2026-08-20 recovery**.

### 15.2 Backup security rules

| Rule | Statement |
|---|---|
| Encrypted at rest | Always |
| Keys stored separately | A key stored with the backup protects nothing |
| Access-controlled | Backup access is a distinct permission, separately audited |
| Independent | Not on the machine being protected; not solely on the same provider |
| Retention defined | Per data classification — **TARGET, M-02-5.1** |
| **Tested** | An untested backup is a hypothesis — **M-08-7.2 Tested restore** |
| Personal data included | A backup of personal data carries the same obligations as the live data |

That last row is the one most often missed: a deletion request that clears the live database and
leaves the data in six months of backups has not been satisfied. Retention policy and deletion
policy must be designed together, which is why both belong to M-02-5.1.

### 15.3 Recovery

Recovery drills are **M-10-7.1**. Nothing has ever been recovery-tested except the git object
recovery of 2026-08-20 — which was not a drill, was performed under pressure, and worked.

**Preserve the damaged state before attempting recovery.** Rename, never delete. `.git-broken` is
the reason the 2026-08-20 recovery could confirm what had been lost.

---

## 16. Where Nexus is most exposed today

Ranked by consequence, not by effort.

| # | Exposure | Closed by |
|---|---|---|
| 1 | **No authentication, no authorization, and the only access control leaves with Dataverse** | M-01-1.2, M-01-2.1, M-01-3.1, gated against M-02-1.4 |
| 2 | Secrets managed by a PowerShell script; no scanning | M-01-5.1 |
| 3 | The antivirus cause of the 2026-08-20 loss is unconfirmed and still live | M-08-2.1 |
| 4 | No backup of any repository | M-08-2.1, M-08-7.1, M-08-7.2 |
| 5 | `main` is pushable directly in all three repositories | M-08-1.4 |
| 6 | Audit entries go to the console and are lost | M-01-4.1 |
| 7 | No correlation id — a security event cannot be traced across hosts | M-10-1.1 |
| 8 | No vulnerability scanning of any dependency | After M-08-1.2 |
| 9 | `PermissiveQuotaPolicy` enforces nothing; model spend is unbounded | M-01-4.2, M-04-4.1 |

The sequencing constraint that matters most: **M-02-1.4 must not land far ahead of M-01-2.1.**
Removing Dataverse removes the only access control that has ever existed. If identity is not ready,
the window between them is a system with real data and no gate on it.

---

## 17. References

- `API_STANDARDS.md` — `401`/`403`/`404` behaviour, Problem Details, rate limits, logging conventions.
- `DATABASE_STANDARDS.md` — audit columns, tenant column indexing, soft delete, encryption at rest.
- `CONFIGURATION_STANDARDS.md` — secret references in configuration; what must never enter git.
- `ASSURANCE_STANDARDS.md` — the security tests, the cross-tenant denial test, nonconformance.
- `GIT_WORKFLOW.md` — the 2026-08-20 incident in full, secrets in history, repository permissions.
- `DEVELOPMENT_WORKFLOW.md` — work item scope, which bounds what a worker may touch.
