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