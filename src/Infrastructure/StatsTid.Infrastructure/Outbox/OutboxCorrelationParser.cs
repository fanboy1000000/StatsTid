namespace StatsTid.Infrastructure.Outbox;

/// <summary>
/// Pure parser that maps the raw <c>outbox_events.correlation_id</c> TEXT
/// column onto the canonical <c>events.correlation_id</c> UUID column shape
/// (S23 / TASK-2302; resolves Reviewer WARN-4 from S22 Step 7a).
///
/// <para>
/// Three outcomes:
/// <list type="bullet">
///   <item><see cref="CorrelationParseOutcome.Null"/> — no correlation_id was
///         enqueued; the events row stores DBNull. Steady state.</item>
///   <item><see cref="CorrelationParseOutcome.Parsed"/> — value is a valid
///         GUID literal; the boxed <see cref="Guid"/> is bound to the events
///         row.</item>
///   <item><see cref="CorrelationParseOutcome.ParseFailure"/> — value is
///         present but not a GUID literal. The events row stores DBNull
///         (because <c>events.correlation_id</c> is typed UUID); the caller
///         should log a warning so the audit-chain breadcrumb survives in
///         logs even though it cannot survive in the canonical store.</item>
/// </list>
/// </para>
///
/// <para>
/// ADR-018 + S23 Step 0b plan-mode review (2026-05-06, Codex Q1) verdict:
/// keep <c>events.correlation_id</c> as <c>UUID</c>; do NOT migrate to TEXT.
/// Schema migration is overkill for a debug-breadcrumb concern. Log-on-fail
/// is the right minimal hardening for the current architecture.
/// </para>
/// </summary>
internal static class OutboxCorrelationParser
{
    /// <summary>
    /// Parses <paramref name="raw"/> into one of three outcomes. The returned
    /// <c>dbValue</c> is always safe to bind directly to a <c>correlation_id</c>
    /// parameter on the events INSERT (it is either a boxed <see cref="Guid"/>
    /// or <see cref="System.DBNull.Value"/>).
    /// </summary>
    internal static (CorrelationParseOutcome outcome, object dbValue) Parse(string? raw)
    {
        if (raw is null)
        {
            return (CorrelationParseOutcome.Null, System.DBNull.Value);
        }
        if (System.Guid.TryParse(raw, out var corr))
        {
            return (CorrelationParseOutcome.Parsed, corr);
        }
        return (CorrelationParseOutcome.ParseFailure, System.DBNull.Value);
    }
}

/// <summary>Three-state outcome of <see cref="OutboxCorrelationParser.Parse"/>.</summary>
internal enum CorrelationParseOutcome
{
    /// <summary>Input was null — there was nothing to parse. Bind DBNull.</summary>
    Null,

    /// <summary>Input was a valid GUID literal. Bind the boxed Guid.</summary>
    Parsed,

    /// <summary>
    /// Input was present but not a GUID literal. Bind DBNull and log the
    /// raw value so the audit-chain breadcrumb survives in logs.
    /// </summary>
    ParseFailure,
}
