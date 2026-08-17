<#
.SYNOPSIS
    Restructures the NexusAI solution into the V2 three-layer architecture:
    Nexus Platform / Nexus Intelligence / Nexus Products.

.DESCRIPTION
    Creates the new folder tree and .csproj files, then uses `git mv` to relocate
    every existing source file into its new layer. Namespaces are NOT rewritten by
    this script - that is Stage 4 in CLAUDE_CODE_MIGRATION_PROMPTS.md, where Claude
    Code does it with full context.

    Files that need a genuine rewrite (not just a move) land in a `_migrate/` folder
    inside their destination project. Those folders are your rewrite worklist and
    must be empty before you tag v2-arch.

.PARAMETER RepoRoot
    Path to the folder containing NexusAI.slnx. Defaults to the current directory.

.PARAMETER Execute
    Actually perform the changes. Without this switch the script runs as a dry run
    and only prints what it would do.

.PARAMETER SkipBranch
    Do not create the arch/v2 branch (use if you already made one).

.EXAMPLE
    # Developer PowerShell in Visual Studio, at the repo root:
    .\nexus-restructure.ps1                 # dry run - review the output
    .\nexus-restructure.ps1 -Execute        # apply

.NOTES
    Run on a clean working tree. The script commits nothing - review `git status`
    and commit yourself.
#>

[CmdletBinding()]
param(
    [string] $RepoRoot = (Get-Location).Path,
    [switch] $Execute,
    [switch] $SkipBranch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

$script:Moves    = 0
$script:Creates  = 0
$script:Deletes  = 0
$script:Warnings = @()

function Write-Step { param([string]$m) Write-Host ""; Write-Host "== $m" -ForegroundColor Cyan }
function Write-Do   { param([string]$m) Write-Host "   $m" -ForegroundColor DarkGray }
function Write-Warn2{ param([string]$m) $script:Warnings += $m; Write-Host "   ! $m" -ForegroundColor Yellow }

function New-Dir {
    param([string]$Path)
    if (Test-Path $Path) { return }
    if ($Execute) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

<#
  Moves a file or folder with `git mv`.
  $To     : repo-relative DESTINATION PARENT folder.
  $Rename : optional new leaf name at the destination.
            Without it the source leaf name is preserved.
#>
function Move-Git {
    param(
        [Parameter(Mandatory)][string]$From,
        [Parameter(Mandatory)][string]$To,
        [string]$Rename = ''
    )

    $abs = Join-Path $RepoRoot $From
    if (-not (Test-Path $abs)) {
        Write-Warn2 "source not found, skipped: $From"
        return
    }

    New-Dir (Join-Path $RepoRoot $To)

    $leaf   = if ($Rename -ne '') { $Rename } else { Split-Path $From -Leaf }
    $target = Join-Path $To $leaf

    Write-Do "git mv `"$From`"  ->  `"$target`""
    $script:Moves++

    if ($Execute) {
        Push-Location $RepoRoot
        try {
            git mv --force -- "$From" "$target" 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warn2 "git mv returned $LASTEXITCODE for $From" }
        }
        catch   { Write-Warn2 "git mv failed for $From : $_" }
        finally { Pop-Location }
    }
}

function Remove-Git {
    param([Parameter(Mandatory)][string]$Path)

    $abs = Join-Path $RepoRoot $Path
    if (-not (Test-Path $abs)) { Write-Warn2 "delete target not found: $Path"; return }

    Write-Do "git rm -r `"$Path`""
    $script:Deletes++

    if ($Execute) {
        Push-Location $RepoRoot
        try   { git rm -r --quiet --force -- "$Path" 2>&1 | Out-Null }
        catch { Write-Warn2 "git rm failed for $Path : $_" }
        finally { Pop-Location }
    }
}

$script:ProjectFolders = @{}

function Get-RelativeProjectPath {
    param([string]$FromFolder, [string]$ToProject)

    if (-not $script:ProjectFolders.ContainsKey($ToProject)) {
        throw "Unknown project reference '$ToProject'."
    }

    $to        = $script:ProjectFolders[$ToProject]
    $fromParts = @($FromFolder -split '[\\/]' | Where-Object { $_ -ne '' })
    $toParts   = @($to         -split '[\\/]' | Where-Object { $_ -ne '' })

    $common = 0
    while ($common -lt $fromParts.Count -and
           $common -lt $toParts.Count   -and
           $fromParts[$common] -eq $toParts[$common]) { $common++ }

    $up = '..\' * ($fromParts.Count - $common)

    $down = ''
    if ($common -lt $toParts.Count) {
        $down = ($toParts[$common..($toParts.Count - 1)]) -join '\'
        $down = "$down\"
    }

    return "$up$down$ToProject.csproj"
}

function New-Csproj {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Name,
        [string]    $Sdk         = 'Microsoft.NET.Sdk',
        [string[]]  $ProjectRefs = @(),
        [hashtable] $Packages    = @{},
        [string]    $UserSecrets = '',
        [switch]    $IsTest
    )

    $full = Join-Path (Join-Path $RepoRoot $Path) "$Name.csproj"
    New-Dir (Join-Path $RepoRoot $Path)

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("<Project Sdk=`"$Sdk`">")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("  <PropertyGroup>")
    [void]$sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>")
    [void]$sb.AppendLine("    <RootNamespace>$Name</RootNamespace>")
    [void]$sb.AppendLine("    <AssemblyName>$Name</AssemblyName>")
    if ($UserSecrets -ne '') { [void]$sb.AppendLine("    <UserSecretsId>$UserSecrets</UserSecretsId>") }
    if ($IsTest) {
        [void]$sb.AppendLine("    <IsPackable>false</IsPackable>")
        [void]$sb.AppendLine("    <IsTestProject>true</IsTestProject>")
    }
    [void]$sb.AppendLine("  </PropertyGroup>")

    if ($ProjectRefs.Count -gt 0) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("  <ItemGroup>")
        foreach ($r in $ProjectRefs) {
            $rel = Get-RelativeProjectPath -FromFolder $Path -ToProject $r
            [void]$sb.AppendLine("    <ProjectReference Include=`"$rel`" />")
        }
        [void]$sb.AppendLine("  </ItemGroup>")
    }

    if ($Packages.Count -gt 0) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("  <ItemGroup>")
        foreach ($k in ($Packages.Keys | Sort-Object)) {
            [void]$sb.AppendLine("    <PackageReference Include=`"$k`" Version=`"$($Packages[$k])`" />")
        }
        [void]$sb.AppendLine("  </ItemGroup>")
    }

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("</Project>")

    Write-Do "create $Path\$Name.csproj"
    $script:Creates++
    if ($Execute) { Set-Content -Path $full -Value $sb.ToString() -Encoding UTF8 }
}

