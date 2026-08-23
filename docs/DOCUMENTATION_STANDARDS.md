# Documentation Standards

**Status:** Active
**Owner:** DATA (Layer 02)
**Last updated:** 2026-08-21
**Layer:** 02 DATA, with DEVELOPER (07) owning structured development state
**Authoritative for:** what documentation exists and why, the division between structured state and
documents, front matter, headings and version metadata, ownership and status, links and structured
references, when documentation is mandatory, when it must be updated, generated versus manually
maintained content, archival, superseding, and the no-duplication rule.

Not authoritative for: file-name **patterns** — `NAMING_STANDARDS.md` §42 owns them; ADR structure,
numbering and status — `ADR_STANDARD.md`; what a code comment should say —
`CODE_CONVENTIONS.md`; what evidence is — `ASSURANCE_STANDARDS.md` §10.

---

## 1. The division: structured state versus documents

This is the organising principle of the whole documentation system, and it is a governing principle
of the roadmap: **structured state belongs to its owning domain; documentation belongs to DATA.**

| Kind | Example | Owner | Form |
|---|---|---|---|
| **Structured development state** | Milestones, features, work items, tasks, dependencies, workers, build records, reviews, progress | **DEVELOPER (07)** | Queryable rows in the `developer` schema |
| **Structured assurance state** | Acceptance criteria, verification runs, evidence, verdicts, defects | **ASSURANCE (09)** | Rows in the `assurance` schema |
| **Documents** | Standards, ADRs, runbooks, specifications, incident records, architecture prose | **DATA (02)** | `Document` + `DocumentVersion` in the `data` schema |

A milestone is **not** a document. It is a row with a phase, an outcome, dependencies and a
`parallel_safe` flag, and it is queried — "what can run now", "what is blocked", "which criteria are
unverified". A specification *describing* that milestone is a document, and the milestone **links**
to it. **M-07-1.1** states it as an acceptance criterion: *a Milestone references a Layer 02
Document by id for its specification.*

Getting this backwards is the failure this division prevents. When structured state is kept in prose,
every question about it becomes a reading exercise, every answer is a snapshot, and nothing can be
validated — a dependency that does not resolve is invisible in a markdown table and is a rejected
import in a work graph.

### 1.1 Which one is a new artefact?

| Question | If yes |
|---|---|
| Will anyone query, filter, sort or aggregate it? | Structured state |
| Does it have a lifecycle with states and transitions? | Structured state |
| Does something else need to reference it by id? | Structured state, with a document for its prose |
| Is it prose a human reads start to finish? | Document |
| Does it explain *why*, rather than record *what*? | Document |

---

## 2. Current reality

| Aspect | State |
|---|---|
| Documents | Markdown files on disk, hand-maintained. **CURRENT** |
| Structured development state | Markdown, YAML, and human memory. **CURRENT** |
| `nexus-roadmap.yaml` | 196 KB of machine-readable structure with no machine to read it. **TRANSITION** |
| Document store | Does not exist. **TARGET — M-02-2.1** |
| Work graph | Does not exist. **TARGET — M-07-1.1** |
| Generated documentation | None, except Swagger from Swashbuckle |

What exists on disk: the numbered canonical set 00–12, `README.md`, `ADR-014_AZURE_SQL_MIGRATION.md`,
`ADR-015_PROJECT_BRIEF.md`, `DATAVERSE_SCHEMA_REFERENCE.md`, `NEXUS_ARCHITECTURE_V2.md`,
`NEXUS_MIGRATION_RUNBOOK.md`, `GIT_RECOVERY_2026-08-20.md`, `MIGRATION_STATE.md`,
`NEXUS_DOCUMENTATION_STANDARD.md`, `09_ROADMAP_AND_MILESTONES.md`, `07_DEVELOPMENT_GUIDE.md`, and at
the repository root `SQL_PROMPTS_STAGE_1B_2A.md`, `SQL_PROMPTS_STAGE_2B_2C.md`,
`FRONTEND_PROMPTS_F0_F4.md`, `DOCS_CONSOLIDATION_PROMPT.md`, plus this standards set.

