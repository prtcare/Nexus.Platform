# VISION.md

## Why NexusAI Exists

NexusAI is a self-owned AI orchestration platform, built to be the operational backbone of a single-developer business that runs on Microsoft's stack — Visual Studio, Dataverse, Power Apps, and Power Automate.

It did not start as "a chatbot wrapper." It started from a specific frustration: general-purpose AI chat tools forget everything between sessions, hit context limits mid-task, lock you into one vendor, and have no durable memory of decisions already made. NexusAI exists to remove those limits by owning the memory, the orchestration, and the data layer — while treating the underlying language model (OpenAI, Claude, or others) as a swappable component rather than the product itself.

## The Core Idea

NexusAI is an **agent orchestration platform**, not a single assistant. It is built around four durable capabilities:

1. **Provider-agnostic reasoning** — any LLM (OpenAI today, Claude next) can sit behind the same `ILLMProvider` contract. Switching providers should never mean losing memory or starting over.
2. **Structured, permanent memory** — conversations live inside a `Workspace → Project → Conversation → Chat` hierarchy, not as a single flat thread. Memory is a first-class citizen, not a side effect of a chat log.
3. **Multi-agent execution** — specialized agents (a developer agent, a Dataverse "compiler" agent, a CNC machine retrofit agent, and more over time) do real work: writing code, generating Dataverse schemas, producing configuration files — not just answering questions.
4. **A real backend, not a toy store** — Microsoft Dataverse is the system of record, chosen deliberately for its place in the Power Platform ecosystem: security roles, Power Automate triggers, Power Apps surfaces, and enterprise-grade governance, all without standing up separate infrastructure.

## Who This Is For

Right now: one person — a single developer building this platform to run their own business. NexusAI's first users are the author's own workflows: software development, Dataverse-backed business systems, and physical machine automation (CNC retrofits). The architecture is deliberately generalized (a registry of pluggable agents, a provider-agnostic reasoning layer) so that it can grow into a broader personal or small-team "AI operating system" without being rebuilt from scratch.

## The Three-Layer Ambition

**Layer 1 — The Brain (NexusAI Core).**
A platform that holds conversation, memory, and reasoning independent of any single AI vendor. This is what Phase 1 built the skeleton for and what Phase 2 makes real: real Dataverse persistence, multiple LLM providers, and a memory model that never silently forgets.

**Layer 2 — The Hands (Agents).**
Specialized workers that act, not just chat. The first three planned agents span deliberately different domains to prove the agent framework generalizes:
- A **Developer/Visual Studio agent** that reads, writes, and validates code.
- A **Compiler agent** that turns an Excel-defined data model into real Dataverse tables, solutions, and relationships — with YAML backups to GitHub for version control and restore.
- A **CNC Retrofit agent** that helps convert physical machinery (starting with a vertical boring machine) into CNC-controlled equipment using Mesa cards and stepper motors — proving the same agent framework can reach beyond software into physical automation.

**Layer 3 — The Body (Business Integration).**
Eventually, NexusAI connects to real business data and exposes itself through front ends the author actually uses day to day — a Power Apps chat client and a Visual Studio-built desktop client — with additional agents for business functions (data/reporting, automation triggers, project management) layered on top of the same registry.

## What "Success" Looks Like

- A conversation can run indefinitely without losing context, because memory lives in Dataverse, not in a single model's context window.
- Switching from OpenAI to Claude (or back) mid-project changes nothing about what the system remembers.
- Adding a new agent — a tenth agent, a fiftieth — is a matter of implementing one interface and registering it, not restructuring the platform.
- A milestone, once approved, never silently drifts or gets hallucinated away by a long conversation.
- The same backend serves a phone app, a desktop app, and a Power Apps canvas app without duplicated logic.

## What NexusAI Is Not

- It is not a wrapper that re-sends a growing chat transcript to an API until it breaks. Memory is structured and retrieved, not replayed.
- It is not vendor-locked. The provider is an implementation detail behind `ILLMProvider`.
- It is not a single monolithic "do everything" agent. It is a registry of narrow, well-scoped agents coordinated by a planner.
- It is not (yet) a multi-tenant product. It is being built first as infrastructure for one business, with room to grow.

## Longer-Term Direction

Once the core platform and first three agents are solid, the natural next step is connecting real business data (SharePoint, SQL, other Dataverse environments) into the `Knowledge` layer, and adding business-function agents — reporting, automation triggers into Power Automate, and project/ops tracking — so that NexusAI becomes the coordination layer across the entire Microsoft-centric toolchain the business already runs on, rather than a separate system that has to be kept in sync with it by hand.
