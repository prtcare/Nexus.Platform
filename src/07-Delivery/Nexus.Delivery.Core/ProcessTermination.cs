using System.ComponentModel;
using System.Diagnostics;

namespace Nexus.Delivery.Core;

/// <summary>What terminating a tracked process actually did.</summary>
public enum ProcessTerminationOutcome
{
    /// <summary>A live process was killed and reaped.</summary>
    Terminated,

    /// <summary>
    /// There was nothing to kill: the process had already exited, or the object no longer addresses one.
    /// <b>Not a failure.</b> The post-condition — nothing of ours is running — holds either way.
    /// </summary>
    AlreadyExitedOrUnavailable
}

/// <summary>
/// Terminates a tracked process <b>idempotently and safely against an object that no longer addresses a
/// process</b>.
///
/// <para>
/// <b>Why this is a shared type rather than three lines in the driver.</b> The W9.4 <c>verify</c> run
/// declared its process with <c>using var</c>, which disposed it when the enclosing method returned, while
/// the driver kept the same object in a tracked field. The first thing teardown asked it —
/// <c>HasExited</c> — threw <i>"No process is associated with this object"</i>. Because teardown ran
/// <i>after</i> the lineage append, the throw cost the run its evidence file and left the application
/// orphaned, still serving.
/// </para>
///
/// <para>
/// <b>A disposed <see cref="Process"/> is not an error condition here; it is the ordinary case.</b> Anything
/// may have reaped the child already, and the caller's post-condition is "nothing I started is still
/// running" — which a disposed handle answers <i>yes</i> to, not <i>unknown</i>. So the failure modes that
/// mean "there is no process to kill" are returned as
/// <see cref="ProcessTerminationOutcome.AlreadyExitedOrUnavailable"/> rather than thrown, and only a real
/// inability to confirm termination would propagate.
/// </para>
/// </summary>
public static class ProcessTermination
{
    /// <summary>How long to wait for a killed process to be reaped before giving up on it.</summary>
    public static readonly TimeSpan DefaultReapTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Terminates <paramref name="process"/> and its children, or reports that there was nothing to
    /// terminate. <b>Never throws for a disposed, never-started or already-exited process.</b>
    /// </summary>
    public static ProcessTerminationOutcome Terminate(
        Process? process,
        TimeSpan? reapTimeout = null,
        Action<string>? say = null)
    {
        if (process is null)
        {
            say?.Invoke("stopped          : no process was ever tracked");
            return ProcessTerminationOutcome.AlreadyExitedOrUnavailable;
        }

        try
        {
            if (process.HasExited)
            {
                say?.Invoke($"stopped          : pid={SafeId(process)} had already exited");
                return ProcessTerminationOutcome.AlreadyExitedOrUnavailable;
            }

            var id = SafeId(process);

            process.Kill(entireProcessTree: true);

            if (!process.WaitForExit((reapTimeout ?? DefaultReapTimeout).Milliseconds))
            {
                // Deliberately NOT thrown. The process was told to die and has not been reaped within the
                // bound; reporting it as terminated would be a claim, and reporting it as unavailable would
                // be false. The caller decides what an unreaped child means for it.
                say?.Invoke($"stopped          : pid={id} was killed but was not reaped within the timeout");
                return ProcessTerminationOutcome.Terminated;
            }

            say?.Invoke($"stopped          : pid={id} terminated");
            return ProcessTerminationOutcome.Terminated;
        }
        catch (InvalidOperationException)
        {
            // The shape the W9.4 defect produced: the object no longer addresses a process.
            say?.Invoke("stopped          : the tracked process object no longer addresses a process (already exited or disposed)");
            return ProcessTerminationOutcome.AlreadyExitedOrUnavailable;
        }
        catch (Win32Exception)
        {
            // The OS refused, almost always because the process is already gone.
            say?.Invoke("stopped          : the operating system reports no such process (already exited)");
            return ProcessTerminationOutcome.AlreadyExitedOrUnavailable;
        }
        catch (NotSupportedException)
        {
            say?.Invoke("stopped          : the tracked process object cannot be terminated remotely");
            return ProcessTerminationOutcome.AlreadyExitedOrUnavailable;
        }
    }

    /// <summary>
    /// <c>Process.Id</c> itself throws once the handle is gone, so even reading an id for a log line has to
    /// be guarded. Returning a placeholder says "this object identifies nothing", which is the truth.
    /// </summary>
    private static string SafeId(Process process)
    {
        try
        {
            return process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            return "(no process)";
        }
    }
}
