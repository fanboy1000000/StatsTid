using System.Linq;

namespace StatsTid.Auth;

/// <summary>
/// Strips control characters (CR, LF, tab, …) from a value before it enters a log message, so an
/// attacker-supplied identifier cannot forge an extra line at a plain-text/console sink
/// (log-forging, CWE-117). This mirrors the <c>SanitizeForLog</c> helper introduced for the
/// failed-login trace (SEC-040) in <c>AuthEndpoints.cs</c>; it is duplicated here (rather than
/// referenced) because that copy is <c>private</c> to a Backend-only file, while this one must be
/// reachable from the shared <see cref="StatsTid.Auth"/> assembly that every host references.
/// <para><b>Consolidation note:</b> the two copies are behaviourally identical; a future cleanup
/// could hoist a single shared sanitizer, but that touches Backend-domain code outside this task's
/// declared scope and is intentionally left as a tracked follow-up.</para>
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Returns <paramref name="value"/> with every Unicode control character removed (collapsing a
    /// multi-line value to a single safe line). <c>null</c>/empty pass through unchanged. Only pays
    /// the allocation cost when a control character is actually present.
    /// </summary>
    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Any(char.IsControl)
            ? new string(value.Where(c => !char.IsControl(c)).ToArray())
            : value;
    }
}
