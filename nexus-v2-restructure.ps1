<#
.SYNOPSIS
    Restructures Nexus into three solutions: Nexus.AI (Platform), Nexus.Int (Intelligence),
    Nexus.Web (Chatbot product).

.DESCRIPTION
    Implements stages 1-3 and 9 of docs\NEXUS_ARCHITECTURE_V2.md.

    Phase "Scaffold" (default):
      - creates the local NuGet feed
      - rebuilds NexusAI as the Platform solution (git mv, same repo)
      - creates the Nexus.Int repo and solution, COPYING intelligence code out of NexusAI
      - creates the .NET projects inside the existing Nexus.Web repo, COPYING product code out

    Phase "Cleanup" (run only after all three solutions build):
      - deletes from NexusAI everything that was copied out

    Cross-repo moves are copy-then-delete, not git mv - git cannot move between repos.
    Copying first means the source survives until you have verified the destination.
    NexusAI's history and the pre-v2 tag retain everything.

    Code that needs a genuine REWRITE rather than a move lands in a `_migrate/` folder in
    its destination. Those folders are the worklist and must be empty before you tag v2-arch.

.PARAMETER Execute
    Actually perform the changes. Without it this is a dry run.

.EXAMPLE
    .\nexus-v2-restructure.ps1                    # dry run, review output
    .\nexus-v2-restructure.ps1 -Execute           # scaffold all three solutions
    .\nexus-v2-restructure.ps1 -Phase Cleanup -Execute    # much later
#>

[CmdletBinding()]
param(
    [string] $NexusAIRoot  = 'C:\Personal\NexusAI',
    [string] $NexusIntRoot = 'C:\Personal\Nexus.Int',
    [string] $NexusWebRoot = 'C:\Personal\Nexus.Web',
    [string] $LocalFeed    = 'C:\Personal\LocalNuGet',
    [ValidateSet('Scaffold','Cleanup')]
    [string] $Phase        = 'Scaffold',
    [switch] $Execute
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:Creates  = 0
$script:Moves    = 0
$script:Copies   = 0
$script:Deletes  = 0
$script:Warnings = @()

function Write-Step { param([string]$m) Write-Host ""; Write-Host "== $m" -ForegroundColor Cyan }
function Write-Do   { param([string]$m) Write-Host "   $m" -ForegroundColor DarkGray }
function Write-Warn2{ param([string]$m) $script:Warnings += $m; Write-Host "   ! $m" -ForegroundColor Yellow }

# Native commands: never let stderr become a terminating error. Exit code is the signal.
function Invoke-Native {
    param([Parameter(Mandatory)][string]$Exe, [string[]]$Arguments = @(), [string]$WorkDir)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    if ($WorkDir) { Push-Location $WorkDir }
    try {
        $script:NativeOutput = @(& $Exe @Arguments 2>&1 | ForEach-Object { "$_" })
        return ($LASTEXITCODE -eq 0)
    }
    finally {
        if ($WorkDir) { Pop-Location }
        $ErrorActionPreference = $prev
    }
}

function New-Dir {
    param([string]$Path)
    if (Test-Path $Path) { return }
    if ($Execute) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

function Write-TextFile {
    param([string]$Path, [string]$Content)
    Write-Do "write $Path"
    $script:Creates++
    if ($Execute) {
        New-Dir (Split-Path $Path -Parent)
        Set-Content -Path $Path -Value $Content -Encoding UTF8
    }
}

# git mv WITHIN one repo.
function Move-Git {
    param([string]$Repo, [string]$From, [string]$To, [string]$Rename = '')
    $abs = Join-Path $Repo $From
    if (-not (Test-Path $abs)) { Write-Warn2 "move source missing: $From"; return }

    $leaf   = if ($Rename -ne '') { $Rename } else { Split-Path $From -Leaf }
    $target = Join-Path $To $leaf
    Write-Do "git mv  $From  ->  $target"
    $script:Moves++
    if ($Execute) {
        New-Dir (Join-Path $Repo $To)
        if (-not (Invoke-Native -Exe 'git' -Arguments @('mv','--force','--',$From,$target) -WorkDir $Repo)) {
            Write-Warn2 "git mv failed ($From): $($script:NativeOutput -join ' ')"
        }
    }
}

# Copy ACROSS repos. Source is left intact until the Cleanup phase.
function Copy-Tree {
    param([string]$FromRoot, [string]$From, [string]$ToRoot, [string]$To, [string]$Rename = '')
    $src = Join-Path $FromRoot $From
    if (-not (Test-Path $src)) { Write-Warn2 "copy source missing: $From"; return }

    $leaf = if ($Rename -ne '') { $Rename } else { Split-Path $From -Leaf }
    $dst  = Join-Path (Join-Path $ToRoot $To) $leaf
    Write-Do "copy    $From  ->  $To\$leaf"
    $script:Copies++
    if ($Execute) {
        New-Dir (Join-Path $ToRoot $To)
        if (Test-Path $src -PathType Container) {
            Copy-Item -Path $src -Destination $dst -Recurse -Force
        }
        else {
            Copy-Item -Path $src -Destination $dst -Force
        }
    }
}

function Remove-Path {
    param([string]$Repo, [string]$Path, [switch]$Git)
    $abs = Join-Path $Repo $Path
    if (-not (Test-Path $abs)) { Write-Warn2 "delete target missing: $Path"; return }
    Write-Do "delete  $Path"
    $script:Deletes++
    if (-not $Execute) { return }
    if ($Git) {
        if (-not (Invoke-Native -Exe 'git' -Arguments @('rm','-r','--quiet','--force','--',$Path) -WorkDir $Repo)) {
            Remove-Item -Path $abs -Recurse -Force
        }
    }
    else { Remove-Item -Path $abs -Recurse -Force }
}

<#
  Emits a .csproj.
  $ProjectRefs : names of projects in the SAME solution (relative paths computed)
  $PackageRefs : hashtable of package -> version (includes cross-solution Nexus packages)
#>
function New-Csproj {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][string] $Folder,      # relative to $Root
        [Parameter(Mandatory)][string] $Name,
        [string]    $Sdk         = 'Microsoft.NET.Sdk',
        [string[]]  $ProjectRefs = @(),
        [hashtable] $PackageRefs = @{},
        [hashtable] $FolderMap   = @{},              # project name -> folder, same solution
        [switch]    $Packable,
        [switch]    $IsTest,
        [string]    $UserSecrets = ''
    )

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("<Project Sdk=`"$Sdk`">")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("  <PropertyGroup>")
    [void]$sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>")
    [void]$sb.AppendLine("    <RootNamespace>$Name</RootNamespace>")
    [void]$sb.AppendLine("    <AssemblyName>$Name</AssemblyName>")
    if ($UserSecrets -ne '') { [void]$sb.AppendLine("    <UserSecretsId>$UserSecrets</UserSecretsId>") }
    if ($Packable) {
        [void]$sb.AppendLine("    <IsPackable>true</IsPackable>")
        [void]$sb.AppendLine("    <PackageId>$Name</PackageId>")
        [void]$sb.AppendLine("    <Authors>PRT</Authors>")
        [void]$sb.AppendLine("    <Description>Nexus V2 - $Name</Description>")
    }
    elseif (-not $IsTest) { [void]$sb.AppendLine("    <IsPackable>false</IsPackable>") }
    if ($IsTest) {
        [void]$sb.AppendLine("    <IsPackable>false</IsPackable>")
        [void]$sb.AppendLine("    <IsTestProject>true</IsTestProject>")
    }
    [void]$sb.AppendLine("  </PropertyGroup>")

    if ($ProjectRefs.Count -gt 0) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("  <ItemGroup>")
        foreach ($r in $ProjectRefs) {
            if (-not $FolderMap.ContainsKey($r)) { throw "Unknown project reference '$r'." }
            $rel = Get-RelativePath -FromFolder $Folder -ToFolder $FolderMap[$r]
            [void]$sb.AppendLine("    <ProjectReference Include=`"$rel$r.csproj`" />")
        }
        [void]$sb.AppendLine("  </ItemGroup>")
    }

    if ($PackageRefs.Count -gt 0) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("  <ItemGroup>")
        foreach ($k in ($PackageRefs.Keys | Sort-Object)) {
            [void]$sb.AppendLine("    <PackageReference Include=`"$k`" Version=`"$($PackageRefs[$k])`" />")
        }
        [void]$sb.AppendLine("  </ItemGroup>")
    }

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("</Project>")

    Write-TextFile (Join-Path (Join-Path $Root $Folder) "$Name.csproj") $sb.ToString()
}

