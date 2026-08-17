# Builds and packs the Platform libraries into the local feed.
# The version carries a timestamp suffix on purpose: NuGet caches by version, so re-packing
# the SAME version would not be picked up by Nexus.Int. Consumers reference '0.1.0-*'.
$ErrorActionPreference = 'Stop'
$feed    = 'C:\Personal\LocalNuGet'
$version = "0.1.0-dev.$(Get-Date -Format yyyyMMddHHmmss)"

Write-Host "packing Nexus.Platform.* as $version -> $feed" -ForegroundColor Cyan
dotnet pack Nexus.AI.slnx -c Release -o $feed -p:PackageVersion=$version --nologo
if ($LASTEXITCODE -ne 0) { throw 'pack failed' }
Write-Host "done. run 'dotnet restore' in Nexus.Int to pick it up." -ForegroundColor Green
