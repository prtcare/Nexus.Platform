# Nexus V2 Migration — Step by Step

Keep this open on a second screen. Every command is copy-paste ready.

**One correction to earlier advice:** do **not** use the Visual Studio terminal for this.
Visual Studio must be closed while ~150 files move, so you need a terminal that lives
outside it. Plain PowerShell is fine — you do not need the Developer PowerShell, because
`dotnet` is on your PATH globally.

---

## Part 0 — What you'll have open

| Window | What it is | Used for |
|---|---|---|
| **PowerShell** | Start menu → type `PowerShell` → Enter | Everything. All commands go here. |
| **File Explorer** at `C:\Personal\NexusAI` | optional | Watching the folder tree change |
| **Visual Studio** | **CLOSED** until Part 5 | — |

You will *not* type anything into Visual Studio until the migration is finished.

---

## Part 1 — Close Visual Studio

If VS has `NexusAI.slnx` open, close it completely. Not just the solution — the whole
application. Check Task Manager for `devenv.exe` if you're unsure.

Why this matters: Visual Studio holds file locks on `bin`/`obj`, rewrites `.slnx` when it
notices projects appearing and disappearing, and caches the project graph in `.vs`. Running
a 150-file move underneath it produces a mess that looks like a script bug but isn't.

---

## Part 2 — Open PowerShell and go to the repo

Start menu → type `PowerShell` → press Enter. A blue window opens. Then:

```powershell
cd C:\Personal\NexusAI
```

Your prompt should now read `PS C:\Personal\NexusAI>`.

---

## Part 3 — Preflight (five checks, two minutes)

Run these one at a time and compare against "expected".

**3.1 — You're in the right folder**

```powershell
dir NexusAI.slnx
```

Expected: one line showing the file. If you get "cannot find path", you're in the wrong
folder — redo Part 2.

**3.2 — All four migration files are present**

```powershell
dir *.md, *.ps1 | Select-Object Name, Length
```

Expected: `NEXUS_ARCHITECTURE_V2.md`, `CLAUDE_CODE_MIGRATION_PROMPTS.md`,
`nexus-restructure.ps1`, `run-migration.ps1` — plus your existing README/CHANGELOG/etc.

**3.3 — Tools are installed**

```powershell
git --version
dotnet --version
claude --version
```

Expected: three version numbers. `dotnet` should start with `10.`. If `claude` says
"not recognized", install it: `irm https://claude.ai/install.ps1 | iex`, then close and
reopen PowerShell.

**3.4 — Claude Code is logged in**

```powershell
claude -p "reply with the word ready and nothing else"
```

Expected: `ready`. If a browser opens asking you to log in, do that, then run it again.
Get this working *now* — you do not want a login prompt appearing in the middle of Stage 4.

**3.5 — Working tree is clean**

```powershell
git status
```

Expected: `nothing to commit, working tree clean`.

If it lists changes, commit them:

```powershell
git add -A
git commit -m "chore: pre-migration checkpoint"
```

If it lists `bin/` or `obj/` folders, fix `.gitignore` first — see Part 7.

**3.6 — PowerShell will let you run local scripts**

```powershell
Get-ExecutionPolicy
```

If this says `Restricted` or `AllSigned`, run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

That applies to this window only and disappears when you close it.

---

## Part 4 — The migration

### 4.1 — Dry run first (nothing changes)

```powershell
.\nexus-restructure.ps1
```

This prints every move it *would* make and changes nothing. Read the output. You're
looking for:

```
   files created    : 26
   files/dirs moved : 66
   paths deleted    : 14
```

A handful of warnings is fine (some paths in your tree may differ slightly from the zip
you sent me). Dozens of `source not found` warnings means something is wrong — stop and
tell me what it printed.

**Do not run this script with `-Execute` yourself.** The driver calls it for you at the
right moment, with the right flags.

### 4.2 — Start the driver

```powershell
.\run-migration.ps1
```

That's the whole migration. It runs eight stages. Everything below is just describing
what you'll see so nothing surprises you.

### 4.3 — What happens, stage by stage

The driver checks your tools, then works through the stages. It pauses after each one.

| Stage | What it does | Roughly how long | Your job |
|---|---|---|---|
| **0** Baseline | Builds the current solution, tags `pre-v2`, creates branch `arch/v2` | 1–2 min | Watch. If the build fails it stops here — see Part 6. |
| **0.5** House rules | Claude writes `CLAUDE.md` so every future session knows the layer rules | 1 min | Press **C** |
| **1** Restructure | Moves ~150 files into the new layer folders | under a minute | Read the summary, press **C** |
| **2** Namespaces | Claude rewrites every namespace and using from `NexusAI.*` to the new names | 10–20 min | Press **C**. Build still fails here — that's expected and the script knows. |
| **3** Platform | Claude authors the Platform contracts and rewrites the OpenAI provider | 15–30 min | **Read this diff.** Then **C**. |
| **4** Intelligence | Claude authors the Intelligence contracts and the turn pipeline | 20–40 min | **Read this diff.** Then **C**. |
| **5** Rewire | Claude rewires the chat turn through Intelligence | 20–40 min | **Read this diff carefully.** This is the one that matters. Then **C**. |
| **6** Host | Claude consolidates to a single host | 10–20 min | Press **C** |
| **7** Enforce | Claude writes the boundary tests, updates the frontend and docs | 15–30 min | Press **C** |

