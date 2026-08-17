<#
.SYNOPSIS
    Drives the Nexus V2 migration end to end: runs the file restructure, then invokes
    Claude Code headlessly for each rewrite stage, building and committing between stages.

.DESCRIPTION
    Each stage is one Claude Code invocation that reads CLAUDE_CODE_MIGRATION_PROMPTS.md
    and executes exactly one stage from it. The runbook stays the single source of truth -
    edit the prompts there, not in this script.

    After every stage the driver builds the solution and stops if the build breaks, so a
    bad stage cannot silently poison the next one. Every stage is its own git commit, so
    `git reset --hard HEAD~1` undoes exactly one stage.

    DEFAULT BEHAVIOUR IS SEMI-AUTOMATIC. The driver pauses after each stage so you can read
    the diff before it commits. Use -Unattended to remove the pauses - but read the warning
    on that parameter first.

.PARAMETER RepoRoot
    Repo root (the folder containing NexusAI.slnx). Defaults to the current directory.

.PARAMETER FromStage
    Stage to start at. Use this to resume after fixing something. Default 0.

.PARAMETER ToStage
    Last stage to run. Default 7. Use -FromStage 3 -ToStage 3 to run a single stage.

.PARAMETER Unattended
    Do not pause between stages. Only sensible once you have run the whole thing on a
    throwaway clone and know what each stage produces. A 150-file architectural refactor
    is not something to run unwatched the first time.

.PARAMETER Yolo
    Pass --dangerously-skip-permissions to Claude Code instead of an explicit tool allowlist.
    Faster, and gives the agent unrestricted tool access in this repo. Prefer the default.

.PARAMETER Model
    Model alias for Claude Code (opus, sonnet, haiku). Default opus - this is architectural
    work and the cheaper models will make boundary mistakes you then have to find.

.EXAMPLE
    .\run-migration.ps1                      # full run, pausing between stages
    .\run-migration.ps1 -FromStage 3         # resume from the Platform stage
    .\run-migration.ps1 -FromStage 5 -ToStage 5   # redo just the rewiring stage
#>

