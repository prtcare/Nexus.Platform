# Nexus.AI — the Platform

The backbone between Nexus products and AI providers. It handles model gateways, provider
neutrality, usage metering, quota policy and audit — the *execution* of an AI call, never
the decision to make one.

**Ships as NuGet packages, not as a service.** Nothing here is deployed; the packages are
consumed in-process by `Nexus.Int`.

This repo is also the **documentation hub** for all of Nexus (see below).

## Is / is not

**Is:** provider gateways, model catalogue, usage metering, quota policy, audit log,
identity abstractions, the `Nexus.Platform.Contracts` seam.

**Is not:** it holds no product data and no product schema. Workspaces, projects,
conversations, knowledge and chat all belong to the **Chat product** in `Nexus.Web`, because
every future product will structure its data differently. Platform that knows about a
product's tables is Platform that cannot serve the next product.

It also does not decide anything — routing, ranking, agent and model selection all live in
`Nexus.Int`. The organising rule across all three repos:

> **Intelligence decides. Platform executes. Products own the data and the experience.**

## Local development

```powershell
dotnet build Nexus.AI.slnx
dotnet test  Nexus.AI.slnx
.\pack-local.ps1              # packs to C:\Personal\LocalNuGet
```

Packages are stamped `0.1.0-dev.<timestamp>` and consumers float on `0.1.0-*`. This matters:
NuGet caches by version, so re-packing the same version number is **silently ignored** — if
a consumer isn't picking up your change, check the stamp before debugging anything else.

## Documentation

Cross-cutting architecture, conventions and decisions for all three repos:

- **`docs\`** — the canonical numbered set (00–12). Start at `docs\README.md`.
- **`docs\00_DOCUMENTATION_STANDARD.md`** — where docs live, how they're numbered, who owns
  which subject, when they get updated. Read this before adding a document.
- **`docs\07_DEVELOPMENT_GUIDE.md`** — coding standards, naming conventions, the
  vertical-slice workflow, the review checklist.
- **`docs\08_DECISIONS_AND_TECHNICAL_DEBT.md`** — the ADR log (one global sequence across
  all repos) and current debt.

`NexusAI Documentation\Phase 1…` and `Phase 2\` are historical snapshots. They are kept for
provenance and are **not** current — do not consult them for how the system works today.

## Related repositories

| Repo | Is | Deployed as |
|---|---|---|
| `C:\Personal\NexusAI` | Platform (this repo) | NuGet packages |
| `C:\Personal\Nexus.Int` | Intelligence — the deciding layer | `/intelligence/v1` |
| `C:\Personal\Nexus.Web` | Chat product — React + .NET | `/api/v1` |
