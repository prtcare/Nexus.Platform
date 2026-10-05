using System.IO.Compression;
using Nexus.Delivery.Contracts;

namespace Nexus.Delivery.Core;

/// <summary>
/// Packages a directory of built output as a deterministic zip archive.
///
/// <para>
/// <b>Determinism is the whole engineering problem here, and it is not free.</b> A zip records each
/// entry's modification time, and the default is the file's timestamp on disk — so archiving the same
/// content twice produces different bytes, and a reproducibility proof would fail for a reason that has
/// nothing to do with the build. This packager therefore fixes every entry's timestamp to a constant, sorts
/// entries by path, and writes them through a fixed compression level, so the archive is a pure function of
/// its content. A test zips one directory twice and compares hashes.
/// </para>
///
/// <para>
/// <b>It injects nothing.</b> No environment name, no endpoint, no configuration, no secret. The output
/// directory is read and archived; nothing else is consulted. That is the negative obligation in
/// <see cref="IArtifactPackager"/>, and it is the one that would be easy to violate accidentally — the
/// packagers in this estate's history took <c>API_INTERNAL_URL</c> as a build argument, and the reason they
/// could is that a packaging step is a convenient place to "fill in" a value.
/// </para>
/// </summary>
public sealed class ZipDirectoryPackager : IArtifactPackager
{
    /// <summary>
    /// The fixed timestamp written into every archive entry. 1980-01-01 is the earliest value the zip
    /// format can represent, so it is also the least likely to be mistaken for a real build time.
    /// </summary>
    private static readonly DateTimeOffset FixedEntryTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public ZipDirectoryPackager(ArtifactType artifactType, string archiveExtension = ".zip")
    {
        if (artifactType is not (ArtifactType.DotnetApplication or ArtifactType.DotnetLibrary or ArtifactType.StaticClientBundle))
        {
            throw new ArgumentException(
                $"{nameof(ZipDirectoryPackager)} packages directory outputs (application, library, client bundle), not {artifactType}.",
                nameof(artifactType));
        }

        ArtifactType = artifactType;
        ArchiveExtension = archiveExtension;
    }

    public ArtifactType ArtifactType { get; }

    public string ArchiveExtension { get; }

    public Task<PackageOutcome> PackageAsync(PackageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Directory.Exists(request.StagingDirectory))
        {
            return Task.FromResult(PackageOutcome.Refused($"Nothing to package: '{Path.GetFileName(request.StagingDirectory)}' does not exist."));
        }

        var files = Directory.GetFiles(request.StagingDirectory, "*", SearchOption.AllDirectories);

        if (files.Length == 0)
        {
            return Task.FromResult(PackageOutcome.Refused(
                "The staging directory is empty. Packaging nothing would produce an artifact that certifies nothing."));
        }

        Directory.CreateDirectory(request.OutputDirectory);

        var fileName = request.ArtifactId.Name + ArchiveExtension;
        var outputPath = Path.Combine(request.OutputDirectory, fileName);

        try
        {
            using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                // Sorted, so entry order is a function of content rather than of the filesystem's
                // enumeration order, which is not specified on any platform.
                foreach (var file in files.OrderBy(f => Path.GetRelativePath(request.StagingDirectory, f), StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entryName = Path.GetRelativePath(request.StagingDirectory, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                    entry.LastWriteTime = FixedEntryTimestamp;

                    using var source = File.OpenRead(file);
                    using var destination = entry.Open();
                    source.CopyTo(destination);
                }
            }

            using var packaged = File.OpenRead(outputPath);
            var digest = ArtifactDigest.Compute(packaged);
            var size = packaged.Length;

            return Task.FromResult(PackageOutcome.Packaged(
                new PackagedArtifact(request.ArtifactId, digest, size, request.BuildId, fileName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(PackageOutcome.Refused($"Packaging failed: {ex.GetType().Name}."));
        }
    }
}

/// <summary>
/// Packages a file that a toolchain has already produced, unchanged.
///
/// <para>
/// For artifacts whose own producer already emits a deterministic file — a <c>.nupkg</c> from
/// <c>dotnet pack</c>, or an image tar — repacking would be worse than useless: it would add a second
/// opportunity for non-determinism and change the bytes a consumer's tooling expects. This packager
/// copies and hashes, and injects nothing.
/// </para>
/// </summary>
public sealed class PreBuiltFilePackager : IArtifactPackager
{
    public PreBuiltFilePackager(ArtifactType artifactType = ArtifactType.Package)
    {
        ArtifactType = artifactType;
    }

    public ArtifactType ArtifactType { get; }

    public Task<PackageOutcome> PackageAsync(PackageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.StagingDirectory))
        {
            return Task.FromResult(PackageOutcome.Refused($"Nothing to package: '{Path.GetFileName(request.StagingDirectory)}' does not exist."));
        }

        Directory.CreateDirectory(request.OutputDirectory);

        var fileName = Path.GetFileName(request.StagingDirectory);
        var outputPath = Path.Combine(request.OutputDirectory, fileName);

        try
        {
            File.Copy(request.StagingDirectory, outputPath, overwrite: true);

            using var stream = File.OpenRead(outputPath);
            var digest = ArtifactDigest.Compute(stream);
            var size = stream.Length;

            return Task.FromResult(PackageOutcome.Packaged(
                new PackagedArtifact(request.ArtifactId, digest, size, request.BuildId, fileName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(PackageOutcome.Refused($"Packaging failed: {ex.GetType().Name}."));
        }
    }
}
