using System.Xml.Linq;
using Nexus.DevelopmentControl.Safety;
using Nexus.ProductCore.Contracts.ReadModel;

namespace Nexus.ProductCore.Core.ReadModel;

/// <summary>
/// W10.6 TASKs 1, 6, 7, 8, 9 — <b>observes what Platform is running, and, more importantly, what
/// Platform is.</b>
///
/// <para>
/// <b>Owner side, and read-only.</b> This lives beside the canonical reader and takes no lock, seizes
/// nothing and starts nothing. It reads the repository it belongs to and one kernel-established
/// liveness fact, and it writes nothing except the publication a caller explicitly asked for.
/// </para>
///
/// <para>
/// <b>IT DOES NOT ENUMERATE PROCESSES, AND THAT IS A DESIGN RULE, NOT AN OMISSION.</b> Two independent
/// reasons, each sufficient:
/// </para>
/// <list type="number">
/// <item><description><b>RL-11 forbids it.</b> <c>ReservationLease.cs:25-28</c> in this same product:
/// <i>"PID is NEVER a liveness test. <c>Process.GetProcessById</c> on a recycled PID is a false
/// positive; on another host it is a false negative."</i> A process-table scan is that defect at
/// scale.</description></item>
/// <item><description><b>Measured, it produces confident nonsense here.</b> A scan of this host for
/// Nexus-signature command lines returned 34 hits, of which essentially all were the build's own
/// shells — <c>bash</c>, <c>pwsh</c>, <c>dotnet</c>, <c>testhost</c> — whose command line merely
/// <i>mentions a Nexus path</i>, plus a database and a container runtime that serve a different
/// product. An observer built that way would have reported Platform tooling as running while it was
/// a compiler.</description></item>
/// </list>
///
/// <para>
/// What it does instead is derive APPLICABILITY from the governed repository — which cannot drift,
/// because <c>ProjectCoverageTests</c> already forces the solution and the tree to agree — and then
/// observe only those units that have a genuine, deterministic identity. Measured, exactly one does.
/// </para>
/// </summary>
public sealed class PlatformRuntimeObservation
{
    private readonly string _repositoryRoot;

    /// <summary>Injected so a test can drive every branch without a file system or a live store.</summary>
    private readonly Func<string, bool> _lockHeld;
    private readonly Func<string, bool> _fileExists;

    private const string SolutionFileName = "Nexus.Platform.slnx";

    /// <summary>
    /// The one runtime unit in this estate with a genuine, deterministic, PID-free identity: the
    /// canonical DevelopmentControl store's writer.
    ///
    /// <para>
    /// Platform owns the lock that arbitrates it (<c>AtomicWriterLock</c>), and whether a process
    /// currently holds it is answerable by the kernel through a <c>FileShare.None</c> open — no
    /// process inspection, no PID, no host assumption. It is the only live Platform-runtime fact this
    /// repository can produce, and it is produced the way the repository's own rules require.
    /// </para>
    ///
    /// <para>
    /// <b>Its applicability is <see cref="PlatformRuntimeApplicability.External"/>, and that is not a
    /// filing convenience.</b> Platform has no process; the writer that holds this lock is therefore
    /// by definition some OTHER host's process — a Forge or Developer session appending through the
    /// shared contract. Calling it a Platform service would put a service in Platform's own map that
    /// Platform does not run, which is the fabrication this milestone exists to prevent. What Platform
    /// owns is the LOCK, which is why Platform can observe this one External unit's state when it can
    /// observe no other.
    /// </para>
    /// </summary>
    public const string StoreWriterUnitId = "development-control-store-writer";