Two earlier meta-documents — `NEXUS_DOCUMENTATION_STANDARD.md` and `00_DOCUMENTATION_STANDARD.md` —
cover the same ground as this file. **This document is authoritative where they overlap.** They are
not deleted: they record how the numbered set came to be, and one of them still carries open
questions its author marked as unconfirmed. Reconciling them is **T-02-2.1.2.2 — establish which
file remains authoritative during transition.**

---

## 3. Document categories

`NAMING_STANDARDS.md` §42 owns the file-name pattern for each. What each category *is*:

| Category | Purpose | Rewritten? |
|---|---|---|
| **Standard** | A binding rule set for how something is built | Yes — a standard is a living document |
| **Reference** | A description of what exists | Yes — kept current or archived |
| **ADR** | One decision and its reasoning — `ADR_STANDARD.md` | **Never.** Superseded, not edited |
| **Runbook** | An executable procedure a human follows under pressure | Yes — and verified by execution |
| **Incident record** | What happened on a specific date | **Never.** It is a historical record |
| **State snapshot** | Where something stood at a point in time | **Never.** Superseded by a later snapshot |
| **Prompt / working file** | Instructions for a specific piece of work | Not maintained — archived when the work completes |
| **Machine-readable structure** | Data with a schema, not prose | Replaced by its owning domain — §8 |

The distinction that matters most: **a standard is maintained; a record is not.**
`GIT_RECOVERY_2026-08-20.md` describes what happened on 2026-08-20 and is wrong to update; the rules
that incident produced live in `GIT_WORKFLOW.md` §2 and are right to update.

---

## 4. Front matter

Every document begins with these fields, before any prose:

```markdown
# <Title>

**Status:** Active
**Owner:** <LAYER (Layer nn)> or <a named person>
**Last updated:** <yyyy-MM-dd>
**Layer:** <nn NAME> — where relevant
**Authoritative for:** <the subjects this document owns, listed>

Not authoritative for: <subject> — `OTHER_DOCUMENT.md`; <subject> — `OTHER_DOCUMENT.md`.
```

| Field | Rule |
|---|---|
| `Status` | `Draft`, `Active`, `Deprecated` or `Superseded`. §5 |
| `Owner` | A layer for a standard, a named person for a decision or a runbook. **Never "the team"** |
| `Last updated` | ISO `yyyy-MM-dd`, set when the content changes — not when the file is touched |
| `Layer` | Present where the document belongs to a layer |
| `Authoritative for` | An explicit list of subjects. This is the field that makes §7 enforceable |

**`Authoritative for` is the most important field in this standard.** It converts "don't duplicate"
from advice into a check anyone can perform: if two documents claim the same subject, one of them is
wrong, and it is visible from the front matter without reading either body. The paired *Not
authoritative for* line is equally load-bearing — it tells a reader where to go next instead of
letting them assume the document is silent because the subject does not matter.

### 4.1 Version metadata

**CURRENT:** `Last updated` plus git history. That is the whole versioning mechanism today, and it is
adequate for markdown in a repository.

**TARGET — M-02-2.1 Document with versioning.** A `Document` points at its current
`DocumentVersion`; **versions are immutable** and prior versions are retrievable. At that point
`Last updated` becomes derived rather than hand-maintained, and the question "what did this standard
say when that decision was made" becomes answerable — which is what makes a document usable as
assurance evidence.

Documents do not carry a semantic version number. A document is not a package;
`STACK_VERSION_POLICY.md` owns versioning for things that are.

---

## 5. Status

| Status | Meaning | Consequence |
|---|---|---|
| **Draft** | Being written. Not binding | Nothing may cite it as a rule |
| **Active** | Current and binding | Code contradicting an Active standard is a defect |
| **Deprecated** | Still describes the current system, but on a stated path out | Cite the replacement and the milestone |
| **Superseded** | Replaced. Kept for the record | Carries `Superseded by:` and is never edited again |

A document is **superseded, never deleted** — the same rule ADR-014 states for ADRs. Deleting a
document removes the only record of why the current shape exists and guarantees the reasoning is
reconstructed from scratch, badly.

