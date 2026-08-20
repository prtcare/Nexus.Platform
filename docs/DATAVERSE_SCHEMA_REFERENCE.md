# Dataverse Schema Reference — `N_001_Nexus`

Extracted from the unmanaged solution export (`customizations.xml`, org `9.2.26081.137`)
on 2026-08-19. **21 entities, 207 custom columns, 100 relationships.**

Input to the ADR-014 Azure SQL migration. Dataverse platform columns (`createdby`,
`modifiedon`, `statecode`, `ownerid`, `versionnumber`…) are excluded — they do not carry
over. The `du_` prefix and `T_nnn_` numbering are dropped in SQL.

> The **C# Domain remains the source of truth** for the 11 modelled aggregates. This
> document is a cross-check and the specification for the 10 that are not yet modelled.

## Coverage

| | Count | Tables |
|---|---|---|
| Has a C# aggregate | 11 | ADR, Artifact, Branch, Conversation, ConversationMessage, Knowledge, Project, Session, Snapshot, WorkItem, Workspace |
| **No C# aggregate** | 10 | AccessGrant, ConversationLink, ConversationSummary, MilestoneCriterion, ProjectBrief, ProjectMember, ProjectMilestone, Team, TeamMember, WorkspaceMember |

---

## Workspace — modelled in C#

`du_T_001_Workspace` → SQL table `Workspace`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `description` | ntext | `nvarchar(max)` |  |  |
| `t_001_workspaceid` | primarykey | `uniqueidentifier` | **req** |  |
| `workspaceid` | nvarchar | `nvarchar(850)` | required |  |
| `workspacename` | nvarchar | `nvarchar(100)` | required |  |
| `workspaceowner` | nvarchar | `nvarchar(100)` |  |  |
| `workspacestatus` | picklist | `int` | required |  |

## WorkspaceMember — **not modelled in C#**

`du_T_002_WorkspaceMember` → SQL table `WorkspaceMember`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `joinedon` | datetime | `datetime2` |  |  |
| `t_002_workspacememberid` | primarykey | `uniqueidentifier` | **req** |  |
| `user` | nvarchar | `nvarchar(100)` |  |  |
| `workspace` | lookup | `uniqueidentifier` | required | **FK → Workspace** |
| `workspacememberid` | nvarchar | `nvarchar(850)` | required |  |
| `workspacemembername` | nvarchar | `nvarchar(100)` | required |  |
| `workspacememberrole` | picklist | `int` |  | `121930000`=Owner, `121930001`=Admin, `121930002`=Member, `121930003`=Viewer |
| `workspacemembersrole` | picklist | `int` |  |  |
| `workspacememberstatus` | picklist | `int` |  | `121930000`=Invited, `121930001`=Active, `121930002`=Suspended, `121930003`=Removed |
| `workspacestatus` | picklist | `int` |  |  |

## Team — **not modelled in C#**

`du_T_003_Team` → SQL table `Team`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `description` | ntext | `nvarchar(max)` |  |  |
| `t_003_teamid` | primarykey | `uniqueidentifier` | **req** |  |
| `teamid` | nvarchar | `nvarchar(850)` | required |  |
| `teamname` | nvarchar | `nvarchar(100)` | required |  |
| `teamstatus` | picklist | `int` |  | `121930000`=Active, `121930001`=Inactive, `121930002`=Archived |
| `teamstatus01` | picklist | `int` |  |  |
| `workspace` | lookup | `uniqueidentifier` | required | **FK → Workspace** |

## TeamMember — **not modelled in C#**

`du_T_004_TeamMember` → SQL table `TeamMember`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `joinedon` | datetime | `datetime2` |  |  |
| `role` | picklist | `int` |  | `121930000`=View |
| `t_004_teammemberid` | primarykey | `uniqueidentifier` | **req** |  |
| `team` | lookup | `uniqueidentifier` | required | **FK → Team** |
| `teammemberid` | nvarchar | `nvarchar(850)` | required |  |
| `teammembername` | nvarchar | `nvarchar(100)` | required |  |
| `teammemberrole` | picklist | `int` |  |  |
| `user` | nvarchar | `nvarchar(100)` |  |  |

## Project — modelled in C#