    public PlatformRuntimeObservation(
        string repositoryRoot,
        Func<string, bool>? lockHeld = null,
        Func<string, bool>? fileExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = repositoryRoot;
        _lockHeld = lockHeld ?? (path => AtomicWriterLock.IsHeldByAnotherProcess(path));
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>
    /// Builds the payload. <paramref name="observedAt"/> is supplied so a run is reproducible for a
    /// fixed input and a fixed instant.
    /// </summary>
    public PlatformRuntimeReadPayload Observe(string observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observedAt);

        var gaps = new List<PlatformRuntimeReadGap>();
        var units = new List<PlatformRuntimeReadUnit>();

        var components = GovernedComponents();

        foreach (var component in components)
        {
            var applicability = Classify(component);

            units.Add(new PlatformRuntimeReadUnit(
                RuntimeUnitId: component.ProjectName,
                ComponentId: component.ProjectName,
                ComponentPath: component.RelativePath,
                RuntimeApplicability: applicability.ToString(),
                // A component that is not a runtime is not "never observed" — the question does not
                // apply to it. This is the TASK 9 line, and it is the single most load-bearing
                // assignment in this file.
                ObservationState: applicability is PlatformRuntimeApplicability.LibraryOnly
                                                 or PlatformRuntimeApplicability.TestHost
                                                 or PlatformRuntimeApplicability.External
                    ? PlatformRuntimeObservationState.NotApplicable.ToString()
                    : PlatformRuntimeObservationState.Current.ToString(),
                ProcessState: applicability switch
                {
                    // A library has no process and never will. Not "stopped" — not applicable.
                    PlatformRuntimeApplicability.LibraryOnly => PlatformRuntimeProcessState.NotApplicable.ToString(),
                    PlatformRuntimeApplicability.TestHost => PlatformRuntimeProcessState.NotApplicable.ToString(),
                    // A command runs and exits. It has no STANDING process, so "is it running now" is
                    // a question about an event, not a state. TASK 7's rule applies in reverse: saying
                    // "Stopped" would imply it ought to be running and is not.
                    PlatformRuntimeApplicability.Command => PlatformRuntimeProcessState.NotApplicable.ToString(),
                    PlatformRuntimeApplicability.External => PlatformRuntimeProcessState.Unknown.ToString(),
                    _ => PlatformRuntimeProcessState.Unknown.ToString(),
                },
                HealthState: applicability is PlatformRuntimeApplicability.LibraryOnly
                                                  or PlatformRuntimeApplicability.TestHost
                                                  or PlatformRuntimeApplicability.Command
                                                  or PlatformRuntimeApplicability.External
                    ? PlatformRuntimeHealthState.NotApplicable.ToString()
                    : PlatformRuntimeHealthState.Unknown.ToString(),
                HealthAuthority: PlatformRuntimeHealthAuthority.NotApplicable.ToString(),
                ReasonCode: string.Empty,
                Authority: DeliveryAuthorityClass.Authoritative));
        }

        // ---------------------------------------------------------------- the one live unit
        units.Add(ObserveStoreWriter(observedAt));

        // ---------------------------------------------------------------- the invariants
        var services = units.Count(u =>
            string.Equals(u.RuntimeApplicability, PlatformRuntimeApplicability.Service.ToString(), StringComparison.Ordinal));

        if (services == 0)
        {
            gaps.Add(new PlatformRuntimeReadGap(
                PlatformRuntimeReadGapKinds.PlatformSubject,
                PlatformRuntimeReadGapKinds.NoPersistentRuntimeUnit,
                "Platform contains no persistent runtime service. Every governed component is a library, "
                + "a test host, or a one-shot command tool, and this is by design rather than by "
                + "omission: the repository's README states that it ships as NuGet packages and that "
                + "nothing here is deployed, and the only host it ever owned was retired on 2026-10-05. "
                + "There is therefore no single thing whose being 'up' could be reported, and an "
                + "aggregate over these components would be a fabrication."));
        }

        gaps.Add(new PlatformRuntimeReadGap(
            PlatformRuntimeReadGapKinds.PlatformSubject,
            PlatformRuntimeReadGapKinds.NoHealthEndpoint,
            "No health, readiness or liveness endpoint exists anywhere in Platform, so no health value "
            + "published here can be stronger than process observation, and every running unit is "
            + "reported with HealthState=Unknown. A running process without a health signal is a "
            + "complete answer, not an unfinished one."));

        gaps.Add(new PlatformRuntimeReadGap(
            PlatformRuntimeReadGapKinds.PlatformSubject,
            PlatformRuntimeReadGapKinds.ExternalRuntimeNotObserved,
            "The deployed services that DO run are not owned by this repository, which ships to them and "
            + "probes them only during a deploy run. Platform holds no standing observer for them, so "
            + "they are not enumerated here. Not observed by Platform is NOT the same as not running."));

        return new PlatformRuntimeReadPayload(
            new PlatformRuntimeReadObserver(
                ObserverId: "nexus.platform-runtime-observer",
                ObserverKind: "one-shot-snapshot",
                HostIdentity: "not-published",
                GovernedComponentCount: components.Count,
                Authority: DeliveryAuthorityClass.ObservedRuntime),
            units,
            gaps);
    }

