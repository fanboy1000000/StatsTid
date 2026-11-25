using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tools.DemoSeed.Generation;

/// <summary>
/// The demo's starting rows for what ADR-043's config stage lets local administrators and HR record:
/// a local profile per org × agreement × OK version carrying only the flex plus cap (D6), three
/// customary days per org (D9), and the two HR facts per person (D4). Without them those editors are
/// empty in the demo world.
///
/// <para><b>No random draw.</b> A pure derivation from facts the generator has already drawn (each
/// org's id; each user's id, agreement and employment category; the activity month). It holds no
/// RNG and runs after the last draw, so no existing demo byte or draw moves and the pay pin (PIN-1)
/// measures the same activity. Why the rows change no pay is stated at each section
/// head in <see cref="SqlEmitter"/>.</para>
/// </summary>
internal static class ConfigStageSeed
{
    /// <summary>The history-covering anchor of init.sql's own seeds and the start-up backfill seeders:
    /// a Monday before every month any demo world can calculate, so no profile or HR-facts row starts
    /// inside a month (only a start inside a month splits it in payroll).</summary>
    internal const string HistoryAnchor = "0001-01-01";

    /// <summary>The demo institutions' flex plus cap (ADR-043 D6: "37 h, as SDU and DTU").</summary>
    internal const decimal FlexPlusCapHours = 37m;

    private static readonly string[] Agreements = ["AC", "HK", "PROSA"];
    private static readonly string[] OkVersions = ["OK24", "OK26"];

    /// <summary>One profile per org (the MAO included, as the project catalogue) × AC, HK, PROSA ×
    /// OK24, OK26, in that order: payroll picks the row by the day's own OK version, so both versions
    /// need one.</summary>
    internal static List<DemoLocalProfile> LocalProfiles(IEnumerable<DemoOrg> orgs)
        => orgs.SelectMany(o => Agreements.SelectMany(a => OkVersions.Select(v => new DemoLocalProfile
        {
            OrgId = o.OrgId,
            AgreementCode = a,
            OkVersion = v,
            EffectiveFrom = HistoryAnchor,
            FlexPlusCapHours = FlexPlusCapHours,
        }))).ToList();

    /// <summary>
    /// Three customs per org, each on a different day (one row per day and scope per organisation, as
    /// the editor's write rule requires): 5 June and 24 December every year as a paid day off given to
    /// all, and the Friday after Ascension Day of <paramref name="activityYear"/> as a closure day; whole
    /// days, every agreement, valid from 1 January of <paramref name="activityYear"/>. The activity
    /// month's year, not the reference date's: for a January reference date the December the demo
    /// calculates must already be covered.
    /// </summary>
    internal static List<DemoCustomaryDay> CustomaryDays(IEnumerable<DemoOrg> orgs, int activityYear)
    {
        var validFrom = Iso(new DateOnly(activityYear, 1, 1));
        // Ascension Day is Easter + 39 (always a Thursday); the closure day is the Friday after it.
        var closureDay = Iso(DanishHolidays.EasterSunday(activityYear).AddDays(40));

        return orgs.SelectMany(o => new[]
        {
            new DemoCustomaryDay
            {
                OrgId = o.OrgId, RecurringMonth = 6, RecurringDay = 5,
                Kind = "PAID_DAY_OFF", Part = "WHOLE_DAY", PaidMode = "GIVEN_TO_ALL", ValidFrom = validFrom,
            },
            new DemoCustomaryDay
            {
                OrgId = o.OrgId, RecurringMonth = 12, RecurringDay = 24,
                Kind = "PAID_DAY_OFF", Part = "WHOLE_DAY", PaidMode = "GIVEN_TO_ALL", ValidFrom = validFrom,
            },
            new DemoCustomaryDay
            {
                OrgId = o.OrgId, SingleDate = closureDay,
                Kind = "CLOSURE_DAY", Part = "WHOLE_DAY", PaidMode = null, ValidFrom = validFrom,
            },
        }).ToList();
    }

    /// <summary>One row per user the file inserts: <c>demo_admin</c> first, then
    /// <paramref name="users"/> in order, leavers included. Everyone is on the flex scheme, from the
    /// anchor.</summary>
    internal static List<DemoHrFacts> HrFacts(IEnumerable<DemoUser> users)
    {
        var rows = new List<DemoHrFacts>
        {
            Facts(SqlEmitter.DemoAdminId, SqlEmitter.DemoAdminAgreementCode, SqlEmitter.DemoAdminEmploymentCategory),
        };
        rows.AddRange(users.Select(u => Facts(u.UserId, u.AgreementCode, u.EmploymentCategory)));
        return rows;
    }

    /// <summary>
    /// The overtime regime a demo person starts with, or null ("missing" in the HR editor, HRP-025).
    /// HK and PROSA: OVERTIME. The AC family (<c>AC</c>, or a code starting <c>AC_</c>): ADR-024's
    /// <c>role_config_overrides</c> rows in init.sql, equal for OK24 and OK26 and provisional
    /// (ADR-043 provisional item 5): Fuldmægtig CONTRACTUAL, Specialkonsulent DISCRETIONARY,
    /// Chefkonsulent and Kontorchef NONE. The demo spells the first category in ASCII
    /// (<c>Fuldmaegtig</c>, <see cref="DanishPools.EmploymentCategories"/>), so a straight match on
    /// init.sql's spelling would miss it. <c>Standard</c> has no row, and any other category or
    /// agreement gets null too: the mapping never guesses. Every non-null answer is one the person's
    /// agreement allows (<c>EmployeeHrFactsRules.AllowedOvertimeRegimes</c>, Infrastructure).
    /// </summary>
    internal static string? OvertimeRegime(string agreementCode, string employmentCategory)
    {
        if (agreementCode is "HK" or "PROSA")
            return "OVERTIME";
        if (agreementCode == "AC" || agreementCode.StartsWith("AC_", StringComparison.Ordinal))
        {
            return employmentCategory switch
            {
                "Fuldmaegtig" => "CONTRACTUAL",
                "Specialkonsulent" => "DISCRETIONARY",
                "Chefkonsulent" or "Kontorchef" => "NONE",
                _ => null,
            };
        }
        return null;
    }

    // Everyone on the flex scheme is the interim limit R-17, registered as QUAL-BF-010 at init.sql's
    // own HR-facts seed: no central source lists the positions off flex, so HR records the exceptions
    // per person (HRP-026).
    private static DemoHrFacts Facts(string employeeId, string agreementCode, string employmentCategory) => new()
    {
        EmployeeId = employeeId,
        OvertimeRegime = OvertimeRegime(agreementCode, employmentCategory),
        OnFlexScheme = true,
        EffectiveFrom = HistoryAnchor,
    };

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
