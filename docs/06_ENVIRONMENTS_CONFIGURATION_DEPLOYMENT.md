# Environments, Configuration, and Deployment

## Environment strategy

NexusAI uses three Dataverse environments:

| Environment | Purpose | Allowed activity |
|---|---|---|
| Development | Authoring and developer integration | Create/change schema, local API testing, seed non-sensitive test data |
| Testing | Release validation | Managed-solution import, integration/UAT, migration and rollback testing |
| Production | Live operation | Approved managed releases only; no ad-hoc development |

Deployment flow:

`Development → Managed solution → Testing → Validated managed solution → Production`

Application development must never depend on Production Dataverse.

## Dataverse solution

- Development environment recorded in the source documentation: `PRT (Dev)`.
- Solution: `N_001_Nexus`.
- Recorded baseline version: `1.0.0.0`.
- Publisher prefix: `du_`.
- Schema changes are authored only in Development.
- Export managed packages for Testing and Production.
- Use semantic solution version increments and retain release artifacts.

## Configuration hierarchy

- Shared non-secret defaults: `appsettings.json`.
- Local development overrides: `appsettings.Development.json` plus user secrets.
- Test overrides: dedicated test settings and secure pipeline variables.
- Production: environment variables, managed secret store, or deployment-platform secret configuration.

Never duplicate the same live secret across Api and Host configuration. The selected canonical process owns its configuration.

## Required configuration groups

```json
{
  "OpenAI": {
    "ApiKey": "",
    "Model": ""
  },
  "Dataverse": {
    "Url": "",
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": ""
  }
}
```

Exact keys must match the options classes. If client-secret authentication is used, Tenant ID, Client ID, Client Secret, and environment URL come from the Entra app registration and Power Platform environment. Prefer certificate or managed identity where supported for production.

## Secret rules

Never commit:

- OpenAI or other model-provider API keys;
- Dataverse client secrets;
- passwords or connection strings containing credentials;
- certificates/private keys;
- production tenant details that are not intended to be public.

Use .NET user secrets for local work. Use protected variables/secret stores in CI/CD. Rotate any secret that appears in source control history; deleting it only from the latest file is insufficient.

## Development deployment process

1. Confirm the live development schema and approved naming registry.
2. Make solution changes in Development.
3. Implement matching domain/infrastructure/application/API changes.
4. Restore and build.
5. Run unit and contract tests.
6. Verify routes through Swagger.
7. Verify actual Dataverse records and round trips.
8. Update solution version and documentation.
9. Export an unmanaged backup for development recovery as required.
10. Export a managed solution for Testing.

## Test promotion

1. Back up the Test environment.
2. Import the managed solution.
3. Apply environment-specific connection references and variables.
4. Run smoke, integration, security-role, and data-migration tests.
5. Record approval, defects, and rollback result.
6. Promote the exact validated managed artifact; do not rebuild a different package for Production.

## Production promotion

1. Obtain release approval.
2. Confirm backup and rollback plan.
3. Import the exact tested managed solution.
4. Apply production connection references/environment variables.
5. Deploy the matching API/frontend versions.
6. Run smoke tests and monitor errors, latency, Dataverse throttling, and model usage.
7. Record release version and outcome.

## API deployment requirements

- HTTPS only.
- CORS restricted to known clients.
- Authentication and authorization enabled before public release.
- Health/readiness endpoints.
- Central structured logging and correlation IDs.
- Rate limiting and request-size limits.
- Retry/backoff for transient Dataverse/provider failures.
- No secrets in logs or error responses.
- Environment-specific API base URLs.

## Frontend deployment requirements

- Backend API URL supplied through environment configuration.
- No Dataverse or LLM credentials in client bundles.
- Versioned static assets and safe rollback.
- CSP and secure headers.
- Observability for UI errors and failed API calls without logging sensitive content.

## Repository packaging

Source handoffs should exclude `.git/objects`, `bin`, and `obj`. Keep source, solution/project files, deployment definitions, migrations/solution artifacts, tests, and canonical documentation. Use Git commits/tags for history instead of nested documentation ZIPs.