`du_T_005_Project` → SQL table `Project`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `currentmilestone` | lookup | `uniqueidentifier` |  | **FK → ProjectMilestone** |
| `description` | ntext | `nvarchar(max)` |  |  |
| `owner` | nvarchar | `nvarchar(100)` |  |  |
| `projectid` | nvarchar | `nvarchar(850)` | required |  |
| `projectname` | nvarchar | `nvarchar(100)` | required |  |
| `projectstatus` | picklist | `int` |  | `121930000`=Planning, `121930001`=Active, `121930002`=OnHold, `121930003`=Completed, `121930004`=Archived |
| `projectstatus01` | picklist | `int` |  |  |
| `projecttype` | picklist | `int` |  | `121930000`=Live |
| `projecttype01` | picklist | `int` |  |  |
| `t_005_projectid` | primarykey | `uniqueidentifier` | **req** |  |
| `workspace` | lookup | `uniqueidentifier` |  | **FK → Workspace** |

## ProjectMember — **not modelled in C#**

`du_T_006_ProjectMember` → SQL table `ProjectMember`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `joinedon` | datetime | `datetime2` |  |  |
| `project` | lookup | `uniqueidentifier` |  | **FK → Project** |
| `projectmemberid` | nvarchar | `nvarchar(850)` | required |  |
| `projectmembername` | nvarchar | `nvarchar(100)` |  |  |
| `projectmemberrole` | picklist | `int` |  | `121930000`=Owner, `121930001`=Admin, `121930002`=Member, `121930003`=Viewer |
| `projectmemberrole01` | picklist | `int` |  |  |
| `projectmemberstatus` | picklist | `int` |  |  |
| `status` | picklist | `int` |  | `121930000`=Active, `121930001`=Inactive, `121930002`=Archived |
| `t_006_projectmemberid` | primarykey | `uniqueidentifier` | **req** |  |
| `user` | nvarchar | `nvarchar(100)` |  |  |

## ProjectBrief — **not modelled in C#**

`du_T_007_ProjectBrief` → SQL table `ProjectBrief`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `currentarchitecture` | ntext | `nvarchar(max)` |  |  |
| `currentdirection` | ntext | `nvarchar(max)` |  |  |
| `currentmilestone` | lookup | `uniqueidentifier` |  | **FK → ProjectMilestone** |
| `currentphase` | ntext | `nvarchar(max)` |  |  |
| `currentstate` | ntext | `nvarchar(max)` |  |  |
| `importantconstraints` | ntext | `nvarchar(max)` |  |  |
| `keydecisions` | ntext | `nvarchar(max)` |  |  |
| `lastreviewedon` | datetime | `datetime2` |  |  |
| `openquestions` | ntext | `nvarchar(max)` |  |  |
| `project` | lookup | `uniqueidentifier` |  | **FK → Project** |
| `projectbriefid` | nvarchar | `nvarchar(850)` | required |  |
| `projectbriefname` | nvarchar | `nvarchar(100)` |  |  |
| `purpose` | ntext | `nvarchar(max)` |  |  |
| `t_007_projectbriefid` | primarykey | `uniqueidentifier` | **req** |  |
| `version` | int | `int` |  |  |

## ProjectMilestone — **not modelled in C#**

`du_T_008_ProjectMilestone` → SQL table `ProjectMilestone`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `completeddate` | datetime | `datetime2` |  |  |
| `completionreason` | ntext | `nvarchar(max)` |  |  |
| `description` | ntext | `nvarchar(max)` |  |  |
| `parentmilestone` | lookup | `uniqueidentifier` |  | **FK → ProjectMilestone** |
| `phase` | nvarchar | `nvarchar(100)` |  |  |
| `project` | lookup | `uniqueidentifier` | required | **FK → Project** |
| `projectmilestoneid` | nvarchar | `nvarchar(850)` | required |  |
| `projectmilestonename` | nvarchar | `nvarchar(100)` |  |  |
| `projectmilestonestatus` | picklist | `int` | required |  |
| `sequence` | int | `int` |  |  |
| `startdate` | datetime | `datetime2` |  |  |
| `status` | picklist | `int` |  | `121930000`=Planned, `121930001`=Active, `121930002`=Blocked, `121930003`=Review, `121930004`=Completed, `121930005`=Cancelled |
| `t_008_projectmilestoneid` | primarykey | `uniqueidentifier` | **req** |  |
| `targetdate` | datetime | `datetime2` |  |  |

## MilestoneCriterion — **not modelled in C#**

