\# NexusAI Dataverse Schema



\## Version



Phase 2

Schema Version: 2.0



\---



\# 1. Purpose



Dataverse is the operational persistence layer for NexusAI.



It stores:



\- Workspaces

\- Projects

\- Conversations

\- Conversation messages

\- Conversation summaries

\- Conversation relationships

\- Sessions

\- Branches

\- Snapshots

\- Knowledge

\- Architecture decisions

\- Work items

\- Artifacts

\- Project briefs

\- Project milestones

\- Collaboration

\- Access control



Dataverse is NexusAI's operational source of truth.



The Data Warehouse is a separate analytical system and must not be used as the operational application database.



\---



\# 2. Design Principles



\## Workspace



Workspace is the highest-level NexusAI organizational boundary.



\## Project



A Project is an optional engineering container inside a Workspace.



\## Conversation



Conversation is the unified chat aggregate.



Standalone conversations, main project chats and sub-chats use the same Conversation table.



\## Knowledge



Knowledge represents durable reusable information.



Knowledge is normally Workspace scoped and may optionally be Project scoped.



\## Project Brief



Project Brief represents the current known state of a project.



\## Milestone



Milestones are optional project planning and progress entities.



\## ADR



ADR represents an accepted or proposed architectural decision.



\## Conversation Summary



Conversation Summary is the compressed representation of a conversation.



\## Conversation Link



Conversation Link represents relationships between conversations.



\## Access Grant



Access Grant controls resource-level sharing.



\## Memory



NexusAI does not use a generic Memory table as the primary persistent-memory model.



Persistent memory is represented through structured domain entities.



\---



\# 3. Entity Inventory



\## Organization



\- Workspace

\- WorkspaceMember

\- Team

\- TeamMember



\## Project



\- Project

\- ProjectMember

\- ProjectBrief

\- ProjectMilestone

\- MilestoneCriterion



\## Conversation



\- Conversation

\- ConversationMessage

\- ConversationSummary

\- ConversationLink

\- Session

\- Branch

\- Snapshot



\## Knowledge



\- Knowledge

\- ADR



\## Engineering



\- WorkItem

\- Artifact



\## Security



\- AccessGrant



\---



\# 4. Workspace



Logical Name:



`nexus\_workspace`



Primary Key:



`nexus\_workspaceid`



Fields:



| Field | Type | Required | Description |

|---|---|---:|---|

| WorkspaceId | Unique Identifier | Yes | Primary key |

| Name | Text | Yes | Workspace name |

| Description | Multiline Text | No | Workspace description |

| Status | Choice | Yes | Workspace status |

| OwnerId | User/Owner | Yes | Workspace owner |

| CreatedOn | DateTime | Yes | Creation timestamp |

| ModifiedOn | DateTime | Yes | Last modification timestamp |



Status:



\- Active

\- Archived

\- Suspended



Relationships:



