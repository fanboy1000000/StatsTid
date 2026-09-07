# model-routing-guard.ps1
#
# PreToolUse hook on the `Agent` tool. Enforces the model-routing table in
# docs/WORKFLOW.md, section "Model Routing" (owner ruling 2026-09-07):
#
#   planning and review run on the most capable model; execution against a
#   reviewed spec runs on cheaper ones.
#
# What it blocks (exit 2, message on stderr):
#   - a `reviewer` spawn whose `model` override is anything but the review floor
#     (the definition in .claude/agents/reviewer.md already says fable; this stops
#     a cheaper override from silently lowering the bar);
#   - any implementer / validator / trace / sweep spawn on `fable` — implementation
#     never runs on the planning-and-review model;
#   - a sweep on anything above `sonnet` (it is pattern-shaped work);
#   - a bare `general-purpose` (or unnamed) spawn that names NO model — the
#     choice must be conscious, so the Orchestrator re-issues with one;
#   - a `general-purpose` spawn on `fable` — use `reviewer` for review work.
#
# What passes untouched: read-only built-ins (Explore, Plan, claude-code-guide,
# statusline-setup), `fork` (the tool ignores model overrides for forks), and any
# spawn that names no override for a role whose definition fixes the model.
#
# Fail-OPEN on hook-internal errors (unparseable input, missing fields) — same
# convention as sprint-close-guard.ps1. The gate is about routing, not about
# stopping work when the hook itself is confused.
#
# Test seam: pipe a PreToolUse payload on stdin, e.g.
#   '{"tool_name":"Agent","tool_input":{"subagent_type":"reviewer","model":"sonnet"}}'
#   | powershell -File .claude/hooks/model-routing-guard.ps1 ; echo $LASTEXITCODE

$ErrorActionPreference = 'Stop'

$rawInput = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($rawInput)) { exit 0 }
if ($rawInput.Length -gt 0 -and $rawInput[0] -eq [char]0xFEFF) { $rawInput = $rawInput.Substring(1) }

try { $payload = $rawInput | ConvertFrom-Json } catch {
    [Console]::Error.WriteLine('model-routing-guard: could not parse hook input as JSON; allowing')
    exit 0
}

if (-not $payload.tool_name -or $payload.tool_name -ne 'Agent') { exit 0 }
$in = $payload.tool_input
if ($null -eq $in) { exit 0 }

$type  = ''
$model = ''
if ($in.PSObject.Properties['subagent_type'] -and $in.subagent_type) { $type  = ([string]$in.subagent_type).Trim().ToLowerInvariant() }
if ($in.PSObject.Properties['model']         -and $in.model)         { $model = ([string]$in.model).Trim().ToLowerInvariant() }

# ---- the routing table (keep in step with docs/WORKFLOW.md, section "Model Routing") ----
$ReviewFloor    = 'fable'
$ReviewRoles    = @('reviewer')
$OpusRoles      = @('rule-engine', 'payroll-integration', 'backend-infrastructure')
$SonnetRoles    = @('data-model', 'api-integration', 'security', 'test-qa', 'ux', 'constraint-validator', 'trace')
$SweepRoles     = @('sweep')
$ReadOnlyPass   = @('explore', 'plan', 'claude-code-guide', 'statusline-setup')
$GenericRoles   = @('', 'general-purpose', 'claude')

function Block([string]$why, [string]$fix) {
    [Console]::Error.WriteLine('model-routing-guard: BLOCKING this Agent spawn.')
    [Console]::Error.WriteLine('')
    [Console]::Error.WriteLine("  subagent_type = '$type'   model = '$(if ($model) { $model } else { '(none)' })'")
    [Console]::Error.WriteLine("  $why")
    [Console]::Error.WriteLine('')
    [Console]::Error.WriteLine("  Fix: $fix")
    [Console]::Error.WriteLine('  Routing table: docs/WORKFLOW.md, section "Model Routing" (owner ruling 2026-09-07).')
    exit 2
}

if ($type -eq 'fork' -or $ReadOnlyPass -contains $type) { exit 0 }

if ($ReviewRoles -contains $type) {
    if ($model -and $model -ne $ReviewFloor) {
        Block "Review runs on the most capable model; '$model' is below the floor '$ReviewFloor'." `
              "drop the model override (the reviewer definition already fixes it) or pass model: '$ReviewFloor'."
    }
    exit 0
}

if ($OpusRoles -contains $type) {
    if ($model -eq $ReviewFloor) { Block 'Implementation never runs on the planning-and-review model.' "pass model: 'opus' (this role handles legal logic, money or the audit chain) or omit it." }
    if ($model -eq 'haiku')      { Block 'This role handles legal logic, money or the audit chain; haiku is below its floor.' "pass model: 'opus' (or 'sonnet' for a narrowly specified task) or omit it." }
    exit 0
}

if ($SonnetRoles -contains $type) {
    if ($model -eq $ReviewFloor) { Block 'Implementation, validation and tracing never run on the planning-and-review model.' "omit the model (the definition fixes sonnet) or pass 'opus' for an unusually hard task." }
    exit 0
}

if ($SweepRoles -contains $type) {
    if ($model -eq $ReviewFloor -or $model -eq 'opus') { Block 'A sweep is pattern-shaped work; it does not need this model.' "omit the model (the definition fixes haiku) or pass 'sonnet'." }
    exit 0
}

if ($GenericRoles -contains $type) {
    if (-not $model) {
        Block 'A generic agent inherits the session model, which is usually the most expensive one; the choice must be conscious.' `
              "pass an explicit model ('sonnet' for reading/diagnosis/implementation, 'haiku' for mechanical work), or spawn a named role from .claude/agents/."
    }
    if ($model -eq $ReviewFloor) {
        Block 'Only review and planning run on this model, and those have their own role.' "use subagent_type 'reviewer' for review work; for anything else pass 'opus' or cheaper."
    }
    exit 0
}

# Unknown custom type: only the one rule that never has an exception.
if ($model -eq $ReviewFloor) {
    Block "Unrecognised subagent_type on the planning-and-review model." "add the role to this guard's table with its floor, or spawn 'reviewer' if this is review work."
}
exit 0
