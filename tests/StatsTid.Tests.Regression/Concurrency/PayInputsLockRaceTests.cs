using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Concurrency.PayInputsLockTestKit;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Concurrency;

/// <summary>
/// S148 / TASK-14831-T (AC-PIL-a, -b, -c, -d, -g (1) and (2); refinement A-3, B-2) - a change dated into the past and a
/// payroll recording take turns on the system-wide "pay inputs" lock, in either order, and nobody is paid on stale inputs
/// or left uninformed.
///
/// <para>
/// <b>In plain language.</b> Payroll first calculates a month and then records it (the export), and an administrator may
/// date a change into that month (a new agreement version, a local profile, a transfer, a profile close) at any moment.
/// Each fact below makes both requests wait for the lock, in a chosen order, and then lets them go. <b>When the change
/// goes first</b>, it commits and finds no recorded month to list (none exists yet); the recording then finds, inside its
/// own turn, that the inputs it calculated with are gone, refuses with 409 <c>payroll-inputs-changed</c> and writes
/// nothing; calculating the month again pays the new values. <b>When the recording goes first</b>, it records the month on
/// the old inputs, and the change then finds the recorded month and puts it on HR's worklist, so nobody is left
/// uninformed. The correction (<c>/recalculate</c>) loses to a change in the same way the export does.
/// </para>
///
/// <para>
/// <b>Why the order is not timing.</b> The test holds the lock itself on its own connection, starts the first request and
/// waits until PostgreSQL shows it waiting for the lock (<c>pg_locks</c>, bounded, never a sleep), starts the second and
/// waits until two are waiting, then releases. PostgreSQL grants a waiting lock in queue order, so the test decides who
/// goes first. <b>Product code runs between the readings</b>: the export calculates the month on the inputs in force
/// BEFORE it asks for the lock (so the change, still waiting, has not committed), then queues; after the release the
/// change commits first or the recording does, by queue order, and the recording's recheck derives the inputs again.
/// </para>
///
/// <para>
/// <b>Fixture.</b> One database per fact; a Backend host and a payroll host over it (<see cref="PayInputsLockTestKit"/>),
/// the Backend clock Tuesday 10 November 2026 (AC-PIL-g: Tuesday 31 March 2026). One HK / OK24 employee in STY01 with
/// February and March 2026 APPROVED and not exported (March opens on a Sunday, so the months-in-order check needs
/// February APPROVED); weekday hours of 7.4 are recorded; the Rule Engine is the shared stub, which records each
/// timeline call so a fact can read the weekly norm every piece was calculated on. HK OK24's weekly norm is 37 from the
/// seed; a change dates it to 32 on a Monday (2 February or 16 March 2026). The serial premise (the Regression suite runs
/// one collection at a time) is the class's. Docker-gated: <b>CI-pending</b>; every fact is F, RED on the code before
/// TASK-14831 (named under each).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayInputsLockRaceTests : DatedConfigDockerFixture, IDisposable
{
    private const string Org = "STY01";

    private const string ExportSentence =
        "The inputs this calculation used changed while it was being recorded; calculate the month again. " +
        "Nothing was exported and no export record was written.";
    private const string CorrectionSentence =
        "The inputs this correction used changed while it was being recorded; run the correction again. " +
        "Nothing was corrected and the export record is unchanged.";

    private static readonly DateOnly Clock = new(2026, 11, 10);
    private static readonly DateOnly CloseClock = new(2026, 3, 31);
    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar15 = new(2026, 3, 15);
    private static readonly DateOnly Mar16 = new(2026, 3, 16);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    private readonly List<WebApplicationFactory<Program>> _hosts = new();
    private readonly List<PayrollHost> _payrolls = new();

    public void Dispose()
    {
        foreach (var payroll in _payrolls)
            payroll.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var host in _hosts)
            host.Dispose();
    }

    /// <summary>Everything a fact starts from: a booted Backend host (a GlobalAdmin client), a payroll host and the employee.</summary>
    private sealed record Scene(HttpClient Admin, PayrollHost Payroll, string Employee);

    private async Task<Scene> SceneAsync(DateOnly clock, Action<IServiceCollection>? payrollServices = null)
    {
        var host = Factory.WithFixedToday(clock);
        _hosts.Add(host);
        var admin = GlobalAdminClient(host); // boots the host before anything is seeded (PAT-008)
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
        var payroll = PayrollHost.Start(ConnectionString, payrollServices);
        _payrolls.Add(payroll);
        var employee = await SeedPayableEmployeeAsync(ConnectionString, Org, (Feb01, Feb28), (Mar01, Mar31));
        return new Scene(admin, payroll, employee);
    }

    /// <summary>The held lock, then request 1 (seen waiting alone), then request 2 (seen waiting beside it), then the release.</summary>
    private async Task<(HttpResponseMessage First, HttpResponseMessage Second)> RunInOrderAsync(
        Func<Task<HttpResponseMessage>> first, string firstWho, Func<Task<HttpResponseMessage>> second, string secondWho)
    {
        await using var held = await HeldLock.AcquirePayInputsAsync(ConnectionString);
        var request1 = first();
        await AssertWaitingAsync(ConnectionString, 1, request1, firstWho);
        var request2 = second();
        await AssertWaitingAsync(ConnectionString, 2, request2, secondWho);
        await held.ReleaseAsync();
        return (await request1, await request2);
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-PIL-a — the change first: the export refuses, the retry pays the new configuration
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI).</b> AC-PIL-a. An HK OK24 draft (weekly norm 32) is published dated Monday 2 February 2026 and seen waiting;
    /// then the export of March is started (it has calculated March on 37) and seen waiting. After the release the publish
    /// commits first and lists nothing (no March record exists), and the export answers 409 <c>payroll-inputs-changed</c> and
    /// writes no export record, no <c>PayrollExportGenerated</c> and no audit row (the calculation's manifest stays). A
    /// second export answers 200 and its timeline request carries weekly norm 32 on every piece that reaches March.
    /// <para><b>Red conditions.</b> Before TASK-14831 the export takes no pay-inputs lock, so it is never observed waiting
    /// (<c>AssertWaitingAsync</c>, two waiters expected, fails) and would answer 200 on 37 (the stale recording).</para>
    /// </summary>
    [Fact]
    public async Task PublishFirst_Export409_RetryPaysNewConfig()
    {
        var s = await SceneAsync(Clock);
        var (configId, version) = await CreateDraftAsync(s.Admin, Norm32());

        var (publish, export) = await RunInOrderAsync(
            () => PublishAsync(s.Admin, configId, version, "2026-02-02"), "the agreement-config publish",
            () => s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31), "the export of March");

        using (publish)
            Assert.True(publish.StatusCode == HttpStatusCode.OK, $"the publish: {await publish.Content.ReadAsStringAsync()}");
        using (export)
            await AssertInputsChangedAsync(export, ExportSentence);
        Assert.Equal(0L, await WorklistRowCountAsync(s.Employee)); // no recorded month existed when the publish committed
        await AssertExportNothingWrittenAsync(s.Employee);

        s.Payroll.Engine.Clear();
        using var retry = await s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31);
        Assert.True(retry.StatusCode == HttpStatusCode.OK, $"the retry: {await retry.Content.ReadAsStringAsync()}");
        Assert.Equal(1L, await ExportRecordCountAsync(s.Employee));
        var pieces = s.Payroll.Engine.LatestTimelinePieces();
        Assert.NotEmpty(pieces);
        Assert.All(pieces, p => Assert.Equal(32m, p.WeeklyNormHours));
    }

    /// <summary>
    /// <b>F (CI).</b> AC-PIL-a2 (a change inside the month, B-2). As above with the publish dated Monday 16 March 2026: March
    /// was calculated as one run on 37 and the new version splits it. The export answers 409 and writes nothing; the retry
    /// pays 1-15 March on 37 and 16-31 March on 32.
    /// <para><b>Red conditions.</b> As above: the export waits on no lock before TASK-14831, so the two-waiter assertion
    /// fails; a recheck that compared only the first run (equal in both) would answer 200 on the old inputs.</para>
    /// </summary>
    [Fact]
    public async Task PublishDated16MarchFirst_Export409_RetrySplitsMarch()
    {
        var s = await SceneAsync(Clock);
        var (configId, version) = await CreateDraftAsync(s.Admin, Norm32());

        var (publish, export) = await RunInOrderAsync(
            () => PublishAsync(s.Admin, configId, version, "2026-03-16"), "the agreement-config publish dated 16 March",
            () => s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31), "the export of March");

        using (publish)
            Assert.True(publish.StatusCode == HttpStatusCode.OK, $"the publish: {await publish.Content.ReadAsStringAsync()}");
        using (export)
            await AssertInputsChangedAsync(export, ExportSentence);
        await AssertExportNothingWrittenAsync(s.Employee);

        s.Payroll.Engine.Clear();
        using var retry = await s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31);
        Assert.True(retry.StatusCode == HttpStatusCode.OK, $"the retry: {await retry.Content.ReadAsStringAsync()}");
        var pieces = s.Payroll.Engine.LatestTimelinePieces();
        Assert.Contains(pieces, p => p.To == Mar15);   // 1-15 March ends on Sunday 15 March...
        Assert.Contains(pieces, p => p.From == Mar16 && p.To == Mar31); // ...and 16-31 March is one piece
        Assert.All(pieces.Where(p => p.To <= Mar15), p => Assert.Equal(37m, p.WeeklyNormHours));
        Assert.All(pieces.Where(p => p.From >= Mar16), p => Assert.Equal(32m, p.WeeklyNormHours));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-PIL-b — the recording first: the change lists the month
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI).</b> AC-PIL-b (agreement-config publish). The export is started first and seen waiting, then the publish
    /// (norm 32, dated Monday 2 February 2026). After the release the export records March on 37 (200), and the publish (200)
    /// then finds the recorded month and lists it: one EXPORTED_MONTH row for March 2026 with the single trigger
    /// <c>CONFIG_PUBLISHED</c>.
    /// <para><b>Red conditions.</b> Before TASK-14831 the export takes no pay-inputs lock, so it is never observed waiting
    /// and the publish does not wait behind it: <c>AssertWaitingAsync</c> fails at the first request.</para>
    /// </summary>
    [Fact]
    public async Task ExportFirst_PublishListsMarch()
    {
        var s = await SceneAsync(Clock);
        var (configId, version) = await CreateDraftAsync(s.Admin, Norm32());

        var (export, publish) = await RunInOrderAsync(
            () => s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31), "the export of March",
            () => PublishAsync(s.Admin, configId, version, "2026-02-02"), "the agreement-config publish");

        using (export)
            Assert.True(export.StatusCode == HttpStatusCode.OK, $"the export: {await export.Content.ReadAsStringAsync()}");
        using (publish)
            Assert.True(publish.StatusCode == HttpStatusCode.OK, $"the publish: {await publish.Content.ReadAsStringAsync()}");
        Assert.All(s.Payroll.Engine.LatestTimelinePieces(), p => Assert.Equal(37m, p.WeeklyNormHours)); // March was recorded on the old inputs
        await AssertMarchListedAsync(s.Employee, "CONFIG_PUBLISHED");
    }

    /// <summary>
    /// <b>F (CI).</b> AC-PIL-b (local-profile write). The export first, then STY01's (HK, OK24) local profile
    /// (<c>If-None-Match: *</c>, dated Monday 2 February 2026, <c>maxFlexBalance</c> only). After the release the export
    /// records March (200) and the write (200) lists it. This run proves the write's move off REPEATABLE READ: under that
    /// isolation the write's snapshot would be taken before the export's record committed during its wait, and it would not
    /// see the record.
    /// <para><b>Red conditions.</b> Before TASK-14831 the export waits on no lock, so the first request is never observed
    /// waiting; and a write that kept REPEATABLE READ would answer 200 but list nothing, failing
    /// <c>AssertMarchListedAsync</c>.</para>
    /// </summary>
    [Fact]
    public async Task ExportFirst_LocalProfileWriteListsMarch()
    {
        var s = await SceneAsync(Clock);

        var (export, write) = await RunInOrderAsync(
            () => s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31), "the export of March",
            () => PutProfileAsync(s.Admin, Org, "2026-02-02", 30m), "the local-profile write");

        using (export)
            Assert.True(export.StatusCode == HttpStatusCode.OK, $"the export: {await export.Content.ReadAsStringAsync()}");
        using (write)
            Assert.True(write.StatusCode == HttpStatusCode.OK, $"the local-profile write: {await write.Content.ReadAsStringAsync()}");
        await AssertMarchListedAsync(s.Employee, "CONFIG_PUBLISHED");
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-PIL-c — the transfer variants
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI).</b> AC-PIL-c (the transfer first). The user edit moving the employee STY01 to STY02, dated Monday
    /// 2 February 2026 (a person who manages nobody, by an actor scoped to both), is seen waiting, then the export. After
    /// the release the transfer answers 200 and the export 409 <c>payroll-inputs-changed</c> (the organisation of the days
    /// differs), nothing written.
    /// <para><b>Red conditions.</b> Before TASK-14831 the export takes no lock, so it is never observed waiting and would
    /// record March on STY01.</para>
    /// </summary>
    [Fact]
    public async Task TransferFirst_Export409()
    {
        var s = await SceneAsync(Clock);

        var (transfer, export) = await RunInOrderAsync(
            () => PutUserAsync(s.Admin, ConnectionString, s.Employee, new { primaryOrgId = "STY02", effectiveFrom = "2026-02-02" }),
            "the transfer",
            () => s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31), "the export of March");

        using (transfer)
            Assert.True(transfer.StatusCode == HttpStatusCode.OK, $"the transfer: {await transfer.Content.ReadAsStringAsync()}");
        using (export)
            await AssertInputsChangedAsync(export, ExportSentence);
        await AssertExportNothingWrittenAsync(s.Employee);
    }

    /// <summary>
    /// <b>F (CI).</b> AC-PIL-c (the export first). The export first, then the same transfer. After the release the export
    /// records March (200) and the transfer (200) lists it with the trigger <c>ORGANISATION_TRANSFER</c>.
    /// <para><b>Red conditions.</b> Before TASK-14831 the first request is never observed waiting.</para>
    /// </summary>
    [Fact]
    public async Task ExportFirst_TransferListsMarch()
    {
        var s = await SceneAsync(Clock);

        var (export, transfer) = await RunInOrderAsync(
            () => s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31), "the export of March",
            () => PutUserAsync(s.Admin, ConnectionString, s.Employee, new { primaryOrgId = "STY02", effectiveFrom = "2026-02-02" }),
            "the transfer");

        using (export)
            Assert.True(export.StatusCode == HttpStatusCode.OK, $"the export: {await export.Content.ReadAsStringAsync()}");
        using (transfer)
            Assert.True(transfer.StatusCode == HttpStatusCode.OK, $"the transfer: {await transfer.Content.ReadAsStringAsync()}");
        await AssertMarchListedAsync(s.Employee, "ORGANISATION_TRANSFER");
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-PIL-d — the correction loses to a publish
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI).</b> AC-PIL-d (NR-13). March is exported through the route first (never by a SQL seed). Then the publish
    /// (norm 32, dated Monday 2 February 2026) is seen waiting, then <c>/recalculate</c> of March. After the release the
    /// publish commits and lists March (its worklist row is OPEN), and the correction answers 409
    /// <c>payroll-inputs-changed</c> with the correction's sentence and writes nothing: the record's lines, hash and balances
    /// unchanged, no <c>RetroactiveCorrectionRequested</c>, no audit row, no idempotency marker for its token.
    /// <para><b>Red conditions.</b> Before TASK-14831 the correction takes no pay-inputs lock, so it is never observed
    /// waiting, and on its own it would commit a correction on the old inputs.</para>
    /// </summary>
    [Fact]
    public async Task PublishFirst_Recalculate409_NothingWritten_RowOpen()
    {
        var s = await SceneAsync(Clock);
        using (var first = await s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31))
            Assert.True(first.StatusCode == HttpStatusCode.OK, $"the first export: {await first.Content.ReadAsStringAsync()}");
        var (configId, version) = await CreateDraftAsync(s.Admin, Norm32());
        var recordBefore = await ExportRecordStateAsync(s.Employee);
        var token = Guid.NewGuid();

        var (publish, correction) = await RunInOrderAsync(
            () => PublishAsync(s.Admin, configId, version, "2026-02-02"), "the agreement-config publish",
            () => s.Payroll.RecalculateAsync(s.Employee, Org, Mar01, Mar31, token), "the correction of March");

        using (publish)
            Assert.True(publish.StatusCode == HttpStatusCode.OK, $"the publish: {await publish.Content.ReadAsStringAsync()}");
        using (correction)
            await AssertInputsChangedAsync(correction, CorrectionSentence);

        Assert.Equal(recordBefore, await ExportRecordStateAsync(s.Employee));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'RetroactiveCorrectionRequested'"));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'RetroactiveCorrectionRequested'"));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", ("t", token)));
        // The publish's own row: March listed, open.
        var row = await AssertMarchListedAsync(s.Employee, "CONFIG_PUBLISHED");
        Assert.True(row.Open, "the publish's worklist row for March must be open.");
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-PIL-g (1), (2) — the profile close on Tuesday 31 March 2026
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI).</b> AC-PIL-g (1). The clock reads Tuesday 31 March 2026 and the employee's March is APPROVED. The profile
    /// close (<c>DELETE /api/admin/employee-profiles/{id}</c>, <c>If-Match</c>) is seen waiting, then the export of March
    /// (calculated with the profile covering the 31st). After the release the close answers 204 (the route's answer: no
    /// body) and lists nothing (no March record yet), and the export answers 409 <c>payroll-inputs-changed</c>, nothing
    /// written: the close ended the profile on the 31st, a day the fresh derivation cannot cover. The retry, a second
    /// <c>/calculate-and-export</c> of March, answers 422 <c>payroll-employment-record-gap</c> (the three-member body byte for
    /// byte): the month is not payable until HR repairs the record, and the manifest count stays 1 (the first calculation's).
    /// <para><b>Red conditions.</b> Before TASK-14831 the export takes no lock, so it is never observed waiting. M-52 (the
    /// recheck's wrapper lets a derivation refusal pass) turns this fact red: the uncovered day is waved through and the
    /// export answers 200.</para>
    /// <para><b>History.</b> The fact was red at <c>f493bb32</c> (the export answered 200 and recorded March); TASK-14847 fixed
    /// the derivation, which now checks each piece's last day.</para>
    /// </summary>
    [Fact]
    public async Task ProfileCloseFirst_Export409()
    {
        var s = await SceneAsync(CloseClock);
        var version = await VersionAsync(s.Employee);

        var (close, export) = await RunInOrderAsync(
            () => IfMatchAsync(s.Admin, HttpMethod.Delete, $"/api/admin/employee-profiles/{s.Employee}", version),
            "the profile close",
            () => s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31), "the export of March");

        using (close)
            Assert.True(close.StatusCode == HttpStatusCode.NoContent, $"the close: {(int)close.StatusCode} {await close.Content.ReadAsStringAsync()}");
        using (export)
            await AssertInputsChangedAsync(export, ExportSentence);
        Assert.Equal(0L, await WorklistRowCountAsync(s.Employee)); // no recorded month existed when the close committed
        await AssertExportNothingWrittenAsync(s.Employee);

        // The retry: the profile still ends inside March, so the month is refused by name before any manifest is written.
        using var retry = await s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31);
        await PayrollEmploymentRecordGapTests.AssertRecordGapAsync(retry, s.Employee);
        await AssertExportNothingWrittenAsync(s.Employee); // the manifest count is still 1
    }

    /// <summary>
    /// <b>F (CI).</b> AC-PIL-g (2). The export first (seen waiting), then the close. After the release the export records
    /// March (200) and the close (204) lists March with the trigger <c>PROFILE_CHANGE</c>, pointing at its
    /// <c>EmployeeProfileSoftDeleted</c> event.
    /// <para><b>Red conditions.</b> Before TASK-14831 the first request is never observed waiting.</para>
    /// </summary>
    [Fact]
    public async Task ExportFirst_ProfileCloseListsMarch()
    {
        var s = await SceneAsync(CloseClock);
        var version = await VersionAsync(s.Employee);

        var (export, close) = await RunInOrderAsync(
            () => s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31), "the export of March",
            () => IfMatchAsync(s.Admin, HttpMethod.Delete, $"/api/admin/employee-profiles/{s.Employee}", version),
            "the profile close");

        using (export)
            Assert.True(export.StatusCode == HttpStatusCode.OK, $"the export: {await export.Content.ReadAsStringAsync()}");
        using (close)
            Assert.True(close.StatusCode == HttpStatusCode.NoContent, $"the close: {(int)close.StatusCode} {await close.Content.ReadAsStringAsync()}");
        var row = await AssertMarchListedAsync(s.Employee, "PROFILE_CHANGE");
        var softDeleted = await ScalarAsync<Guid>(
            "SELECT event_id FROM outbox_events WHERE event_type = 'EmployeeProfileSoftDeleted' AND stream_id = @s",
            ("s", $"employee-profile-{s.Employee}"));
        Assert.Equal(softDeleted, row.TriggerEventId);
    }

    // ═════════════════════════════════════════════════════════════════════
    // The recording-time 503 (TASK-14831 item 5a; TASK-14847-T W5)
    // ═════════════════════════════════════════════════════════════════════

    private const string RecordingConfigUnavailableSentence =
        "The agreement configuration could not be read while this month was being recorded. Nothing was exported or " +
        "corrected, and no export record was written or changed. Retry when the configuration database is reachable.";

    /// <summary>
    /// <b>N (CI).</b> W5, the export route. The export of March calculates normally and is seen waiting on the held lock; the
    /// configuration database then becomes unreadable (the test host's lookup starts to throw) and the lock is released. The
    /// recheck under the lock cannot read the configuration, so the export answers 503 <c>payroll-config-unavailable</c> with
    /// the RECORDING-time sentence (not the calculation-time one, which says nothing was calculated), and writes no export
    /// record, no <c>PayrollExportGenerated</c>, no audit row and no delivery envelope (the calculation's manifest stays).
    /// <para><b>Red conditions (kind N: it names <c>RecordingConfigUnavailableException</c>'s answer).</b> M-53 (the recheck
    /// no longer wraps a failed configuration read): the lookup's exception reaches the route's calculation-time arm and the
    /// body carries the calculation-time sentence, so the sentence assertion fails; with that arm gone too it is an anonymous
    /// 500. Docker-gated: CI-pending (argued, not run, for M-53).</para>
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_ConfigUnreadableAtRecording_503Named_NothingWritten()
    {
        var lookup = new FailingConfigLookup();
        var s = await SceneAsync(Clock, lookup.Install);

        HttpResponseMessage response;
        await using (var held = await HeldLock.AcquirePayInputsAsync(ConnectionString))
        {
            var export = s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31);
            await AssertWaitingAsync(ConnectionString, 1, export, "the export of March");
            lookup.Failing = true;
            await held.ReleaseAsync();
            response = await export;
        }

        using (response)
            await AssertRecordingConfigUnavailableAsync(response);
        await AssertExportNothingWrittenAsync(s.Employee);
    }

    /// <summary>
    /// <b>N (CI).</b> W5, the correction route. March is exported through the route first; the correction (<c>/recalculate</c>)
    /// is seen waiting on the held lock, the configuration database then becomes unreadable and the lock is released. The
    /// correction answers the same 503 body and writes nothing: the record's lines, hash and balances are unchanged, no
    /// <c>RetroactiveCorrectionRequested</c>, no audit row, no idempotency marker for its token.
    /// <para><b>Red conditions (kind N).</b> As above: M-53 changes the answer from the recording-time sentence.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_ConfigUnreadableAtRecording_503Named_NothingWritten()
    {
        var lookup = new FailingConfigLookup();
        var s = await SceneAsync(Clock, lookup.Install);
        using (var first = await s.Payroll.ExportAsync(s.Employee, Org, Mar01, Mar31))
            Assert.True(first.StatusCode == HttpStatusCode.OK, $"the first export: {await first.Content.ReadAsStringAsync()}");
        var recordBefore = await ExportRecordStateAsync(s.Employee);
        var token = Guid.NewGuid();

        HttpResponseMessage response;
        await using (var held = await HeldLock.AcquirePayInputsAsync(ConnectionString))
        {
            var correction = s.Payroll.RecalculateAsync(s.Employee, Org, Mar01, Mar31, token);
            await AssertWaitingAsync(ConnectionString, 1, correction, "the correction of March");
            lookup.Failing = true;
            await held.ReleaseAsync();
            response = await correction;
        }

        using (response)
            await AssertRecordingConfigUnavailableAsync(response);
        Assert.Equal(recordBefore, await ExportRecordStateAsync(s.Employee));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'RetroactiveCorrectionRequested'"));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'RetroactiveCorrectionRequested'"));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", ("t", token)));
    }

    /// <summary>The recording-time 503: exactly the three members the contract names, the fixed sentence and kind, and no
    /// date or employee id (ADR-040 D7). Literals, never the production text.</summary>
    private static async Task AssertRecordingConfigUnavailableAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"expected 503 payroll-config-unavailable, got {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(
            new[] { "error", "kind", "success" },
            root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(RecordingConfigUnavailableSentence, root.GetProperty("error").GetString());
        Assert.Equal("payroll-config-unavailable", root.GetProperty("kind").GetString());
        Assert.DoesNotMatch(new Regex(@"\d{4}-\d{2}-\d{2}"), text);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Requests and reads
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>An HK OK24 draft whose weekly norm and 50 % overtime threshold are 32 (the seed's are 37).</summary>
    private static object Norm32() => ConfigBody("HK", "OK24", weeklyNorm: 32m, overtimeThreshold50: 32m);

    /// <summary>The local-profile PUT for (STY01, HK, OK24) with <c>maxFlexBalance</c> only, <c>If-None-Match: *</c>.</summary>
    private static async Task<HttpResponseMessage> PutProfileAsync(
        HttpClient client, string orgId, string effectiveFrom, decimal maxFlexBalance)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/config/{orgId}/profile/HK/OK24")
        {
            Content = JsonContent.Create(new { effectiveFrom, maxFlexBalance }),
        };
        req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> IfMatchAsync(HttpClient client, HttpMethod method, string url, long version)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(req);
    }

    private Task<long> VersionAsync(string userId) =>
        ScalarAsync<long>("SELECT version FROM users WHERE user_id = @u", ("u", userId));

    /// <summary>The 409 of <c>payroll-inputs-changed</c>: exactly the four members the contract names, the route's
    /// sentence and the month, and no date or employee id anywhere in the body (ADR-040 D7). Literals, never the constants.</summary>
    private static async Task AssertInputsChangedAsync(HttpResponseMessage response, string sentence)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"expected 409 payroll-inputs-changed, got {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(
            new[] { "error", "kind", "month", "success" },
            root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(sentence, root.GetProperty("error").GetString());
        Assert.Equal("payroll-inputs-changed", root.GetProperty("kind").GetString());
        Assert.Equal("2026-03", root.GetProperty("month").GetString());
        Assert.DoesNotMatch(new Regex(@"\d{4}-\d{2}-\d{2}"), text);
    }

    private Task<long> ExportRecordCountAsync(string employee) => CountAsync(
        "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e AND year = 2026 AND month = 3", ("e", employee));

    /// <summary>A refused export wrote no export record, no <c>PayrollExportGenerated</c>, no audit row and no delivery envelope
    /// (an <c>outbox_messages</c> row for <c>payroll</c> that names the employee) for the employee and month; the
    /// calculation's own manifest stays (it was written before the recording began). The envelope is written after the commit
    /// on its own connection, so only a recording that committed reaches it.</summary>
    private async Task AssertExportNothingWrittenAsync(string employee)
    {
        Assert.Equal(0L, await ExportRecordCountAsync(employee));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE destination = 'payroll' AND payload::text LIKE '%' || @e || '%'",
            ("e", employee)));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'",
            ("s", $"employee-{employee}")));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'PayrollExportGenerated' AND target_resource_id = @e",
            ("e", employee)));
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e AND period_start = DATE '2026-03-01'", ("e", employee)));
    }

    /// <summary>Everything a refused correction must leave alone on the record, as one comparable string.</summary>
    private async Task<string> ExportRecordStateAsync(string employee)
    {
        await using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            """
            SELECT content_hash || '|' || current_effective_lines::text || '|' || original_lines::text || '|' ||
                   COALESCE(opening_balance::text, '-') || '|' || COALESCE(closing_balance::text, '-')
            FROM payroll_export_records WHERE employee_id = @e AND year = 2026 AND month = 3
            """, conn);
        cmd.Parameters.AddWithValue("e", employee);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private Task<long> WorklistRowCountAsync(string employee) => CountAsync(
        "SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @e", ("e", employee));

    private sealed record ListedMonth(bool Open, Guid TriggerEventId);

    /// <summary>The employee's worklist holds exactly one row, March 2026, EXPORTED_MONTH, with exactly one trigger of the
    /// given kind.</summary>
    private async Task<ListedMonth> AssertMarchListedAsync(string employee, string triggerKind)
    {
        await using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT kind, year, month, triggers::text, resolved_at IS NULL FROM hr_backdate_worklist WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employee);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "the worklist holds no row for the employee: the change did not list March.");
        Assert.Equal("EXPORTED_MONTH", reader.GetString(0));
        Assert.Equal(2026, reader.GetInt32(1));
        Assert.Equal(3, reader.GetInt32(2));
        var triggers = JsonSerializer.Deserialize<JsonElement>(reader.GetString(3)).EnumerateArray().ToList();
        var open = reader.GetBoolean(4);
        Assert.False(await reader.ReadAsync(), "the worklist holds more than one row for the employee.");
        var trigger = Assert.Single(triggers);
        Assert.Equal(triggerKind, trigger.GetProperty("kind").GetString());
        Guid.TryParse(trigger.GetProperty("eventId").GetString(), out var eventId);
        return new ListedMonth(open, eventId);
    }
}