**Archival:** a superseded or obsolete document moves to an `archive/` folder in the same location,
keeps its filename, and its front matter carries `Status: Superseded` and `Superseded by:`. It is
never edited after archival. **TARGET — M-02-2.1:** archival becomes a status on the `Document`
record rather than a folder move, and the document stays retrievable by id, so existing links do not
break.

---

## 6. Headings, structure and style

| Rule | Statement |
|---|---|
| One `#` title, matching the subject | The filename and the title agree |
| Numbered `##` sections | So `API_STANDARDS.md §7` is a citable address. Unnumbered sections cannot be cited precisely |
| Section numbers are stable | Renumbering breaks every cross-reference silently. Add `§7.4`, do not renumber 7 to 8 |
| `###` for subsections; `####` is a smell | Four levels usually means two documents |
| Tables for rules | A rule per row, with its reason. Nexus standards are read as reference, not as narrative |
| Fenced code with a language | `csharp`, `typescript`, `sql`, `json`, `powershell`, `markdown` |
| Line length around 100 characters | Reviewable diffs; a reflowed paragraph is an unreadable diff |
| Present tense, active voice | "An endpoint returns Problem Details", not "should return" |
| **Real Nexus examples only** | `Nexus.Products.Chat.Domain.Workspace`, `TurnPipeline`, `WKS-00000001`. Never `Foo`, `Bar` or `MyService` |

### 6.1 CURRENT / TARGET / TRANSITION

**Every statement that differs from what builds today is marked, and the marking names the milestone
that closes the gap.**

| Marker | Meaning |
|---|---|
| **CURRENT** | True of the code today. A developer can rely on it now |
| **TARGET** | Not built. Names the milestone that builds it |
| **TRANSITION** | Two states exist at once, with a stated rule for which applies when |

This is not decoration. Most of Nexus is unbuilt: there is no CI, no identity, no logging library, no
correlation id, and exactly two behaviour tests in the entire system. **A developer must never read
a target standard and be unable to build the current code.** An unmarked target reads as a
description of reality, and the developer who trusts it wastes a day looking for something that was
never written.

TRANSITION is the marker most often needed and least often used. `nexus-roadmap.yaml` is one (§8);
the ADR file versus the `Adr` aggregate is another (`ADR_STANDARD.md` §11); Dataverse implementations
still present for ten aggregates while `Workspace` is on SQL is a third.

---

## 7. The no-duplication rule

**One subject, one document. If another document owns a subject, link to it by filename and stop.**

| Rule | Statement |
|---|---|
| Link, do not restate | `See `DATABASE_STANDARDS.md` §3` — not a summary of §3 |
| No "for convenience" copies | The copy is right on the day it is written and wrong shortly after |
| A short pointer is complete | "The cascade rule under SQL Server error 1785 is `DATABASE_STANDARDS.md` §5.3" is a finished sentence |
| Cite by **filename and section number** | Not by page, not by heading text, not by a URL that will move |
| Repeat only what changes meaning by omission | A prohibition may be restated in the place it is most often broken — and it names its owner |

**The one sanctioned exception**, applied narrowly: *no log line contains a secret, a token or a full
prompt body* appears in `SECURITY_STANDARDS.md` §11 (which owns it), `API_STANDARDS.md` §12,
`CODE_CONVENTIONS.md` §11, `TYPESCRIPT_REACT_STANDARDS.md` §17 and `OBSERVABILITY_STANDARDS.md` §9 —
because it is broken at each of those five places, and a pointer at the moment of temptation is
weaker than the rule itself. Every restatement names `SECURITY_STANDARDS.md` §11 as authoritative.

That exception is for **prohibitions at the point of temptation**, not for explanations. It does not
license restating a mechanism.

---

## 8. `nexus-roadmap.yaml` — the transition artefact

`nexus-roadmap.yaml` (v2.1, generated 2026-08-21) holds the entire work graph: 12 layers, their
features, and milestones decomposed to work item, task and subtask, each with `phase`, `outcome`,
`depends_on`, `parallel_safe`, `data_introduced`, `acceptance`, `schema_conflict_group` and a scope
of projects, schemas and contracts.