# ---------------------------------------------------------------------------
# Preflight
# ---------------------------------------------------------------------------

Write-Step "Preflight"

if (-not (Test-Path (Join-Path $RepoRoot 'NexusAI.slnx'))) {
    throw "NexusAI.slnx not found in '$RepoRoot'. Run from the repo root or pass -RepoRoot."
}
Write-Do "repo root: $RepoRoot"

Push-Location $RepoRoot
try {
    $null = git rev-parse --is-inside-work-tree 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Not a git repository. Initialise git before restructuring." }

    $dirty = git status --porcelain
    if ($dirty -and $Execute) {
        throw "Working tree is dirty. Commit or stash first - this script moves ~150 files."
    }
    Write-Do ("working tree: " + $(if ($dirty) { "dirty (dry run only)" } else { "clean" }))
}
finally { Pop-Location }

if (-not $Execute) {
    Write-Host ""
    Write-Host "  DRY RUN - nothing will change. Re-run with -Execute to apply." -ForegroundColor Yellow
}

if ($Execute -and -not $SkipBranch) {
    Write-Do "git checkout -b arch/v2"
    Push-Location $RepoRoot
    try   { git checkout -b arch/v2 2>&1 | Out-Null }
    catch { Write-Warn2 "branch arch/v2 may already exist" }
    finally { Pop-Location }
}

# ---------------------------------------------------------------------------
# Project registry
# ---------------------------------------------------------------------------