`du_T_009_MilestoneCriterion` → SQL table `MilestoneCriterion`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `completeddate` | datetime | `datetime2` |  |  |
| `description` | ntext | `nvarchar(max)` |  |  |
| `evidence` | ntext | `nvarchar(max)` |  |  |
| `milestone` | lookup | `uniqueidentifier` | required | **FK → ProjectMilestone** |
| `milestonecriteriastatus` | picklist | `int` | required |  |
| `milestonecriterionid` | nvarchar | `nvarchar(850)` | required |  |
| `milestonecriterionname` | nvarchar | `nvarchar(100)` |  |  |
| `sequence` | int | `int` |  |  |
| `status` | picklist | `int` |  | `121930000`=Pending, `121930001`=Inprogress, `121930002`=Completed, `121930003`=Blocked, `121930004`=Notapplicable |
| `t_009_milestonecriterionid` | primarykey | `uniqueidentifier` | **req** |  |

## Conversation — modelled in C#

`du_T_010_Conversation` → SQL table `Conversation`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `conversationid` | nvarchar | `nvarchar(850)` | required |  |
| `conversationname` | nvarchar | `nvarchar(100)` |  |  |
| `conversationstatus` | picklist | `int` | required |  |
| `conversationtype01` | picklist | `int` | required |  |
| `conversationvisibility` | picklist | `int` | required |  |
| `description` | ntext | `nvarchar(max)` |  |  |
| `lastmessageon` | datetime | `datetime2` |  |  |
| `owner` | nvarchar | `nvarchar(100)` |  |  |
| `parentconversation` | lookup | `uniqueidentifier` |  | **FK → Conversation** |
| `project` | lookup | `uniqueidentifier` |  | **FK → Project** |
| `t_010_conversationid` | primarykey | `uniqueidentifier` | **req** |  |
| `workspace` | lookup | `uniqueidentifier` | required | **FK → Workspace** |

## ConversationMessage — modelled in C#

`du_T_011_ConversationMessage` → SQL table `ConversationMessage`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `agenttype` | nvarchar | `nvarchar(100)` |  |  |
| `content` | ntext | `nvarchar(max)` |  |  |
| `conversation` | lookup | `uniqueidentifier` | required | **FK → Conversation** |
| `conversationmessageid` | nvarchar | `nvarchar(850)` | required |  |
| `createdby` | nvarchar | `nvarchar(100)` |  |  |
| `latencyms` | int | `int` |  |  |
| `model` | nvarchar | `nvarchar(100)` |  |  |
| `promptversion` | nvarchar | `nvarchar(100)` |  |  |
| `sequence` | int | `int` |  |  |
| `t_011_conversationmessageid` | primarykey | `uniqueidentifier` | **req** |  |
| `tokencount` | int | `int` |  |  |

## ConversationSummary — **not modelled in C#**

`du_T_012_ConversationSummary` → SQL table `ConversationSummary`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `conclusions` | ntext | `nvarchar(max)` |  |  |
| `conversation` | lookup | `uniqueidentifier` | required | **FK → Conversation** |
| `conversationsummaryid` | nvarchar | `nvarchar(850)` | required |  |
| `conversationsummaryname` | nvarchar | `nvarchar(100)` |  |  |
| `currentstate` | ntext | `nvarchar(max)` |  |  |
| `keypoints` | ntext | `nvarchar(max)` |  |  |
| `keywords` | nvarchar | `nvarchar(100)` |  |  |
| `openquestions` | ntext | `nvarchar(max)` |  |  |
| `summary` | ntext | `nvarchar(max)` |  |  |
| `t_012_conversationsummaryid` | primarykey | `uniqueidentifier` | **req** |  |
| `version` | int | `int` |  |  |

## ConversationLink — **not modelled in C#**

`du_T_013_ConversationLink` → SQL table `ConversationLink`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `conversationlinkid` | nvarchar | `nvarchar(850)` | required |  |
| `conversationlinkname` | nvarchar | `nvarchar(100)` | required |  |
| `createdby` | nvarchar | `nvarchar(100)` |  |  |
| `createdon` | datetime | `datetime2` |  |  |
| `fromconversation` | lookup | `uniqueidentifier` | required | **FK → Conversation** |
| `linktype` | picklist | `int` |  |  |
| `linktype01` | picklist | `int` | required |  |
| `t_013_conversationlinkid` | primarykey | `uniqueidentifier` | **req** |  |
| `toconversation` | lookup | `uniqueidentifier` | required | **FK → Conversation** |

## Session — modelled in C#

