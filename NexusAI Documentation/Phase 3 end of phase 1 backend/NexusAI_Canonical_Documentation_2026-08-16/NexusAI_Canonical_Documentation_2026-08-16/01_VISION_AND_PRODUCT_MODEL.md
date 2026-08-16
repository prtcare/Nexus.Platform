# Vision and Product Model

## Why NexusAI exists

Ordinary AI chat is transient and flat. Valuable context, decisions, failed attempts, outputs, and next actions are buried in transcripts. NexusAI turns that activity into durable, structured working memory.

Its purpose is not merely to answer prompts. It should understand where work belongs, retain approved knowledge, connect discussions to outcomes, and help people and agents continue work without repeatedly reconstructing context.

## Product model

NexusAI is organized into three conceptual layers:

1. **Nexus Platform** — identity, workspaces, projects, conversations, persistence, APIs, permissions, providers, and operational infrastructure.
2. **Nexus Intelligence** — context assembly, knowledge retrieval, memory, planning, agent selection, evaluation, result/outcome learning, and model routing.
3. **Nexus Products** — chatbot, Vault, ERP experiences, developer tools, Power Platform tools, machine automation, and other applications using the platform and intelligence APIs.

Products must remain separately deployable. Vault or an ERP is not a module hidden inside the chatbot. Each product owns its user experience and product-specific rules while consuming Nexus capabilities through stable contracts.

## Core information hierarchy

The intended user model is:

`Workspace → Project → Milestone → Conversation / Work Item → Artifact`

- A **Workspace** separates a major context such as PRT Engine, PRT Transport, Personal, or Trips.
- A **Project** is a bounded outcome within a workspace.
- A **Milestone** groups evolving sub-goals and completion criteria.
- A **Conversation** holds the main discussion and may contain or link branches/sub-conversations.
- A **Work Item** is executable work derived from a project or discussion.
- An **Artifact** is a reusable output such as code, a document, design, plan, or configuration.
- **Knowledge** stores trusted reusable facts and instructions.
- An **ADR** stores an explicit architecture or product decision.
- **Sessions, Branches, and Snapshots** preserve execution and conversational history.

## Intelligence principles

- Context should be assembled from the current workspace, project, milestone, conversation, approved knowledge, decisions, and relevant results.
- Knowledge is not simply “the web.” It is private, scoped, curated, traceable information that the system may trust differently from public sources.
- Agents are differentiated by tools, permissions, workflows, evaluation rules, context selection, and outcome history—not only by a different prompt.
- Important milestone content and architectural decisions change only through explicit user approval.
- The system should record results and outcomes so it can distinguish advice that sounded correct from actions that actually worked.
- Model providers must remain replaceable; the product should not be architecturally tied to one LLM vendor.

## Target users

- A single person managing long-running personal and technical work.
- Teams that need structured AI memory across projects.
- Businesses that want AI connected to their operational systems.
- Developers using specialized agents for Visual Studio, Power Platform, Dataverse, and deployment.
- Industrial users connecting AI planning with CNC or machine-automation systems under controlled execution rules.

## Success criteria

NexusAI succeeds when a user can return after days or months and immediately see:

- what the project is trying to achieve;
- which milestone is active;
- what was decided and why;
- which attempts failed and what was learned;
- what knowledge is trusted;
- what work remains;
- which artifacts were produced;
- what the latest real-world result was;
- what the AI recommends doing next.

## What NexusAI is not

- Not a thin ChatGPT wrapper.
- Not a single giant application containing every future product.
- Not a data warehouse used as an operational database.
- Not a system that allows autonomous agents unrestricted access to business or machine operations.
- Not a replacement for source control, deployment pipelines, Dataverse security, or human approvals.
