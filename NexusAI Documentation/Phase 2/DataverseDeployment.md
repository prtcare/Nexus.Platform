\# NexusAI Dataverse Deployment



\## Deployment Flow



Development

&#x20;   ↓

Managed Solution

&#x20;   ↓

Testing

&#x20;   ↓

Managed Solution

&#x20;   ↓

Production



\---



\# Development



The Development Dataverse environment is the authoring environment.



Create and validate NexusAI schema components here.



Components include:



\- Workspace

\- WorkspaceMember

\- Team

\- TeamMember

\- Project

\- ProjectMember

\- ProjectBrief

\- ProjectMilestone

\- MilestoneCriterion

\- Conversation

\- ConversationMessage

\- ConversationSummary

\- ConversationLink

\- Session

\- Branch

\- Snapshot

\- Knowledge

\- ADR

\- WorkItem

\- Artifact

\- AccessGrant



\---



\# Testing



Import the validated solution into Testing.



Validate:



\- Schema

\- Relationships

\- CRUD operations

\- Repository operations

\- Conversation hierarchy

\- Knowledge retrieval

\- AI context

\- Permissions

\- Sharing

\- Milestones



\---



\# Production



Import only the validated solution.



Production deployment requires:



\- Successful Development validation

\- Successful Testing validation

\- Approval for Production deployment



\---



\# Solution Components



The NexusAI solution should contain the Dataverse components required

by the NexusAI application.



The logical names defined in `DataverseSchema.md` are the application

contract.



\---



\# Important Rule



Do not manually create production-only schema changes.



Schema changes must originate in Development and be promoted through

the deployment path.


=update====

# NexusAI Dataverse Deployment

## Current Development Environment

PRT (Dev)

## Current Solution

N_001_Nexus

## Current Version

1.0.0.0

## Publisher

NexusAI

## Publisher Prefix

du_

---

# Deployment Flow

PRT (Dev)
    ↓
Testing
    ↓
Production

---

# Development

PRT (Dev) is the source environment for Dataverse schema development.

Schema components are created here first.

---

# Testing

The validated NexusAI solution will be promoted to the Testing
environment.

Testing validates:

- Dataverse schema
- Relationships
- Repository integration
- CRUD operations
- AI memory
- Knowledge retrieval
- Conversation hierarchy
- Sharing
- Security
- Milestones

---

# Production

Production receives only validated and approved solutions.

Production schema is never the development authoring environment.