`du_T_014_Session` → SQL table `Session`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `conversation` | lookup | `uniqueidentifier` | required | **FK → Conversation** |
| `endedon` | datetime | `datetime2` |  |  |
| `sessionid` | nvarchar | `nvarchar(850)` | required |  |
| `sessionstatus` | picklist | `int` | required |  |
| `startedon` | datetime | `datetime2` |  |  |
| `t_014_sessionid` | primarykey | `uniqueidentifier` | **req** |  |

## Branch — modelled in C#

`du_T_015_Branch` → SQL table `Branch`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `branchid` | nvarchar | `nvarchar(850)` | required |  |
| `branchname` | nvarchar | `nvarchar(100)` |  |  |
| `branchstatus` | picklist | `int` | required |  |
| `conversation` | lookup | `uniqueidentifier` | required | **FK → Conversation** |
| `description` | ntext | `nvarchar(max)` |  |  |
| `t_015_branchid` | primarykey | `uniqueidentifier` | **req** |  |

## Snapshot — modelled in C#

`du_T_016_Snapshot` → SQL table `Snapshot`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `branch` | lookup | `uniqueidentifier` | required | **FK → Branch** |
| `conversation` | lookup | `uniqueidentifier` | required | **FK → Conversation** |
| `createdon` | datetime | `datetime2` |  |  |
| `snapshotid` | nvarchar | `nvarchar(850)` | required |  |
| `snapshotname` | nvarchar | `nvarchar(100)` |  |  |
| `state` | ntext | `nvarchar(max)` |  |  |
| `t_016_snapshotid` | primarykey | `uniqueidentifier` | **req** |  |

## Knowledge — modelled in C#

`du_T_017_Knowledge` → SQL table `Knowledge`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `content` | ntext | `nvarchar(max)` | required |  |
| `keywords` | nvarchar | `nvarchar(100)` |  |  |
| `knowledgeid` | nvarchar | `nvarchar(850)` | required |  |
| `knowledgestatus` | picklist | `int` | required |  |
| `knowledgetype` | picklist | `int` | required |  |
| `project` | lookup | `uniqueidentifier` |  | **FK → Project** |
| `sourceconversation` | lookup | `uniqueidentifier` |  | **FK → Conversation** |
| `summary` | ntext | `nvarchar(max)` |  |  |
| `t_017_knowledgeid` | primarykey | `uniqueidentifier` | **req** |  |
| `title` | nvarchar | `nvarchar(100)` | required |  |
| `workspace` | lookup | `uniqueidentifier` | required | **FK → Workspace** |

## ADR — modelled in C#

`du_T_018_ADR` → SQL table `ADR`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `adrid` | nvarchar | `nvarchar(850)` | required |  |
| `adrstatus` | picklist | `int` | required |  |
| `consequences` | ntext | `nvarchar(max)` |  |  |
| `context` | ntext | `nvarchar(max)` | required |  |
| `decision` | ntext | `nvarchar(max)` | required |  |
| `project` | lookup | `uniqueidentifier` |  | **FK → Project** |
| `sourceconversation` | lookup | `uniqueidentifier` |  | **FK → Conversation** |
| `supersedesadr` | lookup | `uniqueidentifier` |  | **FK → ADR** |
| `t_018_adrid` | primarykey | `uniqueidentifier` | **req** |  |
| `title` | nvarchar | `nvarchar(100)` | required |  |
| `workspace` | lookup | `uniqueidentifier` | required | **FK → Workspace** |

## WorkItem — modelled in C#

`du_T_019_WorkItem` → SQL table `WorkItem`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `adr` | lookup | `uniqueidentifier` |  | **FK → ADR** |
| `conversation` | lookup | `uniqueidentifier` |  | **FK → Conversation** |
| `description` | ntext | `nvarchar(max)` |  |  |
| `milestone` | lookup | `uniqueidentifier` |  | **FK → ProjectMilestone** |
| `project` | lookup | `uniqueidentifier` | required | **FK → Project** |
| `t_019_workitemid` | primarykey | `uniqueidentifier` | **req** |  |
| `workitemid` | nvarchar | `nvarchar(850)` | required |  |
| `workitemname` | nvarchar | `nvarchar(100)` |  |  |
| `workitempriority` | picklist | `int` |  |  |
| `workitemstatus` | picklist | `int` | required |  |
| `workitemtype` | picklist | `int` |  |  |

## Artifact — modelled in C#

