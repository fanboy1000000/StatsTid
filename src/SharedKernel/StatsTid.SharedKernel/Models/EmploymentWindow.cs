namespace StatsTid.SharedKernel.Models;

/// <summary>
/// S137 / ADR-040 D1 — one employment spell as a pair of dates: <c>[Start, End]</c> with
/// the end date INCLUSIVE (the last day employed — the ADR-033 / S70 R1 semantics D1
/// pins). A <c>null</c> side is unbounded (D2): <c>null</c> Start = employed since the
/// beginning of time, <c>null</c> End = open-ended employment; a both-<c>null</c> window
/// covers every date.
///
/// <para>
/// This is the SPELLS-PROOF shape (ADR-040 D1): consumers receive a LIST of these from
/// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentWindowResolver.GetWindowsAsync"/>
/// — 0-or-1 entries while storage is the single <c>users</c>-row window, a genuine list
/// when the deferred spells increment (re-hire) lands. Growing to spells then changes
/// storage + resolver only, never this type or its consumers.
/// </para>
///
/// <para>
/// <b>Fencepost warning for boundary hydration (the codebase's recurring
/// inclusive/exclusive hazard):</b> a segment-boundary date is the FIRST day of the NEW
/// segment, so when translating a window into <see cref="Segmentation.BoundarySources"/>
/// entries, <c>EmploymentStarted</c>'s boundary is <see cref="Start"/> itself (the first
/// employed day) but <c>EmploymentEnded</c>'s boundary is <see cref="End"/><c> + 1</c>
/// (the first NOT-employed day) — never <see cref="End"/> itself, which is still employed.
/// </para>
/// </summary>
/// <param name="Start">First employed day; <c>null</c> = unbounded into the past (ADR-040 D2).</param>
/// <param name="End">Last employed day (INCLUSIVE, ADR-040 D1); <c>null</c> = open-ended.</param>
public sealed record EmploymentWindow(DateOnly? Start, DateOnly? End);
