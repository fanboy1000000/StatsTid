#!/usr/bin/env python3
"""Generate docs/generated/db-schema.md from docker/postgres/init.sql.

This makes the "generated" guarantee in the schema doc TRUE. The doc had
drifted to a hand-maintained snapshot ("32 tables", last touched S17) while
init.sql had grown to 50+ tables. Rather than hand-patch forever, this script
derives the schema doc mechanically so it can never silently diverge again.

Usage:
    python tools/generate_db_schema.py            # regenerate the doc in place
    python tools/generate_db_schema.py --check    # exit 1 if the doc is stale

Design notes:
  - Deterministic: output depends only on init.sql content (no timestamps),
    so --check is a clean byte comparison and CI diffs are meaningful.
  - Structural, not prose: it emits columns/keys/indexes/constraints, not the
    curated "Purpose:" sentences the old doc carried. A correct structural doc
    beats a curated doc that lies.
  - Best-effort DDL parsing tuned to this repo's init.sql conventions
    (one column per line, `CREATE TABLE IF NOT EXISTS name (`, `-- ` comments,
    separate `CREATE [UNIQUE] INDEX ... ON table`). Not a general PG parser.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
INIT_SQL = REPO_ROOT / "docker" / "postgres" / "init.sql"
SCHEMA_DOC = REPO_ROOT / "docs" / "generated" / "db-schema.md"

# Tokens that terminate the "type" portion of a column definition.
TYPE_STOP = {
    "NOT", "NULL", "PRIMARY", "REFERENCES", "UNIQUE", "DEFAULT",
    "CHECK", "GENERATED", "COLLATE", "CONSTRAINT",
}
# Leading keywords that mark a table-level constraint rather than a column.
CONSTRAINT_LEAD = ("PRIMARY KEY", "FOREIGN KEY", "UNIQUE", "CHECK", "CONSTRAINT", "EXCLUDE")


def strip_sql_comment(line: str) -> str:
    """Remove a trailing `-- comment`, respecting single-quoted strings."""
    out = []
    in_str = False
    i = 0
    while i < len(line):
        ch = line[i]
        if ch == "'":
            in_str = not in_str
        elif ch == "-" and i + 1 < len(line) and line[i + 1] == "-" and not in_str:
            break
        out.append(ch)
        i += 1
    return "".join(out)


def split_top_level(body: str) -> list[str]:
    """Split a CREATE TABLE body on commas that are not inside parentheses."""
    items, depth, cur = [], 0, []
    for ch in body:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        if ch == "," and depth == 0:
            items.append("".join(cur).strip())
            cur = []
        else:
            cur.append(ch)
    if "".join(cur).strip():
        items.append("".join(cur).strip())
    return items


def parse_tables(sql: str) -> list[dict]:
    """Return [{name, columns:[...], constraints:[...]}] in file order."""
    tables = []
    lines = sql.splitlines()
    i = 0
    create_re = re.compile(r"^\s*CREATE TABLE(?:\s+IF NOT EXISTS)?\s+([a-zA-Z_][\w]*)\s*\(", re.I)
    while i < len(lines):
        m = create_re.match(lines[i])
        if not m:
            i += 1
            continue
        name = m.group(1)
        # Accumulate the body across lines until the matching close paren.
        # Count parens on COMMENT-STRIPPED lines: a `--` comment containing an
        # unbalanced paren would otherwise corrupt the depth counter and make
        # this block swallow the following tables.
        first = strip_sql_comment(lines[i])
        depth = first.count("(") - first.count(")")
        body_lines = [first[m.end() - 1:]]  # start at the '('
        i += 1
        while i < len(lines) and depth > 0:
            s = strip_sql_comment(lines[i])
            depth += s.count("(") - s.count(")")
            body_lines.append(s)
            i += 1
        cleaned = "\n".join(body_lines)
        # Drop the leading '(' and everything from the final ')'
        cleaned = cleaned[cleaned.find("(") + 1:]
        cleaned = cleaned[: cleaned.rfind(")")]
        cols, constraints = [], []
        for item in split_top_level(cleaned):
            item = " ".join(item.split())  # collapse whitespace/newlines
            if not item:
                continue
            upper = item.upper()
            if any(upper.startswith(lead) for lead in CONSTRAINT_LEAD):
                constraints.append(item)
                continue
            cols.append(parse_column(item))
        tables.append({"name": name, "columns": cols, "constraints": constraints})
    return tables


def parse_column(item: str) -> dict:
    toks = item.split()
    col = toks[0]
    # Type = tokens until the first constraint keyword.
    type_toks = []
    for t in toks[1:]:
        if t.upper().rstrip("(),") in TYPE_STOP and not type_toks:
            break
        if t.upper() in TYPE_STOP:
            break
        type_toks.append(t)
    coltype = " ".join(type_toks) if type_toks else "?"
    upper = item.upper()
    is_pk = "PRIMARY KEY" in upper
    # A PRIMARY KEY column is implicitly NOT NULL in PostgreSQL.
    nullable = "No" if ("NOT NULL" in upper or is_pk) else "Yes"
    keys = []
    if is_pk:
        keys.append("PK")
    ref = re.search(r"REFERENCES\s+([a-zA-Z_][\w]*)", item, re.I)
    if ref:
        keys.append(f"FK→{ref.group(1)}")
    if re.search(r"\bUNIQUE\b", upper) and "PRIMARY KEY" not in upper:
        keys.append("UNIQUE")
    default = ""
    dm = re.search(r"DEFAULT\s+(.+?)(?:\s+(?:NOT NULL|CHECK\b|REFERENCES\b|UNIQUE\b)|$)", item, re.I)
    if dm:
        default = dm.group(1).strip()
    return {"name": col, "type": coltype, "nullable": nullable,
            "key": ", ".join(keys), "default": default}


def parse_alter_columns(sql: str) -> dict[str, list[dict]]:
    """Columns added after CREATE via `ALTER TABLE ... ADD COLUMN`.

    init.sql bakes most columns into greenfield CREATE blocks but adds some via
    guarded `ALTER TABLE <t> ADD COLUMN IF NOT EXISTS ...` (e.g. events.actor_id,
    approval_periods deadlines, reporting_lines.scheduled_expiry). Without these
    the generated doc would describe an incomplete schema yet still pass --check.
    """
    stripped = "\n".join(strip_sql_comment(l) for l in sql.splitlines())
    result: dict[str, list[dict]] = {}
    for m in re.finditer(r"ALTER TABLE\s+(?:IF EXISTS\s+)?(\w+)\s+(.*?);", stripped, re.I | re.S):
        table, body = m.group(1), m.group(2)
        # One ALTER may carry several `ADD COLUMN` actions.
        for frag in re.split(r"(?i)\bADD COLUMN\b", body)[1:]:
            frag = re.sub(r"(?i)^\s*IF NOT EXISTS\s+", "", frag.strip()).strip()
            frag = " ".join(frag.split()).rstrip(",").strip()
            if frag:
                result.setdefault(table, []).append(parse_column(frag))
    return result


def parse_indexes(sql: str) -> dict[str, list[str]]:
    idx: dict[str, list[str]] = {}
    pat = re.compile(
        r"CREATE\s+(UNIQUE\s+)?INDEX(?:\s+IF NOT EXISTS)?\s+(\w+)\s+ON\s+(\w+)([^;]*);",
        re.I | re.S,
    )
    for m in pat.finditer(sql):
        uniq, idxname, table, rest = m.groups()
        cols = ""
        cm = re.search(r"\(([^;]*?)\)", rest, re.S)
        if cm:
            cols = " ".join(cm.group(1).split())
        where = re.search(r"WHERE\s+(.+?)\s*$", rest.strip(), re.I | re.S)
        label = f"`{idxname}`{' (UNIQUE)' if uniq else ''} on ({cols})"
        if where:
            label += f" WHERE {' '.join(where.group(1).split())}"
        idx.setdefault(table, []).append(label)
    return idx


def md_escape(s: str) -> str:
    return s.replace("|", "\\|")


def render(tables: list[dict], indexes: dict[str, list[str]]) -> str:
    primary = [t for t in tables if not t["name"].endswith("_audit")]
    audit = [t for t in tables if t["name"].endswith("_audit")]
    out = []
    out.append("# StatsTid Database Schema")
    out.append("")
    out.append("> **GENERATED FILE — do not edit by hand.**")
    out.append("> Produced by `tools/generate_db_schema.py` from `docker/postgres/init.sql`.")
    out.append("> Update the schema in `init.sql`, then run `python tools/generate_db_schema.py`.")
    out.append("> CI fails (`tools/check_docs.py`) if this file drifts from init.sql.")
    out.append("")
    out.append(f"**Total: {len(tables)} tables** "
               f"({len(primary)} primary, {len(audit)} audit).")
    out.append("")
    out.append("---")
    out.append("")
    for t in tables:
        out.append(f"## {t['name']}")
        out.append("")
        out.append("| Column | Type | Null | Key | Default |")
        out.append("|--------|------|------|-----|---------|")
        for c in t["columns"]:
            out.append(
                f"| {md_escape(c['name'])} | {md_escape(c['type'])} | {c['nullable']} "
                f"| {md_escape(c['key'])} | {md_escape(c['default'])} |"
            )
        out.append("")
        if t["constraints"]:
            out.append("**Table constraints:**")
            for c in t["constraints"]:
                out.append(f"- {md_escape(c)}")
            out.append("")
        if indexes.get(t["name"]):
            out.append("**Indexes:**")
            for ix in indexes[t["name"]]:
                out.append(f"- {ix}")
            out.append("")
    out.append("---")
    out.append("")
    out.append("## Table Summary")
    out.append("")
    out.append("| # | Table | Audit? |")
    out.append("|---|-------|--------|")
    for n, t in enumerate(tables, 1):
        out.append(f"| {n} | {t['name']} | {'audit' if t['name'].endswith('_audit') else '--'} |")
    out.append("")
    return "\n".join(out) + "\n"


def main() -> int:
    sql = INIT_SQL.read_text(encoding="utf-8")
    tables = parse_tables(sql)
    indexes = parse_indexes(sql)
    # Merge ALTER-added columns into their tables (append if not already present).
    alters = parse_alter_columns(sql)
    for t in tables:
        existing = {c["name"] for c in t["columns"]}
        for col in alters.get(t["name"], []):
            if col["name"] not in existing:
                t["columns"].append(col)
                existing.add(col["name"])
    content = render(tables, indexes)
    check = "--check" in sys.argv
    if check:
        # Normalize newlines on both sides so the comparison is line-ending
        # agnostic (Windows autocrlf checkouts must not spuriously fail CI).
        current = ""
        if SCHEMA_DOC.exists():
            current = SCHEMA_DOC.read_text(encoding="utf-8").replace("\r\n", "\n")
        if current != content:
            print("db-schema.md is STALE vs init.sql.")
            print(f"  init.sql defines {len(tables)} tables; regenerate with:")
            print("  python tools/generate_db_schema.py")
            return 1
        print(f"db-schema.md is in sync ({len(tables)} tables).")
        return 0
    # Always write LF, regardless of platform, so committed output is stable.
    SCHEMA_DOC.write_text(content, encoding="utf-8", newline="\n")
    print(f"Wrote {SCHEMA_DOC.relative_to(REPO_ROOT)} ({len(tables)} tables).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
