namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S146 / TASK-14604 (QUAL-205; rulings R-22, R-27) — a calculation that cannot be proven later, refused before
/// any payroll write. Thrown only by
/// <see cref="PeriodCalculationService.PeriodCalculationOutcome.ThrowIfNotProvable"/>, the one predicate both
/// calculating routes call.
///
/// <para>
/// <b>Plain language.</b> Every payroll figure must be provable after the fact from its stored manifest (the
/// record of how the month was split and which inputs it ran on). When the manifest could not be stored, the
/// figure is correct today but unprovable tomorrow, so it is not exported and no correction is written from it.
/// The caller can retry: the refusal leaves nothing behind.
/// </para>
///
/// <para>
/// <b>When.</b> The outcome's audit state is <see cref="PeriodCalculationService.AuditState.NotPersisted"/> (the
/// commit of the one transaction that writes the manifest, its projection row and the legacy summary event was not
/// confirmed; a lost commit acknowledgement can leave all three records committed, and payroll still refuses), or
/// it is <see cref="PeriodCalculationService.AuditState.NoManifest"/> on a SUCCESSFUL result (a forward
/// calculation that emitted no manifest at all). A refused calculation (<c>Success == false</c>) carries
/// <c>NoManifest</c> by design and is not this exception's business; a replay carries <c>NoManifest</c> on
/// success by design (it mints no manifest) and never calls the predicate.
/// </para>
///
/// <para>
/// <b>The response.</b> The host maps it to a 503 <c>{ success:false, error, kind:"payroll-manifest-not-persisted" }</c>
/// with the fixed <see cref="ErrorSentence"/>: no employee id, no date and no manifest id.
/// <see cref="Exception.Message"/> is for diagnosis and never reaches a response.
/// </para>
/// </summary>
public sealed class ManifestNotPersistedException : Exception
{
    /// <summary>The discriminator of the host's 503 body.</summary>
    public const string KindValue = "payroll-manifest-not-persisted";

    /// <summary>
    /// The fixed sentence of the host's 503 body. It contains no date, no id and no digit, so it can never leak
    /// one.
    /// </summary>
    public const string ErrorSentence =
        "The calculation's manifest, the record that makes its result provable later, could not be stored, so " +
        "the result is not used. Nothing was exported, corrected or written to payroll. Retry the request; if it " +
        "fails again, the Payroll service's database needs attention.";

    public ManifestNotPersistedException(Guid manifestId, string message)
        : base(message)
    {
        ManifestId = manifestId;
    }

    /// <summary>
    /// The manifest id of the refused calculation's plan, for diagnosis only. It normally resolves in no store (a
    /// commit that failed in flight may still have stored it, which is harmless: nothing was exported from it).
    /// </summary>
    public Guid ManifestId { get; }
}
