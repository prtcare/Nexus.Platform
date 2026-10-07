using Nexus.ProductCore.Contracts.ReadModel;
using Nexus.ProductCore.Core.ReadModel;
using Xunit;

namespace Nexus.ProductCore.DevelopmentControl.Tests;

/// <summary>
/// W10.6 — <b>the Platform runtime observer, and the failure controls that decide whether what it
/// publishes is safe to render.</b>
///
/// <para>
/// <b>The observer is exercised against a SYNTHETIC repository root and injected probes.</b> That is
/// not a shortcut: the real repository is the observation's subject, and a test that read it would
/// assert today's project count and break the first time a project was added. What is under test here
/// is the DECISION LOGIC — how a component is classified, and what is published when the observation
/// succeeds, fails, or cannot be taken at all.
/// </para>
///
/// <para>
/// <b>Every control is paired.</b> A test asserting "a library is not stopped" passes just as well
/// against an observer that classifies everything as a library and reports nothing, so each absence
/// below is asserted beside the presence it is distinguished from.
/// </para>
/// </summary>
public sealed class PlatformRuntimeObservationTests : IDisposable
{
    private const string ObservedAt = "2026-10-07T12:00:00Z";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "w106-runtime-" + Guid.NewGuid().ToString("N")[..12]);

    public PlatformRuntimeObservationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // An inert temp directory is not a test failure.
        }
    }

    // ================================================================ a governed repository

    /// <summary>
    /// Builds a synthetic governed repository: a solution plus the projects it names.
    ///
    /// <para>
    /// The solution is generated because the observer DERIVES its component set from one — which is
    /// the property that keeps the real map from drifting, and therefore the property a test must
    /// exercise rather than bypass.
    /// </para>
    /// </summary>
    private string Repo(params (string RelPath, string ProjectName, string? OutputType, bool IsTest, bool WebSdk)[] projects)
    {
        var lines = new List<string> { "<Solution>" };

        foreach (var (relPath, projectName, outputType, isTest, webSdk) in projects)
        {
            var dir = Path.Combine(_root, relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);

            var sdk = webSdk ? "Microsoft.NET.Sdk.Web" : "Microsoft.NET.Sdk";
            var body = $"<Project Sdk=\"{sdk}\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n";
            if (outputType is not null) body += $"    <OutputType>{outputType}</OutputType>\n";
            if (isTest) body += "    <IsTestProject>true</IsTestProject>\n";
            body += "  </PropertyGroup>\n</Project>\n";

            var csproj = Path.Combine(dir, projectName + ".csproj");
            File.WriteAllText(csproj, body);
            lines.Add($"    <Project Path=\"{relPath}\\{projectName}.csproj\" />");
        }

        lines.Add("</Solution>");
        File.WriteAllText(Path.Combine(_root, "Nexus.Platform.slnx"), string.Join('\n', lines));
        return _root;
    }

    /// <summary>
    /// An observation whose probes are injected, so every branch is reachable without a live store.
    /// <paramref name="throwOnProbe"/> drives the "the observation was attempted and did not complete"
    /// path, which is the one TASK 7 is written about.
    /// </summary>
    private PlatformRuntimeObservation Observation(bool storeExists = true, bool lockHeld = false, bool throwOnProbe = false) =>
        new(_root,
            lockHeld: _ => throwOnProbe ? throw new IOException("probe failed") : lockHeld,
            fileExists: _ => storeExists);

    private static string ApplicabilityOf(PlatformRuntimeReadPayload payload, string unitId) =>
        payload.Units.Single(u => u.RuntimeUnitId == unitId).RuntimeApplicability;

    private static PlatformRuntimeReadUnit Unit(PlatformRuntimeReadPayload payload, string unitId) =>
        payload.Units.Single(u => u.RuntimeUnitId == unitId);

    // ================================================================ TASK 1 / TASK 9

    /// <summary>
    /// <b>A library is not a stopped service.</b> This is TASK 9, and it is load-bearing rather than
    /// an edge case: every one of Platform's thirteen `src` projects is a library.
    /// </summary>
    [Fact]
    public void A_library_is_LibraryOnly_with_NotApplicable_runtime_state()
    {
        Repo(("src/Nexus.Platform.Contracts", "Nexus.Platform.Contracts", null, false, false));
        var payload = Observation().Observe(ObservedAt);
        var unit = Unit(payload, "Nexus.Platform.Contracts");

        Assert.Equal(PlatformRuntimeApplicability.LibraryOnly.ToString(), unit.RuntimeApplicability);
        Assert.Equal(PlatformRuntimeObservationState.NotApplicable.ToString(), unit.ObservationState);
        Assert.Equal(PlatformRuntimeProcessState.NotApplicable.ToString(), unit.ProcessState);
        Assert.Equal(PlatformRuntimeHealthState.NotApplicable.ToString(), unit.HealthState);

        // ...and specifically NOT any of the three states that would assert a fault that cannot exist.
        Assert.NotEqual(PlatformRuntimeProcessState.Stopped.ToString(), unit.ProcessState);
        Assert.NotEqual(PlatformRuntimeProcessState.Running.ToString(), unit.ProcessState);
        Assert.NotEqual(PlatformRuntimeHealthState.Unhealthy.ToString(), unit.HealthState);
    }

    [Fact]
    public void A_test_host_is_TestHost_and_not_a_deployed_runtime()
    {
        Repo(("tests/Nexus.Thing.Tests", "Nexus.Thing.Tests", null, true, false));
        var payload = Observation().Observe(ObservedAt);
        var unit = Unit(payload, "Nexus.Thing.Tests");

        Assert.Equal(PlatformRuntimeApplicability.TestHost.ToString(), unit.RuntimeApplicability);
        Assert.Equal(PlatformRuntimeProcessState.NotApplicable.ToString(), unit.ProcessState);
    }

    /// <summary>
    /// An executable is a COMMAND, and a command has no STANDING process — so its process state is
    /// <c>NotApplicable</c> rather than <c>Stopped</c>. Saying "Stopped" would imply it ought to be
    /// running and is not; a one-shot tool is idle by design.
    /// </summary>
    [Fact]
    public void An_executable_is_a_COMMAND_with_no_standing_process_state()
    {
        Repo(("tools/Nexus.Delivery.Publish", "Nexus.Delivery.Publish", "Exe", false, false));
        var payload = Observation().Observe(ObservedAt);
        var unit = Unit(payload, "Nexus.Delivery.Publish");

        Assert.Equal(PlatformRuntimeApplicability.Command.ToString(), unit.RuntimeApplicability);
        Assert.Equal(PlatformRuntimeProcessState.NotApplicable.ToString(), unit.ProcessState);
        Assert.Equal(PlatformRuntimeHealthState.NotApplicable.ToString(), unit.HealthState);
    }

    /// <summary>
    /// A web-SDK executable IS a service — the one classification that would change if Platform ever
    /// gained a host. Measured against a synthetic one because the real repository has none.
    /// </summary>
    [Fact]
    public void A_web_sdk_executable_is_a_SERVICE()
    {
        Repo(("tools/Nexus.SomeHost", "Nexus.SomeHost", "Exe", false, webSdk: true));
        var payload = Observation().Observe(ObservedAt);

        Assert.Equal(PlatformRuntimeApplicability.Service.ToString(),
            Unit(payload, "Nexus.SomeHost").RuntimeApplicability);
    }

    [Fact]
    public void A_repository_with_no_solution_refuses_rather_than_publishing_an_empty_map()
    {
        // A root with no solution is NOT a repository with no components.
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        var exception = Assert.Throws<UnsupportedSchemaException>(() => new PlatformRuntimeObservation(empty).Observe(ObservedAt));
        Assert.Contains("governs nothing", exception.Message, StringComparison.Ordinal);
    }

    // ================================================================ TASK 7

    /// <summary>
    /// <b>The distinction TASK 7 is written about.</b> A probe that FAILED yields <c>Unavailable</c> +
    /// <c>Unknown</c>. It must never yield <c>Stopped</c>, because "I could not look" and "it is down"
    /// are different assertions and only one of them was made.
    /// </summary>
    [Fact]
    public void A_failed_observation_is_UNAVAILABLE_and_never_STOPPED()
    {
        Repo();
        var payload = Observation(throwOnProbe: true).Observe(ObservedAt);
        var unit = Unit(payload, PlatformRuntimeObservation.StoreWriterUnitId);

        Assert.Equal(PlatformRuntimeObservationState.Unavailable.ToString(), unit.ObservationState);
        Assert.Equal(PlatformRuntimeProcessState.Unknown.ToString(), unit.ProcessState);
        Assert.Equal("OBSERVATION_FAILED", unit.ReasonCode);

        Assert.NotEqual(PlatformRuntimeProcessState.Stopped.ToString(), unit.ProcessState);
    }

    [Fact]
    public void An_absent_store_is_UNAVAILABLE_and_not_Stopped()
    {
        Repo();
        var payload = Observation(storeExists: false).Observe(ObservedAt);
        var unit = Unit(payload, PlatformRuntimeObservation.StoreWriterUnitId);

        Assert.Equal(PlatformRuntimeObservationState.Unavailable.ToString(), unit.ObservationState);
        Assert.Equal(PlatformRuntimeProcessState.Unknown.ToString(), unit.ProcessState);
        Assert.Equal("STORE_ABSENT", unit.ReasonCode);
    }

    /// <summary>
    /// The other half: a successful probe that finds no holder IS <c>Stopped</c>. Without this, the two
    /// tests above would pass against an observer that never reported anything but Unavailable.
    /// </summary>
    [Fact]
    public void A_successful_probe_that_finds_no_holder_reports_STOPPED()
    {
        Repo();
        var payload = Observation(lockHeld: false).Observe(ObservedAt);
        var unit = Unit(payload, PlatformRuntimeObservation.StoreWriterUnitId);

        Assert.Equal(PlatformRuntimeObservationState.Current.ToString(), unit.ObservationState);
        Assert.Equal(PlatformRuntimeProcessState.Stopped.ToString(), unit.ProcessState);
        Assert.Equal("NO_ACTIVE_WRITER", unit.ReasonCode);
    }

    [Fact]
    public void A_held_lock_reports_RUNNING_with_UNKNOWN_health_and_ProcessOnly_authority()
    {
        Repo();
        var payload = Observation(lockHeld: true).Observe(ObservedAt);
        var unit = Unit(payload, PlatformRuntimeObservation.StoreWriterUnitId);

        Assert.Equal(PlatformRuntimeProcessState.Running.ToString(), unit.ProcessState);

        // TASK 8, and it is the whole answer rather than an unfinished one: a held lock proves a process
        // is alive and says nothing about whether what it serves works. No endpoint exists to raise it.
        Assert.Equal(PlatformRuntimeHealthState.Unknown.ToString(), unit.HealthState);
        Assert.Equal(PlatformRuntimeHealthAuthority.ProcessOnly.ToString(), unit.HealthAuthority);
    }

    // ================================================================ TASK 12 / privacy

    [Fact]
    public void The_observer_publishes_no_host_identity_and_no_local_path()
    {
        Repo(("src/Nexus.Platform.Contracts", "Nexus.Platform.Contracts", null, false, false));
        var payload = Observation().Observe(ObservedAt);

        Assert.Equal("not-published", payload.Observer.HostIdentity);

        var json = System.Text.Json.JsonSerializer.Serialize(payload);

        // No absolute path, no user profile, no machine account.
        Assert.DoesNotContain(_root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);

        // ...and every path that IS published is repository-relative.
        foreach (var unit in payload.Units)
        {
            Assert.False(Path.IsPathRooted(unit.ComponentPath),
                $"'{unit.RuntimeUnitId}' published an absolute path");
        }
    }

    [Fact]
    public void No_member_of_the_contract_can_carry_a_pid()
    {
        // RL-11 forbids PID as a liveness test. The strongest form of that rule is structural: there is
        // no member that could hold one.
        var names = typeof(PlatformRuntimeReadUnit)
            .GetProperties()
            .Select(p => p.Name.ToLowerInvariant())
            .ToArray();

        Assert.DoesNotContain(names, n => n.Contains("pid", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains("processid", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains("hostname", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains("username", StringComparison.Ordinal));
    }

    // ================================================================ TASK 13 — publication

    [Fact]
    public void The_digest_is_deterministic_and_the_publication_is_idempotent()
    {
        Repo(("src/Nexus.Platform.Contracts", "Nexus.Platform.Contracts", null, false, false));
        var observation = Observation();
        var destination = Path.Combine(_root, "published");
        var publisher = new PlatformRuntimeReadPublisher(observation);

        var first = publisher.Publish(destination, ObservedAt);
        Assert.True(first.Published, first.Reason);

        var path = Path.Combine(destination, PlatformRuntimeReadContract.FileName);
        var firstBytes = File.ReadAllBytes(path);

        var second = publisher.Publish(destination, ObservedAt);
        Assert.True(second.Published, second.Reason);

        Assert.Equal(first.PayloadDigest, second.PayloadDigest);
        Assert.Equal(firstBytes, File.ReadAllBytes(path));

        // The digest is over the payload alone, so it must not move with a different instant.
        var later = publisher.Publish(Path.Combine(_root, "published2"), "2026-10-07T13:00:00Z");
        Assert.True(later.Published, later.Reason);
        Assert.Equal(first.PayloadDigest, later.PayloadDigest);
    }

    [Fact]
    public void Publication_does_not_alter_the_observed_repository()
    {
        Repo(("src/Nexus.Platform.Contracts", "Nexus.Platform.Contracts", null, false, false));

        var solution = Path.Combine(_root, "Nexus.Platform.slnx");
        var before = File.ReadAllBytes(solution);

        Assert.True(new PlatformRuntimeReadPublisher(Observation()).Publish(Path.Combine(_root, "published"), ObservedAt).Published);

        Assert.Equal(before, File.ReadAllBytes(solution));
    }

    [Fact]
    public void An_observation_that_cannot_be_taken_refuses_and_publishes_nothing()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        var destination = Path.Combine(_root, "refused");

        var outcome = new PlatformRuntimeReadPublisher(new PlatformRuntimeObservation(empty)).Publish(destination, ObservedAt);

        Assert.False(outcome.Published);
        Assert.False(File.Exists(Path.Combine(destination, PlatformRuntimeReadContract.FileName)),
            "a refused publication must replace nothing and create nothing");
    }

    // ================================================================ TASK 9, enforced on the way out

    [Fact]
    public void The_publisher_refuses_a_unit_whose_applicability_contradicts_its_process_state()
    {
        // The rule TASK 9 states, checked where a document can actually be wrong: an observer is not
        // trusted to have classified correctly, so the pairing is validated before anything is written.
        var notApplicable = PlatformRuntimeApplicability.LibraryOnly.ToString();
        var unit = new PlatformRuntimeReadUnit(
            "u", "u", "src/u", notApplicable,
            PlatformRuntimeObservationState.NotApplicable.ToString(),
            PlatformRuntimeProcessState.Stopped.ToString(),      // ← the contradiction
            PlatformRuntimeHealthState.NotApplicable.ToString(),
            PlatformRuntimeHealthAuthority.NotApplicable.ToString(), "",
            DeliveryAuthorityClass.Authoritative);

        // A model that satisfies every check BEFORE the one under test, so the refusal that comes back
        // is the applicability rule and not "the contract version was wrong" — which is what the first
        // version of this test asserted, and it passed for that reason rather than this one.
        var source = new PlatformRuntimeReadSource(
            PlatformRuntimeReadContract.SchemaVersion, PlatformRuntimeReadContract.Authority,
            "test", "test", ObservedAt, ObservedAt,
            "sha256:" + new string('a', 64));

        var model = new PlatformRuntimeReadModel(
            PlatformRuntimeReadContract.SchemaVersion,
            source,
            new PlatformRuntimeReadPayload(
                new PlatformRuntimeReadObserver("o", "one-shot-snapshot", "not-published", 1, DeliveryAuthorityClass.ObservedRuntime),
                [unit],
                []));

        var refusal = typeof(PlatformRuntimeReadPublisher)
            .GetMethod("Validate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [model]) as string;

        Assert.NotNull(refusal);
        Assert.Contains("LibraryOnly", refusal!, StringComparison.Ordinal);
    }
}