function Get-RelativePath {
    param([string]$FromFolder, [string]$ToFolder)
    $f = @($FromFolder -split '[\\/]' | Where-Object { $_ -ne '' })
    $t = @($ToFolder   -split '[\\/]' | Where-Object { $_ -ne '' })
    $c = 0
    while ($c -lt $f.Count -and $c -lt $t.Count -and $f[$c] -eq $t[$c]) { $c++ }
    $up   = '..\' * ($f.Count - $c)
    $down = if ($c -lt $t.Count) { (($t[$c..($t.Count-1)]) -join '\') + '\' } else { '' }
    return "$up$down"
}

# Local packages float so a re-pack is actually picked up. See the note in pack-local.ps1.
$NexusPkgVersion = '0.1.0-*'

$PkgOptions   = @{ 'Microsoft.Extensions.Options.ConfigurationExtensions' = '10.0.11' }
$PkgOpenAI    = @{ 'OpenAI' = '2.13.0'; 'Microsoft.Extensions.Options.ConfigurationExtensions' = '10.0.11' }
$PkgDataverse = @{ 'Microsoft.PowerPlatform.Dataverse.Client' = '1.2.26'
                   'Microsoft.Extensions.Options.ConfigurationExtensions' = '10.0.11' }
$PkgSwagger   = @{ 'Swashbuckle.AspNetCore' = '10.2.3' }
$PkgHttp      = @{ 'Microsoft.Extensions.Http' = '10.0.11' }
$PkgTest      = @{ 'Microsoft.NET.Test.Sdk' = '17.14.1'; 'xunit' = '2.9.3'
                   'xunit.runner.visualstudio' = '3.1.1' }
$PkgArch      = @{ 'Microsoft.NET.Test.Sdk' = '17.14.1'; 'xunit' = '2.9.3'
                   'xunit.runner.visualstudio' = '3.1.1'; 'NetArchTest.Rules' = '1.3.2' }

function Join-Hash {
    param([hashtable[]]$Tables)
    $out = @{}
    foreach ($t in $Tables) { foreach ($k in $t.Keys) { $out[$k] = $t[$k] } }
    return $out
}

# ---------------------------------------------------------------------------
# Preflight
# ---------------------------------------------------------------------------

Write-Step "Preflight"

if (-not (Test-Path (Join-Path $NexusAIRoot 'NexusAI.slnx'))) {
    throw "NexusAI.slnx not found in $NexusAIRoot"
}
if (-not (Test-Path (Join-Path $NexusWebRoot 'Nexus.Web.slnx'))) {
    throw "Nexus.Web.slnx not found in $NexusWebRoot"
}
Write-Do "Nexus.AI  : $NexusAIRoot"
Write-Do "Nexus.Int : $NexusIntRoot  $(if (Test-Path $NexusIntRoot) {'(exists)'} else {'(will be created)'})"
Write-Do "Nexus.Web : $NexusWebRoot"
Write-Do "Local feed: $LocalFeed"

foreach ($r in @($NexusAIRoot, $NexusWebRoot)) {
    [void](Invoke-Native -Exe 'git' -Arguments @('status','--porcelain') -WorkDir $r)
    $dirty = @($script:NativeOutput | Where-Object { $_ -ne '' })
    if ($dirty.Count -gt 0) {
        if ($Execute) {
            Write-Host ""
            $dirty | Select-Object -First 15 | ForEach-Object { Write-Host "     $_" -ForegroundColor Yellow }
            throw "Working tree dirty in $r. Commit or stash first."
        }
        Write-Warn2 "$r is dirty (dry run only)"
    }
}

if (-not $Execute) {
    Write-Host ""
    Write-Host "  DRY RUN - nothing will change. Re-run with -Execute." -ForegroundColor Yellow
}

# ===========================================================================
if ($Phase -eq 'Cleanup') {

    Write-Step "Cleanup - removing from NexusAI everything that moved out"
    Write-Host "   Only run this once Nexus.Int and Nexus.Web both build." -ForegroundColor Yellow

    foreach ($p in @('src\NexusAI.Domain','src\NexusAI.Application','src\NexusAI.Infrastructure',
                     'src\NexusAI.Core','src\NexusAI.Agents','src\NexusAI.Api','src\NexusAI.Host',
                     'src\NexusAI.Foundation','NexusAI.slnx','NexusAI.slnLaunch.user')) {
        Remove-Path -Repo $NexusAIRoot -Path $p -Git
    }

    Write-Step "Summary"
    Write-Host "   deleted : $($script:Deletes)"
    if ($script:Warnings.Count -gt 0) {
        Write-Host "   warnings: $($script:Warnings.Count)" -ForegroundColor Yellow
        $script:Warnings | ForEach-Object { Write-Host "     - $_" -ForegroundColor Yellow }
    }
    Write-Host ""
    return
}

# ===========================================================================
# A. Local NuGet feed
# ===========================================================================

Write-Step "A. Local NuGet feed"
Write-Do "ensure $LocalFeed"
if ($Execute) { New-Dir $LocalFeed }

$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="nexus-local" value="$LocalFeed" />
  </packageSources>
</configuration>
"@

# ===========================================================================
# B. Nexus.AI  ->  the Platform solution
# ===========================================================================

Write-Step "B. Nexus.AI - Platform"

$AI = @{
    Contracts = 'src\Nexus.Platform.Contracts'
    Core      = 'src\Nexus.Platform.Core'
    OpenAI    = 'src\Nexus.Platform.Providers.OpenAI'
    Anthropic = 'src\Nexus.Platform.Providers.Anthropic'
    Tools     = 'src\Nexus.Platform.Tools'
    Identity  = 'src\Nexus.Platform.Identity'
    Persist   = 'src\Nexus.Platform.Persistence'
    Tests     = 'tests\Nexus.Platform.Tests'
    ArchTests = 'tests\Nexus.Platform.Architecture.Tests'
}
$AIMap = @{
    'Nexus.Platform.Contracts'           = $AI.Contracts
    'Nexus.Platform.Core'                = $AI.Core
    'Nexus.Platform.Providers.OpenAI'    = $AI.OpenAI
    'Nexus.Platform.Providers.Anthropic' = $AI.Anthropic
    'Nexus.Platform.Tools'               = $AI.Tools
    'Nexus.Platform.Identity'            = $AI.Identity
    'Nexus.Platform.Persistence'         = $AI.Persist
}

New-Csproj -Root $NexusAIRoot -Folder $AI.Contracts -Name 'Nexus.Platform.Contracts' -Packable -FolderMap $AIMap
New-Csproj -Root $NexusAIRoot -Folder $AI.Core      -Name 'Nexus.Platform.Core'      -Packable -FolderMap $AIMap `
    -ProjectRefs @('Nexus.Platform.Contracts') -PackageRefs $PkgOptions
New-Csproj -Root $NexusAIRoot -Folder $AI.OpenAI    -Name 'Nexus.Platform.Providers.OpenAI' -Packable -FolderMap $AIMap `
    -ProjectRefs @('Nexus.Platform.Contracts') -PackageRefs $PkgOpenAI
New-Csproj -Root $NexusAIRoot -Folder $AI.Anthropic -Name 'Nexus.Platform.Providers.Anthropic' -Packable -FolderMap $AIMap `
    -ProjectRefs @('Nexus.Platform.Contracts') -PackageRefs $PkgOptions
New-Csproj -Root $NexusAIRoot -Folder $AI.Tools     -Name 'Nexus.Platform.Tools'     -Packable -FolderMap $AIMap -ProjectRefs @('Nexus.Platform.Contracts')
New-Csproj -Root $NexusAIRoot -Folder $AI.Identity  -Name 'Nexus.Platform.Identity'  -Packable -FolderMap $AIMap -ProjectRefs @('Nexus.Platform.Contracts')
New-Csproj -Root $NexusAIRoot -Folder $AI.Persist   -Name 'Nexus.Platform.Persistence' -Packable -FolderMap $AIMap -ProjectRefs @('Nexus.Platform.Contracts')
New-Csproj -Root $NexusAIRoot -Folder $AI.Tests     -Name 'Nexus.Platform.Tests' -IsTest -FolderMap $AIMap `
    -ProjectRefs @('Nexus.Platform.Core','Nexus.Platform.Contracts') -PackageRefs $PkgTest
New-Csproj -Root $NexusAIRoot -Folder $AI.ArchTests -Name 'Nexus.Platform.Architecture.Tests' -IsTest -FolderMap $AIMap `
    -ProjectRefs @('Nexus.Platform.Core','Nexus.Platform.Contracts','Nexus.Platform.Providers.OpenAI') -PackageRefs $PkgArch

# Provider contracts and the OpenAI adapter stay in this repo - git mv, history preserved.
Move-Git -Repo $NexusAIRoot -From 'src\NexusAI.Application\Providers' -To $AI.Contracts -Rename '_migrate'
Move-Git -Repo $NexusAIRoot -From 'src\NexusAI.Infrastructure\OpenAI' -To $AI.OpenAI    -Rename '_migrate'

Write-TextFile (Join-Path $NexusAIRoot 'Nexus.AI.slnx') @"
<Solution>
  <Folder Name="/src/">
    <Project Path="$($AI.Contracts)\Nexus.Platform.Contracts.csproj" />
    <Project Path="$($AI.Core)\Nexus.Platform.Core.csproj" />
    <Project Path="$($AI.OpenAI)\Nexus.Platform.Providers.OpenAI.csproj" />
    <Project Path="$($AI.Anthropic)\Nexus.Platform.Providers.Anthropic.csproj" />
    <Project Path="$($AI.Tools)\Nexus.Platform.Tools.csproj" />
    <Project Path="$($AI.Identity)\Nexus.Platform.Identity.csproj" />
    <Project Path="$($AI.Persist)\Nexus.Platform.Persistence.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="$($AI.Tests)\Nexus.Platform.Tests.csproj" />
    <Project Path="$($AI.ArchTests)\Nexus.Platform.Architecture.Tests.csproj" />
  </Folder>
</Solution>
"@

$aiProps = @'
<Project>

  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <Version>0.1.0</Version>
  </PropertyGroup>

  <!-- Nexus V2 boundary guard: Platform knows nothing above it. -->
  <Target Name="NexusBoundaryGuard" BeforeTargets="Build">
    <ItemGroup Condition="$(MSBuildProjectName.StartsWith('Nexus.Platform.'))">
      <_Bad Include="@(ProjectReference)"
            Condition="$([System.String]::Copy('%(Filename)').StartsWith('Nexus.Intelligence.')) OR $([System.String]::Copy('%(Filename)').StartsWith('Nexus.Products.'))" />
      <_Bad Include="@(PackageReference)"
            Condition="$([System.String]::Copy('%(Identity)').StartsWith('Nexus.Intelligence.')) OR $([System.String]::Copy('%(Identity)').StartsWith('Nexus.Products.'))" />
    </ItemGroup>
    <Error Condition="'@(_Bad)' != ''"
           Text="Nexus V2 boundary violation in $(MSBuildProjectName): Platform may not reference @(_Bad->'%(Identity)', ', '). See NEXUS_ARCHITECTURE_V2.md section 2.3." />
  </Target>

</Project>
'@
Write-TextFile (Join-Path $NexusAIRoot 'Directory.Build.props') $aiProps

$packAI = @"
# Builds and packs the Platform libraries into the local feed.
# The version carries a timestamp suffix on purpose: NuGet caches by version, so re-packing
# the SAME version would not be picked up by Nexus.Int. Consumers reference '0.1.0-*'.
`$ErrorActionPreference = 'Stop'
`$feed    = '$LocalFeed'
`$version = "0.1.0-dev.`$(Get-Date -Format yyyyMMddHHmmss)"

Write-Host "packing Nexus.Platform.* as `$version -> `$feed" -ForegroundColor Cyan
dotnet pack Nexus.AI.slnx -c Release -o `$feed -p:PackageVersion=`$version --nologo
if (`$LASTEXITCODE -ne 0) { throw 'pack failed' }
Write-Host "done. run 'dotnet restore' in Nexus.Int to pick it up." -ForegroundColor Green
"@
Write-TextFile (Join-Path $NexusAIRoot 'pack-local.ps1') $packAI

# ===========================================================================
# C. Nexus.Int  ->  the Intelligence solution (new repo)
# ===========================================================================

Write-Step "C. Nexus.Int - Intelligence (new repo)"

if (-not (Test-Path $NexusIntRoot)) {
    Write-Do "mkdir $NexusIntRoot"
    if ($Execute) { New-Item -ItemType Directory -Path $NexusIntRoot -Force | Out-Null }
}
if (-not (Test-Path (Join-Path $NexusIntRoot '.git'))) {
    Write-Do "git init $NexusIntRoot"
    if ($Execute) { [void](Invoke-Native -Exe 'git' -Arguments @('init','-b','main') -WorkDir $NexusIntRoot) }
}

$INT = @{
    Contracts = 'src\Nexus.Intelligence.Contracts'
    Core      = 'src\Nexus.Intelligence.Core'
    Context   = 'src\Nexus.Intelligence.Context'
    Agents    = 'src\Nexus.Intelligence.Agents'
    Memory    = 'src\Nexus.Intelligence.Memory'
    Api       = 'src\Nexus.Intelligence.Api'
    Tests     = 'tests\Nexus.Intelligence.Tests'
    ArchTests = 'tests\Nexus.Intelligence.Architecture.Tests'
}
$INTMap = @{
    'Nexus.Intelligence.Contracts' = $INT.Contracts
    'Nexus.Intelligence.Core'      = $INT.Core
    'Nexus.Intelligence.Context'   = $INT.Context
    'Nexus.Intelligence.Agents'    = $INT.Agents
    'Nexus.Intelligence.Memory'    = $INT.Memory
    'Nexus.Intelligence.Api'       = $INT.Api
}

$platContractsPkg = @{ 'Nexus.Platform.Contracts' = $NexusPkgVersion }
$platAllPkgs      = @{
    'Nexus.Platform.Contracts'           = $NexusPkgVersion
    'Nexus.Platform.Core'                = $NexusPkgVersion
    'Nexus.Platform.Providers.OpenAI'    = $NexusPkgVersion
    'Nexus.Platform.Providers.Anthropic' = $NexusPkgVersion
    'Nexus.Platform.Tools'               = $NexusPkgVersion
    'Nexus.Platform.Identity'            = $NexusPkgVersion
    'Nexus.Platform.Persistence'         = $NexusPkgVersion
}

New-Csproj -Root $NexusIntRoot -Folder $INT.Contracts -Name 'Nexus.Intelligence.Contracts' -Packable -FolderMap $INTMap
New-Csproj -Root $NexusIntRoot -Folder $INT.Context   -Name 'Nexus.Intelligence.Context' -FolderMap $INTMap `
    -ProjectRefs @('Nexus.Intelligence.Contracts') -PackageRefs $platContractsPkg
New-Csproj -Root $NexusIntRoot -Folder $INT.Agents    -Name 'Nexus.Intelligence.Agents' -FolderMap $INTMap `
    -ProjectRefs @('Nexus.Intelligence.Contracts') -PackageRefs $platContractsPkg
New-Csproj -Root $NexusIntRoot -Folder $INT.Memory    -Name 'Nexus.Intelligence.Memory' -FolderMap $INTMap `
    -ProjectRefs @('Nexus.Intelligence.Contracts')
New-Csproj -Root $NexusIntRoot -Folder $INT.Core      -Name 'Nexus.Intelligence.Core' -FolderMap $INTMap `
    -ProjectRefs @('Nexus.Intelligence.Contracts','Nexus.Intelligence.Context','Nexus.Intelligence.Agents','Nexus.Intelligence.Memory') `
    -PackageRefs (Join-Hash @($platContractsPkg, @{ 'Nexus.Platform.Core' = $NexusPkgVersion }))
New-Csproj -Root $NexusIntRoot -Folder $INT.Api -Name 'Nexus.Intelligence.Api' -Sdk 'Microsoft.NET.Sdk.Web' -FolderMap $INTMap `
    -ProjectRefs @('Nexus.Intelligence.Core','Nexus.Intelligence.Contracts') `
    -PackageRefs (Join-Hash @($platAllPkgs, $PkgSwagger))
New-Csproj -Root $NexusIntRoot -Folder $INT.Tests -Name 'Nexus.Intelligence.Tests' -IsTest -FolderMap $INTMap `
    -ProjectRefs @('Nexus.Intelligence.Core','Nexus.Intelligence.Contracts') -PackageRefs $PkgTest
New-Csproj -Root $NexusIntRoot -Folder $INT.ArchTests -Name 'Nexus.Intelligence.Architecture.Tests' -IsTest -FolderMap $INTMap `
    -ProjectRefs @('Nexus.Intelligence.Core','Nexus.Intelligence.Contracts','Nexus.Intelligence.Context','Nexus.Intelligence.Agents') -PackageRefs $PkgArch

# --- copy intelligence code out of NexusAI ---
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Application\Chat\Prompting' -ToRoot $NexusIntRoot -To $INT.Context
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Application\Knowledge\Services\IKnowledgeRanker.cs'       -ToRoot $NexusIntRoot -To "$($INT.Context)\Ranking\_migrate"
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Application\Knowledge\Services\KeywordKnowledgeRanker.cs' -ToRoot $NexusIntRoot -To "$($INT.Context)\Ranking\_migrate"
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Application\Planning'  -ToRoot $NexusIntRoot -To $INT.Core
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Application\Execution' -ToRoot $NexusIntRoot -To $INT.Core
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Core\Agents'           -ToRoot $NexusIntRoot -To $INT.Agents -Rename 'Abstractions'
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Infrastructure\Services\AgentRegistry.cs'   -ToRoot $NexusIntRoot -To $INT.Agents
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Infrastructure\Services\AgentDispatcher.cs' -ToRoot $NexusIntRoot -To $INT.Agents
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Agents\DeveloperAgent' -ToRoot $NexusIntRoot -To $INT.Agents -Rename 'BuiltIn'
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Domain\Memory'         -ToRoot $NexusIntRoot -To $INT.Memory -Rename '_migrate'

Write-TextFile (Join-Path $NexusIntRoot 'Nexus.Int.slnx') @"
<Solution>
  <Folder Name="/src/">
    <Project Path="$($INT.Contracts)\Nexus.Intelligence.Contracts.csproj" />
    <Project Path="$($INT.Core)\Nexus.Intelligence.Core.csproj" />
    <Project Path="$($INT.Context)\Nexus.Intelligence.Context.csproj" />
    <Project Path="$($INT.Agents)\Nexus.Intelligence.Agents.csproj" />
    <Project Path="$($INT.Memory)\Nexus.Intelligence.Memory.csproj" />
    <Project Path="$($INT.Api)\Nexus.Intelligence.Api.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="$($INT.Tests)\Nexus.Intelligence.Tests.csproj" />
    <Project Path="$($INT.ArchTests)\Nexus.Intelligence.Architecture.Tests.csproj" />
  </Folder>
</Solution>
"@

Write-TextFile (Join-Path $NexusIntRoot 'nuget.config') $nugetConfig
Write-TextFile (Join-Path $NexusIntRoot 'global.json') (Get-Content (Join-Path $NexusAIRoot 'global.json') -Raw)

Write-TextFile (Join-Path $NexusIntRoot 'Directory.Build.props') @'
<Project>

  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <Version>0.1.0</Version>
  </PropertyGroup>

  <!-- Nexus V2 boundary guard: Intelligence never knows a product. -->
  <Target Name="NexusBoundaryGuard" BeforeTargets="Build">
    <ItemGroup Condition="$(MSBuildProjectName.StartsWith('Nexus.Intelligence.'))">
      <_Bad Include="@(ProjectReference)"
            Condition="$([System.String]::Copy('%(Filename)').StartsWith('Nexus.Products.'))" />
      <_Bad Include="@(PackageReference)"
            Condition="$([System.String]::Copy('%(Identity)').StartsWith('Nexus.Products.'))" />
    </ItemGroup>
    <Error Condition="'@(_Bad)' != ''"
           Text="Nexus V2 boundary violation in $(MSBuildProjectName): Intelligence may not reference @(_Bad->'%(Identity)', ', '). See NEXUS_ARCHITECTURE_V2.md section 2.3." />
  </Target>

</Project>
'@

Write-TextFile (Join-Path $NexusIntRoot 'pack-local.ps1') @"
# Packs the product-facing contract so Nexus.Web can consume it.
`$ErrorActionPreference = 'Stop'
`$feed    = '$LocalFeed'
`$version = "0.1.0-dev.`$(Get-Date -Format yyyyMMddHHmmss)"

Write-Host "packing Nexus.Intelligence.Contracts as `$version -> `$feed" -ForegroundColor Cyan
dotnet pack src\Nexus.Intelligence.Contracts\Nexus.Intelligence.Contracts.csproj ``
    -c Release -o `$feed -p:PackageVersion=`$version --nologo
if (`$LASTEXITCODE -ne 0) { throw 'pack failed' }
Write-Host "done. run 'dotnet restore' in Nexus.Web to pick it up." -ForegroundColor Green
"@

Write-TextFile (Join-Path $NexusIntRoot '.gitignore') @'
bin/
obj/
.vs/
*.user
.migration-logs/
'@

# ===========================================================================
# D. Nexus.Web  ->  the Chatbot product
# ===========================================================================

Write-Step "D. Nexus.Web - Chatbot product"

$WEB = @{
    Domain    = 'src\Nexus.Products.Chat.Domain'
    App       = 'src\Nexus.Products.Chat.Application'
    Infra     = 'src\Nexus.Products.Chat.Infrastructure'
    Api       = 'src\Nexus.Products.Chat.Api'
    Tests     = 'tests\Nexus.Products.Chat.Tests'
    ArchTests = 'tests\Nexus.Products.Chat.Architecture.Tests'
}
$WEBMap = @{
    'Nexus.Products.Chat.Domain'         = $WEB.Domain
    'Nexus.Products.Chat.Application'    = $WEB.App
    'Nexus.Products.Chat.Infrastructure' = $WEB.Infra
    'Nexus.Products.Chat.Api'            = $WEB.Api
}

$intContractsPkg = @{ 'Nexus.Intelligence.Contracts' = $NexusPkgVersion }

New-Csproj -Root $NexusWebRoot -Folder $WEB.Domain -Name 'Nexus.Products.Chat.Domain' -FolderMap $WEBMap
New-Csproj -Root $NexusWebRoot -Folder $WEB.App    -Name 'Nexus.Products.Chat.Application' -FolderMap $WEBMap `
    -ProjectRefs @('Nexus.Products.Chat.Domain') -PackageRefs (Join-Hash @($intContractsPkg, $PkgHttp))
New-Csproj -Root $NexusWebRoot -Folder $WEB.Infra  -Name 'Nexus.Products.Chat.Infrastructure' -FolderMap $WEBMap `
    -ProjectRefs @('Nexus.Products.Chat.Domain','Nexus.Products.Chat.Application') -PackageRefs $PkgDataverse
New-Csproj -Root $NexusWebRoot -Folder $WEB.Api -Name 'Nexus.Products.Chat.Api' -Sdk 'Microsoft.NET.Sdk.Web' -FolderMap $WEBMap `
    -ProjectRefs @('Nexus.Products.Chat.Application','Nexus.Products.Chat.Domain','Nexus.Products.Chat.Infrastructure') `
    -PackageRefs $PkgSwagger -UserSecrets '35fb7f5c-1f97-4eb3-81fe-6743ef1dd18f'
New-Csproj -Root $NexusWebRoot -Folder $WEB.Tests -Name 'Nexus.Products.Chat.Tests' -IsTest -FolderMap $WEBMap `
    -ProjectRefs @('Nexus.Products.Chat.Application','Nexus.Products.Chat.Domain') -PackageRefs $PkgTest
New-Csproj -Root $NexusWebRoot -Folder $WEB.ArchTests -Name 'Nexus.Products.Chat.Architecture.Tests' -IsTest -FolderMap $WEBMap `
    -ProjectRefs @('Nexus.Products.Chat.Application','Nexus.Products.Chat.Domain','Nexus.Products.Chat.Infrastructure') -PackageRefs $PkgArch

# --- Domain ---
foreach ($agg in @('Workspace','Project','Conversation','ConversationMessage','Knowledge',
                   'WorkItem','Artifact','Branch','Snapshot','Session','Adr')) {
    Copy-Tree -FromRoot $NexusAIRoot -From "src\NexusAI.Domain\$agg" -ToRoot $NexusWebRoot -To $WEB.Domain
}
foreach ($f in @('AggregateRoot.cs','Entity.cs','IRepository.cs')) {
    Copy-Tree -FromRoot $NexusAIRoot -From "src\NexusAI.Domain\Common\$f" -ToRoot $NexusWebRoot -To "$($WEB.Domain)\Common"
}
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Domain\Common\Identifiers\WorkspaceId.cs' -ToRoot $NexusWebRoot -To "$($WEB.Domain)\Workspace"

# --- Application ---
foreach ($feature in @('Workspaces','Projects','Conversations','ConversationMessages','WorkItem',
                       'Knowledge','Branch','Snapshot','Session','Artifact','Adr','Chat',
                       'DependencyInjection','Abstractions')) {
    Copy-Tree -FromRoot $NexusAIRoot -From "src\NexusAI.Application\$feature" -ToRoot $NexusWebRoot -To $WEB.App
}

# The two pieces that belong to Intelligence must not survive in the product copy.
Write-Do "prune intelligence code from the product copy"
if ($Execute) {
    foreach ($p in @("$($WEB.App)\Chat\Prompting",
                     "$($WEB.App)\Knowledge\Services\IKnowledgeRanker.cs",
                     "$($WEB.App)\Knowledge\Services\KeywordKnowledgeRanker.cs")) {
        $abs = Join-Path $NexusWebRoot $p
        if (Test-Path $abs) { Remove-Item $abs -Recurse -Force }
    }
}

# --- Infrastructure ---
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Infrastructure\Dataverse'    -ToRoot $NexusWebRoot -To $WEB.Infra
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Infrastructure\Registration' -ToRoot $NexusWebRoot -To $WEB.Infra
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Infrastructure\ServiceCollectionExtensions.cs'          -ToRoot $NexusWebRoot -To $WEB.Infra
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Infrastructure\Services\ConversationContextProvider.cs' -ToRoot $NexusWebRoot -To "$($WEB.Infra)\Context"

# --- API ---
foreach ($ep in @('Artifacts','Branches','Chat','Conversations','ConversationMessage',
                  'Knowledge','Projects','Sessions','Snapshots','WorkItems','WorkSpaces')) {
    Copy-Tree -FromRoot $NexusAIRoot -From "src\NexusAI.Api\Endpoints\$ep" -ToRoot $NexusWebRoot -To "$($WEB.Api)\Endpoints"
}
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Api\Endpoints\PlatformHealthEndpoint.cs' -ToRoot $NexusWebRoot -To "$($WEB.Api)\Endpoints"
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Api\Program.cs'                 -ToRoot $NexusWebRoot -To "$($WEB.Api)\_migrate"
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Api\appsettings.json'           -ToRoot $NexusWebRoot -To $WEB.Api
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Api\appsettings.Development.json' -ToRoot $NexusWebRoot -To $WEB.Api
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Api\Properties'                 -ToRoot $NexusWebRoot -To $WEB.Api
Copy-Tree -FromRoot $NexusAIRoot -From 'src\NexusAI.Api\NexusAI.Api.http'           -ToRoot $NexusWebRoot -To $WEB.Api -Rename 'Nexus.Products.Chat.Api.http'

# --- frontend cleanup ---
Remove-Path -Repo $NexusWebRoot -Path 'src\features' -Git   # stray 0-byte WorkspaceContext.tsx

Write-TextFile (Join-Path $NexusWebRoot 'Nexus.Web.slnx') @"
<Solution>
  <Folder Name="/client/">
    <Project Path="src\Nexus.Web.Client\Nexus.Web.Client.esproj" />
  </Folder>
  <Folder Name="/src/">
    <Project Path="$($WEB.Domain)\Nexus.Products.Chat.Domain.csproj" />
    <Project Path="$($WEB.App)\Nexus.Products.Chat.Application.csproj" />
    <Project Path="$($WEB.Infra)\Nexus.Products.Chat.Infrastructure.csproj" />
    <Project Path="$($WEB.Api)\Nexus.Products.Chat.Api.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="$($WEB.Tests)\Nexus.Products.Chat.Tests.csproj" />
    <Project Path="$($WEB.ArchTests)\Nexus.Products.Chat.Architecture.Tests.csproj" />
  </Folder>
</Solution>
"@

Write-TextFile (Join-Path $NexusWebRoot 'nuget.config') $nugetConfig
Write-TextFile (Join-Path $NexusWebRoot 'global.json') (Get-Content (Join-Path $NexusAIRoot 'global.json') -Raw)

Write-TextFile (Join-Path $NexusWebRoot 'Directory.Build.props') @'
<Project>

  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <Version>0.1.0</Version>
  </PropertyGroup>

  <!--
    Nexus V2 boundary guard: the product may reach Intelligence ONLY through its
    Contracts package, and may never reference Platform at all.
  -->
  <Target Name="NexusBoundaryGuard" BeforeTargets="Build">
    <ItemGroup Condition="$(MSBuildProjectName.StartsWith('Nexus.Products.'))">
      <_Bad Include="@(PackageReference)"
            Condition="$([System.String]::Copy('%(Identity)').StartsWith('Nexus.Platform.'))" />
      <_Bad Include="@(PackageReference)"
            Condition="$([System.String]::Copy('%(Identity)').StartsWith('Nexus.Intelligence.')) AND '%(Identity)' != 'Nexus.Intelligence.Contracts'" />
      <_Bad Include="@(ProjectReference)"
            Condition="$([System.String]::Copy('%(Filename)').StartsWith('Nexus.Platform.')) OR $([System.String]::Copy('%(Filename)').StartsWith('Nexus.Intelligence.'))" />
    </ItemGroup>
    <Error Condition="'@(_Bad)' != ''"
           Text="Nexus V2 boundary violation in $(MSBuildProjectName): the product may not reference @(_Bad->'%(Identity)', ', '). Only Nexus.Intelligence.Contracts is allowed. See NEXUS_ARCHITECTURE_V2.md section 2.3." />
  </Target>

</Project>
'@

# ===========================================================================
# Summary
# ===========================================================================

Write-Step "Summary"
Write-Host "   files created : $($script:Creates)"
Write-Host "   git moves     : $($script:Moves)"
Write-Host "   cross-repo copies : $($script:Copies)"
Write-Host "   deletes       : $($script:Deletes)"

if ($script:Warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "   Warnings ($($script:Warnings.Count)):" -ForegroundColor Yellow
    $script:Warnings | ForEach-Object { Write-Host "     - $_" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "  Rewrite worklist - every _migrate folder must be empty before you tag v2-arch:" -ForegroundColor Cyan
Write-Host "    NexusAI  \$($AI.Contracts)\_migrate        ILLMProvider   -> IModelGateway / IModelCatalog"
Write-Host "    NexusAI  \$($AI.OpenAI)\_migrate           OpenAIProvider -> IModelGateway implementation"
Write-Host "    Nexus.Int\$($INT.Context)\Ranking\_migrate KnowledgeRanker -> ContextItem ranker"
Write-Host "    Nexus.Int\$($INT.Memory)\_migrate          Domain Memory  -> ScopeRef-keyed MemoryRecord"
Write-Host "    Nexus.Web\$($WEB.Api)\_migrate             Program.cs     -> product API bootstrap"

Write-Host ""
if ($Execute) {
    Write-Host "  Done. NOTHING BUILDS YET - namespaces still say NexusAI.*" -ForegroundColor Green
    Write-Host "  NexusAI still holds the originals; the Cleanup phase removes them later." -ForegroundColor Green
    Write-Host ""
    Write-Host "  Next: docs\NEXUS_MIGRATION_RUNBOOK.md, Stage 4." -ForegroundColor Green
} else {
    Write-Host "  Dry run complete. Re-run with -Execute to apply." -ForegroundColor Yellow
}
Write-Host ""
