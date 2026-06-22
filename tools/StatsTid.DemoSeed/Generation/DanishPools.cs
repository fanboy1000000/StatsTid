namespace StatsTid.Tools.DemoSeed.Generation;

/// <summary>
/// S84 — fixed Danish name + org-name fragment pools. STATIC data only (no randomness here);
/// the generator draws from these via a seeded RNG so output is deterministic. Diacritics are
/// transliterated in emails (oe/aa/ae) to keep generated addresses ASCII-safe.
/// </summary>
internal static class DanishPools
{
    internal static readonly string[] FirstNames =
    {
        "Anders", "Mette", "Lars", "Anne", "Peter", "Camilla", "Henrik", "Louise",
        "Michael", "Maria", "Thomas", "Pernille", "Jakob", "Hanne", "Morten", "Susanne",
        "Christian", "Lene", "Martin", "Charlotte", "Jesper", "Tina", "Niels", "Bente",
        "Rasmus", "Karen", "Soeren", "Helle", "Mads", "Dorte", "Kasper", "Birgitte",
        "Frederik", "Gitte", "Daniel", "Inge", "Simon", "Marianne", "Jonas", "Lone",
        "Mikkel", "Vibeke", "Emil", "Rikke", "Andreas", "Signe", "Oliver", "Cecilie",
        "Magnus", "Ida", "Tobias", "Astrid", "Victor", "Freja", "William", "Clara",
        "Noah", "Josefine", "Carl", "Laura", "Bo", "Eva", "Klaus", "Else",
    };

    internal static readonly string[] LastNames =
    {
        "Jensen", "Nielsen", "Hansen", "Pedersen", "Andersen", "Christensen", "Larsen",
        "Soerensen", "Rasmussen", "Joergensen", "Petersen", "Madsen", "Kristensen",
        "Olsen", "Thomsen", "Christiansen", "Poulsen", "Johansen", "Knudsen", "Mortensen",
        "Moeller", "Jacobsen", "Olesen", "Frederiksen", "Mikkelsen", "Henriksen",
        "Laursen", "Lund", "Schmidt", "Holm", "Berg", "Dahl", "Bach", "Friis",
        "Kjaer", "Bruun", "Vestergaard", "Soendergaard", "Noergaard", "Bak",
    };

    /// <summary>
    /// S92 / ADR-035 — Enhed (former-unit) display-label fragments. Post-flatten these are NO
    /// longer org rows: the AFDELING/TEAM tree is collapsed, so each fragment becomes the
    /// display-only <c>employee_profiles.enhed_label</c> of a user who now sits directly on the
    /// parent Organisation. The pool is deliberately broad (centre + kontor + team style names)
    /// so the demo shows realistic Enhed variety within a single flat Organisation.
    /// </summary>
    internal static readonly string[] EnhedFragments =
    {
        // Centre-style enheder
        "Center for Drift", "Center for Udvikling", "Center for Tilsyn", "Center for Analyse",
        "Center for Jura", "Center for Oekonomi", "Center for Digitalisering", "Center for HR",
        "Direktionssekretariatet", "Center for Kommunikation", "Center for Sagsbehandling",
        "Center for Borgerservice",
        // Kontor-style enheder
        "Driftskontoret", "Udviklingskontoret", "Tilsynskontoret", "Analysekontoret",
        "Juridisk Kontor", "Oekonomikontoret", "IT-Kontoret", "HR-Kontoret",
        "Planlaegningskontoret", "Sekretariatet", "Sagskontoret", "Servicekontoret",
        "Kvalitetskontoret", "Projektkontoret", "Statistikkontoret", "Indkoebskontoret",
        // Team-style enheder
        "Team Alfa", "Team Beta", "Team Gamma", "Team Delta", "Team Epsilon", "Team Zeta",
        "Team Nord", "Team Syd", "Team Oest", "Team Vest", "Team Blaa", "Team Groen",
        "Team Roed", "Team Gul", "Team 1", "Team 2", "Team 3", "Team 4",
    };

    internal static readonly string[] EmploymentCategories =
    {
        "Fuldmaegtig", "Specialkonsulent", "Chefkonsulent", "Kontorchef", "Standard",
    };

    /// <summary>Position titles for the part-time/profile subset.</summary>
    internal static readonly string[] Positions =
    {
        "Fuldmaegtig", "Specialkonsulent", "Chefkonsulent", "AC-fuldmaegtig",
        "Kontorfunktionaer", "Sagsbehandler", "Konsulent", "Studentermedhjaelper",
        "IT-specialist", "Analytiker",
    };

    /// <summary>
    /// ASCII-transliterate Danish letters so generated emails are valid. Names keep their
    /// pool spelling (already ASCII-safe in the pools above — oe/aa/ae used in place of
    /// oe/aa/ae glyphs); this is a defensive pass for any future diacritics.
    /// </summary>
    internal static string ToAsciiSlug(string s)
    {
        return s
            .Replace("æ", "ae").Replace("Æ", "Ae")
            .Replace("ø", "oe").Replace("Ø", "Oe")
            .Replace("å", "aa").Replace("Å", "Aa")
            .Replace(" ", ".")
            .ToLowerInvariant();
    }
}