`du_T_020_Artifact` → SQL table `Artifact`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `adr` | lookup | `uniqueidentifier` |  | **FK → ADR** |
| `artifactid` | nvarchar | `nvarchar(850)` | required |  |
| `artifactname` | nvarchar | `nvarchar(100)` |  |  |
| `artifacttype` | picklist | `int` | required |  |
| `content` | ntext | `nvarchar(max)` |  |  |
| `conversation` | lookup | `uniqueidentifier` |  | **FK → Conversation** |
| `description` | nvarchar | `nvarchar(100)` |  |  |
| `project` | lookup | `uniqueidentifier` | required | **FK → Project** |
| `t_020_artifactid` | primarykey | `uniqueidentifier` | **req** |  |
| `version` | nvarchar | `nvarchar(100)` |  |  |
| `workitem` | lookup | `uniqueidentifier` |  | **FK → WorkItem** |

## AccessGrant — **not modelled in C#**

`du_T_021_AccessGrant` → SQL table `AccessGrant`

| Column | Type | SQL | Required | Notes |
|---|---|---|---|---|
| `accessgrantid` | nvarchar | `nvarchar(850)` | required |  |
| `expireson` | datetime | `datetime2` |  |  |
| `grantedby` | nvarchar | `nvarchar(100)` |  |  |
| `grantedon` | datetime | `datetime2` |  |  |
| `permission` | picklist | `int` | required |  |
| `principalid` | nvarchar | `nvarchar(100)` | required |  |
| `principaltype` | picklist | `int` | required |  |
| `resourceid` | nvarchar | `nvarchar(100)` | required |  |
| `resourcetype` | picklist | `int` | required |  |
| `t_021_accessgrantid` | primarykey | `uniqueidentifier` | **req** |  |

---

## Schema anomalies

34 issues found while parsing. Your own debt register flags *"Dataverse registry
may differ from live mappings — reconcile logical names and choice values"* — this is that
reconciliation. **Do not carry these into the SQL schema.** Decide which column is real,
drop the other.

| Table | Column(s) | Issue |
|---|---|---|
| ADR | `adrstatus` | picklist with no choice values defined |
| AccessGrant | `permission` | picklist with no choice values defined |
| AccessGrant | `principaltype` | picklist with no choice values defined |
| AccessGrant | `resourcetype` | picklist with no choice values defined |
| Artifact | `artifacttype` | picklist with no choice values defined |
| Branch | `branchstatus` | picklist with no choice values defined |
| Conversation | `conversationstatus` | picklist with no choice values defined |
| Conversation | `conversationtype01` | picklist with no choice values defined |
| Conversation | `conversationvisibility` | picklist with no choice values defined |
| ConversationLink | `linktype` | picklist with no choice values defined |
| ConversationLink | `linktype01` | duplicate of `linktype` |
| ConversationLink | `linktype01` | picklist with no choice values defined |
| Knowledge | `knowledgestatus` | picklist with no choice values defined |
| Knowledge | `knowledgetype` | picklist with no choice values defined |
| MilestoneCriterion | `milestonecriteriastatus` | picklist with no choice values defined |
| Project | `projectstatus01` | duplicate of `projectstatus` |
| Project | `projectstatus01` | picklist with no choice values defined |
| Project | `projecttype01` | duplicate of `projecttype` |
| Project | `projecttype01` | picklist with no choice values defined |
| ProjectMember | `projectmemberrole01` | duplicate of `projectmemberrole` |
| ProjectMember | `projectmemberrole01` | picklist with no choice values defined |
| ProjectMember | `projectmemberstatus` | picklist with no choice values defined |
| ProjectMilestone | `projectmilestonestatus` | picklist with no choice values defined |
| Session | `sessionstatus` | picklist with no choice values defined |
| Team | `teamstatus01` | duplicate of `teamstatus` |
| Team | `teamstatus01` | picklist with no choice values defined |
| TeamMember | `teammemberrole` | picklist with no choice values defined |
| WorkItem | `workitempriority` | picklist with no choice values defined |
| WorkItem | `workitemstatus` | picklist with no choice values defined |
| WorkItem | `workitemtype` | picklist with no choice values defined |
| Workspace | `workspacestatus` | picklist with no choice values defined |
| WorkspaceMember | `workspacememberrole / workspacemembersrole` | near-identical names — likely a typo duplicate |
| WorkspaceMember | `workspacemembersrole` | picklist with no choice values defined |
| WorkspaceMember | `workspacestatus` | picklist with no choice values defined |
