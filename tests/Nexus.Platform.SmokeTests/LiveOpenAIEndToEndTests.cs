using System.Diagnostics;
using Nexus.Platform.SmokeHost;
using Xunit;

namespace Nexus.Platform.SmokeTests;

/// <summary>
/// The three smoke tests that were blocked since the V2.1 migration
/// (docs\NEXUS_MIGRATION_RUNBOOK.md verification items 12-14), restated at the
/// platform layer through a REAL OpenAI call - no mocks:
///
///   LiveOpenAI_RealTurn_ReturnsModelResponse                       - 12: chat works end to end
///   LiveOpenAI_RealTurn_RecordsUsageWithTokenCounts                - 14: usage recorded
///   LiveOpenAI_RealTurn_AssistantMessageSurvivesProcessRestart     - 13: round trip persisted
///
/// These are genuine live-call tests against the real OpenAI API and are intentionally kept
/// OUT of Nexus.Platform.slnx (and therefore out of the CI run): they need an API key and cost
/// money, and there is no runtime-skip mechanism in xunit v2. Run them explicitly with a key set:
///
///   dotnet test tests\Nexus.Platform.SmokeTests -c Release
///
/// Running them without a key fails loudly instead of pretending to pass.
/// </summary>
public sealed class LiveOpenAIEndToEndTests
{
    private const string Prompt = "Reply with exactly the word: pong";

    private static void RequireKey()
    {
        if (!SmokeRunner.KeyAvailable())
        {
            Assert.Fail(
                "No OpenAI API key found on this machine (set OPENAI_API_KEY or run " +
                "set-openai-key.ps1). These are live smoke tests; they cannot run without a key.");
        }
    }

    [Fact]
    public async Task LiveOpenAI_RealTurn_ReturnsModelResponse()
    {
        RequireKey();

        var turn = await SmokeRunner.SendTurnAsync(Prompt);

        Assert.NotNull(turn.Record.AssistantContent);
        Assert.False(string.IsNullOrWhiteSpace(turn.Record.AssistantContent));
        Assert.Equal(SmokeRunner.DefaultModelId, turn.Record.ModelUsed);
    }

    [Fact]
    public async Task LiveOpenAI_RealTurn_RecordsUsageWithTokenCounts()
    {
        RequireKey();

        var turn = await SmokeRunner.SendTurnAsync(Prompt);

        var usage = Assert.Single(turn.UsageRecords);
        Assert.True(usage.Usage.TokensIn > 0, $"expected real input tokens, got {usage.Usage.TokensIn}");
        Assert.True(usage.Usage.TokensOut > 0, $"expected real output tokens, got {usage.Usage.TokensOut}");
        Assert.Equal(SmokeRunner.DefaultModelId, usage.ModelId);
    }

    [Fact]
    public async Task LiveOpenAI_RealTurn_AssistantMessageSurvivesProcessRestart()
    {
        RequireKey();

        var hostDll = Path.Combine(AppContext.BaseDirectory, "Nexus.Platform.SmokeHost.dll");
        Assert.True(File.Exists(hostDll), $"SmokeHost dll not copied to test output: {hostDll}");

        // Process A (write): the host runs a real turn and persists it, then EXITS.
        var (sendOut, sendExit) = await RunHostAsync(hostDll, "send", Prompt);
        Assert.Equal(0, sendExit);

        var id = ReadLine(sendOut, "ID=");
        var assistant = ReadLine(sendOut, "ASSISTANT=");
        Assert.False(string.IsNullOrWhiteSpace(id), $"no record id in host output:\n{sendOut}");
        Assert.False(string.IsNullOrWhiteSpace(assistant), $"no assistant content in host output:\n{sendOut}");

        // Process B (read): a genuinely fresh OS process retrieves the same message.
        var (recvOut, recvExit) = await RunHostAsync(hostDll, "recv", id!);
        Assert.Equal(0, recvExit);
        Assert.Equal(assistant, recvOut.Trim());
    }

    private static async Task<(string Output, int ExitCode)> RunHostAsync(string dll, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet", new[] { dll }.Concat(args))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start dotnet");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            throw new InvalidOperationException($"SmokeHost stderr:\n{stderr}");
        }

        return (stdout, process.ExitCode);
    }

    private static string? ReadLine(string output, string prefix)
        => output.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith(prefix))
            ?[prefix.Length..];
}
