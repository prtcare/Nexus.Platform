5\. WorkspaceMember



Logical Name:



nexus\_workspacemember



Primary Key:



nexus\_workspacememberid



Fields:



Field	Type	Required

WorkspaceMemberId	Unique Identifier	Yes

WorkspaceId	Lookup Workspace	Yes

UserId	User	Yes

Role	Choice	Yes

Status	Choice	Yes

JoinedOn	DateTime	Yes



Roles:



Owner

Admin

Member

Viewer



Statuses:



Active

Invited

Suspended

Removed

6\. Team



Logical Name:



nexus\_team



Primary Key:



nexus\_teamid



Fields:



Field	Type	Required

TeamId	Unique Identifier	Yes

WorkspaceId	Lookup Workspace	Yes

Name	Text	Yes

Description	Multiline Text	No

Status	Choice	Yes

CreatedOn	DateTime	Yes

ModifiedOn	DateTime	Yes

7\. TeamMember



Logical Name:



nexus\_teammember



Primary Key:



nexus\_teammemberid



Fields:



Field	Type	Required

TeamMemberId	Unique Identifier	Yes

TeamId	Lookup Team	Yes

UserId	User	Yes

Role	Choice	Yes

JoinedOn	DateTime	Yes

8\. Project



Logical Name:



nexus\_project



Primary Key:



nexus\_projectid



Fields:



Field	Type	Required

ProjectId	Unique Identifier	Yes

WorkspaceId	Lookup Workspace	Yes

Name	Text	Yes

Description	Multiline Text	No

ProjectType	Choice	No

Status	Choice	Yes

OwnerId	User/Owner	Yes

CurrentMilestoneId	Lookup ProjectMilestone	No

CreatedOn	DateTime	Yes

ModifiedOn	DateTime	Yes



Status:



Planning

Active

OnHold

Completed

Archived



Relationships:



Workspace

&#x20;   ↓

Project

&#x20;├── ProjectMember

&#x20;├── ProjectBrief

&#x20;├── ProjectMilestone

&#x20;├── Conversation

&#x20;├── WorkItem

&#x20;├── Artifact

&#x20;├── Knowledge

&#x20;└── ADR

9\. ProjectMember



Logical Name:



nexus\_projectmember



Primary Key:



nexus\_projectmemberid



Fields:



Field	Type	Required

ProjectMemberId	Unique Identifier	Yes

ProjectId	Lookup Project	Yes

UserId	User	Yes

Role	Choice	Yes

Status	Choice	Yes

JoinedOn	DateTime	Yes



Roles:



Owner

Admin

Member

Viewer

10\. ProjectBrief



Logical Name:



nexus\_projectbrief



Primary Key:



nexus\_projectbriefid



Fields:



Field	Type	Required

ProjectBriefId	Unique Identifier	Yes

ProjectId	Lookup Project	Yes

Purpose	Multiline Text	No

CurrentState	Multiline Text	No

CurrentArchitecture	Multiline Text	No

CurrentPhase	Text	No

CurrentMilestoneId	Lookup ProjectMilestone	No

ImportantConstraints	Multiline Text	No

CurrentDirection	Multiline Text	No

OpenQuestions	Multiline Text	No

KeyDecisions	Multiline Text	No

Version	Integer	Yes

LastReviewedOn	DateTime	No

CreatedOn	DateTime	Yes

ModifiedOn	DateTime	Yes



Constraint:



A Project has at most one active ProjectBrief.



11\. ProjectMilestone



Logical Name:



nexus\_projectmilestone



Primary Key:



nexus\_projectmilestoneid



Fields:



Field	Type	Required

ProjectMilestoneId	Unique Identifier	Yes

ProjectId	Lookup Project	Yes

ParentMilestoneId	Lookup ProjectMilestone	No

Name	Text	Yes

Description	Multiline Text	No

Phase	Text	No

Status	Choice	Yes

Sequence	Integer	Yes

StartDate	Date	No

TargetDate	Date	No

CompletedDate	Date	No

CompletionReason	Multiline Text	No

CreatedOn	DateTime	Yes

ModifiedOn	DateTime	Yes



Statuses:



Planned

Active

Blocked

Review

Completed

Cancelled



A Project may contain zero or more milestones.



12\. MilestoneCriterion



Logical Name:



nexus\_milestonecriterion



Primary Key:



nexus\_milestonecriterionid



Fields:



Field	Type	Required

MilestoneCriterionId	Unique Identifier	Yes

MilestoneId	Lookup ProjectMilestone	Yes