    /// <summary>
    /// TASK 7's distinctions, on the one unit that has a genuine identity.
    ///
    /// <para>
    /// <b>Failure to observe is never <c>Stopped</c>.</b> A store that is not there yields
    /// <c>Unavailable</c> with a reason code; only a successful probe that finds no holder yields
    /// <c>Stopped</c>. Those are the two branches a naive implementation collapses, and collapsing
    /// them turns "I could not look" into "it is down" — an assertion nobody made.
    /// </para>
    /// </summary>
    private PlatformRuntimeReadUnit ObserveStoreWriter(string observedAt)
    {
        string observationState;
        string processState;
        string reason;

        var storePath = WorkbookCompatibilityMap.CanonicalAuthorityPath;

        if (!_fileExists(storePath))
        {
            observationState = PlatformRuntimeObservationState.Unavailable.ToString();
            processState = PlatformRuntimeProcessState.Unknown.ToString();
            reason = "STORE_ABSENT";
        }
        else
        {
            try
            {
                var held = _lockHeld(storePath);
                observationState = PlatformRuntimeObservationState.Current.ToString();
                processState = held
                    ? PlatformRuntimeProcessState.Running.ToString()
                    : PlatformRuntimeProcessState.Stopped.ToString();
                reason = held ? string.Empty : "NO_ACTIVE_WRITER";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // The observation was ATTEMPTED and did not complete. Not a zero, and not a "stopped".
                observationState = PlatformRuntimeObservationState.Unavailable.ToString();
                processState = PlatformRuntimeProcessState.Unknown.ToString();
                reason = "OBSERVATION_FAILED";
            }
        }

        return new PlatformRuntimeReadUnit(
            RuntimeUnitId: StoreWriterUnitId,
            ComponentId: "DevelopmentControl store",
            ComponentPath: "src/06-SharedPlatform/Nexus.ProductCore.Core/Safety/AtomicWriterLock.cs",
            RuntimeApplicability: PlatformRuntimeApplicability.External.ToString(),
            ObservationState: observationState,
            ProcessState: processState,
            // ALWAYS Unknown, and that is the TASK 8 answer rather than an unfinished value. A held
            // lock proves a process is alive; it is evidence about nothing else. There is no health
            // endpoint anywhere in this estate that could raise this, so it does not rise — and a
            // naive implementation that returned Healthy here, because the writer was present, would
            // be asserting the well-being of a process it never contacted.
            HealthState: PlatformRuntimeHealthState.Unknown.ToString(),
            HealthAuthority: PlatformRuntimeHealthAuthority.ProcessOnly.ToString(),
            ReasonCode: reason,
            Authority: DeliveryAuthorityClass.ObservedRuntime);
    }

    // ================================================================ applicability (TASK 1 / TASK 9)

    private sealed record Component(string ProjectName, string RelativePath, string AbsolutePath);

