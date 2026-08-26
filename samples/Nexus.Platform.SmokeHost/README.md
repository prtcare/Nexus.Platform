# Nexus.Platform.SmokeHost

A minimal host that runs a **real** OpenAI chat turn through the platform's routing
gateway (`RoutingModelGateway` → `OpenAIModelGateway`), records usage, and persists the
assistant message to a durable local record — the three M-01-6.1 smoke checks.

It is intentionally **not** part of `Nexus.Platform.slnx`: it calls the live OpenAI API,
which costs money and needs an API key, so it must never run in CI.

## How the key is resolved

The same way `set-openai-key.ps1` stores it: the Nexus.Intelligence.Api user-secrets store
(`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`, key `Platform:Providers:OpenAI:ApiKey`),
with `OPENAI_API_KEY` honoured as the documented environment override. The resolver is wired
through the `ISecretResolver` seam (`StoreSecretResolver`), the same interface a real host
will use once M-01-5.1 lands.

## Usage

```powershell
# Run a real turn (writes a durable record under ./.data and prints it)
dotnet run --project samples\Nexus.Platform.SmokeHost -- send "Reply with exactly one word: pong"

# A FRESH process reads the persisted assistant message back (restart proof)
dotnet run --project samples\Nexus.Platform.SmokeHost -- recv <record-id>
```

## The three smoke tests

`tests\Nexus.Platform.SmokeTests` (also outside the slnx) asserts the three checks by name:

| Test | Check (runbook item) |
|---|---|
| `LiveOpenAI_RealTurn_ReturnsModelResponse` | 12 — chat works end to end |
| `LiveOpenAI_RealTurn_RecordsUsageWithTokenCounts` | 14 — usage recorded |
| `LiveOpenAI_RealTurn_AssistantMessageSurvivesProcessRestart` | 13 — round trip persisted |

```powershell
dotnet test tests\Nexus.Platform.SmokeTests -c Release
```

Usage metering is still the in-memory `InMemoryUsageMeter` (open debt list) — token counts
are real, but they are not durable until a later persistence milestone.
