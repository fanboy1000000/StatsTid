# ADR-008: Materialized Path for Organizational Hierarchy

| Field | Value |
|-------|-------|
| **Status** | approved |
| **Sprint** | S8 |
| **Domains** | Infrastructure, Security |
| **Tags** | organization, hierarchy, materialized-path, postgresql |

## Context
The Danish state requires a multi-org hierarchy: Ministry → Styrelse → Afdeling → Team. Role scopes must efficiently resolve whether a user's org covers a target org (subtree queries). Three approaches were considered: adjacency list only, closure table, materialized path.

## Decision
Use **adjacency list + materialized path** hybrid. Each organization has a `parent_org_id` (adjacency) and a `materialized_path` column (e.g., `/MIN01/STY02/AFD01/`). Subtree queries use `LIKE '/MIN01/%'` with `text_pattern_ops` index.

## Rationale
- The Danish state hierarchy is shallow (3-4 levels) and rarely restructures
- Materialized path enables single-query subtree resolution for scope authorization
- Closure table adds unnecessary complexity for a shallow, stable tree
- `text_pattern_ops` index makes prefix matching efficient in PostgreSQL

## Consequences
- Path must be rebuilt if an org moves in the hierarchy (rare)
- Insertions must compute path from parent
- Scope resolution is O(1) via string prefix matching

## Agent Guidance
- When checking if a scope covers a target org, use `targetPath.StartsWith(scopePath)` (see `RoleScope.CoversOrg`)
- Paths always start and end with `/` (e.g., `/MIN01/STY02/`)
- Index: `CREATE INDEX ... USING btree (materialized_path text_pattern_ops)`