Description	Text	Yes

Status	Choice	Yes

Sequence	Integer	Yes

Evidence	Multiline Text	No

CompletedDate	DateTime	No

CreatedOn	DateTime	Yes

ModifiedOn	DateTime	Yes



Statuses:



Pending

InProgress

Completed

Blocked

NotApplicable

13\. Conversation



Logical Name:



nexus\_conversation



Primary Key:



nexus\_conversationid



Fields:



Field	Type	Required

ConversationId	Unique Identifier	Yes

WorkspaceId	Lookup Workspace	Yes

ProjectId	Lookup Project	No

ParentConversationId	Lookup Conversation	No

Name	Text	Yes

Description	Multiline Text	No

ConversationType	Choice	Yes

Status	Choice	Yes

Visibility	Choice	Yes

OwnerId	User/Owner	Yes

LastMessageOn	DateTime	No

CreatedOn	DateTime	Yes

ModifiedOn	DateTime	Yes



ConversationType:



Standalone

Main

SubChat

Workspace



Status:



Active

Review

Finalized

Archived



Visibility:



Private

Workspace

Project

Shared



Examples:



Standalone:

ProjectId = null

ParentConversationId = null

ConversationType = Standalone



Main Chat:

ProjectId = Project A

ParentConversationId = null

ConversationType = Main



Sub-Chat:

ProjectId = Project A

ParentConversationId = Main Chat

ConversationType = SubChat

14\. ConversationMessage



Logical Name:



nexus\_conversationmessage



Existing Phase 1 entity.



Primary Key:



nexus\_conversationmessageid



Fields:



Field	Type	Required

ConversationMessageId	Unique Identifier	Yes

ConversationId	Lookup Conversation	Yes

Role	Choice	Yes

Content	Multiline Text	Yes

Sequence	Integer	Yes

CreatedBy	User	No

CreatedOn	DateTime	Yes

AgentType	Choice/Text	No

Model	Text	No

PromptVersion	Text	No

TokenCount	Integer	No

LatencyMs	Integer	No



Roles:



User

Assistant

System

Tool

15\. ConversationSummary



Logical Name:



nexus\_conversationsummary



Primary Key:



nexus\_conversationsummaryid



Fields:



Field	Type	Required

ConversationSummaryId	Unique Identifier	Yes

ConversationId	Lookup Conversation	Yes

Summary	Multiline Text	Yes

KeyPoints	Multiline Text	No

Conclusions	Multiline Text	No

OpenQuestions	Multiline Text	No

CurrentState	Multiline Text	No

Keywords	Text	No

Version	Integer	Yes

CreatedOn	DateTime	Yes

ModifiedOn	DateTime	Yes

16\. ConversationLink



Logical Name:



nexus\_conversationlink



Primary Key:



nexus\_conversationlinkid



Fields:



Field	Type	Required

ConversationLinkId	Unique Identifier	Yes

FromConversationId	Lookup Conversation	Yes

ToConversationId	Lookup Conversation	Yes

LinkType	Choice	Yes

CreatedBy	User	Yes

CreatedOn	DateTime	Yes



Link types:



Related

DependsOn

References

DerivedFrom

Continues

Contradicts

17\. Session



Logical Name:



nexus\_session



Existing Phase 1 entity.



Purpose:



Runtime/execution session.



It must not become the long-term memory store.



Relationship:



Conversation

&#x20;   ↓

Session

18\. Branch



Logical Name:



nexus\_branch



Existing Phase 1 entity.



Purpose:



Represent alternate conversation paths.



Conversation

&#x20;├── Main Branch

&#x20;├── Option A

&#x20;└── Option B

19\. Snapshot



Logical Name:



nexus\_snapshot



Existing Phase 1 entity.



Purpose:



Capture historical state.



Snapshots are historical records and normally belong to cold retrieval.



20\. Knowledge



Logical Name:



nexus\_knowledge



Existing Phase 1 entity.



Primary Key:



nexus\_knowledgeid



Fields should support:



Field	Type

KnowledgeId	Unique Identifier

WorkspaceId	Lookup Workspace

ProjectId	Lookup Project, optional

SourceConversationId	Lookup Conversation, optional

Title	Text

Content	Multiline Text

KnowledgeType	Choice

Status	Choice

Summary	Multiline Text

Keywords	Text

CreatedOn	DateTime

ModifiedOn	DateTime



Knowledge types:



Documentation

TechnicalKnowledge

LessonLearned

Specification

Guideline

Reference

ExtractedKnowledge