[CmdletBinding()]
param(
    [string] $RepoRoot   = (Get-Location).Path,
    [int]    $FromStage  = 0,
    [int]    $ToStage    = 7,
    [switch] $Unattended,
    [switch] $Yolo,
    [ValidateSet('opus','sonnet','haiku')]
    [string] $Model      = 'opus'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$LogDir = Join-Path $RepoRoot '.migration-logs'
$Sln    = 'Nexus.slnx'   # becomes valid after stage 1; before that we build NexusAI.slnx

# Tools Claude Code may use without prompting. Deliberately narrow: it can edit code,
# build, and use git - but not delete arbitrary things or reach the network.
$AllowedTools = @(
    'Read','Write','Edit','Glob','Grep','TodoWrite',
    'Bash(dotnet *)',
    'Bash(git add *)','Bash(git commit *)','Bash(git status*)','Bash(git diff*)','Bash(git mv *)',
    'Bash(Select-String *)','Bash(ls *)','Bash(dir *)'
)

# ---------------------------------------------------------------------------
# Stage table
# ---------------------------------------------------------------------------
# Kind: 'script' = we run something ourselves; 'claude' = headless Claude Code call.
# Gate : build must pass after this stage.

$Stages = @(
    @{ N=0;   Name='Baseline';              Kind='script'; Gate=$false }
    @{ N=0.5; Name='CLAUDE.md house rules'; Kind='claude'; Gate=$false
       Prompt='Read CLAUDE_CODE_MIGRATION_PROMPTS.md and execute the section titled "Stage 0.5 - Give Claude Code the rules permanently", exactly as written. Create CLAUDE.md at the repo root with the content that section specifies. Do nothing else. Do not modify any other file.' }
    @{ N=1;   Name='Restructure (file moves)'; Kind='script'; Gate=$false }
    @{ N=2;   Name='Namespace rewrite';     Kind='claude'; Gate=$false
       Prompt='Read CLAUDE_CODE_MIGRATION_PROMPTS.md and execute Stage 2 exactly as written. Do not start any other stage. Do not touch any folder named _migrate. When finished, report which projects build and list any remaining errors, separating expected cross-layer errors (missing ILLMProvider, IPromptBuilder, IKnowledgeRanker) from real mistakes.' }
    @{ N=3;   Name='Platform layer';        Kind='claude'; Gate=$true
       Prompt='Read NEXUS_ARCHITECTURE_V2.md sections 1.1, 2 and 3.2, then read CLAUDE_CODE_MIGRATION_PROMPTS.md and execute Stage 3 exactly as written. Do not start any other stage. Before you finish, verify that no project under src/platform references Nexus.Intelligence or Nexus.Products, and report the result.' }
    @{ N=4;   Name='Intelligence layer';    Kind='claude'; Gate=$true
       Prompt='Read NEXUS_ARCHITECTURE_V2.md sections 1.2, 2 and 3.1, then read CLAUDE_CODE_MIGRATION_PROMPTS.md and execute Stage 4 exactly as written. Do not start any other stage. Before you finish, verify that no file under src/intelligence contains the words Workspace, Conversation, WorkItem, Dataverse or OpenAI, and report the result.' }
    @{ N=5;   Name='Rewire the chat turn';  Kind='claude'; Gate=$true
       Prompt='Read NEXUS_ARCHITECTURE_V2.md section 4 (the end-to-end chat turn), then read CLAUDE_CODE_MIGRATION_PROMPTS.md and execute Stage 5 exactly as written. Do not start any other stage. This is the stage that proves the architecture - be careful with the ContextBundle mapper. Before you finish, verify no file under src/products references ILLMProvider, OpenAI or ModelInvocation, and report the result.' }
    @{ N=6;   Name='Host consolidation';    Kind='claude'; Gate=$true
       Prompt='Read CLAUDE_CODE_MIGRATION_PROMPTS.md and execute Stage 6 exactly as written. Do not start any other stage. When finished, confirm there is exactly one Program.cs in the repo and that it lives under host/.' }
    @{ N=7;   Name='Enforce and update edges'; Kind='claude'; Gate=$true
       Prompt='Read NEXUS_ARCHITECTURE_V2.md section 2 (the reference rules), then read CLAUDE_CODE_MIGRATION_PROMPTS.md and execute Stage 7 exactly as written. Do not start any other stage. The architecture tests are the point of this stage: after writing them, deliberately break one boundary rule, confirm the corresponding test fails, then revert the break. Report which test caught it.' }
)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Banner {
    param([string]$Text)
    Write-Host ""
    Write-Host ("=" * 78) -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ("=" * 78) -ForegroundColor Cyan
}

function Assert-Tool {
    param([string]$Exe, [string]$Hint)
    $found = Get-Command $Exe -ErrorAction SilentlyContinue
    if (-not $found) { throw "$Exe not found on PATH. $Hint" }
    Write-Host "   $Exe : ok" -ForegroundColor DarkGray
}

function Invoke-Build {
    param([string]$Solution)
    Write-Host ""
    Write-Host "   building $Solution ..." -ForegroundColor DarkGray
    Push-Location $RepoRoot
    try {
        & dotnet build $Solution --nologo -v quiet 2>&1 | Tee-Object -Variable out | Out-Null
        $ok = ($LASTEXITCODE -eq 0)
        if (-not $ok) { $out | Select-Object -Last 40 | ForEach-Object { Write-Host "   $_" -ForegroundColor Red } }
        return $ok
    }
    finally { Pop-Location }
}

function Invoke-ClaudeStage {
    param([hashtable]$Stage)

    $log = Join-Path $LogDir ("stage-{0}.log" -f $Stage.N)

    $args = @('-p', $Stage.Prompt, '--model', $Model, '--output-format', 'text')

    if ($Yolo) {
        $args += '--dangerously-skip-permissions'
    }
    else {
        $args += @('--permission-mode','acceptEdits')
        $args += '--allowedTools'
        $args += $AllowedTools
    }

    Write-Host "   claude -p `"<stage $($Stage.N) prompt>`" --model $Model" -ForegroundColor DarkGray
    Write-Host "   log: $log" -ForegroundColor DarkGray
    Write-Host ""

    Push-Location $RepoRoot
    try {
        & claude @args 2>&1 | Tee-Object -FilePath $log
        return ($LASTEXITCODE -eq 0)
    }
    finally { Pop-Location }
}

function Commit-Stage {
    param([hashtable]$Stage)
    Push-Location $RepoRoot
    try {
        $dirty = git status --porcelain
        if (-not $dirty) {
            Write-Host "   nothing to commit for stage $($Stage.N)" -ForegroundColor Yellow
            return
        }
        git add -A | Out-Null
        git commit -m "refactor(v2): stage $($Stage.N) - $($Stage.Name)" | Out-Null
        Write-Host "   committed: stage $($Stage.N) - $($Stage.Name)" -ForegroundColor Green
    }
    finally { Pop-Location }
}

function Wait-ForReview {
    param([hashtable]$Stage)
    if ($Unattended) { return $true }

    Write-Host ""
    Write-Host "   Review the diff:  git diff --stat HEAD" -ForegroundColor DarkGray
    Write-Host ""
    $answer = Read-Host "   Stage $($Stage.N) done. [C]ommit and continue, [S]kip commit, [A]bort"
    switch ($answer.ToUpper()) {
        'C'     { return $true }
        'S'     { return $false }
        'A'     { throw "Aborted by user at stage $($Stage.N)." }
        default { return $true }
    }
}

# ---------------------------------------------------------------------------
# Preflight
# ---------------------------------------------------------------------------

Write-Banner "Nexus V2 migration driver"

Write-Host "   repo   : $RepoRoot"
Write-Host "   stages : $FromStage .. $ToStage"
Write-Host "   model  : $Model"
Write-Host "   mode   : $(if ($Unattended) {'UNATTENDED'} else {'review between stages'})"
Write-Host ""

Assert-Tool 'git'    'Install Git for Windows: https://git-scm.com/downloads/win'
Assert-Tool 'dotnet' 'Install the .NET 10 SDK.'
Assert-Tool 'claude' 'Install Claude Code:  irm https://claude.ai/install.ps1 | iex'

foreach ($f in @('NEXUS_ARCHITECTURE_V2.md','CLAUDE_CODE_MIGRATION_PROMPTS.md','nexus-restructure.ps1')) {
    if (-not (Test-Path (Join-Path $RepoRoot $f))) {
        throw "$f is not at the repo root. All three files must sit next to NexusAI.slnx."
    }
}
Write-Host "   runbook files : ok" -ForegroundColor DarkGray

# Keep the migration logs out of git BEFORE anything checks for a dirty tree.
# .git/info/exclude is local-only, so this never shows up as a change itself -
# unlike editing .gitignore, which would dirty the tree we are about to test.
$ExcludeFile = Join-Path $RepoRoot '.git\info\exclude'
if (Test-Path $ExcludeFile) {
    $excludes = Get-Content $ExcludeFile -Raw -ErrorAction SilentlyContinue
    if ($null -eq $excludes -or $excludes -notmatch '\.migration-logs') {
        Add-Content -Path $ExcludeFile -Value "`n# Nexus V2 migration driver`n.migration-logs/`n"
        Write-Host "   log dir excluded from git : ok" -ForegroundColor DarkGray
    }
}

