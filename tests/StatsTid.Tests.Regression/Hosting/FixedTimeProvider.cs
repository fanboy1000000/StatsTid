namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// A deterministic <see cref="TimeProvider"/> that always answers <see cref="GetUtcNow"/> with one
/// pinned instant — the shared PAT-008 fixture (<c>docs/knowledge-base/patterns/PAT-008-fixed-timeprovider-waf-regression-tests.md</c>).
///
/// <para>
/// <b>Why this exists, in plain language.</b> Sprint 138 lost two CI runs to test pins computed as
/// "today minus N days": on roughly two days in seven that offset lands on a weekend, the product's
/// per-day working norm is zero there, and the pin silently proved nothing. The durable fix is a
/// FIXED clock the test host and the product share, so every "today"-dependent assertion is a pure
/// function of (request, seed, pinned-now) — replay-deterministic regardless of which real calendar
/// day CI happens to run on. Register an instance of this class in a <see cref="TimeProvider"/> slot
/// via <see cref="StatsTidWebApplicationFactory.WithFixedToday"/> to make a test host's "today"
/// match the constant your test asserts against.
/// </para>
///
/// <para>
/// <b>Two constructors, two callers.</b> <see cref="FixedTimeProvider(DateOnly)"/> is the everyday
/// entry point — pin a calendar date and get UTC midnight of that date, matching how the product's
/// today-dependent endpoints derive "today" (<c>DateOnly.FromDateTime</c> over the wall clock's
/// UtcNow instant, once they read this seam instead of reading that wall clock directly). Pinning at
/// UTC MIDNIGHT (not noon, not local midnight)
/// matters: it keeps a UTC-day derivation and a Copenhagen-day derivation in agreement for every hour
/// of the calendar day, because Denmark's UTC offset (+1 winter / +2 summer DST) is never negative —
/// Copenhagen local midnight always falls AT OR AFTER UTC midnight of the same date, so both
/// derivations read back the same <see cref="DateOnly"/>. <see cref="FixedTimeProvider(DateTimeOffset)"/>
/// is PAT-008's original sample constructor (carried over verbatim) for a caller that needs to pin an
/// exact instant, offset and all, rather than a bare date.
/// </para>
///
/// <para>
/// <b>The boot-order rule (read before seeding a fixture against a fixed host).</b> A
/// <c>WithWebHostBuilder</c>-derived host — which is what registering this provider requires —
/// RE-RUNS <c>Program.cs</c>'s startup seeders against the same Postgres container the moment its
/// first <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}.CreateClient"/>
/// is called. Any "absent-state" fixture a test needs (a profile-less employee, a missing eligibility
/// row, or any direct-INSERT the test relies on) MUST be created AFTER that first
/// <c>CreateClient()</c> call on the FIXED derived host — never before, and never only on a
/// differently-clocked host's boot — or the very seeder that (re)populates the "missing" row erases
/// the absence the test is trying to exercise. See
/// <see cref="StatsTidWebApplicationFactory.WithFixedToday"/> for the full rule, including the
/// companion "fixture employee's hire date must be on or before the fixed date" constraint.
/// </para>
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _fixedUtcNow;

    /// <summary>
    /// Pins <see cref="GetUtcNow"/> to UTC midnight of <paramref name="date"/>. This is the
    /// constructor almost every caller wants — see the class doc for why UTC midnight (not local
    /// midnight, not noon) is the right anchor.
    /// </summary>
    public FixedTimeProvider(DateOnly date)
    {
        _fixedUtcNow = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Pins <see cref="GetUtcNow"/> to the exact <paramref name="value"/> supplied — PAT-008's
    /// original sample shape, carried over verbatim for a caller that needs to pin more than a bare
    /// calendar date (e.g. a specific hour, or a non-UTC offset).
    /// </summary>
    public FixedTimeProvider(DateTimeOffset value)
    {
        _fixedUtcNow = value;
    }

    public override DateTimeOffset GetUtcNow() => _fixedUtcNow;
}
