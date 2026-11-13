# PAT-032 — Comments say what is true now and why; a deliberate limit is a tagged `shortcut` backed by a register row

| Field | Value |
|-------|-------|
| **ID** | PAT-032 |
| **Category** | pattern |
| **Status** | approved (a trial: measured S148–S150, kept or reverted at S150's close — `ROADMAP.md` § Governance / docs) |
| **Sprint** | pre-S148 harness change (2026-11-08) |
| **Domains** | All code domains (Backend, Infrastructure, Rule Engine, Payroll, Integrations, Frontend, Tests, Tooling) |
| **Tags** | comments, shortcut, known-limit, register, residual, revisit-not-shield, ponytail, ci, check-docs |

## In plain words
A code comment is the one piece of project knowledge an agent is certain to read, because it sits in the file
being changed. That makes comments the right place for the *reason* behind the code, and the wrong place for a
*story* nobody keeps up to date. This pattern keeps the reason, sends the story to the records that are kept up to
date (the sprint log, the registers, git), and gives a deliberately accepted limit ("fine until X") one fixed,
machine-checked form that always points at a register row.

## Context
- **Comments are 40 % of the C# under `src/`** (2026-11-08): 40,969 of 102,462 lines. Re-measure with
  `files=$(git ls-files 'src/*.cs'); cat $files | wc -l` and `cat $files | grep -cE '^\s*//'`. Counts of comment
  lines that cite ids or narrate a past change depend on the phrase list; quote the list with any such number.
- **No gate reads comments, so they drift.** At least seven quality-register rows are comments that misinform:
  QUAL-069 (a rationale wrong about location and caller count), QUAL-070 (comments promising work already done),
  QUAL-090 (17 "authority" comments, three of four anchors wrong; raised to High by the owner), QUAL-093 (a retired
  reset month), QUAL-094 (self-references off by 443 and 1,190 lines), QUAL-101 and QUAL-107 (comments misinforming
  about a test's own checks).
- **Known limits were narrative "residual" comments** with ids woven into prose, findable only by reading. The
  registers ("revisit, not shield") track open limits, but nothing linked a comment to its row.
- **Source.** "Comment only the why" and a `shortcut:` comment for a known limit come from the Ponytail plugin
  (github.com/dietrichgebert/ponytail, MIT licence). The plugin was assessed and not installed (2026-11-06); this
  entry paraphrases its ideas and copies no code. The tag position, the register link and the CI check are ours.

## The pattern

### 1. Comments state what is true now and why
Keep every reason the code cannot show — an invariant, an owner ruling, a lock order, why a check sits where it
does — with the register, ADR or KB id(s) where the history lives. The history itself (which sprint, which review
round, what the code used to do) belongs in the sprint log, the register and git. A test pin's "Red conditions"
docstring (`docs/AGENTS.md` § RED evidence) is a reason, and is kept. **Forward only:** an old history comment is
rewritten only where that code is being changed anyway, never in a bulk sweep (a bulk rewrite could drop a
load-bearing reason with nothing to catch it).

**Worked example — "deletion beats addition"** (`ReportingLineEndpoints.cs`, 2026-11-08). After the call to
`ValidateSameOrganisationAsync` stood a nine-line block: "S78 R9 — the S74-7403 stale-key residual is now CLOSED …
ValidateSameOrganisationAsync (below) still pins BOTH user rows FOR UPDATE …". The call was *above* it, and every
true claim in the block was already stated in the lock-order comment above the call and in the repository's own
doc comment (`ReportingLineRepository.cs`). The block was deleted, not rewritten. In the same edit, the comment
above the call cited "ADR-027 D2", a decision ADR-035 D6 marks superseded; it now cites "ADR-027 D15, re-keyed by
ADR-035 D6", and "a concurrent R10" (the S74 label for the delete-with-reassignment route, opaque to a reader)
became "a concurrent deactivation of the manager", which is what that route does to the manager. The
other "ADR-027 D2" cite in that file (the vikar path, ~:2083) stays until that code is next changed; ADR-027's
header redirects a reader to ADR-035 D6.

### 2. A deliberate limit is a `shortcut(<tag>)` comment
A limit accepted on purpose — not a bug; a bug is fixed or reported — gets, at the spot in the code (the id below is
an example):

```
// shortcut(QUAL-248): reads the whole month into memory; upgrade when one organisation passes ~10,000 rows
```

Lowercase, no spaces inside `shortcut(<tag>):`, after any comment marker, whole-line or trailing. The tag sits
right after the word (not at the end, as in Ponytail's `shortcut: <limit>, <when>`), so the check is reliable and
survives line-wrapping.

| Tag | Meaning | Record |
|-----|---------|--------|
| `QUAL-nnn` | quality limit at or above the Medium+ floor | a row in `docs/operations/quality-finding-register.md` |
| `SEC-nnn` | security limit | a row in `docs/operations/security-finding-register.md` |
| `F<n>` | performance limit | a row in `docs/operations/performance-finding-register.md` |
| `QUAL-BF-nnn` | quality limit below the floor | a row in the QUAL register's "Shortcuts below the floor" table; its path cell is the repo-relative POSIX path exactly as `git ls-files` prints it, in backticks, no line number (or `no spot`); status `open` / `closed S<n>`, never deleted |
| `pending` | not yet registered | the agent's "Shortcuts taken" — never on master |

A limit with no place in the code (the absence of a mechanism, e.g. a live token acting until it expires) gets no
comment, only its "Shortcuts taken" line and its register row.

### 3. Lifecycle
1. The implementing agent writes `shortcut(pending): …` and lists it under "Shortcuts taken" (file:line or
   "no spot", the limit, the trigger, a severity estimate). Agents cannot edit `docs/`.
2. At acceptance (`WORKFLOW.md` Step 5b) the Orchestrator registers each — a new row, or a dated note on an
   existing row when the limit is already registered — and replaces `pending` with the tag in the same merge.
3. When the row closes, the comment goes in the same change; when a row closes by ruling rather than by a code
   change, the Orchestrator removes the comment as a small task.

### 4. Enforcement
- **Constraint Validator check 9** (`docs/AGENTS.md`): every `shortcut` comment in the changed files has the valid form.
- **`tools/check_docs.py` check 5 (CI, hard)**: scans tracked files with the extensions .cs .ts .tsx .js .jsx .mjs
  .cjs .sql .ps1 .psm1 .py .sh .yml .yaml (never `.md`, which carries the syntax as instruction text; never
  `docs/` or the script itself). A *trigger* is a comment marker (`//`, `///`, `#`, `--`, `/*` anywhere; `*` only
  as a line's first non-blank character), then `shortcut`, then `:` or `(`. It fails, naming `file:line`, on:
  wrong case · untagged (`shortcut:`) · unknown tag · malformed · `pending` · an id with no row · a `QUAL-BF` row
  naming another file. It always prints `[shortcuts] N found` and each valid shortcut, for the Step 0a scan.
- **Step 0a, item 6** (`WORKFLOW.md`): a closed row whose comment remains is DRIFT. **Before Step 7a**: no
  `pending` may remain (the local catch; Python does not run on the owner's machine).

### 5. Known limits of the check (accepted)
- A string literal containing a trigger is flagged — reword the string; there is no exemption.
- A prose comment that starts "Shortcut:" right after the marker is flagged — write "Keyboard shortcut: …".
- A continuation line inside a block comment without a leading `*` is not seen.
- CI proves a row *exists*, not that it is open (Step 0a covers that).
- `QUAL-` and `SEC-` ids are three digits (highest on 2026-11-08: QUAL-247, SEC-058); widen the pattern when
  either passes 999.

## The SMALLEST COMPLETE CHANGE identity check
The block lives in nine places (eight implementer definitions and the Agent Prompt Template). To confirm they
have not drifted, extract each from `SMALLEST COMPLETE CHANGE (standing instruction, 2026-11-08` to
``replaces `pending` (PAT-032).``, collapse whitespace, and compare SHA-256 hashes:

```bash
node -e "const fs=require('fs'),c=require('crypto');const s='SMALLEST COMPLETE CHANGE (standing instruction, 2026-11-08',e='replaces \`pending\` (PAT-032).';
for(const f of ['rule-engine','backend-infrastructure','payroll-integration','data-model','api-integration','security','ux','test-qa'].map(n=>'.claude/agents/'+n+'.md').concat(['docs/AGENTS.md'])){
const t=fs.readFileSync(f,'utf8'),i=t.indexOf(s),j=t.indexOf(e,i);console.log(i<0||j<0?'MISSING':c.createHash('sha256').update(t.slice(i,j+e.length).replace(/\s+/g,' ')).digest('hex').slice(0,16),f)}"
```

All nine lines must show the same hash (2026-11-08: `f57be923033c5ab9`).

## Agent Guidance
- Before writing a comment: is it true now, and is it a reason? If it is a story, put it in your report.
- Before accepting a limit: is it a bug? A bug is fixed or reported, never tagged as a shortcut.
- Never invent a register id; write `pending` and list the shortcut.

## Rationale
The registers already hold the record and are reviewed; the code holds the reader's attention. Linking the two
mechanically means a limit can neither hide in prose nor outlive its row unnoticed, and a reason kept short and
current is one fewer comment to misinform the next reader.