**It is structured state living in a document, and it is the clearest example of why the §1 division
exists.** Everything it holds is queryable in principle and unqueryable in practice: "which
milestones can run now", "which work items conflict on `schema:developer`", "which `depends_on` id
resolves to nothing" are all one query away — from a database nobody has yet built.

**TRANSITION.** The rule for today:

| Question | Answer |
|---|---|
| What is authoritative for milestone structure today? | `nexus-roadmap.yaml` |
| Where do new milestones go today? | Into the YAML, in the same shape |
| May a document restate a milestone's acceptance criteria? | **Quote** them, attributed. Never paraphrase — a paraphrased criterion is a second, weaker criterion |
| What is authoritative after M-07-1.1? | The `developer` schema |

### 8.1 The import

**WI-07-1.1.3 Roadmap import**, task **T-07-1.1.3.1 Import `nexus-roadmap.yaml` into the work
graph**, with three subtasks that are worth reading as a specification of what a good import is:

| Subtask | Requirement | Why |
|---|---|---|
| S-07-1.1.3.1.1 | **Idempotent** — a re-import updates rather than duplicates | The YAML will be re-imported many times during the transition; a non-idempotent import makes the first run the only safe one |
| S-07-1.1.3.1.2 | Import this roadmap and **confirm node counts match the source** | A silent partial import is worse than a failed one |
| S-07-1.1.3.1.3 | **Report any dependency id that does not resolve** | This is the check no markdown table can perform, and the single strongest argument for the migration |

The import depends on **M-02-2.1** (documents), **M-03-1.1** (a `Product` record to hang
`ProductDevelopment` from) and **M-06-1.1** (scope primitives), because a work graph with no product
identity and no document to link a specification to is a hierarchy of orphans.

### 8.2 After the import

| Concern | Authority |
|---|---|
| Milestones, features, work items, tasks, subtasks, dependencies, scope declarations | **DEVELOPER**, `developer` schema |
| Specifications, standards, ADRs, runbooks, references | **DATA**, `data` schema |
| The link between them | A `Milestone` references a Layer 02 `Document` by id (M-07-1.1 acceptance criterion) |
| `nexus-roadmap.yaml` itself | An **export format and an import source**, not a source of truth |

The YAML does not disappear. It stops being authoritative. A file that can be regenerated from the
graph is useful — for review, for diffing, for handing to an agent — and dangerous only while
someone might edit it and expect the edit to count. **The moment the import is proven,
`nexus-roadmap.yaml` is marked `Superseded by: the developer schema` in its own header.**

---

## 9. Generated versus manually maintained

| Kind | Rule |
|---|---|
| **Generated** | Never edited by hand. Carries a header saying what generated it and when |
| **Manual** | Edited by people. Never overwritten by a generator |
| **Hybrid** | Forbidden. A file with generated and hand-written regions loses the hand-written regions |

**CURRENT:** the only generated documentation in Nexus is the Swagger/OpenAPI surface produced by
Swashbuckle. `nexus-roadmap.yaml` carries `generated: '2026-08-21'` in its metadata but is
maintained by hand — a hybrid, and a live example of why the category is uncomfortable.

**TARGET.** Generated from structured state once it exists: the milestone and progress views
(**M-07-5.2 Derived progress**), the traceability and gap reports (**M-09-1.1**), the API reference
(from OpenAPI), and the schema reference (from EF Core migrations).

The rule that follows: **do not hand-write what will be generated.** A hand-maintained milestone
list is work that must be done twice and then reconciled. Where a hand-written document must exist
before its generator does, mark it **TRANSITION** and name the milestone that replaces it — as
`09_ROADMAP_AND_MILESTONES.md` and `MIGRATION_STATE.md` both are.

---

## 10. When documentation is mandatory

| Situation | Required |
|---|---|
| A decision expensive to reverse, or constraining another layer | An **ADR** — `ADR_STANDARD.md` §2 |
| A new layer, schema, contract or repository | A section in the owning standard |
| A convention others must follow | A standard, or a section of one |
| A procedure a human executes under pressure | A **runbook** |
| An incident | An **incident record**, dated, written while it is fresh |
| A new public API surface | `API_STANDARDS.md` conformance plus generated OpenAPI |
| A new persisted entity | The checklist in `DATABASE_STANDARDS.md` §12 |
| A deviation from a standard | A **Deviation** record — `ASSURANCE_STANDARDS.md` §11.3, not a comment |
| A routine work item | **None.** Its record is the work item, its commits and its evidence |