Push-Location $RepoRoot
try {
    $dirty = git status --porcelain
    if ($dirty) {
        Write-Host ""
        Write-Host "   Working tree is dirty:" -ForegroundColor Yellow
        $dirty | Select-Object -First 20 | ForEach-Object { Write-Host "     $_" -ForegroundColor Yellow }
        Write-Host ""
        Write-Host "   Commit it and re-run:" -ForegroundColor Yellow
        Write-Host "     git add -A" -ForegroundColor Yellow
        Write-Host "     git commit -m `"chore: pre-migration checkpoint`"" -ForegroundColor Yellow
        Write-Host "     .\run-migration.ps1" -ForegroundColor Yellow
        throw "Working tree is dirty. Commit or stash before starting."
    }
}
finally { Pop-Location }

if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }

# ---------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------

foreach ($stage in $Stages) {

    if ($stage.N -lt $FromStage -or $stage.N -gt $ToStage) { continue }

    Write-Banner "Stage $($stage.N) - $($stage.Name)"

    # -- Stage 0: baseline -------------------------------------------------
    if ($stage.N -eq 0) {
        if (-not (Invoke-Build 'NexusAI.slnx')) {
            throw "The existing solution does not build. Fix that first - migrating on a broken build means you cannot tell migration errors from pre-existing ones."
        }
        Push-Location $RepoRoot
        try {
            git tag -f pre-v2 | Out-Null
            git checkout -b arch/v2 2>&1 | Out-Null
            Write-Host "   tagged pre-v2, branched arch/v2" -ForegroundColor Green
        }
        catch { Write-Host "   branch arch/v2 already exists - continuing" -ForegroundColor Yellow }
        finally { Pop-Location }
        continue
    }

    # -- Stage 1: the restructure script -----------------------------------
    if ($stage.N -eq 1) {
        Push-Location $RepoRoot
        try {
            & .\nexus-restructure.ps1 -RepoRoot $RepoRoot -Execute -SkipBranch
        }
        finally { Pop-Location }

        Write-Host ""
        Write-Host "   Files moved. The solution will NOT build until stage 2." -ForegroundColor Yellow
        if (Wait-ForReview $stage) { Commit-Stage $stage }
        continue
    }

    # -- Claude Code stages -------------------------------------------------
    $ok = Invoke-ClaudeStage $stage

    if (-not $ok) {
        Write-Host ""
        Write-Host "   Claude Code exited non-zero on stage $($stage.N)." -ForegroundColor Red
        Write-Host "   Read .migration-logs\stage-$($stage.N).log, fix, then resume with:" -ForegroundColor Red
        Write-Host "     .\run-migration.ps1 -FromStage $($stage.N)" -ForegroundColor Red
        throw "Stage $($stage.N) failed."
    }

    if ($stage.Gate) {
        if (-not (Invoke-Build $Sln)) {
            Write-Host ""
            Write-Host "   Build failed after stage $($stage.N). Not committing." -ForegroundColor Red
            Write-Host "   Fix it, or hand the errors back to Claude Code:" -ForegroundColor Red
            Write-Host "     claude -c `"the build fails after stage $($stage.N), fix it`"" -ForegroundColor Red
            Write-Host "   Then resume with:  .\run-migration.ps1 -FromStage $([int]$stage.N + 1)" -ForegroundColor Red
            throw "Build gate failed at stage $($stage.N)."
        }
        Write-Host "   build: green" -ForegroundColor Green
    }

    if (Wait-ForReview $stage) { Commit-Stage $stage }
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------

Write-Banner "Migration driver finished"

Write-Host "   Now work the verification checklist at the end of CLAUDE_CODE_MIGRATION_PROMPTS.md."
Write-Host "   Items 1-9 only prove the layers are separated."
Write-Host "   Items 10-12 prove they still work together - those are the real test."
Write-Host ""
Write-Host "   When it all passes:  git tag v2-arch" -ForegroundColor Green
Write-Host ""
Write-Host "   Roll back one stage :  git reset --hard HEAD~1"          -ForegroundColor DarkGray
Write-Host "   Roll back everything:  git checkout main; git branch -D arch/v2" -ForegroundColor DarkGray
Write-Host ""
