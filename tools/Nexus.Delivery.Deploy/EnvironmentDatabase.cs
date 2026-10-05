using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Deploy;

/// <summary>
/// An isolated environment PostgreSQL, driven through the container engine and the image's own <c>psql</c>.
///
/// <para>
/// <b>Isolation is the property being established, not an assumption.</b> The container is created under
/// its own name, publishes only on the loopback address and on a port no other service in the estate uses,
/// carries its own volume and its own credentials, and is refused if a container of that name already
/// exists with different parameters. <see cref="DescribeAsync"/> emits the observed facts — the bound
/// address, the image, the volume, the network — so that "isolated" is a recorded observation rather than
/// a word in a design document.
/// </para>
///
/// <para>
/// <b>It refuses to touch anything it did not create.</b> Every mutating operation names this
/// environment's container, and the container name is asserted against a prefix in the descriptor before
/// any command is issued, so a descriptor edited to point at
/// <c>prt-market-survey-db-1</c> is refused rather than obeyed.
/// </para>
///
/// <para>
/// <b>The password never appears in an argument vector.</b> The container is created with
/// <c>--env-file</c> pointing at the environment's secret store, and every <c>psql</c> call runs
/// <i>inside</i> the container with no password argument, so the credential is not in this machine's
/// process table, this driver's output, or the evidence.
/// </para>
/// </summary>
internal sealed class EnvironmentDatabase
{
    private readonly EnvironmentDescriptor _descriptor;
    private readonly Action<string> _say;

    public EnvironmentDatabase(EnvironmentDescriptor descriptor, Action<string> say)
    {
        _descriptor = descriptor;
        _say = say;
    }

    private EnvironmentDescriptor.DatabaseDescriptor Db => _descriptor.Database!;

    public string ContainerName => Db.ContainerName;

    /// <summary>
    /// The prefix every container this driver manages must carry. Checked before each mutating command.
    /// The estate runs a live MarketSurvey database under a different name; a descriptor that named it
    /// would otherwise be able to stop it.
    ///
    /// <para>
    /// <b>Read from the descriptor rather than held here.</b> W9.4 held the literal <c>"nexus-env-dev-"</c>,
    /// which was safe only while ENV-DEV was the one environment this class could be pointed at. The
    /// descriptor derives it from the environment it has already validated, so the DEV verb reaches only
    /// <c>nexus-env-dev-*</c> and the TEST verb only <c>nexus-env-test-*</c> — and there is one place that
    /// decides, not two that can drift.
    /// </para>
    /// </summary>
    private string ManagedPrefix => _descriptor.ManagedContainerPrefix;

    /// <summary>Refuses a container name this driver does not own. Called before every mutating command.</summary>
    private void AssertManaged()
    {
        if (!ContainerName.StartsWith(ManagedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Container '{ContainerName}' does not carry the '{ManagedPrefix}' prefix, so this driver does not own it and "
                + "will not start, stop or remove it. The estate runs other databases under other names.");
        }
    }

    // ------------------------------------------------------------------ container lifecycle

    /// <summary>
    /// Creates the environment's database if it does not exist, starting it if it does.
    ///
    /// <para>
    /// <b>The secret file is write-once.</b> If it already exists its password is reused rather than
    /// regenerated — a regenerated password would leave the existing container unable to authenticate and
    /// would turn an idempotent environment step into a destructive one.
    /// </para>
    /// </summary>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        AssertManaged();

        Directory.CreateDirectory(Db.SecretStoreDirectory);