```text

Workspace

&#x20;├── WorkspaceMember

&#x20;├── Team

&#x20;├── Project

&#x20;├── Conversation

&#x20;├── Knowledge

&#x20;└── AccessGrant


====Update====

# NexusAI Dataverse Schema

## Purpose

This document is the authoritative schema definition for the NexusAI
Dataverse implementation.

It defines:

- Dataverse environment
- Solution
- Publisher
- Naming conventions
- Tables
- Primary columns
- Autonumber conventions
- Columns
- Global Choices
- Lookups
- Relationships
- Alternate Keys
- Views
- Security-related tables

The C# Domain, Application and Infrastructure layers must align with
this schema.

---

# Environment

Development Environment:

PRT (Dev)

The Development environment is the authoring environment for the
NexusAI Dataverse schema.

Testing and Production environments will receive promoted solutions.

---

# Solution

Display Name:

N_001_Nexus

Unique Name:

N_001_Nexus

Version:

1.0.0.0

Publisher:

NexusAI

Publisher Prefix:

du_

---

# Naming Convention

## Solutions

Format:

N_<sequence>_<solution-name>

Example:

N_001_Nexus

## Tables

Format:

T_<sequence>_<table-name>

Example:

T_001_Workspace

## Table Schema Names

Format:

du_T_<sequence>_<table-name>

Example:

du_T_001_Workspace

## Primary Columns

Format:

<TableName>ID

Examples:

WorkspaceID
ProjectID
ConversationID
KnowledgeID

## Views

Format:

V_<sequence>_<table-name>

Example:

V_001_Workspace

## Global Choices

Format:

C_<sequence>_<choice-name>

Example:

C_001_WorkspaceStatus


# Table Registry

| # | Display Name | Schema Name | Primary Column | Autonumber |
|---|---|---|---|---|
| 001 | T_001_Workspace | du_T_001_Workspace | WorkspaceID | WS-{SEQNUM:8} |
| 002 | T_002_WorkspaceMember | du_T_002_WorkspaceMember | WorkspaceMemberID | WSM-{SEQNUM:8} |
| 003 | T_003_Team | du_T_003_Team | TeamID | TEAM-{SEQNUM:8} |
| 004 | T_004_TeamMember | du_T_004_TeamMember | TeamMemberID | TM-{SEQNUM:8} |
| 005 | T_005_Project | du_T_005_Project | ProjectID | PRJ-{SEQNUM:8} |
| 006 | T_006_ProjectMember | du_T_006_ProjectMember | ProjectMemberID | PRJM-{SEQNUM:8} |
| 007 | T_007_ProjectBrief | du_T_007_ProjectBrief | ProjectBriefID | PBR-{SEQNUM:8} |
| 008 | T_008_ProjectMilestone | du_T_008_ProjectMilestone | ProjectMilestoneID | MS-{SEQNUM:8} |
| 009 | T_009_MilestoneCriterion | du_T_009_MilestoneCriterion | MilestoneCriterionID | MSC-{SEQNUM:8} |
| 010 | T_010_Conversation | du_T_010_Conversation | ConversationID | CON-{SEQNUM:8} |
| 011 | T_011_ConversationMessage | du_T_011_ConversationMessage | ConversationMessageID | MSG-{SEQNUM:8} |
| 012 | T_012_ConversationSummary | du_T_012_ConversationSummary | ConversationSummaryID | CS-{SEQNUM:8} |
| 013 | T_013_ConversationLink | du_T_013_ConversationLink | ConversationLinkID | CL-{SEQNUM:8} |
| 014 | T_014_Session | du_T_014_Session | SessionID | SES-{SEQNUM:8} |
| 015 | T_015_Branch | du_T_015_Branch | BranchID | BR-{SEQNUM:8} |
| 016 | T_016_Snapshot | du_T_016_Snapshot | SnapshotID | SNP-{SEQNUM:8} |
| 017 | T_017_Knowledge | du_T_017_Knowledge | KnowledgeID | KN-{SEQNUM:8} |
| 018 | T_018_ADR | du_T_018_ADR | ADRID | ADR-{SEQNUM:8} |
| 019 | T_019_WorkItem | du_T_019_WorkItem | WorkItemID | WI-{SEQNUM:8} |
| 020 | T_020_Artifact | du_T_020_Artifact | ArtifactID | ART-{SEQNUM:8} |
| 021 | T_021_AccessGrant | du_T_021_AccessGrant | AccessGrantID | AG-{SEQNUM:8} |

# Table Registry

| # | Display Name | Schema Name | Primary Column | Autonumber |
|---|---|---|---|---|
| 001 | T_001_Workspace | du_T_001_Workspace | WorkspaceID | WS-{SEQNUM:8} |
| 002 | T_002_WorkspaceMember | du_T_002_WorkspaceMember | WorkspaceMemberID | WSM-{SEQNUM:8} |
| 003 | T_003_Team | du_T_003_Team | TeamID | TEAM-{SEQNUM:8} |
| 004 | T_004_TeamMember | du_T_004_TeamMember | TeamMemberID | TM-{SEQNUM:8} |
| 005 | T_005_Project | du_T_005_Project | ProjectID | PRJ-{SEQNUM:8} |
| 006 | T_006_ProjectMember | du_T_006_ProjectMember | ProjectMemberID | PRJM-{SEQNUM:8} |
| 007 | T_007_ProjectBrief | du_T_007_ProjectBrief | ProjectBriefID | PBR-{SEQNUM:8} |
| 008 | T_008_ProjectMilestone | du_T_008_ProjectMilestone | ProjectMilestoneID | MS-{SEQNUM:8} |
| 009 | T_009_MilestoneCriterion | du_T_009_MilestoneCriterion | MilestoneCriterionID | MSC-{SEQNUM:8} |
| 010 | T_010_Conversation | du_T_010_Conversation | ConversationID | CON-{SEQNUM:8} |
| 011 | T_011_ConversationMessage | du_T_011_ConversationMessage | ConversationMessageID | MSG-{SEQNUM:8} |
| 012 | T_012_ConversationSummary | du_T_012_ConversationSummary | ConversationSummaryID | CS-{SEQNUM:8} |
| 013 | T_013_ConversationLink | du_T_013_ConversationLink | ConversationLinkID | CL-{SEQNUM:8} |
| 014 | T_014_Session | du_T_014_Session | SessionID | SES-{SEQNUM:8} |
| 015 | T_015_Branch | du_T_015_Branch | BranchID | BR-{SEQNUM:8} |
| 016 | T_016_Snapshot | du_T_016_Snapshot | SnapshotID | SNP-{SEQNUM:8} |
| 017 | T_017_Knowledge | du_T_017_Knowledge | KnowledgeID | KN-{SEQNUM:8} |
| 018 | T_018_ADR | du_T_018_ADR | ADRID | ADR-{SEQNUM:8} |
| 019 | T_019_WorkItem | du_T_019_WorkItem | WorkItemID | WI-{SEQNUM:8} |
| 020 | T_020_Artifact | du_T_020_Artifact | ArtifactID | ART-{SEQNUM:8} |
| 021 | T_021_AccessGrant | du_T_021_AccessGrant | AccessGrantID | AG-{SEQNUM:8} |


# View Naming Convention

Views use:

V_<sequence>_<table-name>

Examples:

V_001_Workspace
V_002_WorkspaceMember
V_003_Team
V_004_TeamMember
V_005_Project
...
V_021_AccessGrant



# Lookup Registry

| Table | Lookup Column | Target |
|---|---|---|
| T_002_WorkspaceMember | Workspace | T_001_Workspace |
| T_003_Team | Workspace | T_001_Workspace |
| T_004_TeamMember | Team | T_003_Team |
| T_005_Project | Workspace | T_001_Workspace |
| T_005_Project | CurrentMilestone | T_008_ProjectMilestone |
| T_006_ProjectMember | Project | T_005_Project |
| T_007_ProjectBrief | Project | T_005_Project |
| T_007_ProjectBrief | CurrentMilestone | T_008_ProjectMilestone |
| T_008_ProjectMilestone | Project | T_005_Project |
| T_008_ProjectMilestone | ParentMilestone | T_008_ProjectMilestone |
| T_009_MilestoneCriterion | Milestone | T_008_ProjectMilestone |
| T_010_Conversation | Workspace | T_001_Workspace |
| T_010_Conversation | Project | T_005_Project |
| T_010_Conversation | ParentConversation | T_010_Conversation |
| T_011_ConversationMessage | Conversation | T_010_Conversation |
| T_012_ConversationSummary | Conversation | T_010_Conversation |
| T_013_ConversationLink | FromConversation | T_010_Conversation |
| T_013_ConversationLink | ToConversation | T_010_Conversation |
| T_014_Session | Conversation | T_010_Conversation |
| T_015_Branch | Conversation | T_010_Conversation |
| T_016_Snapshot | Conversation | T_010_Conversation |
| T_016_Snapshot | Branch | T_015_Branch |
| T_017_Knowledge | Workspace | T_001_Workspace |
| T_017_Knowledge | Project | T_005_Project |
| T_017_Knowledge | SourceConversation | T_010_Conversation |
| T_018_ADR | Workspace | T_001_Workspace |
| T_018_ADR | Project | T_005_Project |
| T_018_ADR | SourceConversation | T_010_Conversation |
| T_018_ADR | SupersedesADR | T_018_ADR |
| T_019_WorkItem | Project | T_005_Project |
| T_019_WorkItem | Milestone | T_008_ProjectMilestone |
| T_019_WorkItem | Conversation | T_010_Conversation |
| T_019_WorkItem | ADR | T_018_ADR |
| T_020_Artifact | Project | T_005_Project |
| T_020_Artifact | WorkItem | T_019_WorkItem |
| T_020_Artifact | Conversation | T_010_Conversation |
| T_020_Artifact | ADR | T_018_ADR |



# Relationship Registry

## Workspace

T_001_Workspace

- 1:N → T_002_WorkspaceMember
- 1:N → T_003_Team
- 1:N → T_005_Project
- 1:N → T_010_Conversation
- 1:N → T_017_Knowledge

## Project

T_005_Project

- N:1 → T_001_Workspace
- 1:N → T_006_ProjectMember
- 1:1 → T_007_ProjectBrief
- 1:N → T_008_ProjectMilestone
- 1:N → T_010_Conversation
- 1:N → T_017_Knowledge
- 1:N → T_018_ADR
- 1:N → T_019_WorkItem
- 1:N → T_020_Artifact

## Conversation

T_010_Conversation

- N:1 → T_001_Workspace
- N:1 → T_005_Project
- N:1 → T_010_Conversation (ParentConversation)
- 1:N → T_011_ConversationMessage
- 1:N → T_012_ConversationSummary
- 1:N → T_014_Session
- 1:N → T_015_Branch
- 1:N → T_016_Snapshot

## Knowledge

T_017_Knowledge

- N:1 → T_001_Workspace
- N:1 → T_005_Project
- N:1 → T_010_Conversation

## ADR

T_018_ADR

- N:1 → T_001_Workspace
- N:1 → T_005_Project
- N:1 → T_010_Conversation
- N:1 → T_018_ADR (SupersedesADR)
