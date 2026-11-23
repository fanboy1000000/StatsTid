using System.Net;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C1a) - an agreement that exists only in the database is paid.
///
/// <para>
/// <b>In plain language.</b> A configuration administrator can create and publish a brand-new agreement through the
/// screens. Payroll must then pay an employee on it, because the published database version is what pay reads. Before
/// this stack the Rule Engine looked the agreement up in a table compiled into the code, found nothing and refused
/// the whole month ("No agreement configuration exists").
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DatabaseOnlyAgreementPayrollTests : DatedPayrollFixture
{
    private const string OrgId = "STY01";
    private static readonly DateOnly Clock = new(2026, 11, 10);
    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C1a. A test-unique agreement code (<c>T828A…</c>) created and published through the
    /// routes only (OK26, weekly norm 37, applying from Monday 4 May 2026), the one wage mapping its NORMAL_HOURS line
    /// needs seeded, an employee on that code with 7.4 h on every June weekday, June APPROVED.
    /// <c>calculate-and-export</c> June 2026 answers <b>200</b> with a calculated month: the norm figure is 162.80 h (22
    /// weekdays at 7.40) and the export lines carry NORMAL_HOURS hours of 162.80.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). On the code before the stack the engine
    /// resolved the agreement from the compiled table; this code is in none, so the engine refused ("No agreement
    /// configuration exists for a piece's agreement and OK version", <c>RuleRegistry.cs:446-447</c> at <c>bb529e62</c>)
    /// and the month was refused: the status is not 200 and <c>AssertOk</c> fails.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CreatedAndPublishedInDatabaseOnly_IsCalculated()
    {
        using var admin = AdminAt(Clock);
        var code = UniqueCode("T828A");
        const string employeeId = "EMP-S148-PAY-DBONLY";
        await CreateAndPublishAsync(admin, HkBody(37m, agreementCode: code), "2026-05-04");
        await ExecAsync(
            """
            INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description, effective_from)
            VALUES ('NORMAL_HOURS', 'SLS_0110', 'OK26', @code, '', NULL, DATE '2020-01-01')
            """, ("code", code));
        await SeedEmployeeAsync(employeeId, OrgId, code);
        await SeedWeekdayHoursAsync(employeeId, code, Ok26, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, OrgId, code, Ok26, Jun01, Jun30);

        var response = await CalculateAndExportAsync(employeeId, code, Ok26, OrgId, Jun01, Jun30);

        AssertOk(response);
        var result = ReadResult(response.Json);
        Assert.Equal(162.80m, NormTotal(result));
        Assert.Equal(162.80m, result.ExportLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").Sum(l => l.Hours));
    }
}