$P = @{
    Kernel        = 'src\shared\Nexus.Shared.Kernel'

    PlatContracts = 'src\platform\Nexus.Platform.Contracts'
    PlatCore      = 'src\platform\Nexus.Platform.Core'
    PlatOpenAI    = 'src\platform\Nexus.Platform.Providers.OpenAI'
    PlatAnthropic = 'src\platform\Nexus.Platform.Providers.Anthropic'
    PlatTools     = 'src\platform\Nexus.Platform.Tools'
    PlatIdentity  = 'src\platform\Nexus.Platform.Identity'
    PlatPersist   = 'src\platform\Nexus.Platform.Persistence'

    IntContracts  = 'src\intelligence\Nexus.Intelligence.Contracts'
    IntCore       = 'src\intelligence\Nexus.Intelligence.Core'
    IntContext    = 'src\intelligence\Nexus.Intelligence.Context'
    IntAgents     = 'src\intelligence\Nexus.Intelligence.Agents'
    IntMemory     = 'src\intelligence\Nexus.Intelligence.Memory'
    IntApi        = 'src\intelligence\Nexus.Intelligence.Api'

    ChatDomain    = 'src\products\chat\Nexus.Products.Chat.Domain'
    ChatApp       = 'src\products\chat\Nexus.Products.Chat.Application'
    ChatInfra     = 'src\products\chat\Nexus.Products.Chat.Infrastructure'
    ChatApi       = 'src\products\chat\Nexus.Products.Chat.Api'

    Host          = 'host\Nexus.Host'

    ArchTests     = 'tests\Nexus.Architecture.Tests'
    PlatTests     = 'tests\Nexus.Platform.Tests'
    IntTests      = 'tests\Nexus.Intelligence.Tests'
    ChatTests     = 'tests\Nexus.Products.Chat.Tests'
}

$script:ProjectFolders = @{
    'Nexus.Shared.Kernel'                = $P.Kernel
    'Nexus.Platform.Contracts'           = $P.PlatContracts
    'Nexus.Platform.Core'                = $P.PlatCore
    'Nexus.Platform.Providers.OpenAI'    = $P.PlatOpenAI
    'Nexus.Platform.Providers.Anthropic' = $P.PlatAnthropic
    'Nexus.Platform.Tools'               = $P.PlatTools
    'Nexus.Platform.Identity'            = $P.PlatIdentity
    'Nexus.Platform.Persistence'         = $P.PlatPersist
    'Nexus.Intelligence.Contracts'       = $P.IntContracts
    'Nexus.Intelligence.Core'            = $P.IntCore
    'Nexus.Intelligence.Context'         = $P.IntContext
    'Nexus.Intelligence.Agents'          = $P.IntAgents
    'Nexus.Intelligence.Memory'          = $P.IntMemory
    'Nexus.Intelligence.Api'             = $P.IntApi
    'Nexus.Products.Chat.Domain'         = $P.ChatDomain
    'Nexus.Products.Chat.Application'    = $P.ChatApp
    'Nexus.Products.Chat.Infrastructure' = $P.ChatInfra
    'Nexus.Products.Chat.Api'            = $P.ChatApi
    'Nexus.Host'                         = $P.Host
    'Nexus.Architecture.Tests'           = $P.ArchTests
    'Nexus.Platform.Tests'               = $P.PlatTests
    'Nexus.Intelligence.Tests'           = $P.IntTests
    'Nexus.Products.Chat.Tests'          = $P.ChatTests
}

# Package versions carried over from the current solution.
$PkgOptions   = @{ 'Microsoft.Extensions.Options.ConfigurationExtensions' = '10.0.11' }
$PkgDataverse = @{ 'Microsoft.PowerPlatform.Dataverse.Client' = '1.2.26'
                   'Microsoft.Extensions.Options.ConfigurationExtensions' = '10.0.11' }
$PkgOpenAI    = @{ 'OpenAI' = '2.13.0'
                   'Microsoft.Extensions.Options.ConfigurationExtensions' = '10.0.11' }
$PkgSwagger   = @{ 'Swashbuckle.AspNetCore' = '10.2.3' }
$PkgHttp      = @{ 'Microsoft.Extensions.Http' = '10.0.11' }
$PkgTest      = @{ 'Microsoft.NET.Test.Sdk' = '17.14.1'
                   'xunit' = '2.9.3'
                   'xunit.runner.visualstudio' = '3.1.1' }
$PkgArchTest  = @{ 'Microsoft.NET.Test.Sdk' = '17.14.1'
                   'xunit' = '2.9.3'
                   'xunit.runner.visualstudio' = '3.1.1'
                   'NetArchTest.Rules' = '1.3.2' }

# ---------------------------------------------------------------------------
# 1. Create projects
# ---------------------------------------------------------------------------

