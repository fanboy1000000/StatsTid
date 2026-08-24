using StatsTid.Backend.Api.AuditMappers;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.AuditMappers;

/// <summary>
/// S133 / TASK-13308a — AUDIT-PAYLOAD BYTE-FORMAT BEFORE-PIN for the QUAL-027 hoist.
///
/// <para><b>Why this test exists (the auditability invariant).</b> TASK-13308b will MOVE the
/// canonical audit-JSON serializer options out of <c>StatsTid.Backend.Api.AuditMappers</c> into
/// SharedKernel and DELETE the 17 byte-identical private copies that today live one-per-mapper in
/// <c>StatsTid.Infrastructure.AuditMappers</c>. The serialized audit payload is persisted verbatim
/// into <c>audit_log.previous_data</c>/<c>new_data</c> and the ADR-026 audit-projection
/// <c>details</c> JSONB, and is the wire shape audit consumers read. That byte-format must NOT
/// shift across the refactor. This test captures the CURRENT (pre-hoist) output of a representative
/// mapper from BOTH the shared-canonical group and the private-copy group and asserts each equals a
/// hard-coded golden string. Written NOW against pre-hoist code and proven green, the SAME assertions
/// staying green after 13308b is the proof of byte-identity. (A golden written after the move would
/// merely bless the new output — so the timing is the whole point.)</para>
///
/// <para><b>How the golden strings were captured.</b> Each golden below is the literal
/// <c>AuditProjectionRowData.DetailsJson</c> produced by the named mapper's public <c>Map(...)</c>
/// on the fixed deterministic input in the same test — captured once from the pre-hoist tree
/// (2026-08, S133). The inputs use only fixed field values (no clocks, no generated GUIDs), so the
/// serialization is a pure function of input and the output is reproducible on any machine.</para>
///
/// <para><b>Why it survives 13308b's namespace move.</b> The pin snapshots serialized STRINGS only.
/// It does NOT name the <c>AuditMapperJsonOptions</c> type (no <c>using</c> of it, no
/// <c>AuditMapperJsonOptions.X</c> reference) — it exercises the options exclusively through each
/// mapper's public serialization surface (<c>Map</c> -> <c>DetailsJson</c>). So relocating/deleting
/// that type cannot break this pin's COMPILE; only an actual byte-format change can turn it red,
/// which is exactly the signal 13308b needs.</para>
///
/// <para><b>Format dimensions pinned</b> (the ways a serializer-options change could shift bytes):
/// camelCase property naming; property order (anonymous-object declaration order); compact output
/// (no whitespace/indent); <c>WhenWritingNull</c> omission (mid-object and trailing); non-ASCII
/// escaping to UPPERCASE-hex <c>\uXXXX</c> (Danish O-slash / o-slash, section-sign U+00A7);
/// HTML/JS-sensitive escaping (ampersand/less-than/greater-than/plus all escaped to <c>\uXXXX</c>);
/// <c>DateTimeOffset</c> (ISO-8601 with offset); <c>DateOnly</c> (<c>yyyy-MM-dd</c>); <c>Guid</c>
/// (lowercase hyphenated); decimal (no forced trailing zeros: <c>0m</c>-&gt;<c>0</c>, <c>2.5</c>,
/// <c>11.25</c>); integer; and boolean.</para>
///
/// <para>Golden strings are written as regular (non-verbatim) C# literals so a JSON <c>\uXXXX</c>
/// escape appears in source as <c>\\uXXXX</c> — i.e. the six literal characters the serializer
/// emits, NOT a compiler-decoded Unicode char.</para>
/// </summary>
public class AuditPayloadFormatPinTests
{
    // Deterministic context. Only ResolvedTargetOrgId is read by the mappers under test; the
    // clock/GUID-shaped fields are fixed regardless (they never reach DetailsJson).
    private static readonly AuditProjectionContext Ctx = new(
        ActorId: "ADMIN001",
        ActorPrimaryOrgId: "ORG_A",
        CorrelationId: Guid.Empty,
        OccurredAt: DateTimeOffset.UnixEpoch,
        ResolvedTargetOrgId: "ORG_EMP");

    // ── GROUP (b): mappers that use the SHARED Backend canonical options ─────────────────────────

    /// <summary>
    /// <see cref="OrganizationCreatedAuditMapper"/> (shared-canonical group). Pins non-ASCII +
    /// HTML-char escaping (O-slash / ampersand / less-than / greater-than) and mid-object null
    /// omission (parentOrgId).
    /// </summary>
    [Fact]
    public void OrganizationCreated_PayloadFormat_IsByteStable()
    {
        var row = new OrganizationCreatedAuditMapper().Map(new OrganizationCreated
        {
            OrgId = "STY01",
            OrgName = "Ø-styrelsen & <Co>",
            OrgType = "AGENCY",
            ParentOrgId = null, // omitted under WhenWritingNull (mid-object)
            MaterializedPath = "/STY01/",
            AgreementCode = "AC",
            OkVersion = "OK24",
        }, Ctx);

        const string golden =
            "{\"orgId\":\"STY01\",\"orgName\":\"\\u00D8-styrelsen \\u0026 \\u003CCo\\u003E\",\"orgType\":\"AGENCY\",\"materializedPath\":\"/STY01/\",\"agreementCode\":\"AC\",\"okVersion\":\"OK24\"}";
        Assert.Equal(golden, row.DetailsJson);
    }

