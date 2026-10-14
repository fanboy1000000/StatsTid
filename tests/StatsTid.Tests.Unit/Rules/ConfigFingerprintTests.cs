using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S145 / TASK-14506 — pins for <c>ConfigFingerprint.Compute</c> and the evaluation-model marker
/// (refinement A7, "Fingerprint form", :390; TASK-14505 contract names).
///
/// <para>
/// <b>Plain language.</b> A replay has to prove it re-ran a payslip on the same rules. The agreement
/// settings (norm, overtime thresholds, flex cap, …) are <i>code</i>, not stored data, so the engine
/// stamps each calculation with a fingerprint: a SHA-256 over every setting of every configuration it
/// used. Later, a replay recomputes the fingerprint; if it differs, someone changed the rules and the
/// replay is refused by name (<c>replay-config-changed</c>) instead of silently producing a different
/// number. Two things must therefore hold: <b>every</b> setting must move the fingerprint (a setting
/// left out of it could change silently), and the fingerprint must <b>not</b> depend on incidental
/// things (the order properties happen to be declared in, the order configs are listed in, how many
/// trailing zeros a decimal carries) — otherwise a harmless refactor would invalidate every stored
/// fingerprint.
/// </para>
///
/// <para>
/// <b>The canonical text is a contract.</b> The refinement (:390) left the separator, boolean
/// spelling and header open; the Orchestrator has since ruled one exact form (see the block comment
/// above <c>Timeline_ConfigFingerprint_StableAcrossPropertyOrder</c>) and the pins assert it exactly.
/// A hash matching no spelling of it is RED; the form is never widened back.
/// </para>
///
/// <para>
/// <b>Red conditions</b> (RED by MUTATION at the A.1 gate): MU-18 omits one property from the
/// fingerprint and trips the matching row of
/// <c>Timeline_ConfigFingerprint_ChangesWhenAnyConfigPropertyChanges</c>; MU-19 hashes the
/// properties in declaration order instead of ordinal order and trips
/// <c>Timeline_ConfigFingerprint_StableAcrossPropertyOrder</c>.
/// </para>
/// </summary>
public sealed class ConfigFingerprintTests
{
    private static readonly PropertyInfo[] ConfigProperties =
        typeof(AgreementRuleConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static AgreementRuleConfig BaseConfig() => CentralAgreementConfigs.GetConfig("HK", "OK26");

    private static string Fingerprint(AgreementRuleConfig config, string agreement = "HK", string ok = "OK26", string? position = null) =>
        ConfigFingerprint.Compute(new[] { (agreement, ok, position, config) });

    // -------------------------------------------------------------------------
    // Reflection clone: AgreementRuleConfig has init-only properties and no `with`, so a variant is
    // built by copying every public property and replacing one.
    // -------------------------------------------------------------------------
    private static AgreementRuleConfig CloneWith(AgreementRuleConfig source, string? changedProperty = null, object? newValue = null)
    {
        var clone = (AgreementRuleConfig)Activator.CreateInstance(typeof(AgreementRuleConfig))!;
        foreach (var property in ConfigProperties)
        {
            var value = property.Name == changedProperty ? newValue : property.GetValue(source);
            property.SetValue(clone, value);
        }
        return clone;
    }

    /// <summary>A different VALUE of the property's own type (never merely a different decimal scale).</summary>
    private static object ChangedValue(PropertyInfo property, object current) => current switch
    {
        string s => s + "-changed",
        bool b => !b,
        int i => i + 1,
        decimal d => d + 1m,
        Enum e => Enum.GetValues(e.GetType()).Cast<object>().First(v => !v.Equals(e)),
        _ => throw new InvalidOperationException(
            $"AgreementRuleConfig.{property.Name} has type {property.PropertyType.Name}, which this pin does not know how to vary. " +
            "Teach ChangedValue about it — a property the pin cannot vary is a property the fingerprint cannot be proven to cover."),
    };

    public static IEnumerable<object[]> EveryPublicProperty() =>
        ConfigProperties.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(EveryPublicProperty))]
    public void Timeline_ConfigFingerprint_ChangesWhenAnyConfigPropertyChanges(string propertyName)
    {
        var baseline = BaseConfig();
        var property = ConfigProperties.Single(p => p.Name == propertyName);
        var variant = CloneWith(baseline, propertyName, ChangedValue(property, property.GetValue(baseline)!));

        // Control: the clone itself is faithful — copying every property and changing none yields the
        // same fingerprint. Without this a "changed" result could just be a bad clone.
        Assert.Equal(Fingerprint(baseline), Fingerprint(CloneWith(baseline)));

        // The variant differs from the baseline in exactly one property, so the fingerprint must move.
        Assert.True(
            Fingerprint(baseline) != Fingerprint(variant),
            $"Changing only AgreementRuleConfig.{propertyName} did not change the config fingerprint — " +
            "that setting could change silently without a replay noticing (refinement :390: EVERY public property).");
    }

    [Fact]
    public void Timeline_ConfigFingerprint_EnumeratesAtLeastTheKnownConfigSurface()
    {
        // Guards the guard: the theory above is driven by reflection, so an accidental empty or
        // truncated enumeration would pass vacuously. The config has 42 public instance properties
        // (counted at cdd8253); a floor of 40 catches a reflection filter that went wrong without
        // forcing an edit every time a property is added.
        Assert.True(ConfigProperties.Length >= 40, $"Only {ConfigProperties.Length} properties enumerated.");
        Assert.Contains(ConfigProperties, p => p.Name == "WeeklyNormHours");
        Assert.Contains(ConfigProperties, p => p.Name == "NormModel");
    }

    [Fact]
    public void Timeline_ConfigFingerprint_IsLowercaseHexSha256_AndDeterministic()
    {
        var first = Fingerprint(BaseConfig());
        var second = Fingerprint(BaseConfig());

        Assert.Matches(new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant), first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Timeline_ConfigFingerprint_DecimalScale_DoesNotChangeTheFingerprint()
    {
        // "Decimals are value-normalised in invariant culture (no trailing zeros)" (refinement :390):
        // 37.0 and 37.00 are the same value and must give the same fingerprint, or a harmless edit of
        // a literal in CentralAgreementConfigs would invalidate every stored fingerprint.
        var oneDecimal = CloneWith(BaseConfig(), nameof(AgreementRuleConfig.WeeklyNormHours), 37.0m);
        var threeDecimals = CloneWith(BaseConfig(), nameof(AgreementRuleConfig.WeeklyNormHours), 37.000m);
        var integral = CloneWith(BaseConfig(), nameof(AgreementRuleConfig.WeeklyNormHours), 37m);

        Assert.Equal(Fingerprint(oneDecimal), Fingerprint(threeDecimals));
        Assert.Equal(Fingerprint(oneDecimal), Fingerprint(integral));

        // …while a different VALUE still differs (so the check above is not vacuous).
        var different = CloneWith(BaseConfig(), nameof(AgreementRuleConfig.WeeklyNormHours), 36.9m);
        Assert.NotEqual(Fingerprint(oneDecimal), Fingerprint(different));
    }

    [Fact]
    public void Timeline_ConfigFingerprint_IndependentOfConfigOrder_AndCollapsesDuplicates()
    {
        // "For each DISTINCT resolved config (sorted ordinally by agreement, OK version, position)".
        var hk = ("HK", "OK26", (string?)null, CentralAgreementConfigs.GetConfig("HK", "OK26"));
        var ac = ("AC", "OK26", (string?)null, CentralAgreementConfigs.GetConfig("AC", "OK26"));

        var hkThenAc = ConfigFingerprint.Compute(new[] { hk, ac });
        var acThenHk = ConfigFingerprint.Compute(new[] { ac, hk });
        var withDuplicate = ConfigFingerprint.Compute(new[] { hk, ac, hk });

        Assert.Equal(hkThenAc, acThenHk);
        Assert.Equal(hkThenAc, withDuplicate);

        // …and the set {HK, AC} is not the set {HK}.
        Assert.NotEqual(hkThenAc, ConfigFingerprint.Compute(new[] { hk }));
    }

    // -------------------------------------------------------------------------
    // The canonical text — a CONTRACT (Orchestrator ruling, recorded for the A.1 gate), derived from
    // the ruling's text and not from the production code:
    //   each (agreement, OK version, position, config) is one block: header line
    //   `config|{AgreementCode}|{OkVersion}|{Position}` (null position -> empty), then one line per
    //   public instance property as `Name=Value` in ordinal order of the property name. Decimals are
    //   value-normalised, invariant culture, no trailing zeros (37.0 -> 37); integers invariant;
    //   booleans true/false; enums by name; strings as-is; null as empty; any other type throws.
    //   Identical blocks count once; blocks are sorted ordinally by (agreement, OK version, position)
    //   and then by block text; joined with "\n", UTF-8, SHA-256, lowercase hex.
    // -------------------------------------------------------------------------

    private static string NormalizedDecimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string Render(PropertyInfo property, object? value) => value switch
    {
        null => "",
        decimal d => NormalizedDecimal(d),
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        int i => i.ToString(CultureInfo.InvariantCulture),
        string s => s,
        _ => throw new InvalidOperationException(
            $"AgreementRuleConfig.{property.Name} has type {property.PropertyType.Name}, which the canonical form does not define."),
    };

    private static string Block(
        AgreementRuleConfig config, string agreement, string ok, string? position, IEnumerable<PropertyInfo> propertiesInOrder)
    {
        var lines = new List<string> { $"config|{agreement}|{ok}|{position ?? ""}" };
        lines.AddRange(propertiesInOrder.Select(p => $"{p.Name}={Render(p, p.GetValue(config))}"));
        return string.Join("\n", lines);
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static IEnumerable<PropertyInfo> OrdinalProperties() =>
        ConfigProperties.OrderBy(p => p.Name, StringComparer.Ordinal);

    /// <summary>
    /// "In ordinal name order" (refinement :390) and the ONE canonical text above. The produced hash
    /// must equal the hash of the ordinal-ordered text exactly, and must NOT equal the hash of the
    /// declaration-order text (which would silently change if anyone reordered the properties in
    /// <c>AgreementRuleConfig</c>, so every stored fingerprint would read as "the rules changed").
    /// MU-19 (declaration order) trips both assertions.
    /// </summary>
    [Fact]
    public void Timeline_ConfigFingerprint_StableAcrossPropertyOrder()
    {
        var config = BaseConfig();
        var actual = Fingerprint(config);

        var ordinal = OrdinalProperties().ToList();
        var declaration = ConfigProperties.ToList();

        // Sanity: the two orders really differ for this type, otherwise the pin proves nothing.
        Assert.NotEqual(
            ordinal.Select(p => p.Name).ToArray(),
            declaration.Select(p => p.Name).ToArray());

        var expected = Sha256Hex(Block(config, "HK", "OK26", null, ordinal));
        var declarationHash = Sha256Hex(Block(config, "HK", "OK26", null, declaration));

        Assert.NotEqual(declarationHash, actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Timeline_ConfigFingerprint_MultipleBlocks_SortedByKey_JoinedByNewline_PositionInHeader()
    {
        var hk = CentralAgreementConfigs.GetConfig("HK", "OK26");
        var ac = CentralAgreementConfigs.GetConfig("AC", "OK26");
        var acHead = CentralAgreementConfigs.GetConfig("AC", "OK26", "DEPARTMENT_HEAD");
        var ordinal = OrdinalProperties().ToList();

        // Sorted ordinally by (agreement, OK version, position): AC/null < AC/DEPARTMENT_HEAD < HK/null
        // (an empty position sorts before a non-empty one).
        var expected = Sha256Hex(string.Join("\n",
            Block(ac, "AC", "OK26", null, ordinal),
            Block(acHead, "AC", "OK26", "DEPARTMENT_HEAD", ordinal),
            Block(hk, "HK", "OK26", null, ordinal)));

        var actual = ConfigFingerprint.Compute(new[]
        {
            ("HK", "OK26", (string?)null, hk),
            ("AC", "OK26", "DEPARTMENT_HEAD", acHead),
            ("AC", "OK26", (string?)null, ac),
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Timeline_EvaluationModel_IsWeeklyTimelineV1()
    {
        // The marker a replay must match (`replay-evaluation-model-changed`, refinement A7). Changing
        // the arithmetic means bumping this string on purpose; changing the string by accident would
        // make every stored manifest read as "arithmetic changed".
        Assert.Equal("weekly-timeline-v1", WeeklyTimelineCore.EvaluationModel);
    }
}
