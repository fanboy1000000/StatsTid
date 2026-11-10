using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Integrations.Payroll.Services;
using static StatsTid.Tests.Regression.Payroll.FlexLedgerHostFixture;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14802-T (QUAL-232, QUAL-202; AC-S0-1b, AC-S0-1c; R-9, R-20, R-25) — payroll refuses, by name, a month that holds
/// stored time typed as something other than ordinary work, over HTTP against a real Postgres.
///
/// <para>
/// <b>Plain language.</b> An hour typed as on-call duty, a call-in or travel is paid by rules this system does not apply
/// correctly to a stored row (an on-call hour would be paid at the reduced rate and again as a fully worked hour). The route that
/// could register such an hour is gone; payroll now also refuses to calculate or correct a month that still holds one, with a
/// 422 of its own kind, <c>payroll-activity-type-refused</c>, and writes nothing. A month whose hours are all ordinary work
/// (type empty or <c>NORMAL</c>) is paid exactly as before. A month already paid can still be replayed to prove what was paid
/// (replay re-reads the hours the payment used, never the current rows), but cannot be corrected while a typed hour sits in it.
/// </para>
///
/// <para>
/// <b>Harness.</b> <see cref="FlexLedgerHostFixture"/> (the real Payroll host, the configurable stub engine). Every case uses a
/// fresh employee. Typed rows are inserted by SQL (no route registers one any more). Expected values come from the refinement
/// (S0-1, AC-S0-1b, AC-S0-1c) and the plan, never from reading the code: 422; kind <c>payroll-activity-type-refused</c>; a body
/// with no date (ADR-040 D7); nothing written. "Nothing written" means no manifest, no export record, no
/// <c>PayrollExportGenerated</c> event and no ADR-026 audit projection row: the request's own <c>audit_log</c> row is still
/// written for every request, so no fact asserts zero rows there.
/// </para>
///
/// <para>
/// <b>Dates.</b> June 2026 opens on a Monday (1 June), so it needs no previous month; the typed row is on Wednesday 10 June.
/// October 2026 opens on Thursday 1 October; its opening week's Monday is 28 September, so the calculation's hours window starts
/// on Sunday 27 September, the day the context-day arm's typed row sits on.
/// </para>
///
/// <para>
/// <b>Status.</b> Docker-gated: <b>CI-pending</b> (CI-W1); never locally green. The F facts fail on the base (the parent of
/// TASK-14802); the R fact holds on it. The RED of a Docker fact is argued, not run.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollActivityTypeRefusalTests : IClassFixture<FlexLedgerHostFixture>
{
    private static readonly DateOnly Jun = new(2026, 6, 1);
    private static readonly DateOnly Oct = new(2026, 10, 1);

    private const string Kind = "payroll-activity-type-refused";

    private static long _outboxSeq = 148_000_000;

    private readonly FlexLedgerHostFixture _fx;

    public PayrollActivityTypeRefusalTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    // ═════════════════════════════════════════════════════════════════════
    // AC-S0-1b — both calculating routes refuse a typed month and write nothing
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-S0-1b (kind F, Docker; CI-pending). Three arms, one name.
    /// <para><b>calculate-and-export:</b> an APPROVED June with weekday hours and one SQL-inserted entry typed
    /// <c>ON_CALL</c> on Wednesday 10 June 2026 answers 422, kind <c>payroll-activity-type-refused</c>, a date-free body. No
    /// manifest, no export record, no <c>PayrollExportGenerated</c>, no audit projection row, and the rule engine was never
    /// called (the refusal comes before the calculation).</para>
    /// <para><b>recalculate:</b> a second employee's June is exported clean first (200, one record, one manifest); then the same
    /// typed row is inserted. <c>/recalculate</c> answers the same 422 and kind, and nothing is written: the manifest count is
    /// unchanged, no correction event, the record (lines, balances, hash) is unchanged, and the request's idempotency token was
    /// not marked delivered.</para>
    /// <para><b>the context day:</b> an APPROVED October 2026 with an <c>ON_CALL</c> row on Sunday 27 September 2026, the first
    /// day of the calculation's hours window (the day before Monday 28 September, the Monday of October's opening week). It is
    /// refused the same way and nothing is written. A check that started one day late, or that read only the month itself,
    /// would miss this row.</para>
    ///
    /// <para><b>Red conditions.</b> On the base neither route checks the stored type: <c>/calculate-and-export</c> pays the typed
    /// row (200, a manifest, a record), <c>/recalculate</c> pays it as a correction, and October is calculated (or refused for
    /// an unrelated reason, never with this kind). The <c>Assert.Equal(422, ...)</c> and the kind assertion fail in every arm.</para>
    /// </summary>
    [Fact]
    public async Task OnCallTypedEntry_RefusedOnBothRoutes_NothingWritten()
    {
        // ── calculate-and-export ──
        var calc = "EMP-S148-AT-CALC";
        await _fx.SeedEmployeeAsync(calc);
        await _fx.SeedApprovedMonthAsync(calc, Jun);
        await _fx.SeedWeekdayHoursAsync(calc, Jun, new DateOnly(2026, 6, 30));
        await SeedTypedEntryAsync(calc, new DateOnly(2026, 6, 10), "ON_CALL");
        _fx.Engine.Configure(calc, Jun, closing: 5m);

        var calcResponse = await _fx.CalculateAndExportMonthAsync(calc, Jun);

        AssertRefusal(calcResponse);
        Assert.Equal(0, _fx.Engine.CallCountFor(calc));
        await AssertNothingWrittenAsync(calc);

        // ── recalculate ──
        var recalc = "EMP-S148-AT-RECALC";
        await _fx.SeedEmployeeAsync(recalc);
        await _fx.SeedApprovedMonthAsync(recalc, Jun);
        await _fx.SeedWeekdayHoursAsync(recalc, Jun, new DateOnly(2026, 6, 30));
        _fx.Engine.Configure(recalc, Jun, closing: 5m);
        var exported = await _fx.CalculateAndExportMonthAsync(recalc, Jun);
        Assert.True(exported.Status == HttpStatusCode.OK, $"the clean export must succeed first, got {(int)exported.Status}: {exported.Json}");
        Assert.Equal(1, await _fx.CountRecordsAsync(recalc));
        var manifestsBefore = await _fx.CountManifestsAsync(recalc);
        Assert.Equal(1, manifestsBefore);
        var recordBefore = await _fx.RequireRecordAsync(recalc, Jun);
        var exportEventsBefore = await _fx.CountExportEventsAsync(recalc);
        Assert.Equal(1, exportEventsBefore);

        await SeedTypedEntryAsync(recalc, new DateOnly(2026, 6, 10), "ON_CALL");
        var token = Guid.NewGuid();
        var recalcResponse = await _fx.RecalculateMonthAsync(recalc, Jun, token);

        AssertRefusal(recalcResponse);
        Assert.Equal(manifestsBefore, await _fx.CountManifestsAsync(recalc));
        Assert.Equal(0, await _fx.CountCorrectionEventsAsync(recalc));
        Assert.Equal(0, await _fx.CountAuditRowsAsync(recalc, "RetroactiveCorrectionRequested"));
        Assert.Equal(exportEventsBefore, await _fx.CountExportEventsAsync(recalc));
        Assert.Equal(1, await _fx.CountRecordsAsync(recalc));
        Assert.Equal(recordBefore, await _fx.RequireRecordAsync(recalc, Jun)); // lines, balances, hash: unchanged
        Assert.Equal(0, await _fx.CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", ("t", token))); // the token was not marked

        // ── the context day: Sunday 27 September 2026, the first day of October's hours window ──
        var ctx = "EMP-S148-AT-CTX";
        await _fx.SeedEmployeeAsync(ctx);
        await _fx.SeedApprovedMonthAsync(ctx, Oct);
        await _fx.SeedWeekdayHoursAsync(ctx, Oct, new DateOnly(2026, 10, 30));
        var contextDay = new DateOnly(2026, 9, 27);
        Assert.Equal(DayOfWeek.Sunday, contextDay.DayOfWeek);
        await SeedTypedEntryAsync(ctx, contextDay, "ON_CALL");
        _fx.Engine.Configure(ctx, Oct, closing: 5m);

        var ctxResponse = await _fx.CalculateAndExportMonthAsync(ctx, Oct);

        AssertRefusal(ctxResponse);
        Assert.Equal(0, _fx.Engine.CallCountFor(ctx));
        await AssertNothingWrittenAsync(ctx);
    }

    /// <summary>
    /// AC-S0-1b, second fact (kind R, Docker; CI-pending: a regression pin, holds on the base). A June holding the
    /// weekday hours of 1-5 June plus one entry whose type is NULL (Wednesday 10 June) and one typed <c>NORMAL</c> (Thursday
    /// 11 June) calculates and exports as before: 200, one record. The discriminating assertion is the month's ordinary-hours
    /// total, which equals the seeded total of 7 days x 7.4 h = 51.8 h, so both the NULL row and the <c>NORMAL</c> row are
    /// paid (a check that refused <c>NORMAL</c>, or one that dropped a row, would change the status or the total).
    ///
    /// <para><b>Regression pin:</b> on the base the same month calculates the same way; the fact fails only if the new check
    /// refuses or drops an ordinary-work row.</para>
    /// </summary>
    [Fact]
    public async Task NullOrNormalEntries_CalculateAsBefore()
    {
        var emp = "EMP-S148-AT-NORMAL";
        await _fx.SeedEmployeeAsync(emp);
        await _fx.SeedApprovedMonthAsync(emp, Jun);
        await _fx.SeedWeekdayHoursAsync(emp, Jun, new DateOnly(2026, 6, 5)); // Mon 1 - Fri 5 June: five rows, type NULL
        await SeedTypedEntryAsync(emp, new DateOnly(2026, 6, 10), null);
        await SeedTypedEntryAsync(emp, new DateOnly(2026, 6, 11), "NORMAL");
        _fx.Engine.Configure(emp, Jun, closing: 5m);

        var response = await _fx.CalculateAndExportMonthAsync(emp, Jun);

        Assert.True(response.Status == HttpStatusCode.OK, $"expected 200, got {(int)response.Status}: {response.Json}");
        var result = ReadResult(response.Json);
        var ordinary = result.RuleResults
            .SelectMany(r => r.LineItems)
            .Where(li => li.TimeType == "NORMAL_HOURS")
            .Sum(li => li.Hours);
        Assert.Equal(7 * 7.4m, ordinary);
        Assert.Equal(1, await _fx.CountRecordsAsync(emp));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-S0-1c — a month already paid: replay reproduces it, recalculate is refused
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-S0-1c (kind F, Docker; CI-pending; R-9). June is calculated and exported (manifest M). A typed <c>ON_CALL</c> row is
    /// then inserted by SQL for the same employee and month (Wednesday 10 June 2026). A <b>replay of M</b> succeeds and
    /// reproduces what was paid: its export lines serialize byte-identically to the forward calculation's, because a replay reads
    /// the hours rows the calculation recorded (by event id), never the current rows, so the later row is invisible to it
    /// (ADR-028 D6, ADR-016 D10 R42). The replay writes nothing (the manifest count stays 1). <b>/recalculate</b> of June answers
    /// 422, <c>payroll-activity-type-refused</c>, and writes nothing: no correction event, the record unchanged, the token not
    /// marked.
    ///
    /// <para><b>Red conditions.</b> The replay half holds on the base and must keep holding (replay is evaluation, never admission:
    /// a refusal added to replay would fail the success and equality assertions). The refusal half fails on the base: there
    /// <c>/recalculate</c> pays the typed row as a correction, so the 422 and kind assertions fail.</para>
    /// </summary>
    [Fact]
    public async Task TypedRowAfterExport_ReplayReproducesManifest_RecalculateRefused()
    {
        var emp = "EMP-S148-AT-REPLAY";
        await _fx.SeedEmployeeAsync(emp);
        await _fx.SeedApprovedMonthAsync(emp, Jun);
        await _fx.SeedWeekdayHoursAsync(emp, Jun, new DateOnly(2026, 6, 30));
        _fx.Engine.Configure(emp, Jun, closing: 5m);

        var forward = await _fx.CalculateAndExportMonthAsync(emp, Jun);
        Assert.True(forward.Status == HttpStatusCode.OK, $"the clean export must succeed first, got {(int)forward.Status}: {forward.Json}");
        Assert.Equal(1, await _fx.CountManifestsAsync(emp));
        var manifestId = await ReadManifestIdAsync(emp);
        var forwardResult = ReadResult(forward.Json);
        Assert.NotEmpty(forwardResult.ExportLines); // non-vacuity: the month paid something, so the equality below compares lines
        var recordBefore = await _fx.RequireRecordAsync(emp, Jun);
        var exportEventsBefore = await _fx.CountExportEventsAsync(emp);

        await SeedTypedEntryAsync(emp, new DateOnly(2026, 6, 10), "ON_CALL");

        // The replay of M: evaluation over the frozen rows; the typed row is not among them.
        var pcs = _fx.StubHost.Services.GetRequiredService<PeriodCalculationService>();
        var replay = await pcs.ReplayAsync(manifestId);
        Assert.True(replay.Success, replay.ErrorMessage);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(forwardResult.ExportLines, Web),
            System.Text.Json.JsonSerializer.Serialize(replay.ExportLines, Web));
        Assert.Equal(forwardResult.ClosingFlexBalance, replay.ClosingFlexBalance);
        Assert.Equal(1, await _fx.CountManifestsAsync(emp)); // a replay writes no manifest

        // The correction of the same month is refused by name and writes nothing.
        var token = Guid.NewGuid();
        var recalc = await _fx.RecalculateMonthAsync(emp, Jun, token);

        AssertRefusal(recalc);
        Assert.Equal(1, await _fx.CountManifestsAsync(emp));
        Assert.Equal(0, await _fx.CountCorrectionEventsAsync(emp));
        Assert.Equal(exportEventsBefore, await _fx.CountExportEventsAsync(emp));
        Assert.Equal(recordBefore, await _fx.RequireRecordAsync(emp, Jun));
        Assert.Equal(0, await _fx.CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", ("t", token)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // The blank-employee-id branch of /recalculate (the implementer's report, TASK-14802)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kind R (Docker; CI-pending; a regression pin for the one branch of the new check that returns "no refusal"). A
    /// <c>/recalculate</c> whose profile carries a BLANK employee id cannot hold stored entries, so the typed-entry check
    /// leaves it to the route's own outcome: 422, kind <c>payroll-not-exported</c> (nothing to correct), not a 500 from the
    /// reader's argument guard.
    ///
    /// <para><b>Provenance and limit.</b> The expected value is the TASK-14802 implementer's report of what the route answered
    /// for a blank id before the change and still answers; the spec names no behaviour for a blank id. (unproven) until CI-W1.
    /// <b>Red conditions:</b> a check that did not skip a blank id would pass the blank to the reader, which throws on it: a 500,
    /// so the status assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_BlankEmployeeId_StillAnswersNotExported()
    {
        var response = await _fx.RecalculateMonthAsync("", Jun);

        Assert.True(response.Status == HttpStatusCode.UnprocessableEntity, $"expected 422, got {(int)response.Status}: {response.Json}");
        using var doc = JsonDocument.Parse(response.Json);
        Assert.Equal("payroll-not-exported", doc.RootElement.GetProperty("kind").GetString());
    }

    // ═════════════════════════════════════════════════════════════════════
    // helpers
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>422, kind <c>payroll-activity-type-refused</c>, <c>success:false</c>, a non-empty sentence, and a body that
    /// carries no date, no <c>periodStart</c>/<c>periodEnd</c> member and no employee id (ADR-040 D7).</summary>
    private static void AssertRefusal((HttpStatusCode Status, string Json) response)
    {
        Assert.True(response.Status == HttpStatusCode.UnprocessableEntity, $"expected 422, got {(int)response.Status}: {response.Json}");
        using var doc = JsonDocument.Parse(response.Json);
        var root = doc.RootElement;
        Assert.Equal(Kind, root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
        Assert.False(root.TryGetProperty("periodStart", out _));
        Assert.False(root.TryGetProperty("periodEnd", out _));
        Assert.False(root.TryGetProperty("employeeId", out _));
        Assert.DoesNotMatch(new Regex(@"\d{4}-\d{2}-\d{2}"), response.Json);
        Assert.DoesNotContain("EMP-S148", response.Json, StringComparison.Ordinal);
    }

    /// <summary>No manifest, no export record, no <c>PayrollExportGenerated</c> event and no ADR-026 audit projection row.</summary>
    private async Task AssertNothingWrittenAsync(string employeeId)
    {
        Assert.Equal(0, await _fx.CountManifestsAsync(employeeId));
        Assert.Equal(0, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
        Assert.Equal(0, await _fx.CountAuditRowsAsync(employeeId, "PayrollExportGenerated"));
    }

    private async Task<Guid> ReadManifestIdAsync(string employeeId)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT manifest_id FROM segment_manifests WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>One current <c>time_entries_projection</c> row of 7.4 hours with the given <c>activity_type</c> (null = NULL),
    /// inserted by SQL: the column list is <c>FlexLedgerHostFixture.SeedWeekdayHoursAsync</c>'s plus <c>activity_type</c>.</summary>
    private async Task SeedTypedEntryAsync(string employeeId, DateOnly date, string? activityType)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, activity_type, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, @type, @agreement, @ok, FALSE, NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("hours", 7.4m);
        cmd.Parameters.AddWithValue("type", (object?)activityType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("agreement", Agreement);
        cmd.Parameters.AddWithValue("ok", Ok);
        cmd.Parameters.AddWithValue("outbox", Interlocked.Increment(ref _outboxSeq));
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }
}
