namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S145 / TASK-14513 (refinement A7; rulings R14, R15) — a replay that cannot reproduce its calculation
/// faithfully, refused by name. Thrown only by
/// <see cref="PeriodCalculationService.ReplayAsync(Guid, string?, Guid?, CancellationToken)"/>.
///
/// <para>
/// <b>Why refusals have names (plain language).</b> A replay either gives back exactly what was calculated,
/// or says precisely why it cannot: a recorded hours row is gone, a row's content changed, the agreement
/// configs or the arithmetic changed, or the manifest predates frozen inputs. Silently recomputing from
/// today's data would produce a different payslip labelled as the old one, which is the one thing a replay
/// must never do. <see cref="Reason"/> carries the name; <see cref="Exception.Message"/> starts with it too.
/// </para>
///
/// <para>
/// <b>What it carries.</b> The reason, the manifest id and a fixed description: no hours and no dates. A
/// replay writes nothing, so a refusal leaves nothing behind. No HTTP route replays today; a host that adds
/// one should answer from <see cref="Reason"/>, not from the message.
/// </para>
/// </summary>
public sealed class ReplayRefusedException : Exception
{
    /// <summary>A recorded hours row (a time entry or an absence) is genuinely absent from its read model.
    /// A row superseded by a later re-save is not absent: the replay reads it by its event id.</summary>
    public const string EntriesMissing = "replay-entries-missing";

    /// <summary>The recorded time-entry rows no longer hash to the recorded lossless digest (R15).</summary>
    public const string EntriesDigestMismatch = "replay-entries-digest-mismatch";

    /// <summary>The recorded absence rows no longer hash to the recorded lossless digest (R15).</summary>
    public const string AbsencesDigestMismatch = "replay-absences-digest-mismatch";

    /// <summary>The Rule Engine's config fingerprints for the frozen pieces differ from the recorded ones.</summary>
    public const string ConfigChanged = "replay-config-changed";

    /// <summary>The Rule Engine's evaluation-model marker differs from the recorded one.</summary>
    public const string EvaluationModelChanged = "replay-evaluation-model-changed";

    /// <summary>The manifest carries no (complete) frozen inputs: every manifest written before S145, a
    /// projection row whose <c>plan_inputs_jsonb</c> is NULL, or a damaged record.</summary>
    public const string ManifestWithoutFrozenInputs = "replay-manifest-without-frozen-inputs";

    public ReplayRefusedException(string reason, Guid manifestId, string message)
        : base($"{reason}: {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
        ManifestId = manifestId;
    }

    /// <summary>The refusal's name, one of the constants on this type (for example <see cref="EntriesMissing"/>).</summary>
    public string Reason { get; }

    /// <summary>The manifest whose replay was refused.</summary>
    public Guid ManifestId { get; }
}
