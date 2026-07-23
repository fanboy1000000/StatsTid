using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S120 / TASK-12000 (Fork B retrofit Pass 7, PAT-010/PAT-012) — named response records for the
// compliance family (ComplianceEndpoints). EXACT shape-copies, camelCase Web defaults, NO
// [JsonPropertyName]. BYTE-IDENTICAL wire JSON on the 200 paths.
//
// GET /api/compliance/{employeeId}/period declares `.Produces<ComplianceCheckResult>` on the
// NAMED SharedKernel model the handler already serializes verbatim (the PAT-012 named-model
// rule — no record minted). Its enum members (violationType = ComplianceViolationType,
// severity = ComplianceSeverity) carry their authority IN CODE as the CLR enums themselves
// (SharedKernel/Models/ComplianceCheckResult.cs — 6 and 2 members respectively); Swashbuckle
// emits those CLR sets natively, so no [AllowedValues] re-declaration is needed or possible
// here. The S120 ruling-#3 null→502 guard makes the declared 200 structurally non-null.

/// <summary>One row of the GET /api/compliance/{employeeId}/compensatory-rest BARE ARRAY
/// (declared <c>.Produces&lt;IEnumerable&lt;CompensatoryRestItem&gt;&gt;</c>) — the 7-member
/// projection of <c>CompensatoryRestEntry</c>. <paramref name="CompensatoryDate"/> is null
/// while no compensatory day has been scheduled.</summary>
public sealed record CompensatoryRestItem(
    Guid Id,
    string EmployeeId,
    DateOnly SourceDate,
    DateOnly? CompensatoryDate,
    decimal Hours,
    // Authority: the compensatory_rest status CHECK, docker/postgres/init.sql:1337
    // (PENDING / GRANTED / EXPIRED).
    [property: AllowedValues("PENDING", "GRANTED", "EXPIRED")]
    string Status,
    DateTime CreatedAt);
