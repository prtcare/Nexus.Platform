using System.Diagnostics;
using System.Text;

namespace Nexus.Delivery.Core;

/// <summary>
/// Runs an external process and captures its output.
///
/// <para>
/// <b>No shell.</b> Arguments are passed as a list to <see cref="ProcessStartInfo.ArgumentList"/>, never
/// concatenated into a command line, so a path containing a space or a quote cannot become an extra
/// argument or a command. That matters here because the paths involved are real source directories.
/// </para>
///
/// <para>
/// Standard output and error are captured rather than streamed, so a failing step's output is available
/// for the evidence record. Environment variables are cleared to a minimal set rather than inherited:
/// an inherited environment is ambient state, and a build that depends on it cannot be reproduced from its
/// record.
/// </para>
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly TimeSpan _timeout;

    public ProcessRunner(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromMinutes(20);
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Deterministic host for the child: locale-independent output and no ambient dotnet state that
        // could change what a build does.
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };

        if (!process.Start())
        {
            return new ProcessResult(-1, string.Empty, $"Could not start '{fileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited between the cancellation and the kill.
            }

            return new ProcessResult(-1, stdout.ToString(), $"'{fileName}' did not complete within {_timeout.TotalMinutes:0} minutes.");
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
