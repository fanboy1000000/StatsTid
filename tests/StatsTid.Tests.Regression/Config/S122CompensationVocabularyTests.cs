using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S122 / TASK-12201 — the compensation-vocabulary authority pins for the P6 gap +
/// the S17 default-trap eradication. Four distinct guarantees, all against the REAL
/// <c>docker/postgres/init.sql</c> schema applied via
/// <see cref="StatsTidWebApplicationFactory.ApplyFullSchemaAsync"/> (the source of truth —
/// immune to fixture-name drift; Step-0b Reviewer):
///
/// <list type="bullet">
///   <item><description><b>CHECK-violation RED pins (both columns):</b> an out-of-set INSERT
///     into <c>agreement_configs.default_compensation_model</c> ('PAYOUT') and
///     <c>overtime_balances.compensation_model</c> ('XYZ') each raise a Postgres CHECK
///     violation (23514) NAMING the canonical constraint. Proves the DB CHECK — the authority
///     the S120 PAT-012 refusal was waiting for — actually fences the closed vocabulary.</description></item>
///   <item><description><b>Repo-level auto-create pin (the S17-trap flip):</b>
///     <see cref="OvertimeBalanceRepository.AdjustAccumulatedAsync"/> — the zero-caller
///     INSERT…ON CONFLICT path that omits <c>compensation_model</c> — stamps the CORRECTED DB
///     DEFAULT ('AFSPADSERING', flipped from the documented-authority-INVERTING 'UDBETALING')
///     on the row it creates. Driven DIRECTLY at the repo (no endpoint reaches this path).</description></item>
///   <item><description><b>Field-loss override preserve pin (service-level, the WHOLE repair):</b>
///     <see cref="PositionOverrideConfigs.ApplyOverride"/> on a base config whose compensation
///     model DIFFERS from the CLR default preserves the base's model AND the non-compensation
///     overtime-governance fields — the field loss was ALL FOUR overtime-governance fields, so
///     the pin covers the whole repair, not just the model (Step-0b Reviewer). The CLR flip
///     alone would MASK this on an AFSPADSERING base — hence the divergent (UDBETALING) base.</description></item>
/// </list>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test; every identifier is
/// <c>S122*</c>/<c>s122*</c> with okVersion <c>OKS122</c> — DISJOINT from the boot seeders
/// (AC/HK/PROSA × OK24/OK26), from <c>S118AgreementConfigSpecRuntimeTests</c> (<c>S118AGC_*</c>/
/// <c>OKS118</c>), from <c>S120OvertimeSpecRuntimeTests</c> (<c>S120OVT1</c>/<c>s120o_*</c>), from
/// <c>AgreementConfigAtomicTests</c> (<c>FR_AGR_*</c>), and from
/// <c>AgreementConfigConcurrencyTests</c> (<c>CON_AGR_*</c>). The clone field-loss pin lives in
/// the sibling <c>Contracts.S122CompensationCloneContractTests</c> (it needs the HTTP admin
/// clone endpoint + GlobalAdmin auth).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S122CompensationVocabularyTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (2) CHECK-violation RED pins on BOTH columns — real init.sql schema.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>An out-of-set value ('PAYOUT' — a member of the OTHER vocabulary, the classic
    /// cross-vocab confusion) into <c>agreement_configs.default_compensation_model</c> is rejected
    /// by the named CHECK (23514). Every other column is in-set/defaulted, so the constraint is
    /// the ONLY reason the INSERT fails.</summary>
    [Fact]
    public async Task AgreementConfigs_DefaultCompensationModel_OutOfSetInsert_RaisesNamedCheckViolation()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO agreement_configs
                    (agreement_code, ok_version, weekly_norm_hours, max_flex_balance,
                     flex_carryover_max, has_overtime, has_merarbejde, default_compensation_model)
                VALUES ('S122CHK_AGC', 'OKS122', 37.0, 100, 50, true, false, 'PAYOUT')
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        });

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState); // 23514
        Assert.Equal("agreement_configs_default_compensation_model_check", ex.ConstraintName);
    }

    /// <summary>An out-of-set value ('XYZ') into <c>overtime_balances.compensation_model</c> is
    /// rejected by the named CHECK (23514) — the second column carrying the same closed
    /// vocabulary.</summary>
    [Fact]
    public async Task OvertimeBalances_CompensationModel_OutOfSetInsert_RaisesNamedCheckViolation()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO overtime_balances
                    (employee_id, agreement_code, period_year, compensation_model)
                VALUES ('s122chk_emp', 'AC', 2026, 'XYZ')
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        });

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState); // 23514
        Assert.Equal("overtime_balances_compensation_model_check", ex.ConstraintName);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (4) Repo-level auto-create pin — AdjustAccumulatedAsync stamps the corrected default.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The auto-create path (<c>AdjustAccumulatedAsync</c>'s INSERT branch omits
    /// <c>compensation_model</c>) stamps the corrected DB DEFAULT ('AFSPADSERING'). Called
    /// DIRECTLY at the repo — no endpoint drives this path today (the LATENT, defence-in-depth
    /// site), so the pin is repo-level by design. Pre-flip this created a 'UDBETALING' row that
    /// INVERTED the documented per-agreement authority.</summary>
    [Fact]
    public async Task AdjustAccumulatedAsync_AutoCreatedRow_CarriesAfspadseringDbDefault()
    {
        var factory = new DbConnectionFactory(_harness.ConnectionString);
        var repo = new OvertimeBalanceRepository(factory);
        const string employeeId = "s122repo_emp";
        const int year = 2026;

        // No row exists — the INSERT branch fires and omits compensation_model, so the corrected
        // DB DEFAULT stamps the created row.
        var accumulated = await repo.AdjustAccumulatedAsync(employeeId, year, "AC", 6.5m);
        Assert.Equal(6.5m, accumulated);

        var created = await repo.GetByEmployeeAndYearAsync(employeeId, year);
        Assert.NotNull(created);
        Assert.Equal("AFSPADSERING", created!.CompensationModel); // the corrected default, not the old UDBETALING
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  (3b) Field-loss override preserve pin — service-level, the WHOLE governance block.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A base config whose model DIFFERS from the CLR default (UDBETALING vs the
    /// AFSPADSERING CLR default) survives a position override intact — model AND the
    /// non-compensation governance fields (<c>MaxOvertimeHoursPerPeriod</c>,
    /// <c>OvertimeRequiresPreApproval</c>). Pre-fix <c>ApplyOverride</c> dropped all four
    /// overtime-governance fields, so AC position keys resolved the CLR-default model instead of
    /// the base's; the CLR flip alone masked only the AFSPADSERING-base case, so this pin uses a
    /// DIVERGENT base and asserts the WHOLE repair.</summary>
    [Fact]
    public void ApplyOverride_PreservesBaseCompensationModel_AndNonCompensationGovernanceFields()
    {
        // Guard: the base's UDBETALING genuinely differs from the CLR default (AFSPADSERING) — the
        // divergence that makes this pin non-vacuous (and survives a future CLR-default change).
        Assert.Equal("AFSPADSERING", ClrDefaultModel());

        var baseConfig = BuildBaseConfig(
            defaultCompensationModel: "UDBETALING",
            maxOvertimeHoursPerPeriod: 15m,
            overtimeRequiresPreApproval: true);
        Assert.NotEqual(ClrDefaultModel(), baseConfig.DefaultCompensationModel);

        var positionOverride = new PositionOverrideConfigs.PositionConfigOverride
        {
            MaxFlexBalance = 200m,
            NormPeriodWeeks = 4,
        };

        var resolved = PositionOverrideConfigs.ApplyOverride(baseConfig, positionOverride);

        // The override's own fields applied …
        Assert.Equal(200m, resolved.MaxFlexBalance);
        Assert.Equal(4, resolved.NormPeriodWeeks);
        // … AND the WHOLE overtime-governance block survived (the field loss was all four fields):
        Assert.Equal("UDBETALING", resolved.DefaultCompensationModel);   // the compensation-model pin
        Assert.Equal(15m, resolved.MaxOvertimeHoursPerPeriod);           // a NON-compensation governance field
        Assert.True(resolved.OvertimeRequiresPreApproval);               // a second NON-compensation governance field
    }

    // ─────────────────────────────── builders ───────────────────────────────

    /// <summary>The CLR default of <see cref="AgreementRuleConfig.DefaultCompensationModel"/> —
    /// read off a minimally-constructed config that does NOT set the member.</summary>
    private static string ClrDefaultModel() => new AgreementRuleConfig
    {
        AgreementCode = "AC",
        OkVersion = "OK24",
        WeeklyNormHours = 37m,
        HasOvertime = true,
        HasMerarbejde = false,
        MaxFlexBalance = 100m,
        FlexCarryoverMax = 50m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
    }.DefaultCompensationModel;

    private static AgreementRuleConfig BuildBaseConfig(
        string defaultCompensationModel, decimal maxOvertimeHoursPerPeriod, bool overtimeRequiresPreApproval) => new()
    {
        AgreementCode = "AC",
        OkVersion = "OK24",
        WeeklyNormHours = 37m,
        HasOvertime = true,
        HasMerarbejde = false,
        MaxFlexBalance = 100m,
        FlexCarryoverMax = 50m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        NormPeriodWeeks = 1,
        DefaultCompensationModel = defaultCompensationModel,
        MaxOvertimeHoursPerPeriod = maxOvertimeHoursPerPeriod,
        OvertimeRequiresPreApproval = overtimeRequiresPreApproval,
    };
}
