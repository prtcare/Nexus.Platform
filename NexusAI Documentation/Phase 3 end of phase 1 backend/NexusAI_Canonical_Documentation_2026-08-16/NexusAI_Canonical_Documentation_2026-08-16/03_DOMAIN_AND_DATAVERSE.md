# Domain and Dataverse

## Persistence position

Microsoft Dataverse is the operational system of record for NexusAI. Analytics may later flow to a warehouse, but the warehouse must not become the transactional application database.

Persistence is implemented behind repository contracts so Domain and Application remain independent of Dataverse.

## Current domain aggregates

- Workspace
- Project
- Conversation
- Conversation Message
- Knowledge
- Memory
- ADR
- Work Item
- Artifact
- Branch
- Snapshot
- Session

Project Milestone, membership, teams, project briefs, criteria, summaries, conversation links, and access grants appear in the target Dataverse design but are not all implemented in the reviewed source.

## Target Dataverse registry

Environment: `PRT (Dev)`  
Solution: `N_001_Nexus`  
Solution version recorded in the source documents: `1.0.0.0`  
Publisher prefix: `du_`

| No. | Table | Target schema name | Implementation status |
|---|---|---|---|
| T_001 | Workspace | `du_T_001_Workspace` | Implemented |
| T_002 | WorkspaceMember | `du_T_002_WorkspaceMember` | Planned |
| T_003 | Team | `du_T_003_Team` | Planned |
| T_004 | TeamMember | `du_T_004_TeamMember` | Planned |
| T_005 | Project | `du_T_005_Project` | Implemented |
| T_006 | ProjectMember | `du_T_006_ProjectMember` | Planned |
| T_007 | ProjectBrief | `du_T_007_ProjectBrief` | Planned |
| T_008 | ProjectMilestone | `du_T_008_ProjectMilestone` | Planned; next major domain feature |
| T_009 | MilestoneCriterion | `du_T_009_MilestoneCriterion` | Planned |
| T_010 | Conversation | `du_T_010_Conversation` | Implemented |
| T_011 | ConversationMessage | `du_T_011_ConversationMessage` | Implemented |
| T_012 | ConversationSummary | `du_T_012_ConversationSummary` | Planned |
| T_013 | ConversationLink | `du_T_013_ConversationLink` | Planned |
| T_014 | Session | `du_T_014_Session` | Implemented |
| T_015 | Branch | `du_T_015_Branch` | Implemented |
| T_016 | Snapshot | `du_T_016_Snapshot` | Implemented |
| T_017 | Knowledge | `du_T_017_Knowledge` | Implemented |
| T_018 | ADR | `du_T_018_ADR` | Persistence present; API incomplete |
| T_019 | WorkItem | `du_T_019_WorkItem` | Implemented |
| T_020 | Artifact | `du_T_020_Artifact` | Implemented |
| T_021 | AccessGrant | `du_T_021_AccessGrant` | Planned |

Before creating more Dataverse components, reconcile this target registry with the exact logical names currently used by the live mappers and environment. The live schema is authoritative; the registry must be updated rather than silently creating parallel columns.

## Core entity intent

### Workspace

Boundary for ownership and context. Core data: GUID ID, display/business ID, name, owner, description, status, and creation time. Initial status is Active. Domain behavior includes rename and archive.

### Project

Belongs to one Workspace and represents a bounded outcome. Core data: ID, Workspace ID, name, status, and creation time. Project must reference, not duplicate, its workspace.

### Project Milestone

Planned organizer within a Project. Suggested data: ID, Project ID, name, description, sequence, status, target date, completion date, and timestamps. A Milestone contains criteria and groups conversations/work items. Milestone content changes that redefine the agreed outcome require explicit approval.

### Conversation

Belongs to a Workspace and Project. Core data includes ID, title, description, type, visibility, status, optional parent conversation, and creation/update times. Parent relationships support sub-conversations without copying message history.

### Conversation Message

Belongs to a Conversation. Stores role, content, creation time, and optional agent/provider metadata. Roles must use the shared choice/enum contract. Missing optional agent metadata must be tolerated.

### Knowledge

Trusted, reusable context scoped to a Workspace and optionally a Project or Conversation. Core data includes title, content, type, source, status, and timestamps. Knowledge should be traceable and approved/retired rather than overwritten without history.