/// <summary>
/// S148 / TASK-14831-T (Do 11; TASK-14831 item 10, the T14b stack's 5a ruling T13) - a month refused because no employment
/// record covers a day of it answers a NAMED 422 on both calculating routes, not the anonymous 500 it used to.
///
/// <para>
/// <b>In plain language.</b> If an employee's records have a hole (a profile row exists but the agreement-code row that
/// should go with it is missing), payroll cannot say what agreement the person is on, and paying anyway would put a guessed
/// figure on a wage line. It must refuse, and the refusal must say why in terms HR can act on: "an HR administrator must
/// repair the employment record". The answer carries no date, table or employee id (ADR-040 D7), and nothing is calculated,
/// written or exported. The hole is made by deleting the person's agreement-code row, the shape
/// <c>SkemaMonthRecordGapTests</c> seeds.
/// </para>
///
/// <para>
/// <b>Kind F (CI).</b> Red conditions: before TASK-14831 the routes had no arm for the record gap, so the unhandled
/// <c>EmployeeProfileNotFoundException</c> answered an anonymous 500 (the in-process host surfaces the exception to the
/// caller instead); the status assertion fails. No lock is held here: the gap is found by the calculation, before any
/// recording. Docker-gated: CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollEmploymentRecordGapTests : DatedConfigDockerFixture, IDisposable
{
    private const string Org = "STY01";
    private const string GapSentence =
        "No employment record covers a day of this period for this employee. Nothing was calculated, written or exported. " +
        "An HR administrator must repair the employment record.";

    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    private readonly List<PayrollHost> _payrolls = new();

    public void Dispose()
    {
        foreach (var payroll in _payrolls)
            payroll.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async Task<(PayrollHost Payroll, string Employee)> SetUpAsync()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(Db);
        var payroll = PayrollHost.Start(ConnectionString);
        _payrolls.Add(payroll);
        var employee = await SeedPayableEmployeeAsync(ConnectionString, Org, (Feb01, Feb28), (Mar01, Mar31));
        return (payroll, employee);
    }

    /// <summary>A profile row without its agreement-code row: no agreement covers any day of the month.</summary>
    private Task OpenTheGapAsync(string employee) =>
        ExecAsync("DELETE FROM user_agreement_codes WHERE user_id = @e", ("e", employee));

    /// <summary><b>F (CI).</b> <c>/calculate-and-export</c> of a month with a record gap answers 422 with the three-member
    /// body, byte for byte, and writes nothing: no export record, no <c>PayrollExportGenerated</c>, no audit row, no manifest.
    /// <para>Red conditions: before TASK-14831's item 10 the route answers an anonymous 500, so the status assertion in
    /// <c>AssertRecordGapAsync</c> fails.</para></summary>
    [Fact]
    public async Task CalculateAndExport_RecordGap_Answers422Named_NothingWritten()
    {
        var (payroll, employee) = await SetUpAsync();
        await OpenTheGapAsync(employee);

        using var response = await payroll.ExportAsync(employee, Org, Mar01, Mar31);

        await AssertRecordGapAsync(response, employee);
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", employee)));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'", ("s", $"employee-{employee}")));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'PayrollExportGenerated' AND target_resource_id = @e", ("e", employee)));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", employee)));
    }

    /// <summary><b>F (CI).</b> <c>/recalculate</c> of an exported month whose employment record has since gained a gap answers the
    /// same 422 body and writes nothing: the record, the correction event, the audit row, the idempotency marker and the
    /// manifests are as they were.
    /// <para>Red conditions: before TASK-14831's item 10 the route answers an anonymous 500, so the status assertion in
    /// <c>AssertRecordGapAsync</c> fails.</para></summary>
    [Fact]
    public async Task Recalculate_RecordGap_Answers422Named_NothingWritten()
    {
        var (payroll, employee) = await SetUpAsync();
        using (var first = await payroll.ExportAsync(employee, Org, Mar01, Mar31))
            Assert.True(first.StatusCode == HttpStatusCode.OK, $"the first export: {await first.Content.ReadAsStringAsync()}");
        var manifestsBefore = await CountAsync("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", employee));
        var recordBefore = await ScalarAsync<string>(
            "SELECT content_hash || '|' || current_effective_lines::text FROM payroll_export_records WHERE employee_id = @e", ("e", employee));
        await OpenTheGapAsync(employee);
        var token = Guid.NewGuid();

        using var response = await payroll.RecalculateAsync(employee, Org, Mar01, Mar31, token);

        await AssertRecordGapAsync(response, employee);
        Assert.Equal(recordBefore, await ScalarAsync<string>(
            "SELECT content_hash || '|' || current_effective_lines::text FROM payroll_export_records WHERE employee_id = @e", ("e", employee)));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE event_type = 'RetroactiveCorrectionRequested'"));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM audit_projection WHERE event_type = 'RetroactiveCorrectionRequested'"));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", ("t", token)));
        Assert.Equal(manifestsBefore, await CountAsync("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", employee)));
    }

    /// <summary>The 422 of <c>payroll-employment-record-gap</c>: exactly three members, the ruled sentence and kind, and no
    /// date or employee id (ADR-040 D7). Literals, never the production text. Shared with the record-coverage facts.</summary>
    internal static async Task AssertRecordGapAsync(HttpResponseMessage response, string employee)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422 payroll-employment-record-gap, got {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal(
            new[] { "error", "kind", "success" },
            root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(GapSentence, root.GetProperty("error").GetString());
        Assert.Equal("payroll-employment-record-gap", root.GetProperty("kind").GetString());
        // ADR-040 D7: no date, no employee id.
        Assert.DoesNotMatch(new Regex(@"\d{4}-\d{2}-\d{2}"), text);
        Assert.DoesNotContain(employee, text, StringComparison.Ordinal);
    }
}