        if (!File.Exists(_descriptor.SecretFilePath))
        {
            var password = GeneratePassword();
            await File.WriteAllTextAsync(
                _descriptor.SecretFilePath,
                $"POSTGRES_PASSWORD={password}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            _say($"  secret store      : {_descriptor.SecretFilePath} (created write-once; the value is not printed anywhere)");
        }
        else
        {
            _say($"  secret store      : {_descriptor.SecretFilePath} (already present; password reused, not regenerated)");
        }

        var exists = await RunAsync("docker", ["container", "inspect", "--format", "{{.State.Status}}", ContainerName], cancellationToken)
            .ConfigureAwait(false);

        if (exists.ExitCode == 0)
        {
            var status = exists.StandardOutput.Trim();
            _say($"  container         : '{ContainerName}' exists (status={status})");

            if (!string.Equals(status, "running", StringComparison.Ordinal))
            {
                var started = await RunAsync("docker", ["start", ContainerName], cancellationToken).ConfigureAwait(false);
                Require(started, "docker start");
            }
        }
        else
        {
            var volume = $"{ContainerName}-data";

            _say($"  container         : creating '{ContainerName}' from {Db.Image}");

            var created = await RunAsync(
                "docker",
                [
                    "run", "--detach",
                    "--name", ContainerName,
                    "--publish", $"{Db.HostAddress}:{Db.Port}:5432",
                    "--env-file", _descriptor.SecretFilePath,
                    "--env", $"POSTGRES_DB={Db.DatabaseName}",
                    "--env", $"POSTGRES_USER={Db.Username}",
                    "--volume", $"{volume}:/var/lib/postgresql/data",
                    "--label", $"nexus.environment={_descriptor.Environment}",
                    "--label", "nexus.managed-by=Nexus.Delivery.Deploy",
                    Db.Image
                ],
                cancellationToken).ConfigureAwait(false);

            Require(created, "docker run");
        }

        await WaitForReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the environment's database. Used by the failure-injection verb.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        AssertManaged();
        var stop = await RunAsync("docker", ["stop", ContainerName], cancellationToken).ConfigureAwait(false);
        Require(stop, "docker stop");
    }

    /// <summary>Starts the environment's database.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        AssertManaged();
        var start = await RunAsync("docker", ["start", ContainerName], cancellationToken).ConfigureAwait(false);
        Require(start, "docker start");
        await WaitForReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the environment's database and its volume. <b>Never called by the deploy verb</b>, and it
    /// refuses unless the caller has said so explicitly — a teardown that ran as part of a deployment
    /// would make the deployment destroy its own evidence.
    /// </summary>
    public async Task RemoveAsync(bool deleteVolume, CancellationToken cancellationToken = default)
    {
        AssertManaged();

        var rm = await RunAsync("docker", ["rm", "--force", ContainerName], cancellationToken).ConfigureAwait(false);

        if (rm.ExitCode != 0)
        {
            _say($"  remove            : '{ContainerName}' was not present or could not be removed ({FirstLine(rm.StandardError)})");
        }
        else
        {
            _say($"  remove            : '{ContainerName}' removed");
        }

        if (deleteVolume)
        {
            var volume = $"{ContainerName}-data";
            var volumeRm = await RunAsync("docker", ["volume", "rm", "--force", volume], cancellationToken).ConfigureAwait(false);
            _say($"  remove            : volume '{volume}' {(volumeRm.ExitCode == 0 ? "removed" : "not present")}");
        }
    }

    private async Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(3);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var probe = await RunAsync(
                "docker",
                ["exec", ContainerName, "pg_isready", "-U", Db.Username, "-d", Db.DatabaseName],
                cancellationToken).ConfigureAwait(false);