Times are rough. Expect the whole thing to take an afternoon, not ten minutes.

### 4.4 — The pause prompt

After each stage you'll see:

```
   Stage 3 done. [C]ommit and continue, [S]kip commit, [A]bort:
```

- **C** — commit this stage and move to the next. This is what you want almost always.
- **S** — continue without committing. Use if you want to fold this stage into the next commit.
- **A** — stop. Nothing is lost; you resume later with `-FromStage`.

Before pressing C on stages 3, 4 and 5, open a **second** PowerShell window and look at
what changed:

```powershell
cd C:\Personal\NexusAI
git diff --stat            # which files changed and by how much
git diff                   # the actual changes, q to quit
```

### 4.5 — What to look for in the three diffs that matter

**Stage 3 (Platform).** Ask yourself: does anything in `src\platform` mention a
Conversation, a Workspace, or Dataverse? It shouldn't. Platform executes model calls and
knows nothing else.

**Stage 4 (Intelligence).** Same question for `src\intelligence`, plus: does it call the
OpenAI SDK directly anywhere? It shouldn't — it should go through `IModelGateway`.

**Stage 5 (Rewire).** The important file is `ChatContextBundleMapper.cs`. That's where your
Dataverse rows become canonical `ContextItem`s. If that mapper looks thin or lossy, the
Intelligence layer is being starved of context and the chat will feel dumber than V1. Push
back and re-run the stage if so.

---

## Part 5 — After it finishes

The driver prints a completion banner. Now reopen Visual Studio — but on the **new**
solution file:

```powershell
start Nexus.slnx
```

Then in PowerShell:

```powershell
dotnet build Nexus.slnx
dotnet test Nexus.slnx
```

Both should be green. Then run it:

```powershell
dotnet run --project host\Nexus.Host
```

Open the Swagger URL it prints. You should see two API groups: **Nexus Chat** and
**Nexus Intelligence**.

Now work the verification checklist at the end of `CLAUDE_CODE_MIGRATION_PROMPTS.md`.
Items 1–9 only prove the layers are separated. Items 10–12 — send a real chat message and
confirm it round-trips to Dataverse with a recorded cost — prove they still work together.
Those are the real test.

When it all passes:

```powershell
git tag v2-arch
```

---

## Part 6 — When something breaks

### The driver stops at Stage 0: "The existing solution does not build"

This is the script protecting you, not failing. Your canonical docs already flag "no clean
build recorded for this handoff" as critical debt. Fix it first:

```powershell
git checkout main
dotnet restore
dotnet build
```

Fix the errors, commit, then start over with `.\run-migration.ps1`.

### A stage fails, or the build gate goes red

The driver stops and tells you the stage number. Hand the errors back to Claude in the same
session:

```powershell
claude -c "the build fails after stage 4. Here are the errors: <paste them>. Fix them without starting any other stage."
```

Then resume from the *next* stage:

```powershell
.\run-migration.ps1 -FromStage 5
```

### You don't like what a stage produced

Undo exactly that stage and redo it:

```powershell
git reset --hard HEAD~1
.\run-migration.ps1 -FromStage 4 -ToStage 4
```

### You want to abandon the whole thing

```powershell
git checkout main
git branch -D arch/v2
```

Your original solution is untouched. `pre-v2` is tagged as well, so `git checkout pre-v2`
also gets you back.

### Claude Code stops mid-stage or seems stuck

Ctrl+C in the PowerShell window. Check what it managed to do with `git status`, then either
resume that stage or reset and re-run it. Logs for every stage are in
`.migration-logs\stage-N.log`.

---

## Part 7 — The `.gitignore` fix (do this before Part 4 if you haven't)

Your `.gitignore` is 16 bytes — about one line — and `bin`/`obj` folders are committed.
That makes the migration diff unreadable.

```powershell
claude -p "Write a proper .NET .gitignore at the repo root covering bin, obj, .vs, user files and build output. Then untrack any already-committed bin and obj folders using git rm -r --cached. Do not delete anything from disk. Do not touch any other file."

git status
git add -A
git commit -m "chore: proper .gitignore, untrack build output"
```

---

## Quick reference

```powershell
cd C:\Personal\NexusAI

.\nexus-restructure.ps1              # dry run, changes nothing
.\run-migration.ps1                  # the migration, pauses between stages

.\run-migration.ps1 -FromStage 4     # resume from stage 4
.\run-migration.ps1 -FromStage 4 -ToStage 4    # redo just stage 4
.\run-migration.ps1 -Unattended      # no pauses (not on the first run)

git diff --stat                      # what changed
git reset --hard HEAD~1              # undo one stage
git checkout main; git branch -D arch/v2       # undo everything

claude -c "<follow-up>"              # continue the last Claude session
```