Write-Step "1. Creating project files"

New-Csproj -Path $P.Kernel -Name 'Nexus.Shared.Kernel'

New-Csproj -Path $P.PlatContracts -Name 'Nexus.Platform.Contracts' -ProjectRefs @('Nexus.Shared.Kernel')
New-Csproj -Path $P.PlatCore      -Name 'Nexus.Platform.Core'      -ProjectRefs @('Nexus.Platform.Contracts','Nexus.Shared.Kernel') -Packages $PkgOptions
New-Csproj -Path $P.PlatOpenAI    -Name 'Nexus.Platform.Providers.OpenAI'    -ProjectRefs @('Nexus.Platform.Contracts','Nexus.Shared.Kernel') -Packages $PkgOpenAI
New-Csproj -Path $P.PlatAnthropic -Name 'Nexus.Platform.Providers.Anthropic' -ProjectRefs @('Nexus.Platform.Contracts','Nexus.Shared.Kernel') -Packages $PkgOptions
New-Csproj -Path $P.PlatTools     -Name 'Nexus.Platform.Tools'     -ProjectRefs @('Nexus.Platform.Contracts','Nexus.Shared.Kernel')
New-Csproj -Path $P.PlatIdentity  -Name 'Nexus.Platform.Identity'  -ProjectRefs @('Nexus.Platform.Contracts','Nexus.Shared.Kernel')
New-Csproj -Path $P.PlatPersist   -Name 'Nexus.Platform.Persistence' -ProjectRefs @('Nexus.Platform.Contracts','Nexus.Shared.Kernel')

New-Csproj -Path $P.IntContracts -Name 'Nexus.Intelligence.Contracts' -ProjectRefs @('Nexus.Shared.Kernel')
New-Csproj -Path $P.IntContext   -Name 'Nexus.Intelligence.Context'   -ProjectRefs @('Nexus.Intelligence.Contracts','Nexus.Platform.Contracts','Nexus.Shared.Kernel')
New-Csproj -Path $P.IntAgents    -Name 'Nexus.Intelligence.Agents'    -ProjectRefs @('Nexus.Intelligence.Contracts','Nexus.Platform.Contracts','Nexus.Shared.Kernel')
New-Csproj -Path $P.IntMemory    -Name 'Nexus.Intelligence.Memory'    -ProjectRefs @('Nexus.Intelligence.Contracts','Nexus.Shared.Kernel')
New-Csproj -Path $P.IntCore      -Name 'Nexus.Intelligence.Core'      -ProjectRefs @('Nexus.Intelligence.Contracts','Nexus.Intelligence.Context','Nexus.Intelligence.Agents','Nexus.Intelligence.Memory','Nexus.Platform.Contracts','Nexus.Shared.Kernel')
New-Csproj -Path $P.IntApi       -Name 'Nexus.Intelligence.Api' -Sdk 'Microsoft.NET.Sdk.Web' -ProjectRefs @('Nexus.Intelligence.Core','Nexus.Intelligence.Contracts','Nexus.Shared.Kernel')

New-Csproj -Path $P.ChatDomain -Name 'Nexus.Products.Chat.Domain'      -ProjectRefs @('Nexus.Shared.Kernel')
New-Csproj -Path $P.ChatApp    -Name 'Nexus.Products.Chat.Application' -ProjectRefs @('Nexus.Products.Chat.Domain','Nexus.Intelligence.Contracts','Nexus.Shared.Kernel') -Packages $PkgHttp
New-Csproj -Path $P.ChatInfra  -Name 'Nexus.Products.Chat.Infrastructure' -ProjectRefs @('Nexus.Products.Chat.Domain','Nexus.Products.Chat.Application','Nexus.Shared.Kernel') -Packages $PkgDataverse
New-Csproj -Path $P.ChatApi    -Name 'Nexus.Products.Chat.Api' -Sdk 'Microsoft.NET.Sdk.Web' -ProjectRefs @('Nexus.Products.Chat.Application','Nexus.Products.Chat.Domain','Nexus.Products.Chat.Infrastructure','Nexus.Shared.Kernel')

