using StatsTid.SharedKernel.Models;
using Xunit;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S120 / Step-7a Reviewer W1 — the integer-enum WIRE-ORDER tripwire (the prod-bug-#8 class).
///
/// <para><c>ComplianceViolationType</c>/<c>ComplianceSeverity</c> serialize as their NUMERIC
/// values on the HTTP wire (no <c>JsonStringEnumConverter</c> in the path; the spec truthfully
/// declares <c>type: integer</c>), and the FE's <c>COMPLIANCE_VIOLATION_TYPE</c>/
/// <c>COMPLIANCE_SEVERITY</c> constants (frontend/src/hooks/useCompliance.ts) hard-code the SAME
/// numeric values by name. Nothing else pins the name↔value correspondence: the spec emits an
/// order-invariant integer SET (a reordered CLR enum regenerates a byte-identical spec), the
/// Docker enum-fidelity test compares sets (order-blind), the rule tests use symbolic names, and
/// the vitest pins compare FE constants to FE literals. A CLR member transposition would
/// therefore silently mislabel violations in production with zero test firing — unless it fails
/// HERE. If this test breaks, either restore the CLR order or update the FE constants in the
/// same change (both sides, never one).</para>
/// </summary>
public class ComplianceEnumWireOrderTests
{
    [Fact]
    public void ViolationType_NamedMembers_CarryTheirWireValues()
    {
        Assert.Equal(0, (int)ComplianceViolationType.DAILY_REST);
        Assert.Equal(1, (int)ComplianceViolationType.WEEKLY_REST);
        Assert.Equal(2, (int)ComplianceViolationType.MAX_DAILY_HOURS);
        Assert.Equal(3, (int)ComplianceViolationType.WEEKLY_MAX_HOURS);
        Assert.Equal(4, (int)ComplianceViolationType.OVERTIME_EXCEEDED);
        Assert.Equal(5, (int)ComplianceViolationType.OVERTIME_UNAPPROVED);
        Assert.Equal(6, System.Enum.GetValues<ComplianceViolationType>().Length); // a 7th member must re-visit the FE constants
    }

    [Fact]
    public void Severity_NamedMembers_CarryTheirWireValues()
    {
        Assert.Equal(0, (int)ComplianceSeverity.WARNING);
        Assert.Equal(1, (int)ComplianceSeverity.VIOLATION);
        Assert.Equal(2, System.Enum.GetValues<ComplianceSeverity>().Length);
    }
}
