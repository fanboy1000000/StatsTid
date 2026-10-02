namespace StatsTid.SharedKernel.Models;

/// <summary>
/// Which kind of write produced an hours row (S145 / A0, QUAL-192): the closed two-value
/// vocabulary of <c>time_entries_projection.source</c> and <c>absences_projection.source</c>,
/// and of the optional <c>Source</c> field on <see cref="Events.TimeEntryRegistered"/> and
/// <see cref="Events.AbsenceRegistered"/>.
///
/// <para>
/// In plain terms: a Skema cell (one employee, one day, one task or absence type) holds ONE
/// number, so saving it again must REPLACE the earlier number, not add a second row next to it.
/// A row written by <c>POST /api/time-entries</c> is a different thing: each registration is a
/// separate piece of work and stays ADDITIVE, even when it happens to carry a task id. This
/// marker is what lets the database enforce "one current row" for Skema cells only
/// (the partial unique indexes in init.sql select <see cref="Skema"/> rows alone).
/// </para>
///
/// <para>
/// Plain string constants, not an enum: the value is stored as TEXT guarded by a named CHECK
/// constraint, and a string keeps old and new events readable by the generic JSON serializer
/// without a converter (ADR-002 string-vocabulary convention).
/// </para>
/// </summary>
public static class HoursSource
{
    /// <summary>
    /// A Skema save. Latest-wins per cell: a re-save marks the previous row superseded and
    /// inserts a new current one.
    /// </summary>
    public const string Skema = "SKEMA";

    /// <summary>
    /// An additive registration (<c>POST /api/time-entries</c>). Never superseded. Also the
    /// meaning of every event and row written before S145, which carry no source.
    /// </summary>
    public const string TimePost = "TIME_POST";

    /// <summary>
    /// The effective source of an event's optional <c>Source</c>: <c>null</c> (every pre-S145
    /// event, and any writer that does not stamp one) resolves to <see cref="TimePost"/>, so
    /// an unstamped row is additive. Any other value is returned unchanged and is NOT coerced:
    /// the table CHECK refuses an unknown value, so a typo fails loudly at the write instead of
    /// being silently treated as additive.
    /// </summary>
    public static string Resolve(string? source) => source ?? TimePost;
}
