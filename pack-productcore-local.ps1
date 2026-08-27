# Packs Nexus.ProductCore.Contracts and Nexus.ProductCore.Scope and pushes both to
# GitHub Packages, mirroring pack-local.ps1's Step-1 flow (the way the seven
# Nexus.Platform.* packages were published in the package-feed migration).
#
# History: this script started as the local-only sibling that packed straight into
# C:\Personal\LocalNuGet while the device had no network path to GitHub Packages
# (see CHG-20260826-003 and the M-06-1.1 Slice-1/Slice-3 packaging decision,
# 2026-08-27). The M-08-1.1 follow-up adds the GitHub Packages push. The local feed
# write is preserved so Nexus.Developer's 'local' source keeps resolving during the
# transition.
#
# Push-only details are identical to pack-local.ps1: feed URL and api-key are passed
# inline to `dotnet nuget push`, the token is read from $env:GITHUB_PACKAGES_TOKEN
# and never displayed, logged, echoed, or written to any file.
#
# Versioning: every pack is stamped with a unique timestamped prerelease version
# (0.1.0-dev.<yyyyMMddHHmmss>) passed via -p:PackageVersion, so re-running this
# script produces a new version each time. GitHub Packages (like nuget.org) refuses
# to overwrite a published version; --skip-duplicate is kept only as a safety net,
# not as the versioning strategy.
#
# Error handling: $ErrorActionPreference = 'Stop', matching pack-local.ps1.
$ErrorActionPreference = 'Stop'

# Credential: read from $env:GITHUB_PACKAGES_TOKEN (a PAT scoped to write:packages).
if (-not $env:GITHUB_PACKAGES_TOKEN) {
    throw 'GITHUB_PACKAGES_TOKEN is not set. Set it to a GitHub PAT scoped to write:packages before running this script.'
}

$feed = 'C:\Personal\LocalNuGet'
if (-not (Test-Path $feed)) {
    throw "Local feed folder not found: $feed"
}

# The shared local feed still holds pre-migration Nexus.Platform.*/Nexus.Intelligence.*
# packages, so $feed\*.nupkg cannot be globbed for the push - it would sweep up stale
# packages. Pack to a dedicated output folder (holds only the two packages from this
# run), push from there, then copy the nupkgs into the local feed afterwards.
$out = Join-Path $PSScriptRoot 'artifacts\productcore-packages'

# Unique timestamped prerelease version, same scheme as pack-local.ps1, so re-running
# this script always produces a new version and consumers pick it up rather than
# reusing a cached older one.
$version = "0.1.0-dev.$(Get-Date -Format yyyyMMddHHmmss)"

Write-Host "packing Nexus.ProductCore.Contracts + Nexus.ProductCore.Scope as $version -> $out" -ForegroundColor Cyan

dotnet pack src\Nexus.ProductCore.Contracts\Nexus.ProductCore.Contracts.csproj -c Release -o $out -p:PackageVersion=$version --nologo
if ($LASTEXITCODE -ne 0) { throw 'pack failed: Nexus.ProductCore.Contracts' }

dotnet pack src\Nexus.ProductCore.Scope\Nexus.ProductCore.Scope.csproj -c Release -o $out -p:PackageVersion=$version --nologo
if ($LASTEXITCODE -ne 0) { throw 'pack failed: Nexus.ProductCore.Scope' }

# Push both packages to the GitHub Packages feed for 'prtcare'.
Write-Host 'pushing to GitHub Packages (https://nuget.pkg.github.com/prtcare/index.json)' -ForegroundColor Cyan
dotnet nuget push "$out\*.nupkg" --source https://nuget.pkg.github.com/prtcare/index.json --api-key $env:GITHUB_PACKAGES_TOKEN --skip-duplicate
if ($LASTEXITCODE -ne 0) { throw 'nuget push failed' }

# Preserve the original local-feed behavior so Nexus.Developer's 'local' source still
# resolves the freshly packed versions during the transition.
Copy-Item "$out\*.nupkg" $feed -Force
Write-Host "copied nupkgs into local feed ($feed)" -ForegroundColor Cyan

Write-Host 'done. Nexus.ProductCore packages pushed to GitHub Packages.' -ForegroundColor Green