            if (probe.ExitCode == 0)
            {
                _say($"  database          : ready on {Db.HostAddress}:{Db.Port}/{Db.DatabaseName} as '{Db.Username}'");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"The {_descriptor.Environment} database '{ContainerName}' did not become ready within three minutes.");
    }

    // ------------------------------------------------------------------ observation

    /// <summary>
    /// The observed state of the target, in the terms <see cref="MigrationGovernance"/> needs.
    ///
    /// <para>
    /// <b>Every member is read from the database, not inferred from the descriptor.</b> "Reachable" is
    /// <c>pg_isready</c> answering; "schema present" is the migration history table existing; "data state"
    /// is a row sum over the public tables. The basis records the queries, so a reader can see what was
    /// asked rather than being told what was concluded.
    /// </para>
    ///
    /// <para>
    /// <b>The data-state question is deliberately asked in the broadest form, and this was measured rather
    /// than assumed.</b> The first run of this driver reported <c>HoldsData</c> against a container it had
    /// just created with no application schema at all, and the reason was real: the
    /// <c>postgis/postgis</c> image installs <c>spatial_ref_sys</c> into <c>public</c>, and that table
    /// holds reference rows. The tempting repair was to narrow the query to "tables this release would
    /// touch", which would have read <c>Empty</c> and let the migration proceed.
    /// <b>That repair was not made.</b> Narrowing an observation so a control stops refusing is editing the
    /// instrument until it agrees, and it would silently under-report on any future target that held real
    /// data in a table this particular release happens not to name. What was done instead is the thing the
    /// obligation actually asks for: a real, verified backup, recorded with its digest. The obligation is
    /// discharged by keeping the data, not by redefining it.
    /// </para>
    ///
    /// <para>
    /// <b>Correction, 2026-09-26 — the breadth was right and the statistic was wrong.</b> That reasoning
    /// stands and the table set is unchanged. What was wrong was the <i>instrument</i> the broad set was
    /// measured with: <c>sum(n_live_tup)</c> is a planner statistic, reset to zero by a cluster restart. On
    /// the ENV-DEV re-establishment it read <b>0</b> against a target holding <b>8,500</b> rows, and the
    /// judgement discharged the release's backup obligation on it. The same target had read <c>HoldsData</c>
    /// in the original run — nothing had changed but which branch the code took, because the earlier branch
    /// counts public <i>tables</i> and the later one summed <i>estimates</i>.
    /// </para>
    ///
    /// <para>
    /// The fix is in <see cref="DeterministicRowCountAsync"/> and in
    /// <see cref="MigrationTargetDataStatePolicy"/>: the data state now comes from an exact count, the
    /// estimate is recorded and not used, and an emptiness that cannot be measured becomes
    /// <c>Unknown</c> — which keeps the obligation rather than discharging it. The estimate was not
    /// repaired with <c>ANALYZE</c>, because making a statistic agree is not the same as establishing a fact.
    /// </para>
    /// </summary>
    public async Task<MigrationTargetObservation> ObserveAsync(
        DeploymentEnvironmentId environment,
        CancellationToken cancellationToken = default)
    {
        var reachable = await RunAsync(
            "docker",
            ["exec", ContainerName, "pg_isready", "-U", Db.Username, "-d", Db.DatabaseName],
            cancellationToken).ConfigureAwait(false);

        var basis = new StringBuilder(
            $"Observed through the container's own psql on '{ContainerName}' ({Db.HostAddress}:{Db.Port}/{Db.DatabaseName}). "
            + $"pg_isready returned exit {reachable.ExitCode}.");

        if (reachable.ExitCode != 0)
        {
            return new MigrationTargetObservation(
                environment,
                reachable: false,
                schemaPresent: false,
                appliedMigrationIds: [],
                MigrationTargetDataState.Unknown,
                backupVerified: false,
                basis.ToString());
        }

        const string historyExists =
            "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL";

        var historyPresent = await ScalarAsync(historyExists, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(historyPresent, "t", StringComparison.Ordinal))
        {
            basis.Append(" The migration history table was not present, so the schema is uninitialised (0 migrations applied).");

            var tableCount = await ScalarAsync(
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'",
                cancellationToken).ConfigureAwait(false);

            basis.Append($" public tables present: {tableCount ?? "unreadable"}.");

            var empty = tableCount == "0";

            return new MigrationTargetObservation(
                environment,
                reachable: true,
                schemaPresent: false,
                appliedMigrationIds: [],
                empty ? MigrationTargetDataState.Empty : MigrationTargetDataState.HoldsData,
                backupVerified: false,
                basis.ToString());
        }

        var applied = await QueryLinesAsync(
            "SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\"",
            cancellationToken).ConfigureAwait(false);

        basis.Append($" The migration history table was present and reported {applied.Count} applied migration(s).");

        // The tables the release's migration set creates are exactly the public tables that exist once it
        // has been applied — so they are enumerated from the database rather than listed by hand. A
        // hand-kept list would be one more thing that can silently disagree with the schema it describes,
        // which is the defect class this whole mechanism exists to remove.
        // The table set is enumerated from the database rather than listed by hand: a hand-kept list is one
        // more thing that can silently disagree with the schema it describes, which is the defect class this
        // whole mechanism exists to remove. `quote_ident` is applied server-side so a table name that needs
        // quoting cannot break the count or, worse, be made to count something else.
        var tables = await QueryLinesAsync(
            "SELECT quote_ident(tablename) FROM pg_tables WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'",
            cancellationToken).ConfigureAwait(false);

        var exactRows = await DeterministicRowCountAsync(tables, basis, cancellationToken).ConfigureAwait(false);

        // The planner estimate is RECORDED and NOT USED. It is here because a reader comparing this run with
        // the W9.4 run needs to see the number that misled it — and labelled, so no later reader mistakes it
        // for the measurement. n_live_tup lives in shared memory and a cluster restart zeroes it, which is
        // exactly the state a re-establishment produces: the W9.4 target read 0 from it while holding 8,500
        // rows.
        var estimate = await ScalarAsync(
            "SELECT coalesce(sum(n_live_tup), 0) FROM pg_stat_user_tables WHERE schemaname = 'public' AND relname <> '__EFMigrationsHistory'",
            cancellationToken).ConfigureAwait(false);

        basis.Append(
            $" (Recorded for comparison and NOT used to classify: the planner estimate sum(n_live_tup) read {estimate ?? "unreadable"}, "
            + "a statistic held in shared memory that a cluster restart resets to zero.)");

        var dataState = MigrationTargetDataStatePolicy.ClassifyFromDeterministicCount(exactRows);
        var dataStateBasis = MigrationTargetDataStatePolicy.BasisFor(exactRows);

        if (dataState == MigrationTargetDataState.Unknown)
        {
            basis.Append(
                " The exact count could not be completed, so the data state is established as NEITHER empty nor holding data. "
                + "It is recorded as Unknown rather than assumed Empty, and the backup obligation an Unknown target carries is not discharged.");
        }

        return new MigrationTargetObservation(
            environment,
            reachable: true,
            schemaPresent: true,
            applied,
            dataState,
            backupVerified: false,
            basis.ToString(),
            dataStateBasis);
    }

    // ------------------------------------------------------------------ backup

    /// <summary>
    /// Takes a backup of the target and <b>verifies it</b>, returning the observation that discharges
    /// <see cref="MigrationGovernance"/>'s backup obligation — or a refusal, never an unverified claim.
    ///
    /// <para>
    /// <b>Why this exists rather than an exemption.</b> The release records <c>BackupRequired = true</c> and
    /// <c>ForwardFixOnly</c>: an earlier run of this driver had migration governance refuse with
    /// <c>BackupRequired</c> because the target held data. The obligation is discharged by keeping the data
    /// — a restore point that exists and has been read back — not by redefining the target as empty. So
    /// this takes a real dump and then verifies it three ways, and a failure at any of them is a refusal
    /// with a typed reason rather than a deployment that proceeded on a file's existence.
    /// </para>
    ///
    /// <para>
    /// <b>The three verifications, and why each is not redundant.</b>
    /// <list type="number">
    /// <item>The archive's magic header is <c>PGDMP</c>. A zero-length file, a partial write or an error
    /// message redirected into the output all fail here.</item>
    /// <item><c>pg_restore --list</c> reads the archive's table of contents. This is the verification that
    /// the dump is <i>usable</i>: a truncated custom-format archive has a valid header and no contents.
    /// The entry count is recorded.</item>
    /// <item>The SHA-256 of the archive is recorded on the host copy, so a later reader can tell whether
    /// the restore point they have is the one this deployment was authorized by.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>The backup never leaves the machine and is never quoted for its contents.</b> It is written inside
    /// the container, copied to the environment's own backup directory, and only its digest, size, path and
    /// table-of-contents count reach evidence.
    /// </para>
    /// </summary>
    public async Task<BackupObservation> BackupAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        AssertManaged();

        Directory.CreateDirectory(backupDirectory);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var inContainer = $"/tmp/{ContainerName}-{stamp}.dump";
        var hostPath = Path.Combine(backupDirectory, $"{ContainerName}-{stamp}.dump");

        var dump = await RunAsync(
            "docker",
            [
                "exec", ContainerName,
                "pg_dump",
                "-U", Db.Username,
                "-d", Db.DatabaseName,
                "--format=custom",
                "--file", inContainer
            ],
            cancellationToken).ConfigureAwait(false);

        if (dump.ExitCode != 0)
        {
            return BackupObservation.Refused($"pg_dump exited {dump.ExitCode}: {FirstLine(dump.StandardError)}");
        }

        // ---- verification 2: the archive's table of contents is readable ------------------------------
        var toc = await RunAsync(
            "docker",
            ["exec", ContainerName, "pg_restore", "--list", inContainer],
            cancellationToken).ConfigureAwait(false);

        if (toc.ExitCode != 0)
        {
            return BackupObservation.Refused(
                $"the backup was written but 'pg_restore --list' could not read it (exit {toc.ExitCode}): {FirstLine(toc.StandardError)}. "
                + "A dump that cannot be read is not a restore point.");
        }

        var tocEntries = toc.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(line => line.Contains(";", StringComparison.Ordinal) && !line.StartsWith(';'));

        var copy = await RunAsync("docker", ["cp", $"{ContainerName}:{inContainer}", hostPath], cancellationToken).ConfigureAwait(false);

        if (copy.ExitCode != 0)
        {
            return BackupObservation.Refused($"the backup could not be copied out of the container: {FirstLine(copy.StandardError)}");
        }

        await RunAsync("docker", ["exec", ContainerName, "rm", "-f", inContainer], cancellationToken).ConfigureAwait(false);

        // ---- verification 1: the magic header, and verification 3: the digest -------------------------
        await using var stream = File.OpenRead(hostPath);
        var header = new byte[5];
        var read = await stream.ReadAtLeastAsync(header, 5, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        var sha = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
        var bytes = new FileInfo(hostPath).Length;

        var magic = System.Text.Encoding.ASCII.GetString(header, 0, read);

        if (magic != "PGDMP")
        {
            return BackupObservation.Refused(
                $"the file at '{hostPath}' does not begin with the PostgreSQL custom-archive header 'PGDMP' (it begins '{magic}').");
        }

        var basis =
            $"pg_dump --format=custom of {Db.DatabaseName} inside '{ContainerName}', copied to '{hostPath}'. "
            + $"Verified: header PGDMP; 'pg_restore --list' read {tocEntries} TOC entr{(tocEntries == 1 ? "y" : "ies")} (exit 0); "
            + $"sha256:{sha}; {bytes} bytes.";

        return BackupObservation.Verified(hostPath, "sha256:" + sha, bytes, tocEntries, basis);
    }

    /// <summary>
    /// A backup that was taken and verified, or a typed refusal. There is deliberately no third state: a
    /// backup that "probably worked" is the unverified claim the obligation exists to refuse.
    /// </summary>
    public sealed record BackupObservation(
        bool IsVerified,
        string? Path,
        string? Digest,
        long Bytes,
        int TocEntries,
        string Basis,
        string? RefusalReason)
    {
        public static BackupObservation Verified(string path, string digest, long bytes, int tocEntries, string basis)
            => new(true, path, digest, bytes, tocEntries, basis, null);

        public static BackupObservation Refused(string reason)
            => new(false, null, null, 0, 0, reason, reason);
    }

    /// <summary>The container's observed parameters, as the isolation evidence.</summary>
    public async Task<Dictionary<string, string>> DescribeAsync(CancellationToken cancellationToken = default)
    {
        // The environment label is READ, not restated. The W9.4 version of this method emitted the literal
        // string "nexus.environment=ENV-DEV" under the key "labels", so the field that evidence quoted as
        // the container's observed label was not observed at all - it was a constant that happened to be
        // true while ENV-DEV was the only reachable environment. Under a second environment it would have
        // recorded ENV-DEV for an ENV-TEST container: a false statement in an observation record, which is
        // the estate's recurring defect (a check that appears to pass because the thing that would have
        // contradicted it was never run). The go-template now reads the label itself.
        var format = "{{.Name}}|{{.Config.Image}}|{{.State.Status}}|"
            + "{{range $p, $c := .NetworkSettings.Ports}}{{$p}}->{{range $c}}{{.HostIp}}:{{.HostPort}}{{end}} {{end}}|"
            + "{{range .Mounts}}{{.Name}}:{{.Destination}} {{end}}|"
            + "{{.HostConfig.NetworkMode}}|"
            + "{{index .Config.Labels \"nexus.environment\"}}|"
            + "{{index .Config.Labels \"nexus.managed-by\"}}";

        var result = await RunAsync("docker", ["container", "inspect", "--format", format, ContainerName], cancellationToken)
            .ConfigureAwait(false);

        var fields = result.StandardOutput.Trim().Split('|');

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["container"] = ContainerName,
            ["image"] = fields.Length > 1 ? fields[1] : "(unreadable)",
            ["status"] = fields.Length > 2 ? fields[2] : "(unreadable)",
            ["publishedPorts"] = fields.Length > 3 ? fields[3].Trim() : "(unreadable)",
            ["mounts"] = fields.Length > 4 ? fields[4].Trim() : "(unreadable)",
            ["networkMode"] = fields.Length > 5 ? fields[5].Trim() : "(unreadable)",
            ["labelNexusEnvironment"] = fields.Length > 6 ? fields[6].Trim() : "(unreadable)",
            ["labelNexusManagedBy"] = fields.Length > 7 ? fields[7].Trim() : "(unreadable)"
        };
    }

    /// <summary>Every container the engine knows about, so the environment can show it created only its own.</summary>
    public async Task<IReadOnlyList<string>> ListAllContainersAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync("docker", ["ps", "--all", "--format", "{{.Names}}\t{{.Image}}\t{{.Status}}"], cancellationToken)
            .ConfigureAwait(false);

        return [.. result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private async Task<string?> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            "docker",
            ["exec", ContainerName, "psql", "-U", Db.Username, "-d", Db.DatabaseName, "-tAc", sql],
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    private async Task<IReadOnlyList<string>> QueryLinesAsync(string sql, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            "docker",
            ["exec", ContainerName, "psql", "-U", Db.Username, "-d", Db.DatabaseName, "-tAc", sql],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return [];
        }

        return [.. result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    // ------------------------------------------------------------------ deterministic data-state

    /// <summary>
    /// The <b>exact</b> number of rows in the policy table set, or <see langword="null"/> when it could not
    /// be established.
    ///
    /// <para>
    /// <b>Why an exact count and not a statistic.</b> The W9.4 instrument classified a target as empty from
    /// <c>sum(pg_stat_user_tables.n_live_tup)</c> — a planner statistic held in shared memory, which a
    /// cluster restart resets to zero. Against that reading of 0, the target held 8,500 rows, and the
    /// release's recorded backup obligation was discharged without a backup. The re-establishment path
    /// requires exactly the restart that produces that state, so this is not a rare race.
    /// </para>
    ///
    /// <para>
    /// <b>Every table must answer, or the answer is nothing.</b> A partial sum would be a number that looks
    /// like a measurement and is not one — and it would be a sum biased downward, which is the direction
    /// that discharges obligations. One unreadable table therefore returns <see langword="null"/>, which the
    /// policy turns into <c>Unknown</c>, which keeps the obligation standing.
    /// </para>
    ///
    /// <para>
    /// <b>ANALYZE is deliberately not run.</b> Making the statistic agree would hide the instrument defect
    /// rather than fix it, and this method does not read statistics at all.
    /// </para>
    /// </summary>
    private async Task<long?> DeterministicRowCountAsync(
        IReadOnlyList<string> quotedTables,
        StringBuilder basis,
        CancellationToken cancellationToken)
    {
        basis.Append($" Enumerated {quotedTables.Count} public table(s) beyond the history table and counted each exactly.");

        long total = 0;
        var counted = 0;

        foreach (var table in quotedTables)
        {
            var scalar = await ScalarAsync($"SELECT count(*) FROM public.{table}", cancellationToken).ConfigureAwait(false);

            if (scalar is null || !long.TryParse(scalar, out var rows))
            {
                basis.Append(
                    $" The exact count of {table} returned '{scalar ?? "(unreadable)"}', so the total is NOT established "
                    + $"after {counted} of {quotedTables.Count} table(s).");
                return null;
            }

            total += rows;
            counted++;

            if (rows > 0)
            {
                basis.Append($" {table} holds {rows} row(s).");
            }
        }

        basis.Append($" Exact total across all {counted} table(s): {total}.");

        return total;
    }

    // ------------------------------------------------------------------ process plumbing

    /// <summary>
    /// A generated DEV credential. Base64url of 24 random bytes — 32 characters, no separators, and not
    /// shell-significant, so it cannot corrupt the env-file it is written into.
    /// </summary>
    private static string GeneratePassword()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string FirstLine(string value)
    {
        var index = value.IndexOf('\n', StringComparison.Ordinal);
        return index < 0 ? value.Trim() : value[..index].Trim();
    }

    private static void Require(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{operation}' failed with exit {result.ExitCode}: {FirstLine(result.StandardError)}");
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// Runs a command with the driver's own environment — <b>never</b> with the DEV database password added.
    /// The only child that receives it is the artifact itself, and that is set up by the caller.
    /// </summary>
    private static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"'{fileName}' could not be started.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    /// <summary>The JSON shape the environment evidence is written in. Kept here so the descriptor and its observation travel together.</summary>
    public static string ToJson(object value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
}
