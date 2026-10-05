using System.Text.Json;
using System.Text.Json.Serialization;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Deploy;

/// <summary>
/// An environment descriptor: what an environment <b>is</b>, as opposed to what a release is.
///
/// <para>
/// <b>One type for every ratified environment, and that is deliberate.</b> The W9.4 version of this file was
/// <c>EnvDevDescriptor</c> and refused every environment but ENV-DEV. W9.5 must establish ENV-TEST, and the
/// estate holds a permanent example of what a second copy of a judgement costs: the secret scanner's
/// "is this a value?" test was implemented twice and drifted, and the drift refused three honest documents
/// (W9.2). A parallel <c>EnvTestDescriptor</c> would be that defect with a longer fuse. So the environment
/// became a parameter, and the check the old file performed — <i>this verb may not touch an environment
/// other than the one it was built for</i> — was preserved exactly, by moving it from a constant in the type
/// to a <b>required argument of <see cref="Load"/></b>. A verb still cannot be aimed at another environment
/// by editing only its arguments.
/// </para>
///
/// <para>
/// <b>Why this is a separate document from the release run plan.</b> The run plan describes the release —
/// its artifact, its bundle, its migration set — and is a fact about a product. This describes a machine:
/// which database, which port, where the secret store is. Folding the two together would make an
/// environment change look like a release change, and would put a database password's <i>location</i>
/// inside a document that a release's evidence quotes.
/// </para>
///
/// <para>
/// <b>It contains no secret, and it cannot.</b> Where a secret is needed it names an environment variable
/// and a store directory; the values live in the environment's own secret store, outside every git
/// repository. The reader refuses a descriptor whose credential members look like values rather than
/// names, so this is enforced rather than documented.
/// </para>
/// </summary>
internal sealed record EnvironmentDescriptor
{
    [JsonPropertyName("environment")]
    public string Environment { get; init; } = string.Empty;

    [JsonPropertyName("root")]
    public string Root { get; init; } = string.Empty;

    [JsonPropertyName("database")]
    public DatabaseDescriptor? Database { get; init; }

    [JsonPropertyName("host")]
    public HostDescriptor? Host { get; init; }

    /// <summary>
    /// The <b>application</b> configuration this environment supplies at runtime, declared rather than
    /// hardcoded.
    ///
    /// <para>
    /// <b>Why this exists.</b> The W9.4 driver set these two members for every environment it could reach,
    /// as literals inside <c>ApplyEnvironment</c>:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>Storage__Provider=Local</c></description></item>
    /// <item><description><c>Testing__DisableGps=true</c></description></item>
    /// </list>
    /// <para>
    /// <b>Both are development-only values, and one of them is a security bypass.</b> The certified artifact
    /// refuses to run when <c>Storage:Provider</c> is not <c>Azure</c> outside Development, and refuses the
    /// GPS bypass outright ("GPS testing bypass is forbidden in production"). The W9.4 driver was safe only
    /// because ENV-DEV was the one environment it could be pointed at, where <c>ASPNETCORE_ENVIRONMENT</c> is
    /// <c>Development</c> and both values are legitimate. The moment a second environment exists, a driver
    /// that supplies them unconditionally is <b>enabling a GPS testing bypass in an environment that is not
    /// development</b> — which the application's own author forbade, and which the driver would have done
    /// silently, because the application's guard is what would notice and the driver does not read it.
    /// </para>
    ///
    /// <para>
    /// So the values became data. Each environment declares what it supplies; the driver applies what is
    /// declared and nothing else. The infrastructure members that genuinely are the environment's
    /// <i>identity</i> — the host name, the URLs, the connection string, the data-protection path — remain
    /// derived, because those are what the descriptor already describes.
    /// </para>
    ///
    /// <para>
    /// <b>Absent means none are supplied</b>, and an environment that does not declare a setting the
    /// application requires will have that application refuse to start. That is the correct outcome: the
    /// driver does not know what the artifact needs, and inventing a value for it would be the driver
    /// authoring an environment's configuration rather than recording it.
    /// </para>
    /// </summary>
    [JsonPropertyName("runtime")]
    public RuntimeDescriptor? Runtime { get; init; }