The last row matters as much as the others. Documentation that nobody needed is not free: it is read,
trusted, and eventually wrong.

---

## 11. When documentation must be updated

**A standard is updated in the same change that makes it false, not afterwards.**

| Trigger | Update |
|---|---|
| A technology is added or removed | `TECHNOLOGY_STACK.md`, plus an ADR |
| A convention changes | The owning standard, and its `Last updated` |
| A **TARGET** becomes real | The marker changes to CURRENT — **in the work item that made it real** |
| A milestone completes | The structured state; and any document that marked it as TARGET |
| A decision is replaced | A new ADR; the old one becomes `Superseded` |
| An incident occurs | An incident record, and the rule it produces in the owning standard |
| A document's owner changes | Its front matter |

**The stale-TARGET problem is the specific decay mode of this document set.** Nexus documentation is
heavily marked TARGET because most of Nexus is unbuilt; every milestone that completes therefore
falsifies several documents at once, in a direction that is invisible — a TARGET that has quietly
become CURRENT reads as perfectly correct.

The control: **a milestone's definition of done includes updating the documents that marked it as a
target** — `DEFINITION_OF_DONE.md` §5. It is a documentation rule enforced from the development
side, because that is the only side that knows the milestone finished.

---

## 12. Links and structured references

| Rule | Statement |
|---|---|
| Cite a sibling document by filename and section | `` `API_STANDARDS.md` §7 `` |
| Never link to a chat transcript, an issue, or a PR as authority | They are not durable and not readable by everyone |
| Cite code by its real path and type name | `Nexus.Intelligence.Core/Turns/TurnPipeline` |
| Cite a milestone by id **and name** | `M-10-1.1 Correlation across hosts` — an id alone is unreadable, a name alone is unfindable |
| Quote an acceptance criterion; never paraphrase it | A paraphrase is a second, weaker criterion |
| No absolute filesystem paths as links | `C:\Personal\…` is true on one machine |

**TARGET — M-02-2.1.** `DocumentLink` links a document to any layer entity by **layer id, entity type
and entity id, with no foreign key** (S-02-2.1.1.2.1) — the same polymorphic pattern ASSURANCE uses
to link a criterion to a DEVELOPER node. Cross-schema foreign keys are forbidden by
`DATABASE_STANDARDS.md` §5.4, and this is why: a document must be able to reference a milestone, a
work item, a product, an aggregate or a decision without the `data` schema knowing any of their
tables exist.

---

## 13. Where this document set is weakest

| Weakness | Consequence | Closed by |
|---|---|---|
| Three meta-documents describe documentation | The rule about one subject per document is broken by the document that states it | T-02-2.1.2.2 |
| Structured state lives in YAML and markdown | No query, no validation, no dependency check | M-07-1.1 |
| `Last updated` is hand-maintained | It will be wrong, and its wrongness is invisible | M-02-2.1 |
| Nothing verifies a cross-reference | A cited section can be renumbered or deleted silently | Not yet planned |
| Nothing verifies a CURRENT claim | A CURRENT statement can be falsified by a commit | M-07-4.1 makes it detectable; nothing checks it |
| Documentation is not in the quality gate | Nothing blocks a merge that falsifies a standard | M-09-1.3, via `DEFINITION_OF_DONE.md` |

---

## 14. References

- `NAMING_STANDARDS.md` §42 — document, ADR, runbook and script file-name patterns.
- `ADR_STANDARD.md` — ADR numbering, status, sections and template.
- `DEFINITION_OF_DONE.md` §5 — documentation as a completion condition.
- `ASSURANCE_STANDARDS.md` §§10, 11.3 — evidence, and deviations as records rather than comments.
- `DEVELOPMENT_WORKFLOW.md` §§1, 2 — the work graph this document defers structured state to.
- `DATABASE_STANDARDS.md` §§5.4, 12 — cross-schema references and the new-entity checklist.
- `STACK_VERSION_POLICY.md` — versioning for things that are packages rather than documents.