New-Csproj -Path $P.Host -Name 'Nexus.Host' -Sdk 'Microsoft.NET.Sdk.Web' `
    -ProjectRefs @('Nexus.Products.Chat.Api','Nexus.Products.Chat.Infrastructure',
                   'Nexus.Intelligence.Api','Nexus.Intelligence.Core',
                   'Nexus.Platform.Core','Nexus.Platform.Providers.OpenAI',
                   'Nexus.Platform.Providers.Anthropic','Nexus.Platform.Tools',
                   'Nexus.Platform.Identity','Nexus.Platform.Persistence',
                   'Nexus.Shared.Kernel') `
    -Packages $PkgSwagger -UserSecrets '35fb7f5c-1f97-4eb3-81fe-6743ef1dd18f'

New-Csproj -Path $P.ArchTests -Name 'Nexus.Architecture.Tests' -IsTest -Packages $PkgArchTest `
    -ProjectRefs @('Nexus.Shared.Kernel','Nexus.Platform.Core','Nexus.Platform.Contracts',
                   'Nexus.Intelligence.Core','Nexus.Intelligence.Contracts',
                   'Nexus.Products.Chat.Application','Nexus.Products.Chat.Domain',
                   'Nexus.Products.Chat.Infrastructure')
New-Csproj -Path $P.PlatTests -Name 'Nexus.Platform.Tests'      -IsTest -Packages $PkgTest -ProjectRefs @('Nexus.Platform.Core','Nexus.Platform.Contracts')
New-Csproj -Path $P.IntTests  -Name 'Nexus.Intelligence.Tests'  -IsTest -Packages $PkgTest -ProjectRefs @('Nexus.Intelligence.Core','Nexus.Intelligence.Contracts')
New-Csproj -Path $P.ChatTests -Name 'Nexus.Products.Chat.Tests' -IsTest -Packages $PkgTest -ProjectRefs @('Nexus.Products.Chat.Application','Nexus.Products.Chat.Domain')

# ---------------------------------------------------------------------------
# 2. Shared kernel
# ---------------------------------------------------------------------------

Write-Step "2. Shared kernel (no business meaning)"

Move-Git 'src\NexusAI.Domain\Common\AggregateRoot.cs'              "$($P.Kernel)\Domain"
Move-Git 'src\NexusAI.Domain\Common\Entity.cs'                     "$($P.Kernel)\Domain"
Move-Git 'src\NexusAI.Domain\Common\IRepository.cs'                "$($P.Kernel)\Domain"
Move-Git 'src\NexusAI.Application\Abstractions\ICommandHandler.cs' "$($P.Kernel)\Abstractions"
Move-Git 'src\NexusAI.Application\Abstractions\IQueryHandler.cs'   "$($P.Kernel)\Abstractions"
Move-Git 'src\NexusAI.Core\Modules\INexusModule.cs'                "$($P.Kernel)\Modules"
Move-Git 'src\NexusAI.Core\Abstractions\IClock.cs'                 "$($P.Kernel)\Time"
Move-Git 'src\NexusAI.Infrastructure\Services\SystemClock.cs'      "$($P.Kernel)\Time"

# ---------------------------------------------------------------------------
# 3. Platform - extract the vendor layer BEFORE the bulk product move
# ---------------------------------------------------------------------------

Write-Step "3. Platform (the only layer that knows a vendor exists)"

# ILLMProvider + ChatRequest/Response/Message  ->  IModelGateway + ModelInvocation.
Move-Git 'src\NexusAI.Application\Providers' $P.PlatContracts -Rename '_migrate'

# OpenAIProvider  ->  IModelGateway implementation + IModelCatalog contribution.
Move-Git 'src\NexusAI.Infrastructure\OpenAI' $P.PlatOpenAI -Rename '_migrate'

# ---------------------------------------------------------------------------
# 4. Intelligence - extract the deciding layer BEFORE the bulk product move
# ---------------------------------------------------------------------------

Write-Step "4. Intelligence (what to do, where, how)"

# Prompt assembly is an intelligence decision, not product code.
Move-Git 'src\NexusAI.Application\Chat\Prompting' $P.IntContext

# Ranking generalises from Knowledge to ContextItem.
Move-Git 'src\NexusAI.Application\Knowledge\Services\IKnowledgeRanker.cs'       "$($P.IntContext)\Ranking\_migrate"
Move-Git 'src\NexusAI.Application\Knowledge\Services\KeywordKnowledgeRanker.cs' "$($P.IntContext)\Ranking\_migrate"

# Planning and execution orchestration.
Move-Git 'src\NexusAI.Application\Planning'  $P.IntCore
Move-Git 'src\NexusAI.Application\Execution' $P.IntCore

# Agent contracts, runtime, dispatcher and the built-in agent.
Move-Git 'src\NexusAI.Core\Agents'                               $P.IntAgents -Rename 'Abstractions'
Move-Git 'src\NexusAI.Infrastructure\Services\AgentDispatcher.cs' $P.IntAgents
Move-Git 'src\NexusAI.Infrastructure\Services\AgentRegistry.cs'   $P.IntAgents
Move-Git 'src\NexusAI.Agents\DeveloperAgent'                     $P.IntAgents -Rename 'BuiltIn'

# Memory becomes an Intelligence concern keyed by ScopeRef (decision D-2).
Move-Git 'src\NexusAI.Domain\Memory' $P.IntMemory -Rename '_migrate'

# ---------------------------------------------------------------------------
# 5. Product: Chat - Domain
# ---------------------------------------------------------------------------

Write-Step "5. Product Chat - Domain (every product entity leaves the platform)"

foreach ($agg in @('Workspace','Project','Conversation','ConversationMessage','Knowledge',
                   'WorkItem','Artifact','Branch','Snapshot','Session','Adr')) {
    Move-Git "src\NexusAI.Domain\$agg" $P.ChatDomain
}
Move-Git 'src\NexusAI.Domain\Common\Identifiers\WorkspaceId.cs' "$($P.ChatDomain)\Workspace"

# ---------------------------------------------------------------------------
# 6. Product: Chat - Application
# ---------------------------------------------------------------------------

Write-Step "6. Product Chat - Application"

foreach ($feature in @('Workspaces','Projects','Conversations','ConversationMessages','WorkItem',
                       'Knowledge','Branch','Snapshot','Session','Artifact','Adr','Chat',
                       'DependencyInjection')) {
    Move-Git "src\NexusAI.Application\$feature" $P.ChatApp
}

# ---------------------------------------------------------------------------
# 7. Product: Chat - Infrastructure and API
# ---------------------------------------------------------------------------

Write-Step "7. Product Chat - Infrastructure and API"

Move-Git 'src\NexusAI.Infrastructure\Dataverse'    $P.ChatInfra
Move-Git 'src\NexusAI.Infrastructure\Registration' $P.ChatInfra
Move-Git 'src\NexusAI.Infrastructure\ServiceCollectionExtensions.cs'          $P.ChatInfra
Move-Git 'src\NexusAI.Infrastructure\Services\ConversationContextProvider.cs' "$($P.ChatInfra)\Context"

foreach ($ep in @('Artifacts','Branches','Chat','Conversations','ConversationMessage',
                  'Knowledge','Projects','Sessions','Snapshots','WorkItems','WorkSpaces')) {
    Move-Git "src\NexusAI.Api\Endpoints\$ep" "$($P.ChatApi)\Endpoints"
}

# ---------------------------------------------------------------------------
# 8. Host
# ---------------------------------------------------------------------------

Write-Step "8. Host (single deployable, mounts both API surfaces)"

Move-Git 'src\NexusAI.Api\Endpoints\PlatformHealthEndpoint.cs' "$($P.Host)\Endpoints"
Move-Git 'src\NexusAI.Api\Program.cs'                          "$($P.Host)\_migrate"
Move-Git 'src\NexusAI.Api\appsettings.json'                    $P.Host
Move-Git 'src\NexusAI.Api\appsettings.Development.json'        $P.Host
Move-Git 'src\NexusAI.Api\Properties'                          $P.Host
Move-Git 'src\NexusAI.Api\NexusAI.Api.http'                    $P.Host -Rename 'Nexus.Host.http'

# ---------------------------------------------------------------------------
# 9. Delete dead weight
# ---------------------------------------------------------------------------

Write-Step "9. Deleting template samples and superseded projects"

Remove-Git 'src\NexusAI.Api\Controllers'
Remove-Git 'src\NexusAI.Api\WeatherForecast.cs'
Remove-Git 'src\NexusAI.Api\libman.json'
Remove-Git 'src\NexusAI.Foundation'
Remove-Git 'src\NexusAI.Host'
Remove-Git 'src\NexusAI.Api\NexusAI.Api.csproj'
Remove-Git 'src\NexusAI.Api\NexusAI.Api.csproj.user'
Remove-Git 'src\NexusAI.Application\NexusAI.Application.csproj'
Remove-Git 'src\NexusAI.Domain\NexusAI.Domain.csproj'
Remove-Git 'src\NexusAI.Infrastructure\NexusAI.Infrastructure.csproj'
Remove-Git 'src\NexusAI.Core\NexusAI.Core.csproj'
Remove-Git 'src\NexusAI.Agents\NexusAI.Agents.csproj'
Remove-Git 'NexusAI.slnLaunch.user'

# ---------------------------------------------------------------------------
# 9b. Sweep up the now-empty NexusAI.* shells
# ---------------------------------------------------------------------------

Write-Step "9b. Removing emptied NexusAI.* folders"

foreach ($old in @('src\NexusAI.Domain','src\NexusAI.Application','src\NexusAI.Infrastructure',
                   'src\NexusAI.Core','src\NexusAI.Agents','src\NexusAI.Api')) {
    $abs = Join-Path $RepoRoot $old
    if (-not (Test-Path $abs)) { continue }

    $remaining = @(Get-ChildItem -Path $abs -Recurse -File -Force |
                   Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })

    if ($remaining.Count -eq 0) {
        Write-Do "remove empty $old"
        if ($Execute) { Remove-Item -Path $abs -Recurse -Force }
    }
    else {
        Write-Warn2 "$old still holds $($remaining.Count) file(s) - review before deleting:"
        $remaining | Select-Object -First 10 | ForEach-Object {
            Write-Host "        $($_.FullName.Substring($RepoRoot.Length + 1))" -ForegroundColor Yellow
        }
    }
}

# ---------------------------------------------------------------------------
# 10. Solution file and build props
# ---------------------------------------------------------------------------

Write-Step "10. Writing Nexus.slnx and Directory.Build.props"

$slnx = @"
<Solution>
  <Folder Name="/shared/">
    <Project Path="$($P.Kernel)\Nexus.Shared.Kernel.csproj" />
  </Folder>
  <Folder Name="/platform/">
    <Project Path="$($P.PlatContracts)\Nexus.Platform.Contracts.csproj" />
    <Project Path="$($P.PlatCore)\Nexus.Platform.Core.csproj" />
    <Project Path="$($P.PlatOpenAI)\Nexus.Platform.Providers.OpenAI.csproj" />
    <Project Path="$($P.PlatAnthropic)\Nexus.Platform.Providers.Anthropic.csproj" />
    <Project Path="$($P.PlatTools)\Nexus.Platform.Tools.csproj" />
    <Project Path="$($P.PlatIdentity)\Nexus.Platform.Identity.csproj" />
    <Project Path="$($P.PlatPersist)\Nexus.Platform.Persistence.csproj" />
  </Folder>
  <Folder Name="/intelligence/">
    <Project Path="$($P.IntContracts)\Nexus.Intelligence.Contracts.csproj" />
    <Project Path="$($P.IntCore)\Nexus.Intelligence.Core.csproj" />
    <Project Path="$($P.IntContext)\Nexus.Intelligence.Context.csproj" />
    <Project Path="$($P.IntAgents)\Nexus.Intelligence.Agents.csproj" />
    <Project Path="$($P.IntMemory)\Nexus.Intelligence.Memory.csproj" />
    <Project Path="$($P.IntApi)\Nexus.Intelligence.Api.csproj" />
  </Folder>
  <Folder Name="/products/">
    <Project Path="$($P.ChatDomain)\Nexus.Products.Chat.Domain.csproj" />
    <Project Path="$($P.ChatApp)\Nexus.Products.Chat.Application.csproj" />
    <Project Path="$($P.ChatInfra)\Nexus.Products.Chat.Infrastructure.csproj" />
    <Project Path="$($P.ChatApi)\Nexus.Products.Chat.Api.csproj" />
  </Folder>
  <Folder Name="/host/">
    <Project Path="$($P.Host)\Nexus.Host.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="$($P.ArchTests)\Nexus.Architecture.Tests.csproj" />
    <Project Path="$($P.PlatTests)\Nexus.Platform.Tests.csproj" />
    <Project Path="$($P.IntTests)\Nexus.Intelligence.Tests.csproj" />
    <Project Path="$($P.ChatTests)\Nexus.Products.Chat.Tests.csproj" />
  </Folder>
</Solution>
"@

Write-Do "create Nexus.slnx"
$script:Creates++
if ($Execute) { Set-Content -Path (Join-Path $RepoRoot 'Nexus.slnx') -Value $slnx -Encoding UTF8 }

Remove-Git 'NexusAI.slnx'

# Boundary guard: fast build-time feedback. The architecture tests are the real gate.
$props = @'
<Project>

  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>

  <!--
    Nexus V2 boundary guard.
    See NEXUS_ARCHITECTURE_V2.md section 2 for the rule table.
    Nexus.Host is deliberately exempt: it is the composition root.
  -->
  <Target Name="NexusBoundaryGuard" BeforeTargets="Build">

    <!-- Products: no Platform at all; Intelligence only via Contracts. -->
    <ItemGroup Condition="$(MSBuildProjectName.StartsWith('Nexus.Products.'))">
      <_NexusBadRef Include="@(ProjectReference)"
                    Condition="$([System.String]::Copy('%(Filename)').StartsWith('Nexus.Platform.'))" />
      <_NexusBadRef Include="@(ProjectReference)"
                    Condition="$([System.String]::Copy('%(Filename)').StartsWith('Nexus.Intelligence.')) AND '%(Filename)' != 'Nexus.Intelligence.Contracts'" />
    </ItemGroup>

    <!-- Intelligence: never a product. -->
    <ItemGroup Condition="$(MSBuildProjectName.StartsWith('Nexus.Intelligence.'))">
      <_NexusBadRef Include="@(ProjectReference)"
                    Condition="$([System.String]::Copy('%(Filename)').StartsWith('Nexus.Products.'))" />
    </ItemGroup>

    <!-- Platform: never Intelligence, never a product. -->
    <ItemGroup Condition="$(MSBuildProjectName.StartsWith('Nexus.Platform.'))">
      <_NexusBadRef Include="@(ProjectReference)"
                    Condition="$([System.String]::Copy('%(Filename)').StartsWith('Nexus.Intelligence.')) OR $([System.String]::Copy('%(Filename)').StartsWith('Nexus.Products.'))" />
    </ItemGroup>

    <Error Condition="'@(_NexusBadRef)' != ''"
           Text="Nexus V2 boundary violation in $(MSBuildProjectName): illegal reference(s) to @(_NexusBadRef->'%(Filename)', ', '). See NEXUS_ARCHITECTURE_V2.md section 2." />

  </Target>

</Project>
'@

Write-Do "overwrite Directory.Build.props"
$script:Creates++
if ($Execute) { Set-Content -Path (Join-Path $RepoRoot 'Directory.Build.props') -Value $props -Encoding UTF8 }

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

Write-Step "Summary"
Write-Host "   files created    : $($script:Creates)"
Write-Host "   files/dirs moved : $($script:Moves)"
Write-Host "   paths deleted    : $($script:Deletes)"

if ($script:Warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "   Warnings ($($script:Warnings.Count)):" -ForegroundColor Yellow
    $script:Warnings | ForEach-Object { Write-Host "     - $_" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host "  Rewrite worklist - every _migrate folder must be empty before you tag v2-arch:" -ForegroundColor Cyan
Write-Host "    $($P.PlatContracts)\_migrate       ILLMProvider    -> IModelGateway / IModelCatalog"
Write-Host "    $($P.PlatOpenAI)\_migrate          OpenAIProvider  -> IModelGateway implementation"
Write-Host "    $($P.IntContext)\Ranking\_migrate  KnowledgeRanker -> ContextItem ranker"
Write-Host "    $($P.IntMemory)\_migrate           Domain Memory   -> ScopeRef-keyed MemoryRecord"
Write-Host "    $($P.Host)\_migrate                Program.cs      -> host bootstrap + product module"

Write-Host ""
if ($Execute) {
    Write-Host "  Done. The solution will NOT build yet - namespaces still say NexusAI.*" -ForegroundColor Green
    Write-Host "  Next: open CLAUDE_CODE_MIGRATION_PROMPTS.md and run Stage 1." -ForegroundColor Green
    Write-Host ""
    Write-Host "  Review:  git status" -ForegroundColor DarkGray
    Write-Host "  Undo:    git checkout . ; git checkout main ; git branch -D arch/v2" -ForegroundColor DarkGray
} else {
    Write-Host "  Dry run complete. Re-run with -Execute to apply." -ForegroundColor Yellow
}
Write-Host ""