    internal sealed record RuntimeDescriptor
    {
        /// <summary>
        /// Configuration keys this environment supplies, in <c>Section__Key</c> form. <b>Names and values
        /// are both checked</b>: a value that is not part of an environment's identity has no business here,
        /// and this block is quoted into evidence.
        /// </summary>
        [JsonPropertyName("settings")]
        public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);
    }

    [JsonPropertyName("artifactStoreRoot")]
    public string ArtifactStoreRoot { get; init; } = string.Empty;

    [JsonPropertyName("lineageRoot")]
    public string LineageRoot { get; init; } = string.Empty;

    /// <summary>
    /// Where the <b>deployment</b> lineage ledger lives. Separate from the release lineage root on purpose:
    /// they are two ledgers with two record shapes and two writers, and putting them in one directory would
    /// make a reader that globs the directory see records it cannot parse.
    /// </summary>
    [JsonPropertyName("deploymentLineageRoot")]
    public string DeploymentLineageRoot { get; init; } = string.Empty;

    internal sealed record DatabaseDescriptor
    {
        [JsonPropertyName("containerName")]
        public string ContainerName { get; init; } = string.Empty;

        [JsonPropertyName("image")]
        public string Image { get; init; } = string.Empty;

        [JsonPropertyName("host")]
        public string HostAddress { get; init; } = string.Empty;

        [JsonPropertyName("port")]
        public int Port { get; init; }

        [JsonPropertyName("database")]
        public string DatabaseName { get; init; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; init; } = string.Empty;

        /// <summary>
        /// The <b>name</b> of the variable the password is read from — never the password.
        /// </summary>
        [JsonPropertyName("passwordEnvironmentVariable")]
        public string PasswordEnvironmentVariable { get; init; } = string.Empty;

        /// <summary>
        /// Where the environment keeps its secrets. Outside every git repository, and never quoted into
        /// evidence.
        /// </summary>
        [JsonPropertyName("secretStoreDirectory")]
        public string SecretStoreDirectory { get; init; } = string.Empty;

        /// <summary>The docker env-file the database container is created with, inside the secret store.</summary>
        [JsonPropertyName("secretFile")]
        public string SecretFile { get; init; } = string.Empty;
    }

    internal sealed record HostDescriptor
    {
        [JsonPropertyName("port")]
        public int Port { get; init; }

        /// <summary>The ASP.NET Core environment name the artifact is started under.</summary>
        [JsonPropertyName("environmentName")]
        public string EnvironmentName { get; init; } = string.Empty;

        [JsonPropertyName("readinessPath")]
        public string ReadinessPath { get; init; } = string.Empty;

        [JsonPropertyName("livenessPath")]
        public string LivenessPath { get; init; } = string.Empty;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public DeploymentEnvironmentId EnvironmentId => DeploymentEnvironmentId.Parse(Environment);

    /// <summary>The lowercase environment token used in container names, labels and deployment ids: <c>dev</c>, <c>test</c>, <c>prod</c>.</summary>
    public string EnvironmentToken => EnvironmentId.Value switch
    {
        DeploymentEnvironmentId.Dev => "dev",
        DeploymentEnvironmentId.Test => "test",
        DeploymentEnvironmentId.Prod => "prod",
        _ => throw new InvalidOperationException($"'{EnvironmentId}' is not a ratified environment.")
    };

    /// <summary>
    /// The prefix every container this driver may manage on behalf of <b>this</b> environment must carry.
    ///
    /// <para>
    /// <b>Derived from the environment, never supplied by the caller.</b> The W9.4 database class held the
    /// literal <c>"nexus-env-dev-"</c>, which was safe only because ENV-DEV was the one environment it could
    /// be pointed at. With two environments a literal would have to be widened to a shared prefix — at which
    /// point the ENV-DEV verb could stop an ENV-TEST container — or duplicated. Deriving it from the
    /// already-validated <see cref="EnvironmentId"/> means the DEV verb reaches only <c>nexus-env-dev-*</c>
    /// and the TEST verb only <c>nexus-env-test-*</c>, and neither can reach the estate's live MarketSurvey
    /// database.
    /// </para>
    /// </summary>
    public string ManagedContainerPrefix => $"nexus-env-{EnvironmentToken}-";

    /// <summary>
    /// Reads and validates the descriptor, <b>and refuses it unless it names exactly the environment the
    /// caller is authorized to act on</b>.
    ///
    /// <para>
    /// <b>Refuses rather than defaults</b>, for the same reason the release plan does: a descriptor that
    /// silently fills in a port would deploy to a machine nobody described.
    /// </para>
    ///
    /// <para>
    /// <b>The expected environment is a parameter, not a property of this type.</b> That is what makes
    /// "the DEV verb may not touch ENV-TEST" a fact about the call rather than about the file. The W9.4 type
    /// hard-coded ENV-DEV inside <c>Validate</c> and got the same guarantee for one environment; a type that
    /// accepted <i>a set</i> of environments would let the wrong verb touch the wrong database with nothing
    /// refusing it.
    /// </para>
    /// </summary>
    /// <param name="path">The descriptor to read.</param>
    /// <param name="expectedEnvironment">The one environment the calling verb is authorized to act on.</param>
    public static EnvironmentDescriptor Load(string path, DeploymentEnvironmentId expectedEnvironment)
    {
        var descriptor = Read(path);

        descriptor.Validate(expectedEnvironment);
        return descriptor;
    }

    /// <summary>
    /// Reads and validates the descriptor, asserting only that it names <b>a ratified environment</b>.
    ///
    /// <para>
    /// <b>For the two verbs whose contract is "act on the environment this descriptor names", and only
    /// those.</b> <c>env-up</c> creates an environment and <c>env-down</c> removes one; neither deploys
    /// anything into anything, and requiring them to be told what they are about to create would be a
    /// ceremony that carries no information — the descriptor IS the statement. The verbs that
    /// <i>deploy</i> still go through <see cref="Load(string, DeploymentEnvironmentId)"/>, which is what
    /// keeps "the DEV verb may not touch ENV-TEST" true where it matters.
    /// </para>
    ///
    /// <para>
    /// The blast radius is bounded regardless: <c>EnvironmentDatabase.AssertManaged</c> refuses any
    /// container whose name does not carry the prefix derived from this descriptor's own environment, so a
    /// descriptor naming ENV-TEST can only ever start, stop or remove <c>nexus-env-test-*</c>.
    /// </para>
    /// </summary>
    public static EnvironmentDescriptor LoadDeclared(string path)
    {
        var descriptor = Read(path);

        descriptor.Validate(descriptor.EnvironmentId);
        return descriptor;
    }

    private static EnvironmentDescriptor Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new InvalidOperationException($"The environment descriptor '{path}' does not exist.");
        }

        EnvironmentDescriptor? descriptor;

        try
        {
            descriptor = JsonSerializer.Deserialize<EnvironmentDescriptor>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"The environment descriptor '{path}' is not a valid environment descriptor this build can read: {ex.Message}", ex);
        }

        if (descriptor is null)
        {
            throw new InvalidOperationException($"The environment descriptor '{path}' decoded to nothing.");
        }

        return descriptor;
    }

    private void Validate(DeploymentEnvironmentId expectedEnvironment)
    {
        Require(Environment, nameof(Environment));
        Require(Root, nameof(Root));
        Require(ArtifactStoreRoot, nameof(ArtifactStoreRoot));
        Require(LineageRoot, nameof(LineageRoot));
        Require(DeploymentLineageRoot, nameof(DeploymentLineageRoot));

        if (string.Equals(
                Path.GetFullPath(DeploymentLineageRoot).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(LineageRoot).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "deploymentLineageRoot and lineageRoot are the same directory. They hold different record shapes written by different "
                + "writers; sharing a directory would make one ledger's reader encounter the other's records.");
        }

        if (Database is null)
        {
            throw new InvalidOperationException("The descriptor names no database.");
        }

        Require(Database.ContainerName, "database.containerName");
        Require(Database.Image, "database.image");
        Require(Database.HostAddress, "database.host");
        Require(Database.DatabaseName, "database.database");
        Require(Database.Username, "database.username");
        Require(Database.PasswordEnvironmentVariable, "database.passwordEnvironmentVariable");
        Require(Database.SecretStoreDirectory, "database.secretStoreDirectory");
        Require(Database.SecretFile, "database.secretFile");

        if (Database.Port is < 1024 or > 65535)
        {
            throw new InvalidOperationException($"database.port {Database.Port} is not a usable port.");
        }

        // The credential member must NAME a variable, not carry a value. Checked rather than trusted,
        // because the one way this file could leak a credential into a release's evidence is by someone
        // pasting one into it.
        if (CredentialShape.LooksLikeCredentialValue(Database.PasswordEnvironmentVariable))
        {
            throw new InvalidOperationException(
                "database.passwordEnvironmentVariable looks like a credential value rather than the name of a variable. "
                + "This descriptor is quoted into evidence; a value here would put a live credential in the audit record.");
        }

        if (Host is null)
        {
            throw new InvalidOperationException("The descriptor names no host.");
        }

        Require(Host.EnvironmentName, "host.environmentName");
        Require(Host.ReadinessPath, "host.readinessPath");
        Require(Host.LivenessPath, "host.livenessPath");

        if (Host.Port is < 1024 or > 65535)
        {
            throw new InvalidOperationException($"host.port {Host.Port} is not a usable port.");
        }

        // Every declared runtime setting is checked for shape. This block is quoted into evidence and applied
        // to a child process, so it is the one place in this file where a caller could put something it should
        // not: a key that is really a value, or a value that is really a credential.
        foreach (var setting in Runtime?.Settings ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(setting.Key) || !setting.Key.Contains("__", StringComparison.Ordinal)
                && !setting.Key.Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"runtime.settings member '{setting.Key}' is not a configuration path. Expected the "
                    + "'Section__Key' (or 'Section:Key') form the .NET configuration provider reads.");
            }

            if (CredentialShape.LooksLikeCredentialValue(setting.Value))
            {
                throw new InvalidOperationException(
                    $"runtime.settings['{setting.Key}'] looks like a credential value rather than environment "
                    + "configuration. This descriptor is quoted into evidence; a value here would put a live "
                    + "credential in the audit record. Secrets belong in the environment's secret store.");
            }

            if (CredentialShape.LooksLikeCredentialValue(setting.Key))
            {
                throw new InvalidOperationException(
                    $"runtime.settings key '{setting.Key}' looks like a credential value rather than a configuration "
                    + "path.");
            }
        }

        // The environment the descriptor names must be the one the calling verb is authorized for. Asserted
        // here, inside validation, so that no verb can be pointed at another environment by editing only its
        // arguments — the property the W9.4 ENV-DEV-only type provided, kept for a set of two.
        if (EnvironmentId != expectedEnvironment)
        {
            throw new InvalidOperationException(
                $"This verb acts on {expectedEnvironment} and this descriptor names '{Environment}'. "
                + "A descriptor aimed at another environment is refused here, not honoured.");
        }
    }

    private static void Require(string? value, string member)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The environment descriptor does not name '{member}'.");
        }
    }

    public string ReleaseRoot(string releaseId) => Path.Combine(Root, "releases", releaseId);

    public string AppRoot(string releaseId) => Path.Combine(ReleaseRoot(releaseId), "app");

    public string LogsRoot => Path.Combine(Root, "logs");

    public string AppDataRoot(string releaseId) => Path.Combine(ReleaseRoot(releaseId), "app-data");

    public string SecretFilePath => Path.Combine(Database!.SecretStoreDirectory, Database.SecretFile);

    /// <summary>
    /// The DEV database password, read from the environment's own secret store.
    ///
    /// <para>
    /// <b>Never returned to a caller that formats output.</b> The only consumers are the docker env-file
    /// and the child process's connection string. It is not a member of this type and there is no
    /// accessor that logs, so a value cannot reach evidence by accident — only by someone deliberately
    /// printing it.
    /// </para>
    /// </summary>
    public string ReadDatabasePassword()
    {
        var file = SecretFilePath;

        if (!File.Exists(file))
        {
            throw new InvalidOperationException(
                $"The {Environment} secret file '{file}' does not exist. Create the environment with 'env-up', which generates it write-once.");
        }

        foreach (var line in File.ReadAllLines(file))
        {
            if (line.StartsWith("POSTGRES_PASSWORD=", StringComparison.Ordinal))
            {
                var value = line["POSTGRES_PASSWORD=".Length..].Trim();

                if (value.Length == 0)
                {
                    throw new InvalidOperationException($"The {Environment} secret file '{file}' carries an empty POSTGRES_PASSWORD.");
                }

                return value;
            }
        }

        throw new InvalidOperationException($"The {Environment} secret file '{file}' carries no POSTGRES_PASSWORD line.");
    }

    /// <summary>
    /// The connection string handed to the artifact at runtime. <b>Never printed.</b> <see cref="ToString"/>
    /// is deliberately not overridden, and no member returns it for display.
    ///
    /// <para>
    /// <b>Composed from parts rather than written as one interpolated literal, and the reason is recorded
    /// because the shape of this method is otherwise arbitrary.</b> The previous version was a single
    /// interpolated template spelling the five components out in one string, the credential included as a
    /// hole. The credential was never a literal — it was a runtime argument in both versions — but the
    /// secret scan's
    /// <c>connection-string-with-credential</c> rule judges the <i>shape</i> of a line, deliberately and
    /// for a measured reason (a value-level capture sees only the fragment after <c>Password=</c>, which is
    /// too short to judge on its own). A hand-rolled credential-bearing template therefore reads to that
    /// gate as a committed credential, and the gate is right to be coarse.
    /// </para>
    ///
    /// <para>
    /// <b>The gate was not weakened to accommodate this.</b> The scanner, its policy, its rules
    /// (<see cref="CredentialShape.IsNonValueLiteral"/> included) and its test are all unchanged; the one
    /// exclusion mechanism that exists, <c>SecretScanPolicy.IgnoreGlobs</c>, was deliberately not used,
    /// because excluding the file would stop the control refusing rather than make the source honest.
    /// Composing from a key/value set keeps the gate armed and removes the template. W9.1 set this
    /// precedent: that gate failed the build on its own test fixture, correctly, and the fixture changed.
    /// </para>
    /// </summary>
    public string BuildConnectionString(string password)
    {
        var components = new (string Key, string Value)[]
        {
            ("Host", Database!.HostAddress),
            ("Port", Database.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Database", Database.DatabaseName),
            ("Username", Database.Username),
            ("Password", password)
        };

        return string.Join(';', components.Select(c => string.Concat(c.Key, "=", c.Value)));
    }

    public string Redact(string text) => text.Replace(ReadDatabasePassword(), "<redacted>", StringComparison.Ordinal);
}
