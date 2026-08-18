using StatsTid.Backend.Api.Endpoints;

namespace StatsTid.Tests.Unit;

/// <summary>
/// SEC-035 — the AgreementConfig supersession publish emits the ARCHIVED audit row + outbox event
/// only when a prior-ACTIVE config was actually archived. That decision hangs on
/// <c>SaveAgreementConfigResult.ArchivedId</c> + <c>ArchivedVersion</c>, which the repository
/// assigns TOGETHER from one <c>RETURNING config_id, version</c> row (both NOT NULL) — so today
/// they can only be both-null (nothing superseded) or both-set (one superseded). A divergent pair
/// (exactly one set) cannot arise from the current repo.
///
/// <para>The owner ruled to HARDEN anyway, defending the inviolable Auditability invariant against
/// a FUTURE repo change: <see cref="AgreementConfigEndpoints.EvaluateArchivalAudit"/> was extracted
/// as a pure, DB-free gate that returns None / Emit for the two real cases and THROWS on divergence
/// (the endpoint's inner <c>catch { Rollback; throw }</c> then rolls the whole publish back —
/// fail-closed). Because the real repo cannot produce a divergent pair, this Docker-free unit test
/// is the ONLY way to reach and pin the fail-loud branch.</para>
/// </summary>
public class SEC035ArchivalAuditDecisionTests
{
    [Fact]
    public void BothNull_NoSupersession_ReturnsNone()
    {
        var decision = AgreementConfigEndpoints.EvaluateArchivalAudit(archivedId: null, archivedVersion: null);

        Assert.Equal(AgreementConfigEndpoints.ArchivalAuditDecision.None, decision);
    }

    [Fact]
    public void BothSet_Supersession_ReturnsEmit()
    {
        var decision = AgreementConfigEndpoints.EvaluateArchivalAudit(
            archivedId: Guid.NewGuid(), archivedVersion: 3L);

        Assert.Equal(AgreementConfigEndpoints.ArchivalAuditDecision.Emit, decision);
    }

    [Fact]
    public void IdOnly_Divergence_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => AgreementConfigEndpoints.EvaluateArchivalAudit(archivedId: Guid.NewGuid(), archivedVersion: null));

        Assert.Contains("supersession invariant violated", ex.Message);
    }

    [Fact]
    public void VersionOnly_Divergence_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => AgreementConfigEndpoints.EvaluateArchivalAudit(archivedId: null, archivedVersion: 3L));

        Assert.Contains("diverged", ex.Message);
    }
}