Knowledge is:



Workspace-scoped



by default, with optional:



Project scope

21\. ADR



Logical Name:



nexus\_adr



Existing Phase 1 entity.



Fields should support:



Field	Type

AdrId	Unique Identifier

WorkspaceId	Lookup Workspace

ProjectId	Lookup Project

SourceConversationId	Lookup Conversation

Title	Text

Context	Multiline Text

Decision	Multiline Text

Consequences	Multiline Text

Status	Choice

SupersedesAdrId	Lookup ADR

CreatedOn	DateTime

ModifiedOn	DateTime



Statuses:



Proposed

Accepted

Rejected

Superseded

Deprecated

22\. WorkItem



Logical Name:



nexus\_workitem



Existing Phase 1 entity.



Phase 2 relationships should support:



Project

Milestone

Conversation

ADR

Artifact



This allows:



Conversation

&#x20;   ↓

Decision

&#x20;   ↓

WorkItem

&#x20;   ↓

Artifact

23\. Artifact



Logical Name:



nexus\_artifact



Existing Phase 1 entity.



Artifacts should maintain provenance where applicable:



Project

WorkItem

Conversation

ADR

CreatedBy

CreatedOn



Artifact types:



Code

Document

Schema

Configuration

API

Test

Diagram

Other

24\. AccessGrant



Logical Name:



nexus\_accessgrant



Primary Key:



nexus\_accessgrantid



Fields:



Field	Type	Required

AccessGrantId	Unique Identifier	Yes

ResourceType	Choice	Yes

ResourceId	Unique Identifier	Yes

PrincipalType	Choice	Yes

PrincipalId	Unique Identifier	Yes

Permission	Choice	Yes

GrantedBy	User	Yes

GrantedOn	DateTime	Yes

ExpiresOn	DateTime	No



Resource types:



Workspace

Project

Conversation

Knowledge

Artifact



Principal types:



User

Team



Permissions:



View

Collaborate

Edit

Admin

25\. Relationship Summary

Workspace

│

├── WorkspaceMember

├── Team

│   └── TeamMember

│

├── Project

│   ├── ProjectMember

│   ├── ProjectBrief

│   ├── ProjectMilestone

│   │   └── MilestoneCriterion

│   │

│   ├── Conversation

│   │   ├── ConversationMessage

│   │   ├── ConversationSummary

│   │   ├── Session

│   │   ├── Branch

│   │   ├── Snapshot

│   │   └── ConversationLink

│   │

│   ├── WorkItem

│   │   └── Artifact

│   │

│   ├── Knowledge

│   │   └── ADR

│   │

│   └── AccessGrant

│

└── Standalone Conversation

🧠 26. Persistent Memory Model



Do not create:



nexus\_memory



as the central memory table.



Instead:



Raw Memory

&#x20;   │

&#x20;   └── ConversationMessage



Compressed Memory

&#x20;   │

&#x20;   └── ConversationSummary



Durable Knowledge

&#x20;   │

&#x20;   └── Knowledge



Architectural Memory

&#x20;   │

&#x20;   └── ADR



Current Project Memory

&#x20;   │

&#x20;   └── ProjectBrief



Current Project Progress

&#x20;   │

&#x20;   └── ProjectMilestone

🔥 27. Retrieval Model

HOT

├── Current Conversation

├── ProjectBrief

├── Active Milestone

├── Accepted ADRs

└── Current WorkItems



WARM

├── Knowledge

├── ConversationSummary

├── Finalized Sub-Chats

└── Recent Artifacts



COLD

├── Historical Messages

├── Archived Conversations

├── Superseded ADRs

└── Snapshots

🔐 28. Security Boundary



Retrieval must always follow:



User

&#x20;↓

Workspace authorization

&#x20;↓

Project authorization

&#x20;↓

Conversation authorization

&#x20;↓

Knowledge authorization

&#x20;↓

Retrieve

&#x20;↓

Rank

&#x20;↓

Build Context

&#x20;↓

LLM



Never retrieve unauthorized data and rely on the LLM to hide it.



📊 29. Data Warehouse Mapping



The operational tables will eventually feed:



FactConversation

FactMessage

FactAgentExecution

FactWorkItem

FactMilestone

FactKnowledge

FactRetrieval



and:



DimDate

DimWorkspace

DimProject

DimUser

DimTeam

DimConversation

DimConversationType

DimAgent

DimModel

DimMessageRole

DimKnowledgeType

DimWorkItemStatus

DimMilestone

DimArtifactType

DimSourceType

