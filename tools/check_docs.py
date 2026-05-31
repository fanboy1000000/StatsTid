#!/usr/bin/env python3
"""Documentation consistency gate for StatsTid.

Mechanical enforcement for the failure modes the 2026-05-31 doc audit found:
the docs rotted because facts were duplicated and freshness was discretionary.
This script makes the load-bearing freshness checks fail CI instead of relying
on someone remembering to run the entropy scan.

HARD checks (exit 1 on failure):
  1. db-schema.md is in sync with init.sql  (delegates to generate_db_schema.py --check)
  2. KB INDEX completeness: every knowledge-base entry file is linked from
     INDEX.md, and every INDEX link resolves (no orphans, no dangling)
  3. Sprint-log inventory: every sprint that shipped in git history has a
     SPRINT-<n>.md log (catches the "S55 black hole"). Git-based; skipped
     gracefully if history is shallow/unavailable.

SOFT checks (report-only warnings, never fail):
  4. Doc freshness: docs carrying an `<!-- anchor-sprint: N -->` marker that
     trail the latest sprint by more than ANCHOR_SLACK.

Usage:
    python tools/check_docs.py
"""
from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
KB = REPO / "docs" / "knowledge-base"
KB_INDEX = KB / "INDEX.md"
SPRINTS = REPO / "docs" / "sprints"
KB_CATS = ["decisions", "patterns", "dependencies", "resolutions", "failures"]

# Sprint numbers that never shipped as their own log because the slot collapsed
# into a lettered amendment sub-sprint (see WORKFLOW.md "binding to architectural
# events, not sprint numbers"). Keep this list tiny and documented.
SPRINT_EXCEPTIONS = {"41", "42"}  # collapsed into S41a / S42a
ANCHOR_SLACK = 3  # sprints a doc may trail HEAD before we warn

failures: list[str] = []
warnings: list[str] = []


def check_db_schema() -> None:
    res = subprocess.run(
        [sys.executable, str(REPO / "tools" / "generate_db_schema.py"), "--check"],
        capture_output=True, text=True,
    )
    if res.returncode != 0:
        failures.append("db-schema.md out of sync with init.sql:\n    "
                        + res.stdout.strip().replace("\n", "\n    "))
    else:
        print(f"[ok] {res.stdout.strip()}")


def check_kb_index() -> None:
    on_disk = {f"{cat}/{p.name}"
               for cat in KB_CATS
               for p in (KB / cat).glob("*.md")}
    text = KB_INDEX.read_text(encoding="utf-8")
    linked = set(re.findall(r"\]\((" + "|".join(KB_CATS) + r")/([^)\s]+\.md)\)", text))
    linked = {f"{cat}/{name}" for cat, name in linked}

    orphans = sorted(on_disk - linked)
    dangling = sorted(linked - on_disk)
    if orphans:
        failures.append("KB entries on disk but NOT linked from INDEX.md (orphans):\n    "
                        + "\n    ".join(orphans))
    if dangling:
        failures.append("INDEX.md links to KB files that don't exist (dangling):\n    "
                        + "\n    ".join(dangling))
    if not orphans and not dangling:
        print(f"[ok] KB INDEX complete ({len(on_disk)} entries, 0 orphans, 0 dangling)")


def sprint_files() -> set[str]:
    out = set()
    for p in SPRINTS.glob("SPRINT-*.md"):
        m = re.match(r"SPRINT-(\d+[a-z]?)\.md", p.name)
        if m:
            out.add(m.group(1))
    return out


def check_sprint_inventory() -> None:
    try:
        res = subprocess.run(["git", "-C", str(REPO), "log", "--pretty=%s"],
                             capture_output=True, text=True, timeout=30)
    except Exception as e:  # noqa: BLE001
        warnings.append(f"sprint inventory: git unavailable ({e}); skipped")
        return
    if res.returncode != 0:
        warnings.append("sprint inventory: `git log` failed; skipped (shallow clone?)")
        return

    # Sprint commit subjects use two conventions over the project's history:
    #   - S20+ : "S56 ...", "S44b ..." (S<N> prefix)
    #   - pre-S20 : "Sprint 3: ...", "Complete Sprint 4: ...", "Promote Sprint 5 ..."
    # Many pre-S20 commits are tagged only by TASK id ("TASK-501: ...") with no sprint
    # reference in the subject, so they CANNOT be matched here — those sprints are not
    # auto-verified by this check (their logs all exist; this is a known coverage limit).
    shipped = set()
    for subj in res.stdout.splitlines():
        m = re.match(r"\s*S(\d+[a-z]?)\b", subj)
        if m:
            shipped.add(m.group(1))
            continue
        m = re.search(r"(?i)\bSprint\s+(\d+)\b", subj)
        if m:
            shipped.add(m.group(1))
    if not shipped:
        warnings.append("sprint inventory: no sprint commits found; skipped (shallow clone?)")
        return

    files = sprint_files()
    # Exempt ONLY the exact plain numbers that collapsed into lettered sub-sprints
    # (S41→S41a, S42→S42a). A shipped LETTERED sprint (e.g. S41a) still requires its
    # own SPRINT-41a.md — the base-number exemption must not suppress it.
    missing = sorted(
        (s for s in shipped if s not in files and s not in SPRINT_EXCEPTIONS),
        key=lambda s: (int(re.sub(r"[a-z]", "", s)), s),
    )
    if missing:
        failures.append(
            "Sprints shipped in git history with NO docs/sprints/SPRINT-<n>.md log:\n    "
            + ", ".join(f"S{s}" for s in missing)
            + "\n    (add the log, or add to SPRINT_EXCEPTIONS with rationale)"
        )
    else:
        latest = max((int(re.sub(r"[a-z]", "", s)) for s in shipped), default=0)
        print(f"[ok] sprint inventory: {len(shipped)} sprint(s) referenced in git subjects "
              f"(through S{latest}), all have logs. "
              f"Pre-S20 commits without a sprint reference are not auto-verified.")


def latest_sprint() -> int:
    nums = [int(re.sub(r"[a-z]", "", s)) for s in sprint_files()]
    return max(nums) if nums else 0


def check_freshness() -> None:
    head = latest_sprint()
    if not head:
        return
    anchored = 0
    for md in REPO.glob("**/*.md"):
        if any(part in {"node_modules", ".claude", ".git"} for part in md.parts):
            continue
        try:
            text = md.read_text(encoding="utf-8")
        except Exception:  # noqa: BLE001
            continue
        m = re.search(r"<!--\s*anchor-sprint:\s*(\d+)\s*-->", text)
        if not m:
            continue
        anchored += 1
        anchor = int(m.group(1))
        if anchor < head - ANCHOR_SLACK:
            warnings.append(
                f"freshness: {md.relative_to(REPO)} anchored at S{anchor} "
                f"but HEAD is S{head} (>{ANCHOR_SLACK} behind)"
            )
    if anchored:
        print(f"[ok] freshness: checked {anchored} anchored doc(s) against S{head}")


def main() -> int:
    print("== StatsTid doc consistency check ==")
    check_db_schema()
    check_kb_index()
    check_sprint_inventory()
    check_freshness()

    if warnings:
        print("\nWARNINGS (report-only):")
        for w in warnings:
            print(f"  - {w}")
    if failures:
        print("\nFAILURES:")
        for f in failures:
            print(f"  - {f}")
        print(f"\n{len(failures)} hard check(s) failed.")
        return 1
    print("\nAll hard checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
