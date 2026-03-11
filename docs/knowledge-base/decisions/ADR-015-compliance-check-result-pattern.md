# ADR-015: ComplianceCheckResult as Separate Return Type

| Field | Value |
|-------|-------|
| **ID** | ADR-015 |
| **Status** | approved |
| **Sprint** | S16 |
| **Domains** | Rule Engine, SharedKernel |
| **Tags** | compliance, rule-engine, return-type, eu-working-time-directive |

## Context

PAT-006 established that all rule endpoints should return CalculationResult-compatible responses. Sprint 16 introduces a compliance checking rule (RestPeriodRule) that validates EU Working Time Directive constraints. Its output semantics differ fundamentally from calculation rules:

- **CalculationResult** produces wage lines (hours × rate → SLS export). It answers "what should be paid?"
- **ComplianceCheckResult** produces violation/warning lists with severity levels. It answers "is this legal?"

Forcing compliance results into CalculationResult would require misusing fields (e.g., encoding violation types as "wage types", severity as "rates") and would break the semantic contract that downstream consumers (payroll export, SLS formatter) depend on.

## Decision

ComplianceCheckResult is a separate model in SharedKernel, not a CalculationResult variant:

```csharp
public sealed class ComplianceCheckResult
{
    public required string RuleId { get; init; }
    public required string EmployeeId { get; init; }
    public required bool Success { get; init; }
    public required IReadOnlyList<ComplianceViolation> Violations { get; init; }
    public required IReadOnlyList<ComplianceViolation> Warnings { get; init; }
}
```

The rule engine exposes compliance via a separate endpoint (`POST /api/rules/check-compliance`) rather than mixing it into existing calculation endpoints.

## Rationale

1. **Semantic clarity**: Compliance results are not payroll calculations. Mixing them violates the principle of least surprise.
2. **Consumer safety**: Payroll export pipeline processes CalculationResult → PayrollExportLine → SLS. ComplianceCheckResult must never enter this pipeline.
3. **Independent lifecycle**: Compliance checks can evolve (new violation types, configurable thresholds) without affecting the stable CalculationResult contract.
4. **PAT-006 scope**: PAT-006 was designed for rules that produce *payable outputs*. Compliance is a *validation concern*, not a *calculation concern*.

## Consequences

- Two return type families exist in the rule engine: CalculationResult (payroll) and ComplianceCheckResult (compliance).
- Future validation-style rules (e.g., data quality checks) should use ComplianceCheckResult, not CalculationResult.
- PAT-006 remains valid for its original scope — all *calculation* endpoints still return CalculationResult.
- Endpoint naming convention: `/api/rules/check-*` for compliance, `/api/rules/calculate-*` or `/api/rules/evaluate-*` for calculations.
