using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S122 / TASK-12201 — the HTTP-level clone field-loss regression pin (the genuinely-LIVE
/// inversion the census exposed: the CLONE endpoint dropped the four overtime-governance fields,
/// so a cloned config PERSISTED the CLR-default compensation model instead of the source's).
///
/// <para>The clone endpoint (<c>POST /api/agreement-configs/{configId}/clone</c>, GlobalAdmin) is
/// driven for real against a source config whose <c>default_compensation_model</c> DIFFERS from
/// the CLR default (UDBETALING vs the AFSPADSERING CLR default — the case the CLR flip alone
/// CANNOT mask). The clone must PRESERVE the source's model AND at least one non-compensation
/// governance field (<c>max_overtime_hours_per_period</c>, <c>overtime_requires_pre_approval</c>) —
/// the field loss was all four overtime-governance fields, so the pin covers the whole repair
/// (Step-0b Reviewer).</para>
///
/// <para><b>Why the persisted row, not the response body:</b> <c>AgreementConfigResponse</c> does
/// not expose the compensation-governance members (the create-DTO gap NOTED as orthogonal in the
/// sprint plan), so the four fields are asserted on the persisted clone row read back by
/// <c>cloned_from_id</c>. The source's UDBETALING/25/true is seeded through the REAL repo
/// <c>CreateAsync</c> (whose InsertSql writes <c>default_compensation_model</c>) — the create DTO
/// cannot express these fields, mirroring the S120 INPUT-seed convention.</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test; codes
/// <c>S122CLN_SRC</c>/<c>S122CLN_DST</c>, okVersion <c>OKS122</c>, actor <c>s122c_gadmin</c>, org
/// <c>S122CM</c> — DISJOINT from the boot seeders (AC/HK/PROSA × OK24/OK26), from
/// <c>S118AgreementConfigSpecRuntimeTests</c> (<c>S118AGC_*</c>/<c>OKS118</c>), from
/// <c>AgreementConfigAtomicTests</c> (<c>FR_AGR_*</c>), and from the sibling
/// <c>Config.S122CompensationVocabularyTests</c> (<c>S122CHK_*</c>/<c>s122repo_*</c>).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S122CompensationCloneContractTests : IAsyncLifetime
{
    private const string ActorId = "s122c_gadmin";
    private const string JwtOrg = "S122CM"; // JWT claim only — config-family rows are GLOBAL (no org FK)
    private const string OkVersion = "OKS122";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (AC/HK/PROSA baseline configs)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>The clone PRESERVES the source's compensation model AND the non-compensation
    /// overtime-governance fields on the PERSISTED clone row. Pre-fix the clone dropped all four,
    /// so the clone landed the CLR default (AFSPADSERING) with zeroed governance.</summary>
    [Fact]
    public async Task Clone_PreservesSourceCompensationModel_AndNonCompensationGovernanceFields()
    {
        // ── Arrange: a source config whose model DIFFERS from the CLR default, seeded through the
        //    REAL repo insert (the create DTO cannot express these four fields). ──
        var factory = new DbConnectionFactory(_harness.ConnectionString);
        var repo = new AgreementConfigRepository(factory);
        var sourceId = await repo.CreateAsync(NewSourceEntity(
            defaultCompensationModel: "UDBETALING",
            maxOvertimeHoursPerPeriod: 25m,
            overtimeRequiresPreApproval: true));

        // ── Act: the real HTTP admin clone. ──
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, ActorId, JwtOrg);
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post,
            $"/api/agreement-configs/{sourceId}/clone?agreementCode=S122CLN_DST&okVersion={OkVersion}"));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Clone of {sourceId} returned {(int)response.StatusCode}: {body}");

        var root = JsonDocument.Parse(body).RootElement;
        var cloneId = root.GetProperty("configId").GetGuid();
        Assert.Equal(sourceId, root.GetProperty("clonedFromId").GetGuid()); // lineage sanity
        Assert.NotEqual(sourceId, cloneId);

        // ── Assert: the PERSISTED clone row carries the source's governance block verbatim. ──
        var (model, maxHours, requiresPreApproval) = await ReadCloneGovernanceAsync(sourceId);
        Assert.Equal("UDBETALING", model);          // the compensation-model pin (NOT the CLR default)
        Assert.Equal(25m, maxHours);                // a NON-compensation governance field
        Assert.True(requiresPreApproval);           // a second NON-compensation governance field
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task<(string Model, decimal MaxHours, bool RequiresPreApproval)> ReadCloneGovernanceAsync(Guid sourceId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT default_compensation_model, max_overtime_hours_per_period, overtime_requires_pre_approval
            FROM agreement_configs
            WHERE cloned_from_id = @sourceId
            """, conn);
        cmd.Parameters.AddWithValue("sourceId", sourceId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "expected exactly one cloned row");
        var result = (reader.GetString(0), reader.GetDecimal(1), reader.GetBoolean(2));
        Assert.False(await reader.ReadAsync(), "expected exactly one cloned row");
        return result;
    }

    private static AgreementConfigEntity NewSourceEntity(
        string defaultCompensationModel, decimal maxOvertimeHoursPerPeriod, bool overtimeRequiresPreApproval) => new()
    {
        ConfigId = Guid.Empty, // repo assigns
        AgreementCode = "S122CLN_SRC",
        OkVersion = OkVersion,
        Status = AgreementConfigStatus.DRAFT,
        WeeklyNormHours = 37m,
        NormPeriodWeeks = 1,
        NormModel = NormModel.WEEKLY_HOURS,
        AnnualNormHours = 1924m,
        MaxFlexBalance = 100m,
        FlexCarryoverMax = 50m,
        HasOvertime = true,
        HasMerarbejde = false,
        OvertimeThreshold50 = 37m,
        OvertimeThreshold100 = 40m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        EveningStart = 17,
        EveningEnd = 23,
        NightStart = 23,
        NightEnd = 6,
        EveningRate = 1.25m,
        NightRate = 1.5m,
        WeekendSaturdayRate = 1.5m,
        WeekendSundayRate = 2m,
        HolidayRate = 2m,
        OnCallDutyEnabled = false,
        OnCallDutyRate = 0.33m,
        CallInWorkEnabled = false,
        CallInMinimumHours = 3m,
        CallInRate = 1m,
        TravelTimeEnabled = false,
        WorkingTravelRate = 1m,
        NonWorkingTravelRate = 0.5m,
        MaxDailyHours = 13m,
        MinimumRestHours = 11m,
        RestPeriodDerogationAllowed = false,
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
        DefaultCompensationModel = defaultCompensationModel,
        EmployeeCompensationChoice = false,
        MaxOvertimeHoursPerPeriod = maxOvertimeHoursPerPeriod,
        OvertimeRequiresPreApproval = overtimeRequiresPreApproval,
        CreatedBy = "tester",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Description = "s122-clone-source",
    };
}
