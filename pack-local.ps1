# Builds and pushes the seven Nexus.Platform.* packages to GitHub Packages.
#
# Push-only flow: NexusAI never restores from this feed - every internal
# dependency is a ProjectReference and external PackageReferences resolve from
# the default nuget.org source - so no nuget.config and no source registration
# are needed. The feed URL and api-key are passed inline to `dotnet nuget push`.
# No nuget.config file is created by this script.
#
# Versioning: every pack is stamped with a unique timestamped prerelease version
# (0.1.0-dev.<yyyyMMddHHmmss>) passed via -p:PackageVersion, so re-running this
# script produces a new version each time. GitHub Packages (like nuget.org)
# refuses to overwrite a published version; --skip-duplicate is kept only as a
# safety net, not as the versioning strategy.
#
# Error handling: this script uses $ErrorActionPreference = 'Stop'. The repo has no
# documented PowerShell error-handling convention yet; closing that documentation
# gap is tracked separately.
$ErrorActionPreference = 'Stop'

# Credential: read from $env:GITHUB_PACKAGES_TOKEN (a PAT scoped to write:packages).
# Its value is never read for display, logged, echoed, or written to any file -
# it is only passed as the api-key argument to dotnet nuget push.
if (-not $env:GITHUB_PACKAGES_TOKEN) {
    throw 'GITHUB_PACKAGES_TOKEN is not set. Set it to a GitHub PAT scoped to write:packages before running this script.'
}

# Local artifacts folder (gitignored - /artifacts/ is in .gitignore).
$out = Join-Path $PSScriptRoot 'artifacts\packages'

# Unique timestamped prerelease version so every run publishes a distinct version.
$version = "0.1.0-dev.$(Get-Date -Format yyyyMMddHHmmss)"

# 1. Pack all seven Nexus.Platform.* projects (the two test projects are IsPackable=false).
Write-Host "packing Nexus.Platform.* as $version -> $out" -ForegroundColor Cyan
dotnet pack Nexus.AI.slnx -c Release -o $out -p:PackageVersion=$version --nologo
if ($LASTEXITCODE -ne 0) { throw 'pack failed' }

# 2. Push everything in $out to the GitHub Packages feed for 'prtcare'.
Write-Host 'pushing to GitHub Packages (https://nuget.pkg.github.com/prtcare/index.json)' -ForegroundColor Cyan
dotnet nuget push "$out\*.nupkg" --source https://nuget.pkg.github.com/prtcare/index.json --api-key $env:GITHUB_PACKAGES_TOKEN --skip-duplicate

# 3. Fail the script (non-zero exit) if the push failed - do not swallow the error.
if ($LASTEXITCODE -ne 0) { throw 'nuget push failed' }

Write-Host 'done. packages pushed to GitHub Packages.' -ForegroundColor Green
