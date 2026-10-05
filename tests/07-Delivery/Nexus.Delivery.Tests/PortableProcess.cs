using System.Diagnostics;

namespace Nexus.Delivery.Tests;

/// <summary>
/// W10.0A FINAL — the one place a test starts a real child process.
///
/// <para>
/// <b>Why this exists.</b> <c>W94InstrumentDefectReproductionTests</c> reproduces the D-R2 teardown
/// defect, whose subject is a <see cref="Process"/> object that was started and then disposed. To
/// reproduce it the test needs a real process that exits promptly. It used
/// <c>Process.Start("cmd", "/c exit 0")</c> — a Windows shell — which meant a control about
/// <b>platform-neutral</b> behaviour (<c>ProcessTermination</c> must not throw for an object that no
/// longer addresses a process) could not run on the Linux CI runner. It failed there with
/// <i>"An error occurred trying to start process 'cmd' … No such file or directory"</i>.
/// </para>
///
/// <para>
/// <b>Why this is a helper and not an inline fix.</b> The defect was not the string <c>"cmd"</c>; it
/// was that a Windows assumption was embedded in a test whose subject is platform-neutral. Replacing
/// it inline would leave the next test free to make the same assumption. There is exactly one place
/// in this assembly that starts a child process, and it is here.
/// </para>
///
/// <para>
/// <b>Why the .NET host.</b> The test is running under it, so it is present by construction on every
/// machine and every CI image that can run these tests — no platform branch, no shell, no
/// <c>/bin/true</c> versus <c>cmd</c> choice to keep in sync. <c>--version</c> prints and exits on
/// both Windows and Unix.
/// </para>
///
/// <para>
/// <b>What this deliberately is NOT.</b> It is not a test that tolerates the process failing to start.
/// The returned process is asserted by its callers to be real and exited, so a helper that silently
/// produced nothing would fail the control rather than let it pass vacuously.
/// </para>
/// </summary>
internal static class PortableProcess
{
    /// <summary>
    /// Starts a real child process that exits promptly, on Windows and on Unix alike.
    /// </summary>
    public static Process StartOneThatExitsImmediately()
    {
        var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        if (process is null)
        {
            throw new InvalidOperationException(
                "PortableProcess could not start the .NET host. This helper must never return null: a "
                + "caller that received nothing would be reproducing 'no process was ever tracked', "
                + "which is a different case from the one under test.");
        }

        return process;
    }
}
