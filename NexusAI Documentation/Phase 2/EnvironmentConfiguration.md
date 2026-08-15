\# NexusAI Environment Configuration



\## Configuration hierarchy



Application configuration is environment-specific.



Development:



`appsettings.Development.json`



Testing:



`appsettings.Test.json`



Production:



Environment variables / secure deployment configuration.



\---



\## Secrets



The following values must never be committed to source control:



\- OpenAI API keys

\- Dataverse credentials

\- Client secrets

\- Certificates

\- Connection secrets



\---



\## OpenAI



Example structure:



```json

{

\\\&#x20; "OpenAI": {

\\\&#x20;   "ApiKey": "",

\\\&#x20;   "Model": "gpt-4.1"

\\\&#x20; }

}



