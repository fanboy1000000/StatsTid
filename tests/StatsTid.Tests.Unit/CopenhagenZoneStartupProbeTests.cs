using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S142 / TASK-14211a (owner ruling OQ-11) — the Copenhagen time-zone STARTUP PROBE.
///
/// Plain-language: StatsTid now computes every Danish business date (employment start/end, the
/// §21 stk.2 vacation-transfer deadline, settlement and leaver boundaries) from the
/// Europe/Copenhagen calendar day rather than the UTC one. If the host it runs on has no real
/// Copenhagen time zone — a slim container image without tzdata, say — the old code quietly fell
/// back to UTC and carried on, which would record those dates a day early for anyone working
/// after local midnight. The owner ruled that the application must refuse to start instead.
///
/// These tests exist because a guard nobody can prove rejects a bad input is worth nothing. The
/// load-bearing ones are the three REJECTION cases: the probe must refuse UTC, a zone hardcoded
/// to +01:00, and a zone hardcoded to +02:00. The +01:00 case is the sharpest — that zone is
/// EXACTLY the QUAL-005 bug (right all winter, a day wrong all summer), and a probe that only
/// checked a winter instant would happily pass it. Checking both seasons is what makes the guard
/// able to fail at all.
/// </summary>
public class CopenhagenZoneStartupProbeTests
{
    /// <summary>
    /// A zone with a FIXED offset and no adjustment rules — the shape a hardcoded "+01:00 is
    /// Denmark" assumption has, and the shape a stripped tz database can degrade to.
    /// </summary>
    private static TimeZoneInfo FixedOffsetZone(int hours)
    {
        var label = "UTC+" + hours.ToString("00") + ":00";
        return TimeZoneInfo.CreateCustomTimeZone(
            id: "StatsTidTestFixed" + hours.ToString("00"),
            baseUtcOffset: TimeSpan.FromHours(hours),
            displayName: label + " (test, no DST rules)",
            standardDisplayName: label);
    }

    // ── The three rejections. These are the whole point of the task. ──

    /// <summary>
    /// UTC reports +00:00 in both seasons. This is the fallback the old
    /// <c>ResolveCopenhagenZone()</c> returned, and the one that would silently reinstate the
    /// off-by-one-day defect S142 removed.
    /// </summary>
    [Fact]
    public void EnsureZoneIsCopenhagen_Utc_IsRejected()
    {
        var ex = Assert.Throws<InvalidTimeZoneException>(
            () => CopenhagenBusinessDate.EnsureZoneIsCopenhagen(TimeZoneInfo.Utc));

        Assert.Contains("+00:00", ex.Message);
        Assert.Contains(CopenhagenBusinessDate.IanaZoneId, ex.Message);
    }

    /// <summary>
    /// The QUAL-005 shape: correct every winter, a full day wrong every summer. A winter-only
    /// probe PASSES this zone, which is why the probe must check both offsets — the guard would
    /// otherwise be incapable of failing on the exact defect it was written for.
    /// </summary>
    [Fact]
    public void EnsureZoneIsCopenhagen_HardcodedPlusOne_IsRejected()
    {
        var ex = Assert.Throws<InvalidTimeZoneException>(
            () => CopenhagenBusinessDate.EnsureZoneIsCopenhagen(FixedOffsetZone(1)));

        // Reported +01:00 in BOTH seasons; the summer half is what convicts it.
        Assert.Contains("+01:00", ex.Message);
        Assert.Contains("+02:00", ex.Message);
    }

    /// <summary>
    /// The mirror defect: permanent CEST. Right all summer, a day wrong all winter. Rejected by
    /// the same two-offset check.
    /// </summary>
    [Fact]
    public void EnsureZoneIsCopenhagen_HardcodedPlusTwo_IsRejected()
    {
        var ex = Assert.Throws<InvalidTimeZoneException>(
            () => CopenhagenBusinessDate.EnsureZoneIsCopenhagen(FixedOffsetZone(2)));

        Assert.Contains("+02:00", ex.Message);
        Assert.Contains("+01:00", ex.Message);
    }

    /// <summary>
    /// Proves the two-offset rule is doing the rejecting, not some incidental property of the
    /// custom zones above: a winter-only assertion cannot tell a hardcoded +01:00 zone from the
    /// real Copenhagen zone, while a summer instant separates them immediately. This test would
    /// still pass if the probe were broken — it characterises WHY the probe is shaped as it is,
    /// and the three rejection tests above are what actually pin the behaviour.
    /// </summary>
    [Fact]
    public void WinterOnlyCheck_CannotDistinguish_HardcodedPlusOne_FromRealZone()
    {
        var winterInstant = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var summerInstant = new DateTimeOffset(2024, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var hardcoded = FixedOffsetZone(1);
        var real = CopenhagenBusinessDate.Zone;

        Assert.Equal(real.GetUtcOffset(winterInstant), hardcoded.GetUtcOffset(winterInstant));
        Assert.NotEqual(real.GetUtcOffset(summerInstant), hardcoded.GetUtcOffset(summerInstant));
    }

    // ── The acceptance side: a correctly configured host must still boot. ──

    /// <summary>
    /// The real resolved zone passes the probe. If this fails on a dev machine or a CI runner,
    /// that host genuinely lacks usable timezone data and the exception message says what to
    /// install — which is the behaviour being shipped, not a test defect.
    /// </summary>
    [Fact]
    public void EnsureZoneIsCopenhagen_RealResolvedZone_IsAccepted()
    {
        CopenhagenBusinessDate.EnsureZoneIsCopenhagen(CopenhagenBusinessDate.Zone);
    }

    /// <summary>
    /// The startup gate itself — the exact call <c>Program.cs</c> makes before building the host.
    /// Green here means a correctly configured host still starts.
    /// </summary>
    [Fact]
    public void EnsureHostZoneResolves_OnThisHost_DoesNotThrow()
    {
        CopenhagenBusinessDate.EnsureHostZoneResolves();
    }

    /// <summary>
    /// The resolved zone observes real Danish DST: +01:00 (CET) in winter, +02:00 (CEST) in
    /// summer. Stated directly so the expectation the probe encodes is readable without decoding
    /// the probe.
    /// </summary>
    [Fact]
    public void ResolvedZone_ObservesBothDanishOffsets()
    {
        var zone = CopenhagenBusinessDate.Zone;

        Assert.Equal(TimeSpan.FromHours(1), zone.GetUtcOffset(new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(TimeSpan.FromHours(2), zone.GetUtcOffset(new DateTimeOffset(2024, 7, 15, 12, 0, 0, TimeSpan.Zero)));
    }

    // ── The message must be actionable, not merely present. ──

    /// <summary>
    /// An operator reading the boot failure in a container log gets: the zone that is required,
    /// what is likely missing from the host (tzdata / ICU), and the package to install. A guard
    /// that fails without saying what to do just relocates the outage.
    /// </summary>
    [Fact]
    public void RejectionMessage_NamesTheZone_AndTheHostFix()
    {
        var ex = Assert.Throws<InvalidTimeZoneException>(
            () => CopenhagenBusinessDate.EnsureZoneIsCopenhagen(TimeZoneInfo.Utc));

        Assert.Contains("Europe/Copenhagen", ex.Message);
        Assert.Contains("tzdata", ex.Message);
        Assert.Contains("ICU", ex.Message);
        Assert.Contains("InvariantGlobalization", ex.Message);
    }

    [Fact]
    public void EnsureZoneIsCopenhagen_NullZone_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => CopenhagenBusinessDate.EnsureZoneIsCopenhagen(null!));
    }
}
