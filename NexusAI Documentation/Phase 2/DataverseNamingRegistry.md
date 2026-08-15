# NexusAI Dataverse Naming Registry

## Environment

PRT (Dev)

## Solution

N_001_Nexus

## Version

1.0.0.0

## Publisher Prefix

du_

---

# Tables

| Number | Table | Schema |
|---|---|---|
| T_001 | Workspace | du_T_001_Workspace |
| T_002 | WorkspaceMember | du_T_002_WorkspaceMember |
| T_003 | Team | du_T_003_Team |
| T_004 | TeamMember | du_T_004_TeamMember |
| T_005 | Project | du_T_005_Project |
| T_006 | ProjectMember | du_T_006_ProjectMember |
| T_007 | ProjectBrief | du_T_007_ProjectBrief |
| T_008 | ProjectMilestone | du_T_008_ProjectMilestone |
| T_009 | MilestoneCriterion | du_T_009_MilestoneCriterion |
| T_010 | Conversation | du_T_010_Conversation |
| T_011 | ConversationMessage | du_T_011_ConversationMessage |
| T_012 | ConversationSummary | du_T_012_ConversationSummary |
| T_013 | ConversationLink | du_T_013_ConversationLink |
| T_014 | Session | du_T_014_Session |
| T_015 | Branch | du_T_015_Branch |
| T_016 | Snapshot | du_T_016_Snapshot |
| T_017 | Knowledge | du_T_017_Knowledge |
| T_018 | ADR | du_T_018_ADR |
| T_019 | WorkItem | du_T_019_WorkItem |
| T_020 | Artifact | du_T_020_Artifact |
| T_021 | AccessGrant | du_T_021_AccessGrant |

---

# Views

Each primary table has a corresponding primary view:

V_001_Workspace
V_002_WorkspaceMember
V_003_Team
V_004_TeamMember
V_005_Project
V_006_ProjectMember
V_007_ProjectBrief
V_008_ProjectMilestone
V_009_MilestoneCriterion
V_010_Conversation
V_011_ConversationMessage
V_012_ConversationSummary
V_013_ConversationLink
V_014_Session
V_015_Branch
V_016_Snapshot
V_017_Knowledge
V_018_ADR
V_019_WorkItem
V_020_Artifact
V_021_AccessGrant

---

# Global Choices

C_001_WorkspaceStatus
C_002_WorkspaceRole
C_003_MembershipStatus
C_004_ProjectStatus
C_005_MilestoneStatus
C_006_CriterionStatus
C_007_ConversationType
C_008_ConversationStatus
C_009_ConversationVisibility
C_010_ConversationLinkType
C_011_KnowledgeType
C_012_ADRStatus
C_013_ResourceType
C_014_PrincipalType
C_015_AccessPermission
C_016_MessageRole
C_017_ArtifactType
C_018_ProjectType
C_019_SessionStatus
C_020_KnowledgeStatus
C_021_WorkItemStatus
C_022_WorkItemPriority
C_023_WorkItemType

---

# Autonumber

All table primary identifiers use:

<PREFIX>-{SEQNUM:8}

Seed:

1

Examples:

WS-00000001
PRJ-00000001
CON-00000001
MSG-00000001
KN-00000001