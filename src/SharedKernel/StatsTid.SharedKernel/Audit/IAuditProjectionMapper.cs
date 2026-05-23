namespace StatsTid.SharedKernel.Audit;

/// <summary>
/// S43 / ADR-026 D2 dispatch contract. Per-event mapper that derives the
/// event-specific projection payload from a typed domain event.
///
/// <para>
/// <b>Signature evolution from ADR-026 D2 L90-110</b> (Step 7a cycle 1
/// Codex W2 documentation): the ADR's draft signature names
/// <c>MapToRow</c> with <c>DateTime OccurredAt</c> and
/// <c>JsonDocument Details</c>. Implementation ships with shorter
/// <c>Map</c>, timezone-aware <c>DateTimeOffset</c>, and pre-serialized
/// <c>string DetailsJson</c> (avoids JsonDocument disposal). S44 mapper
/// authors follow the live signature; the ADR text retains the draft as
/// historical authoring intent.
/// </para>
///
/// <para>
/// <b>Pure mapper.</b> Implementations are synchronous + side-effect-free —
/// they do NOT open connections, hit the database, or call other services.
/// Cross-table lookups (e.g., resolving an employee_id to their primary_org_id
/// for the <c>target_org_id</c> field) are performed at the EMITTING ENDPOINT
/// before the event is published; the endpoint passes the resolved value via
/// the event payload (preferred) OR threads it through a dedicated
/// <c>endpoint-direct</c> mapper variant where the endpoint constructs the
/// <see cref="AuditProjectionRowData"/> itself.
/// </para>
///
/// <para>
/// Sub-Sprint 1 (S43) ships the interface only; Sub-Sprint 2 (S44) wires
/// ~53 concrete mapper implementations per the catalog at
/// <c>docs/operations/audit-projection-catalog.md</c>.
/// </para>
/// </summary>
public interface IAuditProjectionMapper<TEvent> where TEvent : class
{
    /// <summary>
    /// Map the source <paramref name="event"/> + endpoint-supplied
    /// <paramref name="context"/> into the projection row data.
    /// </summary>
    AuditProjectionRowData Map(TEvent @event, AuditProjectionContext context);
}