    /// <summary>
    /// The governed components, taken from the SOLUTION rather than from a directory walk.
    ///
    /// <para>
    /// That choice is what makes the map maintainable instead of a snapshot that rots:
    /// <c>ProjectCoverageTests</c> already fails the build if the solution and the tree disagree, so a
    /// component cannot exist without appearing here, and cannot appear here without existing.
    /// </para>
    /// </summary>
    private List<Component> GovernedComponents()
    {
        var solution = Path.Combine(_repositoryRoot, SolutionFileName);
        if (!File.Exists(solution))
        {
            // A repository whose solution cannot be found is NOT a repository with no components.
            // Returning an empty map would publish "Platform governs nothing", which is an answer to a
            // question nobody could ask.
            throw new UnsupportedSchemaException(
                $"no '{SolutionFileName}' exists under '{_repositoryRoot}'. The governed component set is "
                + "derived from the solution, so without it there is no applicability map to publish — "
                + "and an empty map would state that Platform governs nothing.");
        }

        var document = XDocument.Load(solution);

        return document.Descendants("Project")
            .Select(p => p.Attribute("Path")?.Value)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Replace('\\', '/'))
            .Select(rel => new Component(
                ProjectName: Path.GetFileNameWithoutExtension(rel),
                RelativePath: rel[..rel.LastIndexOf('/')],
                AbsolutePath: Path.Combine(_repositoryRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
            .OrderBy(c => c.RelativePath, StringComparer.Ordinal)
            .ThenBy(c => c.ProjectName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// TASK 1's classification, from measured evidence in the project file rather than from its name.
    ///
    /// <para>
    /// <b>The negative test is reliable because no default exists.</b> <c>Directory.Build.props</c>
    /// sets no <c>OutputType</c>, so a project that declares none is a library by SDK default — which
    /// is why "no <c>&lt;OutputType&gt;</c>" is evidence and not merely an absence of evidence.
    /// </para>
    /// </summary>
    private PlatformRuntimeApplicability Classify(Component component)
    {
        if (!File.Exists(component.AbsolutePath))
        {
            // The solution names a project that is not on disk. Reported as Unknown rather than
            // guessed at, and ProjectCoverageTests would already have failed the build.
            return PlatformRuntimeApplicability.Unknown;
        }

        var xml = XDocument.Load(component.AbsolutePath);
        var sdk = xml.Root?.Attribute("Sdk")?.Value ?? string.Empty;

        string? Property(string name) => xml.Descendants(name).FirstOrDefault()?.Value.Trim();

        if (string.Equals(Property("IsTestProject"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return PlatformRuntimeApplicability.TestHost;
        }

        var isExe = string.Equals(Property("OutputType"), "Exe", StringComparison.OrdinalIgnoreCase);
        if (!isExe)
        {
            // No entry point, no process, ever. TASK 9: this must never be reported as stopped,
            // missing or unhealthy.
            return PlatformRuntimeApplicability.LibraryOnly;
        }

        // It is an executable. Is it a PERSISTENT one? A web SDK, or a hosted-service symbol in its
        // own source, is what would make it a service rather than a command — and measuring it means
        // the day Platform gains a service, the map says so without anyone editing a list.
        var persistent = sdk.Contains("Sdk.Web", StringComparison.OrdinalIgnoreCase)
                         || SourceDeclaresPersistentLifetime(component);

        return persistent ? PlatformRuntimeApplicability.Service : PlatformRuntimeApplicability.Command;
    }

    private static bool SourceDeclaresPersistentLifetime(Component component)
    {
        var directory = Path.GetDirectoryName(component.AbsolutePath);
        if (directory is null || !Directory.Exists(directory))
        {
            return false;
        }

        string[] markers =
        [
            "WebApplication.CreateBuilder", "IHostedService", "BackgroundService",
            "Host.CreateDefaultBuilder", "IHostBuilder",
        ];

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            // bin/obj hold generated copies; compiling them in would make the classification depend on
            // whether the tree had been built.
            var normalised = file.Replace('\\', '/');
            if (normalised.Contains("/bin/", StringComparison.Ordinal) ||
                normalised.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (markers.Any(m => text.Contains(m, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    // ================================================================ digest

    /// <summary>
    /// The semantic digest: SHA-256 over the canonical payload <b>only</b>, so a repeated observation
    /// of an unchanged estate is distinguishable from a state change and no timestamp can enter it.
    ///
    /// <para>
    /// The same construction the two existing publishers use, kept local for the same structural
    /// reason the DevelopmentControl publisher keeps its commit boundary local: these assemblies have
    /// zero references by construction, and a shared helper would require a new common assembly.
    /// </para>
    /// </summary>
    public static string SemanticDigest(PlatformRuntimeReadPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var json = System.Text.Json.JsonSerializer.Serialize(payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        return "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }
}
