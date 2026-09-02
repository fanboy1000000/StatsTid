using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S137 Step-5a (Reviewer WARNING 1) — the <see cref="PeriodCalculationService"/> constructor
/// couples the two resolvers of the DI-wired path: a profile resolver WITHOUT an
/// employment-window resolver is refused.
///
/// <para>
/// Plain-language: after S137 the payroll host reads two facts before calculating — "when was
/// this person employed?" (window resolver) and "which profile applied?" (profile resolver).
/// The window resolver is an OPTIONAL constructor parameter because legacy test fixtures build
/// PCS with neither resolver and must keep working. But on the real host, "optional" hides a
/// payroll error: if the <c>IEmploymentWindowResolver</c> DI registration were ever dropped,
/// PCS would still construct, plan every employee as employed all month, and re-pay leavers
/// for days after their end date — and no existing check would notice (the CI smoke probe's
/// employee has NULL dates, so its export would look identical). The profile resolver marks
/// the DI-wired, fail-closed path (ADR-023 D3), so its presence without the window resolver
/// is always a wiring defect. Refusing to construct turns a silent over-payment into a loud
/// failure on the host's first calculation.
/// </para>
///
/// <para>These pins bypass the fixture's <c>BuildPcs</c> (which auto-supplies a fake window
/// resolver precisely so ordinary tests never trip this guard) and construct PCS directly.</para>
/// </summary>
public sealed class PcsConstructionCouplingTests
{
    [Fact]
    public void ProfileResolverWithoutWindowResolver_Throws_NamingTheMissingRegistration()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Construct(profileResolver: new CountingProfileResolver(), windowResolver: null));

        // The message must let an operator fix the wiring without reading the code: the
        // missing type, the consequence, and where the registration lives.
        Assert.Contains("IEmploymentWindowResolver", ex.Message);
        Assert.Contains("IEmploymentProfileResolver", ex.Message);
        Assert.Contains("Program.cs", ex.Message);
        Assert.Contains("employment_end_date", ex.Message);
        Assert.Contains("EmployeeProfileRepository", ex.Message); // the optional sibling is named too
    }

    /// <summary>The legacy fixture path (neither resolver) is untouched — every pre-S137
    /// direct-construction test keeps working.</summary>
    [Fact]
    public void NeitherResolver_LegacyPath_Constructs()
    {
        var pcs = Construct(profileResolver: null, windowResolver: null);

        Assert.NotNull(pcs);
    }

    /// <summary>The DI-wired shape (both resolvers) constructs.</summary>
    [Fact]
    public void BothResolvers_Constructs()
    {
        var pcs = Construct(
            profileResolver: new CountingProfileResolver(),
            windowResolver: new FakeWindowResolver(new EmploymentWindow(null, null)));

        Assert.NotNull(pcs);
    }

    /// <summary>The coupling is one-directional: a window resolver WITHOUT a profile resolver
    /// is fine (typed segments on the legacy copy-caller-profile path — no over-payment risk).</summary>
    [Fact]
    public void WindowResolverWithoutProfileResolver_Constructs()
    {
        var pcs = Construct(
            profileResolver: null,
            windowResolver: new FakeWindowResolver(new EmploymentWindow(null, null)));

        Assert.NotNull(pcs);
    }

    private static PeriodCalculationService Construct(
        IEmploymentProfileResolver? profileResolver,
        IEmploymentWindowResolver? windowResolver) =>
        new(
            new SingleClientHttpFactory(new RecordingRuleEngine()),
            mappingService: null!,
            eventStore: new InMemoryEventStore(),
            connectionFactory: null!,
            Configuration(),
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: null,
            localAgreementProfileRepo: null,
            profileResolver: profileResolver,
            employmentWindowResolver: windowResolver,
            employeeProfileRepo: null);
}
