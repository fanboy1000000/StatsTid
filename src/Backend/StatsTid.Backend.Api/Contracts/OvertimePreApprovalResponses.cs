using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S116 / TASK-11600 + TASK-11601 (Fork B retrofit Pass 3, PAT-010/PAT-012) — named response
// records for the overtime pre-approval family (OvertimeEndpoints). Each retrofit record is an
// EXACT shape-copy of the anonymous object its handler previously returned: same member NAMES,
// same ORDER, same nullability — serialized camelCase via the .NET 8 minimal-API
// JsonSerializerDefaults.Web default, NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON.
//
// THE SIBLINGS STAY SEPARATE RECORDS (S116 Entropy-Scan pin): approve (4f, +approvedBy) vs reject
// (3f); create-201 (7f) vs the per-employee list element (10f) vs the S116 admin-list element
// (11f, +non-null employeeName) — an optional shared member would ADD a null-valued key to an
// existing wire (forbidden).
//
// Enum authority (OQ-3, owner-ruled S116): overtime status = the overtime_pre_approvals status
// CHECK (init.sql:1858 — PENDING / APPROVED / REJECTED).

/// <summary>One element of the <c>GET /api/overtime/{employeeId}/pre-approvals</c> BARE ARRAY
/// (declared <c>.Produces&lt;IEnumerable&lt;OvertimePreApprovalListItem&gt;&gt;</c>) — the
/// 10-field per-employee pre-approval row. <paramref name="ApprovedBy"/>/
/// <paramref name="ApprovedAt"/> are null while PENDING; <paramref name="Reason"/> is
/// optional.</summary>
public sealed record OvertimePreApprovalListItem(
    Guid Id,
    string EmployeeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal MaxHours,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    // Authority: overtime_pre_approvals status CHECK, init.sql:1858.
    [property: AllowedValues("PENDING", "APPROVED", "REJECTED")]
    string Status,
    string? Reason,
    DateTime CreatedAt);

/// <summary>The <c>POST /api/overtime/pre-approval</c> 201 body — the 7-field creation receipt
/// (a TRUE 201: <c>Results.Created</c>). A SEPARATE record from the 10-field list element — the
/// created row has no approvedBy/approvedAt/createdAt on the wire.</summary>
public sealed record OvertimePreApprovalCreatedResponse(
    Guid Id,
    string EmployeeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal MaxHours,
    // Authority: overtime_pre_approvals status CHECK, init.sql:1858.
    [property: AllowedValues("PENDING", "APPROVED", "REJECTED")]
    string Status,
    string? Reason);

/// <summary>The <c>PUT /api/overtime/pre-approval/{id}/approve</c> 200 body.
/// <paramref name="ApprovedBy"/> echoes <c>actor.ActorId</c> (nullable) and
/// <paramref name="Reason"/> the optional request reason. A SEPARATE sibling from
/// <see cref="OvertimePreApprovalRejectResponse"/> — sharing would add <c>approvedBy: null</c>
/// to the reject wire.</summary>
public sealed record OvertimePreApprovalApproveResponse(
    Guid Id,
    // Authority: overtime_pre_approvals status CHECK, init.sql:1858.
    [property: AllowedValues("PENDING", "APPROVED", "REJECTED")]
    string Status,
    string? ApprovedBy,
    string? Reason);

/// <summary>The <c>PUT /api/overtime/pre-approval/{id}/reject</c> 200 body — the 3-field sibling
/// (NO approvedBy).</summary>
public sealed record OvertimePreApprovalRejectResponse(
    Guid Id,
    // Authority: overtime_pre_approvals status CHECK, init.sql:1858.
    [property: AllowedValues("PENDING", "APPROVED", "REJECTED")]
    string Status,
    string? Reason);

/// <summary>S116 / TASK-11601 — one element of the NEW <c>GET /api/overtime/pre-approvals</c>
/// scope-bounded admin list BARE ARRAY (typed from birth — never entered the grandfather
/// manifest). The 10-field per-employee core PLUS the NON-NULL <paramref name="EmployeeName"/>
/// (from the <c>users</c> admission join's <c>display_name</c>; the join's
/// <c>is_active = TRUE</c> predicate guarantees a live users row). A SEPARATE record from
/// <see cref="OvertimePreApprovalListItem"/> — an optional shared member would add
/// <c>employeeName: null</c> to the existing per-employee wire.</summary>
public sealed record OvertimePreApprovalAdminListItem(
    Guid Id,
    string EmployeeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal MaxHours,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    // Authority: overtime_pre_approvals status CHECK, init.sql:1858.
    [property: AllowedValues("PENDING", "APPROVED", "REJECTED")]
    string Status,
    string? Reason,
    DateTime CreatedAt,
    string EmployeeName);
