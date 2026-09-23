using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// The first <see cref="IArtifactRegistry"/> adapter: a deterministic, local, file-backed store.
///
/// <para>
/// <b>Chosen because it is the least capable thing that satisfies the contract</b>, which is the right
/// first adapter for an abstraction whose portability is a requirement. Nothing about a filesystem
/// biases the interface — if the contract only works against a filesystem, that is discovered here,
/// cheaply, rather than when an OCI or object-storage adapter is written. The Owner's ruling permits
/// exactly this for the W9 proof and requires that the cloud backends stay replaceable.
/// </para>
///
/// <para>
/// <b>Immutability is enforced, not assumed.</b> A bundle directory is created once and its manifest
/// is written with <see cref="FileMode.CreateNew"/>, which the operating system makes atomic: two
/// concurrent registrations of the same id cannot both succeed, and no caller has to check-then-write
/// and hope nothing raced it. That is the requirement recorded in W9.0 §4 — a store that <i>permits</i>
/// an overwrite and relies on people not doing it is not a control.
/// </para>
///
/// <para>
/// Layout, under the configured root:
/// <code>
/// bundles/&lt;bundleId&gt;/manifest.json     the canonical manifest (see BundleManifestCodec)
/// bundles/&lt;bundleId&gt;/quarantine.json   present only while the bundle is quarantined
/// </code>
/// </para>
/// </summary>
public sealed class FileArtifactRegistry : IArtifactRegistry
{
    private const string BundlesDirectoryName = "bundles";
    private const string ManifestFileName = "manifest.json";
    private const string QuarantineFileName = "quarantine.json";

    private readonly string _root;

    public FileArtifactRegistry(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("An artifact registry needs a root directory.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
    }

    public string Root => _root;

    public Task<RegistrationOutcome> RegisterAsync(ReleaseBundle bundle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveBundleDirectory(bundle.BundleId, out var directory))
        {
            return Task.FromResult(RegistrationOutcome.Refused(
                RegistryRefusalReason.PathEscapesRegistryRoot,
                $"Bundle id '{bundle.BundleId}' does not resolve inside the registry root."));
        }

        var manifestPath = Path.Combine(directory, ManifestFileName);
        var manifestBytes = BundleManifestCodec.Encode(bundle);

        try
        {
            Directory.CreateDirectory(directory);

            // CreateNew is the write-once guarantee. It is an OS-level atomic create: if the manifest
            // already exists this throws, and that throw IS the refusal. Check-then-write would let
            // two registrations of the same id both pass the check.
            using var stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(manifestBytes);
        }
        catch (IOException) when (File.Exists(manifestPath))
        {
            return Task.FromResult(RegistrationOutcome.Refused(
                RegistryRefusalReason.BundleIdAlreadyExists,
                $"Bundle '{bundle.BundleId}' is already registered. A changed artifact is a NEW bundle, never an overwrite."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(RegistrationOutcome.Refused(
                RegistryRefusalReason.StorageFailure,
                $"Writing the manifest failed: {ex.GetType().Name}."));
        }

        var entry = new ArtifactRegistryEntry(
            bundle.BundleId,
            ArtifactDigest.Compute(manifestBytes),
            bundle.CreatedAt,
            bundle.AllArtifacts.ToArray());

        return Task.FromResult(RegistrationOutcome.Accepted(entry));
    }

    public Task<bool> ExistsAsync(BundleId bundleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundleId);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            TryResolveBundleDirectory(bundleId, out var directory)
            && File.Exists(Path.Combine(directory, ManifestFileName)));
    }

    public async Task<ReleaseBundle?> TryOpenAsync(BundleId bundleId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundleId);

        if (!TryResolveBundleDirectory(bundleId, out var directory))
        {
            return null;
        }

        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);

        return BundleManifestCodec.TryDecode(bytes, out var bundle) ? bundle : null;
    }

    public async Task<ArtifactRegistryEntry?> TryGetEntryAsync(BundleId bundleId, CancellationToken cancellationToken = default)
    {
        var bundle = await TryOpenAsync(bundleId, cancellationToken).ConfigureAwait(false);
        if (bundle is null)
        {
            return null;
        }

        var quarantine = ReadQuarantine(bundleId);

        return new ArtifactRegistryEntry(
            bundle.BundleId,
            BundleManifestCodec.ComputeManifestDigest(bundle),
            bundle.CreatedAt,
            bundle.AllArtifacts.ToArray(),
            quarantine is not null,
            quarantine);
    }

    public async Task<IReadOnlyList<ArtifactRegistryEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var bundlesRoot = Path.Combine(_root, BundlesDirectoryName);
        if (!Directory.Exists(bundlesRoot))
        {
            return [];
        }

        var entries = new List<ArtifactRegistryEntry>();

        foreach (var directory in Directory.EnumerateDirectories(bundlesRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!BundleId.IsValid(Path.GetFileName(directory)))
            {
                continue;
            }

            var entry = await TryGetEntryAsync(BundleId.Parse(Path.GetFileName(directory)), cancellationToken)
                .ConfigureAwait(false);

            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        // Ordered by registration time then id, so the order is identical across adapters and across
        // filesystems — which a directory enumeration order is not.
        return [.. entries.OrderBy(e => e.RegisteredAt).ThenBy(e => e.BundleId.Value, StringComparer.Ordinal)];
    }

    public Task QuarantineAsync(BundleId bundleId, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundleId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A quarantine requires a reason. A quarantine without one cannot be reviewed, and a reason is what distinguishes it from a corrupt entry.",
                nameof(reason));
        }

        if (!TryResolveBundleDirectory(bundleId, out var directory) || !Directory.Exists(directory))
        {
            throw new InvalidOperationException($"Bundle '{bundleId}' is not registered, so it cannot be quarantined.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Overwriting a quarantine marker is deliberate: re-quarantining an already-quarantined bundle
        // with a new reason is a legitimate act, and the marker records a current state rather than an
        // immutable fact. The lineage record is where the history of the decision lives.
        File.WriteAllText(Path.Combine(directory, QuarantineFileName), reason);

        return Task.CompletedTask;
    }

    private string? ReadQuarantine(BundleId bundleId)
    {
        if (!TryResolveBundleDirectory(bundleId, out var directory))
        {
            return null;
        }

        var marker = Path.Combine(directory, QuarantineFileName);
        return File.Exists(marker) ? File.ReadAllText(marker) : null;
    }

    /// <summary>
    /// Resolves a bundle's directory and refuses anything that escapes the registry root.
    /// <see cref="BundleId"/> already restricts the character set to letters, digits, <c>-</c>,
    /// <c>.</c> and <c>_</c>, so traversal should be unrepresentable — this is the defence in depth
    /// that keeps it that way if the id rule is ever relaxed.
    /// </summary>
    private bool TryResolveBundleDirectory(BundleId bundleId, out string directory)
    {
        directory = string.Empty;

        if (bundleId is null || string.IsNullOrWhiteSpace(bundleId.Value))
        {
            return false;
        }

        var candidate = Path.GetFullPath(Path.Combine(_root, BundlesDirectoryName, bundleId.Value));
        var expectedPrefix = Path.Combine(_root, BundlesDirectoryName) + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        directory = candidate;
        return true;
    }
}
