namespace StatsTid.Backend.Api;

/// <summary>
/// S127 / TASK-12705 — the ONE definition of "is this day's worked total equal to its allocated
/// total". Originally TASK-5604's allocation-reconciliation rule.
///
/// <para><b>What the rule is.</b> For a single day: round both totals to hundredths of an hour, then
/// compare them. The day is BALANCED when they are equal and IMBALANCED otherwise; an imbalanced day
/// has direction <c>"under"</c> when the employee worked more than they allocated onto projects and
/// <c>"over"</c> otherwise. Because both operands are rounded first, the smallest real difference is
/// <c>0.01</c> — the tolerance below only absorbs representation noise (7.40 vs 7.4), never a genuine
/// mismatch.</para>
///
/// <para><b>Why it is a type and not three inline expressions.</b> Refinement §3.8 counted FIVE
/// encodings of this predicate. Three of them were hand-written copies inside
/// <c>ApprovalEndpoints.cs</c> — the send command's allocation gate, the team-overview row's
/// <c>hasWarning</c> chip, and the allocation-breakdown's <c>hasAllocationImbalance</c> flag — bound
/// to each other only by comments asserting they were computed "IDENTICALLY". This type is that
/// binding made real: all three now call <see cref="Evaluate"/>, so the manager's warning chip, the
/// expandable detail's imbalance flag and the refusal the employee actually hits cannot drift apart.
/// The two encodings that remain outside it are deliberate and named:</para>
/// <list type="bullet">
///   <item><c>frontend/src/lib/allocation.ts</c> — the browser cannot call this, and ADR-028 D4
///     mandates the client-side mirror. It is pinned by its own tests.</item>
///   <item><c>SkemaGrid.tsx</c>'s <c>absenceOverNorm</c> — NOT this rule. It compares summed absence
///     hours against the daily norm and merely borrows the same numeric tolerance. Binding it to this
///     constant would tie two independent rules to one number.</item>
/// </list>
///
/// <para><b>THE DESIGN CONSTRAINT — this file must not learn how to load data.</b> The three call
/// sites reach their inputs by completely different routes: the gate loads one employee's month from
/// its own repositories, while <c>hasWarning</c> and the breakdown read month-wide dictionaries that
/// were batched across a whole roster. An "obvious" extraction that pulled the loading in here would
/// turn both read surfaces into per-employee queries — the S125/F1 lesson, stated as a rule: share
/// the rule, not the fetching. Hence a pure function of two decimals, with no I/O, no repository, no
/// <c>async</c>, and no knowledge of days, employees or months.</para>
///
/// <para>For the same reason the per-day loop is NOT shared. Each call site builds its own set of
/// days-to-compare from the shape it already holds, and those shapes differ: the gate unions two
/// <c>DateOnly</c>-keyed dictionaries, the breakdown unions two more, and the team-overview walks the
/// calendar month probing two <c>(employeeId, date)</c>-keyed dictionaries. Hoisting that loop here
/// would force the team-overview to scan the roster-wide key set once per employee — quadratic in the
/// roster, for no gain. The loop is iteration over data already in memory; the RULE is what was
/// duplicated, and the rule is what lives here.</para>
///
/// <para><b>The <c>&lt;</c>/<c>&gt;</c> split this collapse resolves, stated honestly.</b> Before the
/// collapse the gate spelled the test <c>|Δ| &lt; tolerance ⇒ balanced</c> while the two read surfaces
/// spelled it <c>|Δ| &gt; tolerance ⇒ warn</c>. Those two spellings disagree at exactly one input,
/// <c>|Δ| == 0.005</c>, and this type adopts the gate's. That is not a behaviour change, because that
/// input is arithmetically unreachable: both operands are rounded to hundredths before the
/// subtraction, so every operand and every difference is a whole number of hundredths, and half a
/// hundredth cannot arise. The argument is written down as executable arithmetic — with its own
/// non-vacuity guard — in <c>AllocationPredicateRoundingLimitTests</c>, and the rounding it depends on
/// is watched on the production code by case C5 of <c>AllocationPredicateCharacterizationTests</c>.
/// Removing <see cref="Round"/> from this file would make the boundary reachable and would break both.
/// </para>
/// </summary>
public static class AllocationBalance
{
    /// <summary>
    /// The single production declaration of the reconciliation tolerance (TASK-5604).
    ///
    /// <para>It is NOT a slack allowance. <see cref="Round"/> reduces both operands to whole
    /// hundredths before they are compared, so the smallest difference that can survive is
    /// <c>0.01</c> — twice this value. What the tolerance absorbs is representation noise only: a
    /// worked total that arrives as <c>7.4</c> (seconds ÷ 3600, scale 1) against an allocated total
    /// read from a <c>NUMERIC(8,4)</c> column as <c>7.4000</c> (scale 4) are the same VALUE and must
    /// compare equal. A genuine one-øre mismatch blocks the send.</para>
    ///
    /// <para>The frontend mirror in <c>lib/allocation.ts</c> carries this same number by hand — it
    /// cannot reference this constant, and ADR-028 D4 accepts that. The
    /// <c>ToleranceAllowListTests</c> static check exists so that pair stays the ONLY pair.</para>
    /// </summary>
    public const decimal Tolerance = 0.005m;