    /// <summary>
    /// <see cref="UserUpdatedAuditMapper"/> (shared-canonical group). Pins mid-object null omission
    /// (email) and Danish-char escaping (o-slash).
    /// </summary>
    [Fact]
    public void UserUpdated_PayloadFormat_IsByteStable()
    {
        var row = new UserUpdatedAuditMapper().Map(new UserUpdated
        {
            UserId = "USR-01",
            DisplayName = "Søren",
            Email = null, // omitted under WhenWritingNull (mid-object)
            PrimaryOrgId = "STY01",
            AgreementCode = "AC",
        }, Ctx);

        const string golden =
            "{\"userId\":\"USR-01\",\"displayName\":\"S\\u00F8ren\",\"primaryOrgId\":\"STY01\",\"agreementCode\":\"AC\"}";
        Assert.Equal(golden, row.DetailsJson);
    }

    // ── GROUP (a): mappers that carry a byte-identical PRIVATE copy (17 QUAL-027 targets) ─────────

    /// <summary>
    /// <see cref="PayrollExportGeneratedAuditMapper"/> (private-copy group). Pins Guid formatting,
    /// DateTimeOffset ISO-8601-with-offset formatting, integer formatting, and trailing null
    /// omission (periodId).
    /// </summary>
    [Fact]
    public void PayrollExportGenerated_PayloadFormat_IsByteStable()
    {
        var row = new PayrollExportGeneratedAuditMapper().Map(new PayrollExportGenerated
        {
            EmployeeId = "EMP042",
            Year = 2026,
            Month = 8,
            ExportId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ContentHash = "abc123",
            ExportedAt = new DateTimeOffset(2026, 8, 19, 10, 30, 0, TimeSpan.Zero),
            PeriodId = null, // omitted under WhenWritingNull (trailing)
        }, Ctx);

        const string golden =
            "{\"action\":\"PAYROLL_EXPORTED\",\"employeeId\":\"EMP042\",\"year\":2026,\"month\":8,\"exportId\":\"11111111-1111-1111-1111-111111111111\",\"contentHash\":\"abc123\",\"exportedAt\":\"2026-08-19T10:30:00+00:00\"}";
        Assert.Equal(golden, row.DetailsJson);
    }

    /// <summary>
    /// <see cref="EmployeeEmploymentEndDateSetAuditMapper"/> (private-copy group). Pins DateOnly
    /// formatting (yyyy-MM-dd), boolean formatting, long formatting, and mid-object null omission
    /// (oldEndDate).
    /// </summary>
    [Fact]
    public void EmployeeEmploymentEndDateSet_PayloadFormat_IsByteStable()
    {
        var row = new EmployeeEmploymentEndDateSetAuditMapper().Map(new EmployeeEmploymentEndDateSet
        {
            EmployeeId = "EMP042",
            OldEndDate = null, // omitted under WhenWritingNull (mid-object)
            NewEndDate = new DateOnly(2026, 7, 31),
            OldIsActive = true,
            NewIsActive = true,
            VersionBefore = 4,
            VersionAfter = 5,
        }, Ctx);

        const string golden =
            "{\"kind\":\"EmployeeEmploymentEndDateSet\",\"employeeId\":\"EMP042\",\"newEndDate\":\"2026-07-31\",\"oldIsActive\":true,\"newIsActive\":true,\"versionBefore\":4,\"versionAfter\":5}";
        Assert.Equal(golden, row.DetailsJson);
    }

    /// <summary>
    /// <see cref="TerminationSettledAuditMapper"/> (private-copy group). Pins decimal formatting
    /// (0m-&gt;0, 2.5, 11.25 — no forced trailing zeros), snapshot-derived fields, and a
    /// section-sign/plus escaping stress value in the paragraph field.
    /// </summary>
    [Fact]
    public void TerminationSettled_PayloadFormat_IsByteStable()
    {
        var row = new TerminationSettledAuditMapper().Map(new TerminationSettled
        {
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            Sequence = 1,
            PayoutDays = 11.25m,
            ModregningDays = 0m,
            UnearnedAdvanceDays = 0m,
            Snapshot = new VacationSettlementSnapshot { CarryoverIn = 2.5m, OkVersion = "OK24" },
        }, Ctx);

        const string golden =
            "{\"kind\":\"TerminationSettled\",\"paragraph\":\"\\u00A726\\u002B\\u00A77\",\"employeeId\":\"EMP042\",\"entitlementType\":\"VACATION\",\"entitlementYear\":2025,\"sequence\":1,\"payoutDays\":11.25,\"modregningDays\":0,\"unearnedAdvanceDays\":0,\"carryoverIn\":2.5,\"okVersion\":\"OK24\"}";
        Assert.Equal(golden, row.DetailsJson);
    }
}
