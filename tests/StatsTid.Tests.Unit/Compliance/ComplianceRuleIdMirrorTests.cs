using System.Reflection;
using StatsTid.Backend.Api.Endpoints;
using StatsTid.RuleEngine.Api.Rules;

namespace StatsTid.Tests.Unit.Compliance;

/// <summary>
/// S138 / TASK-13806 — pins the Backend's string MIRROR of the rule engine's compliance
/// <c>RuleId</c> against the real constant.
///
/// <para>
/// Plain-language: when a month has no employed day, the compliance endpoint answers with an
/// empty result "in the same shape the rule engine would return" — including the rule id. The
/// Backend cannot reference the rule-engine assembly (PAT-005 keeps that boundary HTTP-only), so
/// it carries the id as a copied string. A copied string drifts silently: if someone renamed
/// <see cref="RestPeriodRule.RuleId"/>, the empty result would carry a stale id and no compiler
/// would notice. This test is the compiler's stand-in — the ONE test project that legitimately
/// references BOTH assemblies compares the two literals.
/// </para>
///
/// <para>
/// <b>Why reflection, and why the constant stays private.</b> The mirror is an implementation
/// detail of one endpoint; the S137 Reviewer NOTE that motivated this task specifically flagged
/// a helper made public only so a test could reach it. Reading the private constant by
/// reflection keeps the endpoints class's public surface to its routes (no
/// <c>InternalsVisibleTo</c> on the Backend assembly, no csproj change), and follows the suite's
/// established idiom for private production seams (<c>PcsJsonOptions.Real</c>,
/// <c>EmploymentWindowHydrationTests</c>). A rename of the field fails this test loudly — which
/// is the desired signal, because the mirror is exactly the kind of thing a rename must revisit.
/// </para>
/// </summary>
public sealed class ComplianceRuleIdMirrorTests
{
    private const string MirrorFieldName = "ComplianceRuleId";

    [Fact]
    public void ComplianceRuleId_MirrorsRestPeriodRuleRuleId()
    {
        var field = typeof(ComplianceEndpoints).GetField(
            MirrorFieldName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.True(field!.IsLiteral, $"{MirrorFieldName} must stay a const — it is a wire-shape literal.");

        var mirrored = Assert.IsType<string>(field.GetRawConstantValue());
        Assert.Equal(RestPeriodRule.RuleId, mirrored);
    }
}