### ADR

Stores an explicit decision, its context, chosen option, consequences, status, scope, and optional superseded ADR. Accepted ADRs are durable records, not casual chat summaries.

### Work Item

Executable work belonging to a Project and optionally related to a Conversation/Milestone. Core data includes title, description, type, priority, status, and timestamps.

### Artifact

Reusable output related to a Work Item and, in the target model, optionally Project/Conversation. Core data includes name, type, content or resource reference, and creation time. Large binary content should use suitable file/blob storage with a reference in Dataverse.

### Session

An execution/chat session belonging to a Conversation, with status, start time, and optional end time.

### Branch

A named alternate line of work within a Conversation. Stores description, status, and creation time.

### Snapshot

Captured state associated with a Branch and Conversation, including name, serialized state, and creation time.

### Memory

Internal retained context with type, source, scope, and content. It should not become an uncontrolled duplicate of Knowledge; Knowledge is curated truth, while Memory may contain recalled working context.

## Relationship model

| Parent | Relationship |
|---|---|
| Workspace | 1:N WorkspaceMember, Team, Project, Conversation, Knowledge |
| Project | N:1 Workspace; 1:N ProjectMember, Milestone, Conversation, Knowledge, ADR, WorkItem, Artifact; 1:1 ProjectBrief |
| Milestone | N:1 Project; 1:N Criterion, Conversation, WorkItem |
| Conversation | N:1 Workspace and Project; optional N:1 parent Conversation; 1:N Message, Summary, Session, Branch, Snapshot |
| Knowledge | optional N:1 Workspace, Project, Conversation depending on scope |
| ADR | N:1 Workspace/Project/Conversation; optional self-reference to superseded ADR |
| WorkItem | N:1 Project; optional N:1 Milestone/Conversation; 1:N Artifact |
| Artifact | N:1 WorkItem in current API; target may also reference Project/Conversation |

Avoid cascade deletion that could erase durable knowledge, decisions, messages, or artifacts. Prefer archive/status transitions and explicit retention rules.

## Global choice registry

The target documents define these choice families. Remove duplicate semantic choices before deployment and freeze numeric values before generating frontend clients.

| Code | Choice |
|---|---|
| C_001 | WorkspaceStatus |
| C_002 | WorkspaceRole |
| C_003 | MembershipStatus |
| C_004 | ProjectStatus |
| C_005 | MilestoneStatus |
| C_006 | CriterionStatus |
| C_007 | ConversationType |
| C_008 | ConversationStatus |
| C_009 | ConversationVisibility |
| C_010 | ConversationLinkType |
| C_011 | KnowledgeType |
| C_012 | ADRStatus |
| C_013 | ResourceType |
| C_014 | PrincipalType |
| C_015 | AccessPermission |
| C_016 | MessageRole |
| C_017 | ArtifactType |
| C_018 | ProjectType |
| C_019 | SessionStatus |
| C_020 | KnowledgeStatus |
| C_021 | WorkItemStatus |
| C_022 | WorkItemPriority |
| C_023 | WorkItemType |

The former list repeated ResourceType, PrincipalType, and Permission under C_024–C_026. This canonical registry removes those duplicates. If the live environment already contains both versions, deprecate rather than abruptly delete them.

## ID and naming rules

- Domain IDs are immutable `readonly record struct` wrappers around `Guid` where practical.
- HTTP and Dataverse boundaries convert typed IDs to/from GUIDs.
- Do not interchange IDs belonging to different aggregate types.
- Use schema names from the approved registry; never invent a new logical name in a mapper.
- Use one primary name column per Dataverse table.
- Use UTC date/time values and ISO 8601 at API boundaries.
- Enum/choice value `0` may be used only when explicitly defined; never rely on implicit default meaning.

## Persistence verification checklist

For each feature:

1. Confirm live table and column logical names.
2. Complete domain model and repository contract.
3. Complete Dataverse entity and both mapper directions.
4. Implement create/get/list/update repository operations with server-side filtering.
5. Register services.
6. Add command/query handlers.
7. Add API contract and validation.
8. Build.
9. Test through Swagger.
10. Confirm the persisted Dataverse record and round-trip values.
11. Add mapper/repository/API tests.
12. Update documentation and commit.
