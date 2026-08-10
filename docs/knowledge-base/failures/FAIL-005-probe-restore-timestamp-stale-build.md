# FAIL-005 — Restoring a probe backup by file copy preserves the OLD timestamp, so the next test run silently executes the PROBE build

| Field | Value |
|-------|-------|
| **ID** | FAIL-005 |
| **Category** | failure |
| **Status** | recorded |
| **Sprint** | S127 |
| **Domains** | Tooling, Test, Ops |
| **Tags** | falsification-probe, msbuild, incremental-build, false-red, false-green, scratchpad-restore |
| **Origin** | TASK-12701a (S127), reported by the implementing agent after it cost a false 4-test failure |

## What happened

The project's standing probe discipline is: **copy the file to the scratchpad, apply the deliberate
breakage, run the tests, then restore from the copy** — never `git checkout -- <file>`, which restores
HEAD and destroys uncommitted work in that file.

The restore step has its own trap. `Copy-Item` / `cp` **preserves the backup's original modification
timestamp**, which is *older* than the probe-modified file MSBuild already compiled. MSBuild's
incremental build compares timestamps, judges the existing DLL up to date, and **skips recompilation** —
so the next `dotnet test` runs the **probe** build against restored source.

In S127 this surfaced as a 4-test failure immediately after probe cleanup. The source on disk was
correct. Read as a regression, it would have sent someone hunting a defect that did not exist.

## Why it is dangerous in both directions

- **False RED** (what happened): the probe's breakage still in the DLL after the source is restored.
- **False GREEN** (the worse case): if the probe *removed* a guard and the restore is skipped by MSBuild,
  a test that should now fail keeps passing against the stale binary. A falsification probe that reports
  "restored, still green" then proves nothing at all — which defeats the entire purpose of running it.

## The fix

After restoring from a scratchpad backup, force the rebuild:

```powershell
# make the restored file newer than the compiled output
(Get-Item path\to\File.cs).LastWriteTime = Get-Date
# or, decisively:
Remove-Item -Recurse -Force src\<Project>\bin, src\<Project>\obj
```

Then **rebuild before re-running**, and confirm the restore really took — compare a hash of the restored
file against the backup, and re-run the probe's own assertion to see it flip back.

## Checklist for any falsification probe

- [ ] Copy to scratchpad first (never rely on `git checkout --`)
- [ ] Apply the breakage, build, run, record the failure output
- [ ] Restore from the copy
- [ ] **Touch the restored file or delete `bin`/`obj`**
- [ ] Rebuild, re-run, confirm green — and confirm the file hash matches the backup
- [ ] State in the report that the restore was verified, not assumed

## Relationship to other entries

Direct companion to the standing "never `git checkout -- <file>` to undo a probe" lesson: that rule names
the *unsafe* restore path, and this entry names the trap inside the *safe* one. Probes are mandated by
[PAT-013](../patterns/PAT-013-on-conflict-vs-23505-transaction-liveness.md) and
[PAT-014](../patterns/PAT-014-characterization-baseline-one-inversion-per-encoding.md), so both inherit
this hazard.
