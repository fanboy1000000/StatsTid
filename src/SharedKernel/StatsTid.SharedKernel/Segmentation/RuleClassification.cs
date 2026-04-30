namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Multi-axis classification of a single registered rule (ADR-016 D2). Carries the
/// <see cref="Span"/>, <see cref="SplitBehavior"/>, <see cref="Family"/> triple together
/// with the <em>resolved</em> <see cref="MergeStrategy"/> (default-derived from the triple
/// at registration time per ADR-016 D3, or a per-rule override) and an optional
/// <see cref="SnapshotContract"/> declaring which non-effective-dated source fields the
/// rule reads (ADR-016 D5b).
///
/// <strong>Owner of this type:</strong> the planner is the consumer that defines the
/// input contract; <c>RuleRegistry</c> (TASK-2006) is the producer. Producing this record
/// from <c>RuleRegistry.Register</c>'s arguments + per-rule override is TASK-2006's job.
///
/// The <see cref="MergeStrategy"/> field is non-nullable here because the planner depends
/// on every classification carrying a resolved strategy — a missing strategy is a
/// compile-time failure at registration time, not a runtime invariant we re-check here.
/// (ADR-016 D9 lists "every rule has a non-null resolved <see cref="MergeStrategy"/>" as
/// a planner-checked invariant; the C# type system carries that invariant for us as long
/// as <c>RuleRegistry.Register</c> populates the field — TASK-2006 owns that.)
/// </summary>
public sealed record RuleClassification(
    string RuleId,
    Span Span,
    SplitBehavior SplitBehavior,
    Family Family,
    MergeStrategy MergeStrategy,
    SnapshotContract? SnapshotContract);
