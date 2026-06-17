using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S80 / TASK-8003 (ADR-033 slice 2; SPRINT-80 R7/R11/R12) — Docker-gated D-tests for the §15
/// stk.2/§17 SPECIAL_HOLIDAY godtgørelse leg of the
/// <see cref="StatsTid.Integrations.Payroll.Services.SettlementExportEmitter"/>: the
/// <c>SaerligeFeriedagePaidOut</c> consumer stages ONE money-free <c>ORIGINAL</c> line
/// (<c>SLS_TBD_S15S17</c>, <c>hours = PayoutDays</c>, the new <c>GODTGOERELSE_S15S17</c> bucket),
/// EXACTLY ONCE (the <c>(source_event_id, bucket)</c> checkpoint), replay-deterministically (off
/// the immutable snapshot), and never delivered (the <c>SLS_TBD_*</c> outbound guard).
///
/// <para>
/// The godtgørelse path mirrors the §24 <c>VacationAutoPaidOut</c> structure (the template): the
/// event carries its own immutable snapshot, and the line keys off it at
/// <c>asOf = Snapshot.SettlementBoundaryDate</c> (the 30-Apr-Y+2 afholdelsesperiode end the close
/// captured). Harness + FAIL-002 protocol: see <see cref="SettlementEmitterFixture"/>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class Settlement15S17GodtgoerelseEmitterTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private string Cs => _harness.ConnectionString;
    private DbConnectionFactory Factory => new(Cs);

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Cs);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 1 — exactly-once: one event ⇒ exactly ONE staged ORIGINAL line at the new bucket,
    //              re-poll ⇒ no duplicate (the checkpoint dedup).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A <c>SaerligeFeriedagePaidOut</c> stages EXACTLY ONE <c>GODTGOERELSE_S15S17</c>
    /// <c>line_kind = 'ORIGINAL'</c> line carrying the <c>SLS_TBD_S15S17</c> lønart + the
    /// <c>PayoutDays</c> day-count + a PROCESSED inbox checkpoint. A TRUE re-poll of the same,
    /// still-selectable event (the checkpoint dropped, the line persists) re-runs the full claim
    /// path through the line-UNIQUE BenignRedelivery branch — STILL exactly one line, inbox
    /// PROCESSED again, the line byte-identical (exactly-once via the <c>(source_event_id, bucket)</c>
    /// checkpoint, NOT merely the poll filter).</summary>
    [Fact]
    public async Task ProcessOnce_StagesExactlyOneGodtgoerelseLine_AndInboxProcessed_DuplicateClaimIsBenignRedelivery()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s80_");
        var eventId = await SettlementEmitterFixture.WriteSaerligeFeriedagePaidOutEventAsync(
            Factory, emp, payoutDays: 5m, agreementCode: "AC", okVersion: "OK24");

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "PROCESSED");

        Assert.Equal("PROCESSED", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.GodtgoerelseBucket));
        Assert.Equal(1L, await SettlementEmitterFixture.LineCountAsync(
            Cs, emp, year: 2022, bucket: SettlementEmitterFixture.GodtgoerelseBucket,
            entitlementType: SettlementEmitterFixture.SpecialHolidayType));

        var firstLine = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, year: 2022, bucket: SettlementEmitterFixture.GodtgoerelseBucket,
            entitlementType: SettlementEmitterFixture.SpecialHolidayType);
        Assert.NotNull(firstLine);
        Assert.Equal("ORIGINAL", firstLine!.Value.LineKind);
        Assert.Equal(SettlementEmitterFixture.GodtgoerelseSentinelWageType, firstLine.Value.WageType);
        Assert.Equal(5m, firstLine.Value.Hours);

        // TRUE duplicate claim: drop ONLY the inbox checkpoint (the line stays). The event has no
        // terminal inbox row ⇒ the poll RE-SELECTS it; the second drain hits the line-UNIQUE conflict,
        // sees the SAME source_event_id ⇒ BenignRedelivery ⇒ re-promote PROCESSED. No double-stage.
        await SettlementEmitterFixture.DeleteInboxRowAsync(Cs, eventId);

        var emitter2 = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter2,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "PROCESSED");

        Assert.Equal(1L, await SettlementEmitterFixture.LineCountAsync(
            Cs, emp, year: 2022, bucket: SettlementEmitterFixture.GodtgoerelseBucket,
            entitlementType: SettlementEmitterFixture.SpecialHolidayType));
        Assert.Equal("PROCESSED", await SettlementEmitterFixture.InboxStatusForBucketAsync(
            Cs, eventId, SettlementEmitterFixture.GodtgoerelseBucket));
        var secondLine = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, year: 2022, bucket: SettlementEmitterFixture.GodtgoerelseBucket,
            entitlementType: SettlementEmitterFixture.SpecialHolidayType);
        Assert.NotNull(secondLine);
        Assert.Equal(firstLine.Value, secondLine!.Value); // value equality ⇒ all fields byte-identical
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 2 — money-free + the DISTINCT mapping (R7/R11/R12): hours = PayoutDays, amount = 0,
    //              the lønart is SLS_TBD_S15S17 — NOT the consumption SLS_0570.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The staged godtgørelse line is money-free (<c>hours = PayoutDays</c>, <c>amount = 0</c>)
    /// and maps to the SETTLEMENT sentinel <c>SLS_TBD_S15S17</c> — NOT the SPECIAL_HOLIDAY CONSUMPTION
    /// lønart <c>SLS_0570</c>. A discriminating assertion that the two mappings are distinct (the line
    /// is the unused-day cash payout, not a taken-day posting; SLS owns the 2½% §17 rate, ≠ §10's
    /// 2,02%, D12).</summary>
    [Fact]
    public async Task StagedGodtgoerelseLine_IsMoneyFree_AndUsesSettlementSentinel_NotConsumptionLonart()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s80_");
        var eventId = await SettlementEmitterFixture.WriteSaerligeFeriedagePaidOutEventAsync(
            Factory, emp, payoutDays: 3.5m, agreementCode: "AC", okVersion: "OK24");

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.LineCountAsync(
                Cs, emp, year: 2022, bucket: SettlementEmitterFixture.GodtgoerelseBucket,
                entitlementType: SettlementEmitterFixture.SpecialHolidayType) == 1);

        var line = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, year: 2022, bucket: SettlementEmitterFixture.GodtgoerelseBucket,
            entitlementType: SettlementEmitterFixture.SpecialHolidayType);
        Assert.NotNull(line);
        Assert.Equal(3.5m, line!.Value.Hours);   // hours = PayoutDays (day-count, not kroner)
        Assert.Equal(0m, line.Value.Amount);      // money-free (CHECK amount = 0)
        Assert.Equal(SettlementEmitterFixture.GodtgoerelseSentinelWageType, line.Value.WageType); // SLS_TBD_S15S17
        Assert.NotEqual(SettlementEmitterFixture.SpecialHolidayConsumptionWageType, line.Value.WageType); // ≠ SLS_0570
        Assert.Equal("OK24", line.Value.OkVersion);
        Assert.Equal("AC", line.Value.AgreementCode);
        Assert.Equal(eventId, line.Value.SourceEventId);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 3 — replay-determinism: re-running the emitter over the same event reproduces the
    //              same line, off the immutable snapshot (a SUPERSEDED mapping is NOT used).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The godtgørelse lønart resolves off the snapshot's <c>SettlementBoundaryDate</c> via
    /// the dated natural key. With the mapping SUPERSEDED by a NEW row effective AFTER the boundary, a
    /// DELAYED FIRST consumption stages the HISTORICAL (as-of-boundary) lønart — NOT the superseded
    /// one. Replay-deterministic: the event is consumed for the FIRST time AFTER the supersession, so
    /// a non-dated lookup would (wrongly) pick the open V2 row.</summary>
    [Fact]
    public async Task DelayedFirstConsumption_UsesHistoricalMapping_NotSuperseded_ReplayDeterministic()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s80_");

        const string agreement = "ZZ_S80_HIST";
        var boundary = SettlementEmitterFixture.BoundaryDate; // 2025-12-31 (a fixed deterministic asOf)
        var supersedeDate = new DateOnly(2026, 6, 1);         // AFTER the boundary

        // Historical row effective [2020-01-01, 2026-06-01) — in force AT the boundary.
        await InsertGodtgoerelseMappingAsync(agreement, "OK24", "SLS_TBD_S15S17", new DateOnly(2020, 1, 1), effectiveTo: supersedeDate);
        // Superseding row effective [2026-06-01, ∞) — a DIFFERENT lønart, in force only AFTER the boundary.
        await InsertGodtgoerelseMappingAsync(agreement, "OK24", "SLS_TBD_S15S17_V2", supersedeDate, effectiveTo: null);

        var eventId = await SettlementEmitterFixture.WriteSaerligeFeriedagePaidOutEventAsync(
            Factory, emp, payoutDays: 5m, agreementCode: agreement, okVersion: "OK24", boundaryDate: boundary);

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "PROCESSED");

        var line = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, year: 2022, bucket: SettlementEmitterFixture.GodtgoerelseBucket,
            entitlementType: SettlementEmitterFixture.SpecialHolidayType);
        Assert.NotNull(line);
        Assert.Equal("SLS_TBD_S15S17", line!.Value.WageType);       // the as-of-boundary historical row
        Assert.NotEqual("SLS_TBD_S15S17_V2", line.Value.WageType);  // NOT the superseded (later) row
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 4 — §24/§26 paths UNCHANGED (a regression pin): adding the godtgørelse consumer does
    //              not alter the §24 staging behavior.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Regression pin (R7): the §24 <c>VacationAutoPaidOut</c> path is UNCHANGED by the
    /// godtgørelse consumer — a §24 event still stages exactly ONE line at the §24
    /// <c>AUTO_PAYOUT_24</c> bucket carrying the <c>SLS_TBD_S24</c> lønart, and NO godtgørelse line
    /// leaks onto its (VACATION) settlement.</summary>
    [Fact]
    public async Task Section24Path_StillStagesItsOwnLine_NoGodtgoerelseLeak_RegressionPin()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s80_");
        var eventId = await SettlementEmitterFixture.WriteAutoPaidOutEventAsync(
            Factory, emp, payoutDays: 5m, agreementCode: "AC", okVersion: "OK24");

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "PROCESSED");

        // The §24 line is staged unchanged at its own bucket / sentinel.
        Assert.Equal(1L, await SettlementEmitterFixture.LineCountAsync(
            Cs, emp, bucket: SettlementEmitterFixture.AutoPayoutBucket));
        var s24Line = await SettlementEmitterFixture.ReadLineAsync(
            Cs, emp, bucket: SettlementEmitterFixture.AutoPayoutBucket);
        Assert.NotNull(s24Line);
        Assert.Equal(SettlementEmitterFixture.SentinelWageType, s24Line!.Value.WageType); // SLS_TBD_S24

        // And NO godtgørelse line was created (the whole employee has exactly the ONE §24 line).
        Assert.Equal(1L, await SettlementEmitterFixture.TotalLineCountAsync(Cs, emp));
        Assert.Equal(0L, await SettlementEmitterFixture.LineCountAsync(
            Cs, emp, year: 2022, bucket: SettlementEmitterFixture.GodtgoerelseBucket,
            entitlementType: SettlementEmitterFixture.SpecialHolidayType));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Scenario 5 — fail-closed: no godtgørelse mapping for the snapshot's agreement ⇒ RETRY_PENDING,
    //              no line (no live/empty/hard-coded fallback).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A deterministic failure (no <c>SPECIAL_HOLIDAY_SETTLEMENT_PAYOUT</c> mapping for the
    /// snapshot's agreement) leaves a <c>RETRY_PENDING</c> inbox row (<c>attempts ≥ 1</c>) and NO
    /// line — fail-closed, never a fallback lønart.</summary>
    [Fact]
    public async Task NoGodtgoerelseMapping_FailsClosed_RetryPending_NoLine()
    {
        var emp = await SettlementEmitterFixture.SeedEmployeeAsync(Cs, "emp_s80_");
        const string unmappedAgreement = "ZZ_S80_NOMAP";
        var eventId = await SettlementEmitterFixture.WriteSaerligeFeriedagePaidOutEventAsync(
            Factory, emp, payoutDays: 5m, agreementCode: unmappedAgreement, okVersion: "OK24");

        var emitter = SettlementEmitterFixture.BuildEmitter(Factory);
        await SettlementEmitterFixture.ProcessOnceAsync(emitter,
            until: async () => await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId) == "RETRY_PENDING");

        Assert.Equal("RETRY_PENDING", await SettlementEmitterFixture.InboxStatusAsync(Cs, eventId));
        Assert.True((await SettlementEmitterFixture.InboxAttemptsAsync(Cs, eventId) ?? 0) >= 1);
        Assert.Equal(0L, await SettlementEmitterFixture.LineCountAsync(
            Cs, emp, year: 2022, bucket: SettlementEmitterFixture.GodtgoerelseBucket,
            entitlementType: SettlementEmitterFixture.SpecialHolidayType));
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task InsertGodtgoerelseMappingAsync(
        string agreementCode, string okVersion, string wageType, DateOnly effectiveFrom, DateOnly? effectiveTo = null)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings
                (time_type, wage_type, ok_version, agreement_code, position, description, effective_from, effective_to)
            VALUES
                (@tt, @wt, @ok, @ac, '', 'test §15 stk.2/§17 godtgørelse mapping', @from, @to)
            """, conn);
        cmd.Parameters.AddWithValue("tt", SettlementEmitterFixture.GodtgoerelseTimeType);
        cmd.Parameters.AddWithValue("wt", wageType);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