    /// <summary>
    /// Hundredths of an hour — the granularity the product displays and settles in, and the
    /// granularity both sides of the comparison are reduced to BEFORE they are compared.
    /// </summary>
    public const int Scale = 2;

    /// <summary>
    /// Rounds an hours total to the comparison granularity. <see cref="MidpointRounding.ToEven"/> by
    /// omission, matching every pre-collapse call site verbatim.
    /// </summary>
    public static decimal Round(decimal hours) => Math.Round(hours, Scale);

    /// <summary>
    /// Evaluates ONE day. Both arguments are raw (unrounded) totals; the returned
    /// <see cref="DayBalance"/> carries the rounded values that the verdict was actually taken on —
    /// which are also the values the gate echoes back in its <c>unbalancedDays</c> payload and the
    /// values the breakdown accumulates its directional sums from. Rounding therefore happens exactly
    /// once per day, in one place, and no caller can compare an unrounded pair by accident.
    /// </summary>
    /// <param name="workedHours">
    /// Raw worked total for the day: summed work-interval hours plus manual hours from
    /// <c>work_time_projection</c>. Zero when the day has no row at all.
    /// </param>
    /// <param name="allocatedHours">
    /// Raw allocated total for the day: summed hours of <c>time_entries_projection</c> rows with
    /// <c>activity_type = 'NORMAL'</c> AND a non-null <c>task_id</c>. Absence-type rows and ordinary
    /// rows naming no project both contribute nothing.
    /// </param>
    public static DayBalance Evaluate(decimal workedHours, decimal allocatedHours)
    {
        var worked = Round(workedHours);
        var allocated = Round(allocatedHours);
        return new DayBalance(worked, allocated, Math.Abs(worked - allocated) < Tolerance);
    }
}

/// <summary>
/// The verdict for one day, plus the rounded operands it was taken on. A value type: it holds no
/// date, no employee and no reference to where the numbers came from — see
/// <see cref="AllocationBalance"/> for why that separation is load-bearing.
/// </summary>
/// <param name="Worked">The worked total, rounded to <see cref="AllocationBalance.Scale"/>.</param>
/// <param name="Allocated">The allocated total, rounded to <see cref="AllocationBalance.Scale"/>.</param>
/// <param name="IsBalanced">True when the two rounded totals match within the tolerance.</param>
public readonly record struct DayBalance(decimal Worked, decimal Allocated, bool IsBalanced)
{
    /// <summary>The negation, so call sites that report imbalance read as their own intent.</summary>
    public bool IsImbalanced => !IsBalanced;

    /// <summary>
    /// Hours worked but not allocated onto any project, for this day. Zero on a balanced day and on
    /// an over-allocated one — this is a directional component, not a magnitude.
    /// </summary>
    public decimal UnderAllocated => Math.Max(0m, Worked - Allocated);

    /// <summary>
    /// Hours allocated onto projects beyond what was worked, for this day. Zero on a balanced day and
    /// on an under-allocated one.
    /// </summary>
    public decimal OverAllocated => Math.Max(0m, Allocated - Worked);

    /// <summary>
    /// The wire vocabulary the send command's <c>422 {kind:"allocation"}</c> payload reports per day:
    /// <c>"under"</c> when more was worked than allocated, <c>"over"</c> otherwise. Only meaningful
    /// when <see cref="IsImbalanced"/>; on a balanced day the two totals are equal and the value is
    /// <c>"over"</c> by the tie-break, exactly as the pre-collapse expression produced it (that
    /// expression was only ever read on an imbalanced day, and still is).
    /// </summary>
    public string Direction => Worked > Allocated ? "under" : "over";
}
