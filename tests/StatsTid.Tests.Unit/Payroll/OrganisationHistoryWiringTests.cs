using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Segmentation;
using Xunit;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S148 / TASK-14822-T (ADR-040 D4, ADR-043 D12; AC-C13g) — "optional but never silent" for the organisation
/// history reader, the third reader <see cref="PeriodCalculationService"/> takes as an optional constructor
/// parameter (after the prior-month approval reader and the flex ledger reader).
///
/// <para>
/// <b>Plain language.</b> The reader answers "on which days did this employee move to another institution?".
/// Payroll cuts the month at each such day. Today no paid figure depends on the organisation; once the T14b stack
/// sends each part's own configuration to the engine, each part is priced under its own institution's rules. A host
/// that forgot to register the reader would plan every month as one piece, quietly, and a part of a transferred
/// employee's month would then be priced under the wrong institution's rules. So a service built without the
/// reader refuses to calculate, by name, before anything is read or sent to the rule engine.
/// </para>
///
/// <para>
/// <b>Kind: N</b> (names the interface and the constructor parameter TASK-14822 creates). Red conditions: proven by
/// the named mutation M-6 ("skip the organisation-history read when unwired": the refusal replaced by "no
/// organisation dates"), run at the TASK-14822-T gate in a worktree detached at the branch tip, never by reverting;
/// under M-6 the calculation completes and <c>Assert.ThrowsAsync</c> fails with "No exception was thrown". The
/// fact is Unit (no database). June 2026 opens on a Monday, so no previous-month question arises and the
/// prior-month fake is a formality; the hours reader and the ledger reader are present, so the only missing
/// registration is the one named.
/// </para>
/// </summary>
public sealed class OrganisationHistoryWiringTests
{
    private const string Emp = "EMP-S148-ORGWIRING";

    /// <summary>
    /// A calculation of June 2026 on a service built WITHOUT an organisation-history reader throws
    /// <see cref="InvalidOperationException"/>; the message names <c>IOrganisationHistoryReader</c> and the file to
    /// fix (<c>src/Integrations/StatsTid.Integrations.Payroll/Program.cs</c>), names none of the other readers (so
    /// no other refusal can satisfy this fact), and the stub engine received no call.
    /// </summary>
    [Fact]
    public async Task Unwired_RefusesWithFixedMessage()
    {
        var engine = new RecordingRuleEngine();
        var pcs = new PeriodCalculationService(
            new SingleClientHttpFactory(engine),
            // Never dereferenced: the refusal comes before any rule call, so no mapping lookup happens.
            mappingService: null!,
            new InMemorySegmentManifestStore(),
            Configuration(),
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new Classifications(),
            hoursReader: new InMemoryRecordedHoursReader(),
            priorPeriodApprovalReader: new AlwaysApprovedPriorPeriods(),
            flexLedgerReader: new InMemoryFlexLedgerReader());
        // organisationHistoryReader deliberately omitted: this fact is ABOUT the missing registration.

#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pcs.CalculateWithOutcomeAsync(Profile(Emp), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
#pragma warning restore CS0618

        Assert.Contains("IOrganisationHistoryReader", ex.Message);
        Assert.Contains("src/Integrations/StatsTid.Integrations.Payroll/Program.cs", ex.Message);
        // Fails only on its own refusal: no other reader's refusal text may satisfy this fact.
        Assert.DoesNotContain("IPriorPeriodApprovalReader", ex.Message);
        Assert.DoesNotContain("IFlexLedgerReader", ex.Message);
        Assert.DoesNotContain("IRecordedHoursReader", ex.Message);
        Assert.Empty(engine.Calls);
    }

    private sealed class Classifications : IRuleClassificationProvider
    {
        public IReadOnlyList<RuleClassification> GetClassifications() => RuleSet;
    }
}
