namespace StatsTid.Infrastructure;

/// <summary>
/// "What did HR record about this person on day X" (ADR-040 D4, per attribute): the facts row
/// covering the day, read against the agreement in force that day
/// (<see cref="UserAgreementCodeRepository.GetByUserIdAtAsync"/>). A stored overtime regime that day's
/// agreement does not allow reads as null, "not recorded": a person who changes agreement keeps the
/// old row, and its value is not valid under the new agreement. The flex answer is read as stored.
/// <para>No calculation reads either fact yet; the first reader brings their freezing into the month's
/// proof, their place among payroll's boundary sources and the pay-inputs lock (R-21).</para>
/// </summary>
public sealed class EmployeeHrFactsResolver
{
    private readonly EmployeeHrFactsRepository _factsRepository;
    private readonly UserAgreementCodeRepository _agreementCodeRepository;

    public EmployeeHrFactsResolver(
        EmployeeHrFactsRepository factsRepository, UserAgreementCodeRepository agreementCodeRepository)
    {
        _factsRepository = factsRepository;
        _agreementCodeRepository = agreementCodeRepository;
    }

    /// <summary>The person's facts as of <paramref name="day"/>.</summary>
    public async Task<ResolvedHrFacts> ResolveAsync(string employeeId, DateOnly day, CancellationToken ct = default)
    {
        var row = await _factsRepository.GetCoveringAsync(employeeId, day, ct);
        var agreementCode = await _agreementCodeRepository.GetByUserIdAtAsync(employeeId, day, ct);
        return new ResolvedHrFacts(
            Day: day,
            AgreementCode: agreementCode,
            AllowedOvertimeRegimes: EmployeeHrFactsRules.AllowedOvertimeRegimes(agreementCode),
            OvertimeRegime: EmployeeHrFactsRules.ResolveOvertimeRegime(row?.OvertimeRegime, agreementCode),
            OnFlexScheme: row?.OnFlexScheme,
            EffectiveFrom: row?.EffectiveFrom);
    }
}

/// <summary>A person's HR facts as of <see cref="Day"/>: the agreement in force that day (null when
/// none), the regimes it allows, the resolved regime and the flex answer (null = not recorded), and
/// the start of the row covering the day (null when no row does).</summary>
public sealed record ResolvedHrFacts(
    DateOnly Day,
    string? AgreementCode,
    IReadOnlyList<string> AllowedOvertimeRegimes,
    string? OvertimeRegime,
    bool? OnFlexScheme,
    DateOnly? EffectiveFrom);

/// <summary>
/// The overtime-regime values and which of them each agreement allows (ADR-043 D4; all provisional,
/// ADR-043 provisional item 5). The only place the sets live: the write's validation, the resolver
/// and the editor's offered values all come from <see cref="AllowedOvertimeRegimes"/>.
/// </summary>
public static class EmployeeHrFactsRules
{
    /// <summary>The CHECK vocabulary of <c>employee_hr_facts.overtime_regime</c> (init.sql), compared
    /// ordinally: overtime, exempt, a contractual (merarbejde) claim, discretionary, none.</summary>
    public static readonly IReadOnlyList<string> OvertimeRegimes =
        ["OVERTIME", "EXEMPT", "CONTRACTUAL", "DISCRETIONARY", "NONE"];

    private static readonly IReadOnlyList<string> Hk = ["OVERTIME", "EXEMPT"];
    private static readonly IReadOnlyList<string> Prosa = ["OVERTIME", "CONTRACTUAL"];
    private static readonly IReadOnlyList<string> AcFamily = ["CONTRACTUAL", "DISCRETIONARY", "NONE"];

    /// <summary>
    /// The regimes <paramref name="agreementCode"/> allows: HK overtime or exempt; PROSA overtime or a
    /// contractual claim (pay groups 3–4); the AC family (<c>AC</c>, or any code starting <c>AC_</c>)
    /// a contractual claim, discretionary or none; any other code, or none, nothing.
    /// </summary>
    public static IReadOnlyList<string> AllowedOvertimeRegimes(string? agreementCode) => agreementCode switch
    {
        "HK" => Hk,
        "PROSA" => Prosa,
        "AC" => AcFamily,
        { } code when code.StartsWith("AC_", StringComparison.Ordinal) => AcFamily,
        _ => [],
    };

    /// <summary>True when <paramref name="value"/> is one of the five values.</summary>
    public static bool IsOvertimeRegime(string value) => OvertimeRegimes.Contains(value, StringComparer.Ordinal);

    /// <summary>
    /// The write's refusal code for a non-null regime under <paramref name="agreementCode"/>:
    /// <c>UNKNOWN_VALUE</c> when it is not one of the five, <c>NOT_ALLOWED_FOR_AGREEMENT</c> when the
    /// agreement does not allow it, null when it is allowed.
    /// </summary>
    public static string? OvertimeRegimeRefusal(string regime, string? agreementCode) =>
        !IsOvertimeRegime(regime) ? "UNKNOWN_VALUE"
        : !AllowedOvertimeRegimes(agreementCode).Contains(regime, StringComparer.Ordinal) ? "NOT_ALLOWED_FOR_AGREEMENT"
        : null;

    /// <summary>The stored regime when <paramref name="agreementCode"/> allows it, else null ("not
    /// recorded").</summary>
    public static string? ResolveOvertimeRegime(string? stored, string? agreementCode) =>
        stored is not null && AllowedOvertimeRegimes(agreementCode).Contains(stored, StringComparer.Ordinal)
            ? stored
            : null;
}
