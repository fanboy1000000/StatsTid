using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S60 / TASK-6009 / ADR-030 — MONTHLY_ACCRUAL behavior of
/// <see cref="EntitlementValidationRule"/> via the new per-type <c>BookableLimit</c> cap.
///
/// Pins: (1) for MONTHLY_ACCRUAL the rejection cap is the supplied carryover-INCLUSIVE
/// <c>BookableLimit</c> — an under-earned-but-within-bookable VACATION request (forskud) is
/// ALLOWED; a SPECIAL_HOLIDAY request beyond its earned+carryover cap is REJECTED. (2) the
/// 20%-warning threshold and per-episode branch keep keying off the ANNUAL effectiveQuota, not
/// earned-to-date — so no spurious early-ferieår warnings. (3) IMMEDIATE-style requests (null
/// BookableLimit, null AccrualModel) are byte-for-byte unchanged.
/// </summary>
public class EntitlementValidationRuleBookableLimitTests
{
    private static ValidateEntitlementRequest Monthly(
        decimal annualQuota,
        decimal requestedDays,
        decimal? bookableLimit,
        decimal used = 0m,
        decimal planned = 0m,
        decimal carryoverIn = 0m,
        decimal partTimeFraction = 1.0m) => new()
    {
        AnnualQuota = annualQuota,
        Used = used,
        Planned = planned,
        CarryoverIn = carryoverIn,
        RequestedDays = requestedDays,
        PartTimeFraction = partTimeFraction,
        ProRateByPartTime = false,
        IsPerEpisode = false,
        AccrualModel = "MONTHLY_ACCRUAL",
        BookableLimit = bookableLimit,
    };

    [Fact]
    public void Vacation_UnderEarnedButWithinForskudCap_IsAllowed()
    {
        // Early in the ferieår: earned ≈ 4 d but the dynamic forskud cap (earned + still-
        // accruable + carryover) = full annual 25. Booking 10 days is forskudsferie — it
        // exceeds earned-to-date yet is WITHIN bookableLimit ⇒ ALLOWED (manager approval = §7).
        var request = Monthly(annualQuota: 25m, requestedDays: 10m, bookableLimit: 25m, used: 0m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("ALLOWED", result.Status); // 25 - 10 = 15 remaining, well above 20% (5)
    }

    [Fact]
    public void Vacation_BeyondForskudCap_IsRejected()
    {
        // bookableLimit = 25; requesting 26 ⇒ exceeds even the full-ferieår forskud cap.
        var request = Monthly(annualQuota: 25m, requestedDays: 26m, bookableLimit: 25m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
    }

    [Fact]
    public void Vacation_MidYearHire_LowerForskudCap_RejectsAboveProRatedCap()
    {
        // Mid-ferieår hire: can't borrow against next ferieår ⇒ bookableLimit < 25 (say 12.5).
        var request = Monthly(annualQuota: 25m, requestedDays: 15m, bookableLimit: 12.5m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
    }

    [Fact]
    public void SpecialHoliday_BookingBeyondEarned_NoForskud_IsRejected()
    {
        // SPECIAL_HOLIDAY: no forskud (§13 stk.4). Early ferieår earned ≈ 0,42 d, carryover 0 ⇒
        // bookableLimit = 0,42. Booking 1 full day exceeds it ⇒ REJECTED (proves no-forskud).
        var request = Monthly(
            annualQuota: 5m, requestedDays: 1m, bookableLimit: 0.4166666m, carryoverIn: 0m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed);
        Assert.Equal("REJECTED", result.Status);
    }

    [Fact]
    public void SpecialHoliday_WithinEarned_IsAllowed()
    {
        // Later in the ferieår: earned 3 d, carryover 0 ⇒ bookableLimit = 3. Booking 2 ⇒ ALLOWED.
        var request = Monthly(annualQuota: 5m, requestedDays: 2m, bookableLimit: 3m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void WarningThreshold_KeysOffAnnualQuota_NotEarnedOrBookableLimit()
    {
        // Mid-ferieår VACATION: bookableLimit = 25 (full forskud), annual = 25. Book 21 d ⇒
        // remainingAfter = 25 - 21 = 4 <= warningThreshold (25 * 0.2 = 5) ⇒ WARNING.
        // The warning math uses effectiveQuota (annual 25), NOT earned-to-date. The reported
        // EffectiveQuota in the response must be the ANNUAL quota.
        var request = Monthly(annualQuota: 25m, requestedDays: 21m, bookableLimit: 25m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("WARNING", result.Status);
        Assert.Equal(25m, result.EffectiveQuota); // annual, not earned
        Assert.Equal(4m, result.RemainingAfter);
    }

    [Fact]
    public void NoSpuriousEarlyFerieaarWarning_WhenBookableLimitIsFullAnnual()
    {
        // Early ferieår, earned only ~2 d but forskud cap = full annual 25. A small 1-day
        // booking must NOT warn just because earned-to-date is low: remainingAfter = 24,
        // threshold = 5 ⇒ ALLOWED. (If the warning keyed off earned, it would mis-fire.)
        var request = Monthly(annualQuota: 25m, requestedDays: 1m, bookableLimit: 25m);

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.True(result.Allowed);
        Assert.Equal("ALLOWED", result.Status);
    }

    [Fact]
    public void ImmediateType_NullBookableLimit_NullAccrualModel_UnchangedBehavior()
    {
        // No BookableLimit, no AccrualModel ⇒ legacy path: cap = effectiveQuota + carryoverIn.
        var request = new ValidateEntitlementRequest
        {
            AnnualQuota = 2m,
            Used = 0m,
            Planned = 0m,
            CarryoverIn = 0m,
            RequestedDays = 3m,
            PartTimeFraction = 1.0m,
            ProRateByPartTime = false,
            IsPerEpisode = false,
            AccrualModel = null,
            BookableLimit = null,
        };

        var result = EntitlementValidationRule.Evaluate(request);

        Assert.False(result.Allowed); // 3 > 2 ⇒ REJECTED, exactly as pre-S60
        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(2m, result.EffectiveQuota);
    }
}
