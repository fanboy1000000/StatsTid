namespace StatsTid.Tools.DemoSeed.Generation;

/// <summary>Per-tree generation profile (OQ-3 org/agreement-mix defaults).</summary>
internal sealed class TreeProfile
{
    public required string OrganisationId { get; init; }
    public required string OrgName { get; init; }

    /// <summary>Target user count for this tree.</summary>
    public required int TargetUsers { get; init; }

    /// <summary>Agreement weights (AC, HK, PROSA) — normalised at draw time.</summary>
    public required (int Ac, int Hk, int Prosa) AgreementMix { get; init; }

    /// <summary>The styrelse root org's own agreement_code (display anchor).</summary>
    public required string RootAgreement { get; init; }
}

/// <summary>
/// S84 — the two scales. <c>smoke</c> is tiny (~3 orgs, ~30 users across 1 tree)
/// for fast end-to-end pipeline validation; <c>full</c> is the realistic ~3,350-employee
/// dataset across 5 styrelse trees (OQ-3 defaults).
/// </summary>
internal sealed class ScaleConfig
{
    public required string Name { get; init; }

    /// <summary>The demo ministry id all trees hang under.</summary>
    public required string MinistryId { get; init; }
    public required string MinistryName { get; init; }

    public required IReadOnlyList<TreeProfile> Trees { get; init; }

    /// <summary>Target span of control (direct reports per manager) — the generator stays in a band around this.</summary>
    public required int TargetSpan { get; init; }

    /// <summary>Fraction of users that get an activity script (OQ-1 light subset).</summary>
    public required double ActivityFraction { get; init; }

    /// <summary>Fraction of users that get a part-time/position profile edit.</summary>
    public required double PartTimeFraction { get; init; }

    /// <summary>Number of hand-curated messy cases.</summary>
    public required int MessyCaseCount { get; init; }

    public static ScaleConfig For(string scale) => scale switch
    {
        "smoke" => Smoke,
        "full" => Full,
        _ => throw new ArgumentException($"Unknown scale '{scale}' (expected 'smoke' or 'full')", nameof(scale)),
    };

    // ── SMOKE: 1 tree, ~30 users, fast pipeline validation ──
    private static readonly ScaleConfig Smoke = new()
    {
        Name = "smoke",
        MinistryId = "MINX",
        MinistryName = "Demoministeriet",
        TargetSpan = 4,
        ActivityFraction = 0.30,
        PartTimeFraction = 0.15,
        MessyCaseCount = 4,
        Trees = new[]
        {
            new TreeProfile
            {
                OrganisationId = "STYX1",
                OrgName = "Demostyrelsen (smoke)",
                TargetUsers = 30,
                AgreementMix = (55, 35, 10),
                RootAgreement = "AC",
            },
        },
    };

    // ── FULL: 5 trees (~2000 / ~600 / 3×~250 = ~3,350), OQ-3 profiles ──
    private static readonly ScaleConfig Full = new()
    {
        Name = "full",
        MinistryId = "MINX",
        MinistryName = "Demoministeriet",
        TargetSpan = 7,
        ActivityFraction = 0.15,
        PartTimeFraction = 0.10,
        MessyCaseCount = 26,
        Trees = new[]
        {
            // Big mixed operational agency
            new TreeProfile
            {
                OrganisationId = "STYX1",
                OrgName = "Den Store Operationelle Styrelse",
                TargetUsers = 2000,
                AgreementMix = (55, 35, 10),
                RootAgreement = "AC",
            },
            // Mid-size policy styrelse, AC-heavy
            new TreeProfile
            {
                OrganisationId = "STYX2",
                OrgName = "Politikstyrelsen",
                TargetUsers = 600,
                AgreementMix = (85, 12, 3),
                RootAgreement = "AC",
            },
            // Small board/naevn — AC
            new TreeProfile
            {
                OrganisationId = "STYX3",
                OrgName = "Klagenaevnet",
                TargetUsers = 250,
                AgreementMix = (80, 18, 2),
                RootAgreement = "AC",
            },
            // Small inspection agency — HK-heavy
            new TreeProfile
            {
                OrganisationId = "STYX4",
                OrgName = "Inspektionsstyrelsen",
                TargetUsers = 250,
                AgreementMix = (25, 70, 5),
                RootAgreement = "HK",
            },
            // Small IT unit — PROSA-present
            new TreeProfile
            {
                OrganisationId = "STYX5",
                OrgName = "Den Digitale Styrelse",
                TargetUsers = 250,
                AgreementMix = (30, 25, 45),
                RootAgreement = "PROSA",
            },
        },
    };
}
