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

    // ── S114 / TASK-11400 — unit-name pools for the derived unit spine. ASCII-transliterated
    //    like every other pool (repo convention: "Driftsomraadet", "Klagenaevnet"). The depth-0
    //    direktion is always named "Direktionen" (one per org); depths 1–4 draw from these via
    //    the DERIVED unit RNG with per-parent used-name tracking + a numbered suffix fallback
    //    (sibling ACTIVE-name uniqueness is a DB constraint — the API 409s duplicates). ──

    /// <summary>Depth-1 <c>omrade</c> base names.</summary>
    internal static readonly string[] OmraadeNames =
    {
        "Driftsomraadet", "Politikomraadet", "Digitaliseringsomraadet", "Oekonomiomraadet",
        "HR-omraadet", "Tilsynsomraadet", "Analyseomraadet", "Juraomraadet",
    };

    /// <summary>Depth-2 <c>kontor</c> base names.</summary>
    internal static readonly string[] KontorNames =
    {
        "Oekonomikontoret", "Personalekontoret", "IT-kontoret", "Juridisk Kontor",
        "Analysekontoret", "Tilsynskontoret", "Sekretariatet", "Driftskontoret",
        "Udviklingskontoret", "Planlaegningskontoret", "Kommunikationskontoret", "Indkoebskontoret",
    };

    /// <summary>Depth-3 <c>team</c> base names.</summary>
    internal static readonly string[] TeamNames =
    {
        "Team Nord", "Team Syd", "Team Oest", "Team Vest", "Team Midt",
        "Team Alfa", "Team Beta", "Team Gamma", "Team Delta", "Team Omega",
    };

    /// <summary>Depth-4 <c>enhed</c> base names.</summary>
    internal static readonly string[] EnhedNames =
    {
        "Driftsenheden", "Supportenheden", "Sagsenheden", "Kontrolenheden", "Dataenheden",
        "Serviceenheden", "Arkivenheden", "Vagtenheden", "Kvalitetsenheden", "Registerenheden",
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
