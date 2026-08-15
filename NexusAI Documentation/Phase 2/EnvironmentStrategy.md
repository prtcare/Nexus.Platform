\# NexusAI Environment Strategy



\## Environments



NexusAI uses three Dataverse environments:



\- Development

\- Testing

\- Production



The deployment path is:



Development

&#x20;   ↓

Testing

&#x20;   ↓

Production



\---



\## Development



Development is the primary environment for local development.



All new Dataverse tables, columns, relationships and configuration are

created and tested here first.



Application development must never depend on Production Dataverse.



\---



\## Testing



Testing receives validated Dataverse solutions from Development.



Testing is used for:



\- Integration testing

\- Repository testing

\- AI retrieval testing

\- Permission testing

\- Conversation testing

\- Milestone testing

\- Production deployment validation



\---



\## Production



Production contains only validated and approved versions.



Production must not be used for:



\- Local development

\- Experimental schema changes

\- Developer testing

\- Temporary data

\- Debugging



\---



\# Dataverse Solution



NexusAI Dataverse components are deployed as a managed solution through:



Development

&#x20;   ↓

Testing

&#x20;   ↓

Production



The solution contains:



\- NexusAI tables

\- Columns

\- Relationships

\- Choices

\- Views

\- Security configuration

\- Other Dataverse components required by NexusAI



\---



\# Environment Configuration



Environment-specific values must not be hard-coded.



Examples:



\- Dataverse URL

\- OpenAI API key

\- OpenAI model

\- Environment name

\- Other service endpoints



Development configuration must remain separate from Testing and Production.



Secrets must not be committed to Git.



\---



\# Source Control



Git stores:



\- C# source code

\- Configuration structure

\- Dataverse schema documentation

\- Deployment documentation



Secrets and environment-specific credentials must not be committed.



\---



\# Deployment Principle



Schema changes are made in Development.



Validated changes are promoted to Testing.



Validated and approved changes are promoted to Production.



Production is never modified manually as part of normal development.



\---



\# Phase 2 Environment Rule



No Phase 2 production data is required during development.



All Phase 2 implementation and schema validation occurs against Development first.





====update====



\# NexusAI Environment Strategy



\## Current Environment



The current Dataverse development environment is:



PRT (Dev)



This is the first NexusAI Dataverse environment.



\---



\## Environment Lifecycle



PRT (Dev)

&#x20;   ↓

Testing

&#x20;   ↓

Production



PRT (Dev) is the schema authoring environment.



No NexusAI production workloads are deployed to PRT (Dev).



\---



\## Current Solution



Solution:



N\_001\_Nexus



Version:



1.0.0.0



Publisher Prefix:



du\_



\---



\## Development Rule



All schema changes are created and validated in PRT (Dev).



Once the schema and application integration are validated,

the solution will be promoted to Testing.



Production will only receive a validated solution.

