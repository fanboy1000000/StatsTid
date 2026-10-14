using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Segmentation.ReplayTestKit;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S145 / TASK-14515 — the replay contract's pins (refinement A7; rulings R14, R15, R41, R42, R56,
/// R60). Written from the spec BEFORE the code exists (TASK-14513), so most of this file does not
/// compile against today's master; it merges at the A.5 gate.
///
/// <para>
/// <b>Plain language.</b> A payslip must be provable after the fact. "Replay" recomputes a past
/// calculation from what that calculation recorded about its own inputs, never from today's data,
/// and it must refuse BY NAME when it cannot do that faithfully. These facts change the world after
/// a calculation (a backdated fraction, a re-saved cell, a removed end date, a re-opened previous
/// month, a tampered row) and then check two things: replay still gives the identical answer where
/// the contract says it must, and it refuses with the exact promised name where it must not.
/// </para>
///
/// <para>
/// <b>How a comparison can fail (C-R4, R60).</b> Both PCS instances of every fact are built with the
/// test-local <see cref="ReplayEchoRuleEngine"/>, never the real engine and never the default stub.
/// The echo writes every request entry (context days included), each piece's part-time fraction,
/// the settlement cutoff (null encoded as the word NULL) and the per-segment profile into the rule
/// results. "Byte-identical" and "differs" compare the FULL <see cref="PeriodCalculationResult"/>
/// (every rule result's line items), not the export lines; "differs" is compared modulo the
/// manifest id, which a fresh calculation always mints anew (a comparison that included it could
/// never fail).
/// </para>
///
/// <para>
/// <b>Conventions.</b> Docker-gated; reported as CI-verified, not locally green. Both PCS instances
/// of a fact share the same fakes (dated profile history, window), so a replay that wrongly asked
/// today's world would see the change. <c>// S145 R26:</c> every fact that is not about the
/// prior-month check treats the previous month as APPROVED through the fake. Each fact asserts
/// only what its own mutation can change: extra sensitivity would break the evidence-run partition
/// (R59). Assumed API names are listed in the task report (re-spawn 1 settles them).
/// </para>
///
/// <para>
/// <b>Calendar used throughout (2026).</b> 31 Aug is a Monday; 1 Sep a Tuesday; 28 Sep a Monday;
/// 30 Sep a Wednesday; 1 Oct a Thursday; 4 Oct a Sunday. September's opening week is Mon 31 Aug –
/// Sun 6 Sep (context day: 31 Aug). October's opening week is Mon 28 Sep – Sun 4 Oct (context days:
/// 28, 29, 30 Sep).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ReplayFrozenInputsTests : IAsyncLifetime
{
    private const string Emp = "EMP-RPL-14515";

    private static readonly DateOnly Aug31 = new(2026, 8, 31);
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep15 = new(2026, 9, 15);
    private static readonly DateOnly Sep16 = new(2026, 9, 16);
    private static readonly DateOnly Sep28 = new(2026, 9, 28);
    private static readonly DateOnly Sep29 = new(2026, 9, 29);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    private TestFixtures.DockerHarness _harness = null!;
    private HoursSeeder _seed = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await PrepareDatabaseAsync(_harness);
        _seed = new HoursSeeder(_harness);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // Fixtures
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record SeptemberRun(
        PeriodCalculationService.PeriodCalculationOutcome Forward,
        Dictionary<DateOnly, Guid> EntryIds,
        Guid AbsenceId);

    /// <summary>
    /// September 2026 for the stayer: a 7.4 h SKEMA entry on every weekday 31 Aug – 30 Sep except
    /// Wed 16 Sep, which carries a 7.4 h LEAVE_WITH_PAY absence (a non-entitlement type, so no
    /// quota is involved); opening flex balance 5 h (a non-zero value, so a replay that ignored the
    /// frozen balance would show it in the echo).
    /// </summary>
    private async Task<SeptemberRun> CalculateSeptemberAsync(PeriodCalculationService pcs)
    {
        var ids = await _seed.WeekdaysAsync(Emp, Aug31, Sep30, 7.4m, Sep16);
        var absenceId = await _seed.AbsenceAsync(Emp, Sep16, AbsenceTypes.LeaveWithPay, 7.4m);
        var forward = await ForwardAsync(pcs, Emp, Sep1, Sep30, openingFlexBalance: 5m);
        return new SeptemberRun(forward, ids, absenceId);
    }

    /// <summary>The 7.4 h SKEMA weekday entries 28 Sep – 31 Oct (October's closing-week fixture).</summary>
    private async Task<Dictionary<DateOnly, Guid>> SeedOctoberAsync(decimal sep28Hours = 7.4m)
    {
        var ids = await _seed.WeekdaysAsync(Emp, Sep28, Oct31, 7.4m, Sep28);
        ids[Sep28] = await _seed.EntryAsync(Emp, Sep28, sep28Hours);
        return ids;
    }

    private PeriodCalculationService Pcs(
        ReplayEchoRuleEngine engine,
        FakeApprovalReader approval,
        DatedProfileResolver? dated = null,
        StatsTid.SharedKernel.Interfaces.IEmploymentWindowResolver? window = null) =>
        BuildPcs(_harness, engine, approval, RealHoursReader(_harness), dated, window);


    private async Task AssertSupersededAsync(Guid eventId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "SELECT superseded_by_outbox_id FROM time_entries_projection WHERE event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        var scalar = await cmd.ExecuteScalarAsync();
        Assert.True(scalar is not null and not DBNull,
            "fixture precondition: the re-save must have superseded the bound row (C-R2)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RP1 — the dated profile at the frozen segment start is not re-read
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RP1.</b> September is calculated with a part-time fraction of 1.0 in force from the
    /// beginning. After the calculation a row is backdated effective EXACTLY 1 September, the
    /// frozen segment start, with fraction 0.5. A replay by manifest id must still be
    /// byte-identical to the original.
    ///
    /// <para><b>Expected, derived from the spec.</b> Refinement A7, "Per-piece inputs … Frozen …
    /// the dated read at <c>PeriodCalculationService.cs:570</c> is not made at replay", and the
    /// criterion at :661: "Backdate a row effective <b>exactly 1 September</b>, the frozen segment
    /// start … Replay is byte-identical. Today the first edit changes the replay through :570."
    /// The echo writes the per-segment profile's fraction into the result, so a replay that
    /// re-resolved the profile at 1 September (now 0.5) would echo 0.5 against the frozen 1.</para>
    ///
    /// <para><b>Red conditions.</b> MD-31 (replay's per-segment lane re-resolves the profile at
    /// <c>segment.StartDate</c>): the per-segment <c>ECHO_PROFILE</c> lines then carry 0.5 and the
    /// full-result equality fails. Not MD-36 (31 August is not touched here).</para>
    /// </summary>
    [Fact]
    public async Task Replay_AfterBackdateAtFrozenStart1Sep_ByteIdentical()
    {
        var dated = new DatedProfileResolver(fractionFromTheBeginning: 1.0m);
        var approval = new FakeApprovalReader(); // S145 R26: M-1 is treated as APPROVED
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval, dated);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval, dated);

        var run = await CalculateSeptemberAsync(pcs);

        dated.AddRow(Sep1, 0.5m); // the backdate: in force from exactly the frozen start

        var replay = await replayPcs.ReplayAsync(run.Forward.ManifestId);

        Assert.True(replay.Success);
        Assert.Equal(Canonical(run.Forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RP2 — a context day's dated fraction is not re-read
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RP2.</b> After the September calculation, 31 August's dated fraction is changed to 0.5
    /// (a row effective 31 Aug, followed by a row effective 1 Sep that restores 1.0, so 1 September
    /// and every later day are unchanged). 31 August is a CONTEXT day of September (the Monday of
    /// its opening week). Replay by manifest id must be byte-identical.
    ///
    /// <para><b>Expected, derived from the spec.</b> Refinement A7 table: per-piece inputs are
    /// frozen for "plan segments <b>and</b> context days", and "both lanes build the profile from the
    /// piece covering the day" at replay; criterion :661 "change 31 August's dated fraction (a
    /// context day). Replay is byte-identical." The echo writes each piece's fraction, so the frozen
    /// 31 August piece (1.0) is visible in the result.</para>
    ///
    /// <para><b>Red conditions.</b> MD-36 (replay's per-plan lane re-resolves the context pieces'
    /// fractions from the dated resolver): the 31 August piece then echoes 0.5. Not MD-31: the
    /// segment start (1 Sep) resolves to the restoring row, 1.0, as before.</para>
    /// </summary>
    [Fact]
    public async Task Replay_After31AugContextFractionChange_ByteIdentical()
    {
        var dated = new DatedProfileResolver(fractionFromTheBeginning: 1.0m);
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval, dated);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval, dated);

        var run = await CalculateSeptemberAsync(pcs);

        dated.AddRow(Aug31, 0.5m);
        dated.AddRow(Sep1, 1.0m);

        var replay = await replayPcs.ReplayAsync(run.Forward.ManifestId);

        Assert.True(replay.Success);
        Assert.Equal(Canonical(run.Forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RP3 — a genuinely absent recorded id is refused by name
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RP3.</b> After September is calculated, the row of one bound entry (Tue 15 Sep) is
    /// DELETED outright (the product never removes rows; the test does, to make an id genuinely
    /// absent). Replay must be refused with <c>replay-entries-missing</c>.
    ///
    /// <para><b>Expected, derived from the spec.</b> Refinement A7 table, hours rows: re-read by
    /// recorded event id; "Only a <b>genuinely absent</b> id is refused: <c>replay-entries-missing</c>
    /// (R14 as amended)". The refusal must name the MISSING id, not a digest mismatch (the digest
    /// would also differ, so only the exact name proves the id check ran first).</para>
    ///
    /// <para><b>Red conditions.</b> MD-37 (replay proceeds past an absent recorded id): the replay
    /// then reaches the digest comparison and refuses <c>replay-entries-digest-mismatch</c> instead,
    /// so the exact-name assertion fails. MD-38 keeps the id check, so it does not redden this fact.</para>
    /// </summary>
    [Fact]
    public async Task Replay_RecordedEntryIdAbsent_RefusesReplayEntriesMissing()
    {
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval);

        var run = await CalculateSeptemberAsync(pcs);

        await ExecAsync(_harness, "DELETE FROM time_entries_projection WHERE event_id = @id",
            ("id", run.EntryIds[Sep15]));

        await AssertRefusedAsync("replay-entries-missing",
            () => replayPcs.ReplayAsync(run.Forward.ManifestId));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RP4 — a superseded bound row is NOT absent
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RP4.</b> The bound cell Tue 15 Sep (7.4 h) is re-saved to 6.0 h at repository level
    /// through <c>InsertAsync</c> as a SKEMA row, which supersedes the row the manifest recorded
    /// (C-R2). Replay by manifest id must still be byte-identical: it reads the superseded row by
    /// its recorded id.
    ///
    /// <para><b>Expected, derived from the spec.</b> A0 constraint 5 and the A7 table: a row
    /// superseded by a later re-save is still read, because <c>GetByEventIdsAsync</c> ignores the
    /// current-row filter; "a superseded row does <b>not</b> count as absent" (criterion :662). The
    /// original 7.4 h row's content is unchanged, so its digest still matches.</para>
    ///
    /// <para><b>Red conditions.</b> MD-15 (the by-id read gains the current-row predicate): the
    /// superseded id is then not found and replay refuses <c>replay-entries-missing</c>. MD-38
    /// (replay reads the current set instead of by id): the current set no longer holds the
    /// recorded row, so replay refuses or throws. Both are refusals and show whatever the engine
    /// encodes (C-R4).</para>
    /// </summary>
    [Fact]
    public async Task Replay_SupersededBoundRow_IsNotAbsent_ReplaysByteIdentical()
    {
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval);

        var run = await CalculateSeptemberAsync(pcs);

        await _seed.EntryAsync(Emp, Sep15, 6.0m); // same cell (employee, date, task T1), SKEMA: supersedes
        await AssertSupersededAsync(run.EntryIds[Sep15]);

        var replay = await replayPcs.ReplayAsync(run.Forward.ManifestId);

        Assert.True(replay.Success);
        Assert.Equal(Canonical(run.Forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RP5 / RP6 — tampered rows are refused by name
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RP5.</b> After September is calculated, one bound entry row (Tue 15 Sep) is altered in
    /// place, 7.4 h to 7.3 h. Replay must be refused with <c>replay-entries-digest-mismatch</c>.
    ///
    /// <para><b>Expected, derived from the spec.</b> A7 table: "The digests are recomputed, and a
    /// mismatch is refused: <c>replay-entries-digest-mismatch</c>"; criterion :662 "a tampered row
    /// (digest mismatch, for entries and for absences)". The id is present, so the refusal is the
    /// digest's, not the missing-id one.</para>
    ///
    /// <para><b>Red conditions.</b> MD-32 (both digest comparisons skipped): the replay then
    /// succeeds, so no refusal is raised and the assertion fails. Not MD-38 (refused either way).</para>
    /// </summary>
    [Fact]
    public async Task Replay_TamperedEntryRow_RefusesEntriesDigestMismatch()
    {
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval);

        var run = await CalculateSeptemberAsync(pcs);

        await ExecAsync(_harness, "UPDATE time_entries_projection SET hours = 7.3 WHERE event_id = @id",
            ("id", run.EntryIds[Sep15]));

        await AssertRefusedAsync("replay-entries-digest-mismatch",
            () => replayPcs.ReplayAsync(run.Forward.ManifestId));
    }

    /// <summary>
    /// <b>RP6.</b> As RP5 for the absence side: the LEAVE_WITH_PAY row on Wed 16 Sep is altered in
    /// place, 7.4 h to 6.0 h. Replay must be refused with <c>replay-absences-digest-mismatch</c>
    /// (the entries are untouched, so the entries digest still matches and the name must be the
    /// absences one).
    ///
    /// <para><b>Expected, derived from the spec.</b> A7 table and criterion :662, as RP5, for
    /// absences. The absences digest line is <c>EventId|Date|AbsenceType|Hours</c> (refinement
    /// :388); it does not include <c>Feriedage</c>, so QUAL-197's rebuild drift cannot trip it (the
    /// guard for that is the Unit pin <c>AbsencesDigest_IgnoresFeriedage…</c>).</para>
    ///
    /// <para><b>Red conditions.</b> MD-32 (both digest comparisons skipped): the replay succeeds and
    /// no refusal is raised.</para>
    /// </summary>
    [Fact]
    public async Task Replay_TamperedAbsenceRow_RefusesAbsencesDigestMismatch()
    {
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval);

        var run = await CalculateSeptemberAsync(pcs);

        await ExecAsync(_harness, "UPDATE absences_projection SET hours = 6.0 WHERE event_id = @id",
            ("id", run.AbsenceId));

        await AssertRefusedAsync("replay-absences-digest-mismatch",
            () => replayPcs.ReplayAsync(run.Forward.ManifestId));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RP7 / RP8 — code changes are refused by name
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RP7.</b> September is calculated against an engine whose config fingerprint is
    /// <c>["stub"]</c>; the replay PCS talks to an engine that answers <c>["stub-2"]</c> (the
    /// refinement's "synthetic config", rev 3.4 :662). The stored record is NOT edited. Replay must
    /// be refused with <c>replay-config-changed</c>.
    ///
    /// <para><b>Expected, derived from the spec.</b> A7 table: the engine-returned fingerprint "must
    /// match: <c>replay-config-changed</c>"; configs are code, so a changed fingerprint is refused by
    /// name rather than silently recomputed.</para>
    ///
    /// <para><b>Red conditions.</b> MD-33 (the fingerprint comparison skipped): the replay succeeds
    /// and no refusal is raised.</para>
    /// </summary>
    [Fact]
    public async Task Replay_ConfigFingerprintChanged_RefusesReplayConfigChanged()
    {
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(fingerprints: new[] { "stub-2" }), approval);

        var run = await CalculateSeptemberAsync(pcs);

        await AssertRefusedAsync("replay-config-changed",
            () => replayPcs.ReplayAsync(run.Forward.ManifestId));
    }

    /// <summary>
    /// <b>RP8.</b> As RP7 for the evaluation-model marker: the replay PCS's engine answers
    /// <c>weekly-timeline-v2</c> against the recorded <c>weekly-timeline-v1</c>. Replay must be
    /// refused with <c>replay-evaluation-model-changed</c>.
    ///
    /// <para><b>Expected, derived from the spec.</b> A7 table, "Evaluation-model marker … Must match:
    /// <c>replay-evaluation-model-changed</c>". The fingerprints are left equal, so only the marker
    /// differs and the refusal name must be the marker's.</para>
    ///
    /// <para><b>Red conditions.</b> MD-34 (the marker comparison skipped): the replay succeeds.</para>
    /// </summary>
    [Fact]
    public async Task Replay_EvaluationModelChanged_RefusesReplayEvaluationModelChanged()
    {
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(evaluationModel: "weekly-timeline-v2"), approval);

        var run = await CalculateSeptemberAsync(pcs);

        await AssertRefusedAsync("replay-evaluation-model-changed",
            () => replayPcs.ReplayAsync(run.Forward.ManifestId));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RP9 — a manifest without the frozen inputs is refused by name
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RP9.</b> After September is calculated, the manifest row's <c>plan_inputs_jsonb</c> slot is
    /// set to NULL (the shape of a manifest written before S145). Replay must be refused with
    /// <c>replay-manifest-without-frozen-inputs</c>. The fact first asserts the forward calculation
    /// DID populate the slot, so the refusal cannot pass by the slot never having been written.
    ///
    /// <para><b>Expected, derived from the spec.</b> A7 table last row: "Manifest without these …
    /// Refused: <c>replay-manifest-without-frozen-inputs</c>"; the ADR-016:242 exception: pre-S145
    /// manifests are refused, not replayable.</para>
    ///
    /// <para><b>Red conditions.</b> MD-35 (a null <c>PlanInputs</c> falls back to live inputs): the
    /// replay then succeeds from today's data and no refusal is raised.</para>
    /// </summary>
    [Fact]
    public async Task Replay_ManifestWithoutPlanInputs_RefusesManifestWithoutFrozenInputs()
    {
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval);

        var run = await CalculateSeptemberAsync(pcs);

        Assert.NotNull(await ReadPlanInputsAsync(_harness, run.Forward.ManifestId));
        await ExecAsync(_harness, "UPDATE segment_manifests SET plan_inputs_jsonb = NULL WHERE manifest_id = @id",
            ("id", run.Forward.ManifestId));

        await AssertRefusedAsync("replay-manifest-without-frozen-inputs",
            () => replayPcs.ReplayAsync(run.Forward.ManifestId));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RC1 / RC2 — reconstruction without resupplying anything
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RC1.</b> October is calculated (closing-week fixture; September APPROVED through the
    /// fake). Afterwards a NEW entry is registered on Tue 29 September, a CONTEXT day of October
    /// (its opening week is Mon 28 Sep – Sun 4 Oct), as a SKEMA row of a different cell (task T2,
    /// 2.0 h). Replay October BY MANIFEST ID ALONE must be byte-identical to the original; a FRESH
    /// calculation of October must differ.
    ///
    /// <para><b>Expected, derived from the spec.</b> Refinement :666-670 ("Register a new 29
    /// September entry at repository level … Replay October by manifest id alone: byte-identical to
    /// the original. A fresh forward calculation of October gives a different result. This is PCS
    /// <c>CalculateAsync</c>"). The replay reads the recorded id set, which does not contain the new
    /// row. The fresh read covers the context days, so the echo of its entries holds one more line
    /// (the 2.0 h row), and the fresh result differs modulo the manifest id (C-R1, C-R4).</para>
    ///
    /// <para><b>Red conditions.</b> MD-20 (the hours read starts on the first day of the period's
    /// month): the forward read and the fresh read both start on 1 October and miss 29 September,
    /// so the fresh result does not differ. MD-38 (replay reads the current set over the window):
    /// the new row enters the replay's set and its digest no longer matches the recorded one, so
    /// replay refuses. Not MD-15 (nothing is superseded here).</para>
    /// </summary>
    [Fact]
    public async Task Reconstruction_NewEntryAfterCalc_ReplayByManifestIdOnly_ByteIdentical_FreshCalcDiffers()
    {
        var approval = new FakeApprovalReader(); // S145 R26: September treated as APPROVED
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval);

        await SeedOctoberAsync();
        var forward = await ForwardAsync(pcs, Emp, Oct1, Oct31);

        await _seed.EntryAsync(Emp, Sep29, 2.0m, taskId: "T2"); // a new cell on a context day

        var replay = await replayPcs.ReplayAsync(forward.ManifestId);
        var fresh = await FreshAsync(pcs, Emp, Oct1, Oct31);

        Assert.True(replay.Success);
        Assert.Equal(Canonical(forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));
        Assert.True(fresh.Success);
        Assert.NotEqual(Canonical(forward.Result, withManifestIds: false), Canonical(fresh, withManifestIds: false));
    }

    /// <summary>
    /// <b>RC2.</b> October is calculated with the bound cell Mon 28 September at 9.0 h (a context
    /// day). Afterwards the cell is re-saved at repository level, as a SKEMA row through
    /// <c>InsertAsync</c> (C-R2), from 9.0 h to 7.4 h, which supersedes the row the manifest
    /// recorded. Replay by manifest id alone must be byte-identical (it reads the superseded 9.0 h
    /// row by id); a FRESH calculation of October must differ (it reads the current 7.4 h row).
    ///
    /// <para><b>Expected, derived from the spec.</b> Refinement :671: "Re-save a <i>bound</i> cell …
    /// for example 28 September from 9 h to 7.4 h. This supersedes the row the manifest recorded.
    /// Replay is still byte-identical, because it reads the superseded row by id. A fresh
    /// <c>CalculateAsync</c> differs." The echo encodes the 28 September entry with its hours, so
    /// 9.0 versus 7.4 is visible in the compared result.</para>
    ///
    /// <para><b>Red conditions.</b> MD-20 (read starts at 1 October): the forward read and the fresh
    /// read both miss 28 September, so the fresh result does not differ. MD-15 (the by-id read gains
    /// the current-row predicate) and MD-38 (replay reads the current set): the superseded row is
    /// not found, so replay refuses <c>replay-entries-missing</c>. Not MD-03: under it the fresh
    /// half encodes both 28 September rows (9.0 and 7.4) and still differs from the frozen 9.0 only
    /// (C-R4).</para>
    /// </summary>
    [Fact]
    public async Task Reconstruction_BoundCellResaved_ReplayByteIdentical_FreshCalcDiffers()
    {
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval);

        var ids = await SeedOctoberAsync(sep28Hours: 9.0m);
        var forward = await ForwardAsync(pcs, Emp, Oct1, Oct31);

        await _seed.EntryAsync(Emp, Sep28, 7.4m); // same cell (T1), SKEMA: supersedes the 9.0 h row
        await AssertSupersededAsync(ids[Sep28]);

        var replay = await replayPcs.ReplayAsync(forward.ManifestId);
        var fresh = await FreshAsync(pcs, Emp, Oct1, Oct31);

        Assert.True(replay.Success);
        Assert.Equal(Canonical(forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));
        Assert.True(fresh.Success);
        Assert.NotEqual(Canonical(forward.Result, withManifestIds: false), Canonical(fresh, withManifestIds: false));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RL — the frozen settlement cutoff (R41)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RL (R41).</b> September is calculated for the 30 September leaver of
    /// <c>Leaver_LastDayWed30Sep_…</c>: the employment window ends Wed 30 September, so the frozen
    /// settlement cutoff is 30 September. The end date is then REMOVED (the window becomes
    /// open-ended). A replay must be byte-identical (it uses the frozen cutoff); a FRESH calculation
    /// must differ (its cutoff is now null, so, under the real engine, September pays nothing for
    /// 28-30 September).
    ///
    /// <para><b>Expected, derived from the spec.</b> R41: the cutoff is "the <c>End</c> of the
    /// employment window (the inclusive last employed day …) when <c>End ≤ periodEnd</c>; otherwise
    /// null"; it "must be frozen" because a leaver's and a stayer's pieces and segments are identical
    /// for the month; "replay feeds it to the core". The first assertion pins the derivation itself
    /// (the forward echo reads <c>ECHO_CUTOFF|2026-09-30</c>); the echo writes null as the word NULL,
    /// distinct from any date, so the fresh result's <c>ECHO_CUTOFF|NULL</c> makes it differ.</para>
    ///
    /// <para><b>Red conditions.</b> MD-52 (the cutoff derivation always null): the forward echo
    /// already reads NULL, so the derivation assertion fails (and the fresh half could no longer
    /// differ). MD-43 (cutoff = the last piece day, which is 30 September before and after the end
    /// date is removed): the fresh half echoes the same cutoff and "differs" fails. MD-44 (replay
    /// re-derives the cutoff from the live window): the replay encodes the live NULL instead of the
    /// frozen 30 September and byte-identity fails. Both PCS instances share one window object, so
    /// the re-derivation is visible.</para>
    /// </summary>
    [Fact]
    public async Task Replay_LeaverEndDateRemovedAfterCalc_ByteIdentical_FreshCalcDiffers()
    {
        var window = new MutableWindowResolver(new EmploymentWindow(null, Sep30));
        var approval = new FakeApprovalReader(); // S145 R26
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval, window: window);
        var replayPcs = Pcs(new ReplayEchoRuleEngine(), approval, window: window);

        await _seed.WeekdaysAsync(Emp, Aug31, Sep30);
        var forward = await ForwardAsync(pcs, Emp, Sep1, Sep30);
        Assert.Contains(forward.Result.RuleResults.SelectMany(r => r.LineItems),
            l => l.TimeType == "ECHO_CUTOFF|2026-09-30");

        window.Window = new EmploymentWindow(null, null); // the end date is removed

        var replay = await replayPcs.ReplayAsync(forward.ManifestId);
        var fresh = await FreshAsync(pcs, Emp, Sep1, Sep30);

        Assert.True(replay.Success);
        Assert.Equal(Canonical(forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));
        Assert.True(fresh.Success);
        Assert.Contains(fresh.RuleResults.SelectMany(r => r.LineItems), l => l.TimeType == "ECHO_CUTOFF|NULL");
        Assert.NotEqual(Canonical(forward.Result, withManifestIds: false), Canonical(fresh, withManifestIds: false));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RS — replay never runs admission checks (R42, R56)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>RS (R42, R56).</b> October is calculated forward on a FIRST PCS whose approval reader is
    /// the fake answering APPROVED for September. October is then replayed through a SECOND PCS built
    /// with SENTINELS, so each of R42's four sources fails at once if consulted: an
    /// <c>IPriorPeriodApprovalReader</c> that throws; an <c>IEmploymentWindowResolver</c> that
    /// throws; an <c>IRecordedHoursReader</c> whose <c>ReadCurrentAsync</c> throws while
    /// <c>ReadByEventIdsAsync</c> delegates to the real <c>ProjectionHoursReader</c>; and a real
    /// <c>UserAgreementCodeRepository</c> over an unreachable database (port 1, timeout 1 s), since a
    /// null repository would be silently skipped (<c>PeriodCalculationService.cs:1056</c>) and prove
    /// nothing. The second PCS has no profile resolver (C-R3). The replay must be byte-identical.
    /// Then a FRESH calculation of October on the FIRST, non-sentinel PCS, with its fake swapped to
    /// answer EMPLOYEE_APPROVED (September reopened), must throw
    /// <see cref="PriorMonthNotApprovedException"/> naming September.
    ///
    /// <para><b>Expected, derived from the spec.</b> R42: "replay proves what <i>was</i> calculated;
    /// whether September is approved <i>today</i> cannot change whether October's result then is
    /// reproducible"; replay enters evaluation only and never consults the four sources. R16/R21:
    /// the previous month must be APPROVED (EMPLOYEE_APPROVED is not enough), and a refusal is
    /// <c>PriorMonthNotApprovedException</c> carrying M-1's period (1-30 September). R26: only a
    /// dependency that fails when consulted proves "not consulted". The fresh half runs on the
    /// non-sentinel PCS because the window sentinel would otherwise throw before the 409 is reached.</para>
    ///
    /// <para><b>Red conditions.</b> MD-45 (replay consults a registered approval reader): the approval
    /// sentinel throws. MD-44 (replay re-derives the cutoff from the live window): the window sentinel
    /// throws. MD-38 (replay reads <c>ReadCurrentAsync</c>): the hours sentinel throws. MD-22 (the
    /// prior-month predicate accepts EMPLOYEE_APPROVED): the fresh calculation no longer throws.</para>
    /// </summary>
    [Fact]
    public async Task Replay_AfterPriorMonthReopened_ByteIdentical_FreshCalcRefused409()
    {
        var approval = new FakeApprovalReader("APPROVED"); // S145 R26: September APPROVED at forward time
        var pcs = Pcs(new ReplayEchoRuleEngine(), approval);

        await SeedOctoberAsync();
        var forward = await ForwardAsync(pcs, Emp, Oct1, Oct31);

        var sentinelPcs = BuildPcs(
            _harness,
            new ReplayEchoRuleEngine(),
            approval: new ThrowingApprovalReader(),
            hours: new CurrentReadSentinelHoursReader(RealHoursReader(_harness)),
            profileResolver: null, // C-R3: the replay PCS has no profile resolver
            windowResolver: new ThrowingWindowResolver(),
            agreementCodeRepo: new UserAgreementCodeRepository(new DbConnectionFactory(UnreachableConnectionString)));

        var replay = await sentinelPcs.ReplayAsync(forward.ManifestId);

        Assert.True(replay.Success);
        Assert.Equal(Canonical(forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));

        approval.Status = "EMPLOYEE_APPROVED"; // September reopened
        var ex = await Assert.ThrowsAsync<PriorMonthNotApprovedException>(
            () => FreshAsync(pcs, Emp, Oct1, Oct31));
        Assert.Equal(new DateOnly(2026, 9, 1), ex.PeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 30), ex.PeriodEnd);
    }
}
