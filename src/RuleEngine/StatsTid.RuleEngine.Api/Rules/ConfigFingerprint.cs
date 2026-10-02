using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// S145 / TASK-14505 — the fingerprint of the agreement configs a calculation actually ran with
/// (replay contract, refinement rev 3.4 A7: "freeze what is data, fingerprint what is code").
///
/// <para>
/// <b>Why.</b> Agreement configs are code (static tables in SharedKernel), so a stored
/// calculation cannot keep a copy of them. Instead the Rule Engine returns a fingerprint of the
/// configs it resolved, payroll stores it with the calculation, and a replay whose engine now
/// resolves different configs is refused by name (<c>replay-config-changed</c>) instead of
/// silently producing different money.
/// </para>
///
/// <para>
/// <b>Algorithm.</b>
/// <list type="number">
///   <item><description>Each (agreement, OK version, position, config) becomes one text block:
///   a header line <c>config|{AgreementCode}|{OkVersion}|{Position}</c> (null position → empty),
///   then one line per public instance property of <see cref="AgreementRuleConfig"/> as
///   <c>Name=Value</c>, in ordinal order of the property name. Values: decimals value-normalised
///   in invariant culture (no trailing zeros: <c>37.0</c> → <c>37</c>), integers invariant,
///   booleans <c>true</c>/<c>false</c>, enums by name, strings as-is, null as empty. A property
///   of any other type throws, so a future property can never be silently left out.</description></item>
///   <item><description>Identical blocks are counted once; blocks are sorted ordinally by
///   (agreement, OK version, position), then by block text.</description></item>
///   <item><description>The blocks are joined with <c>\n</c>, encoded UTF-8 and hashed with
///   SHA-256; the result is lowercase hex (64 characters).</description></item>
/// </list>
/// Order-independent in its input and in property declaration order; changes when any property
/// value of any resolved config changes. Pure: reflection over a fixed type, no I/O (ADR-002).
/// </para>
/// </summary>
public static class ConfigFingerprint
{
    private static readonly PropertyInfo[] FingerprintedProperties = typeof(AgreementRuleConfig)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetIndexParameters().Length == 0)
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Lowercase-hex SHA-256 over the canonical text of the distinct resolved configs.</summary>
    public static string Compute(
        IEnumerable<(string AgreementCode, string OkVersion, string? Position, AgreementRuleConfig Config)> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var blocks = new List<(string Agreement, string OkVersion, string Position, string Text)>();
        foreach (var (agreementCode, okVersion, position, config) in configs)
        {
            ArgumentNullException.ThrowIfNull(config);
            var agreement = agreementCode ?? string.Empty;
            var version = okVersion ?? string.Empty;
            var pos = position ?? string.Empty;
            blocks.Add((agreement, version, pos, Render(agreement, version, pos, config)));
        }

        var canonical = string.Join(
            "\n",
            blocks
                .DistinctBy(b => b.Text, StringComparer.Ordinal)
                .OrderBy(b => b.Agreement, StringComparer.Ordinal)
                .ThenBy(b => b.OkVersion, StringComparer.Ordinal)
                .ThenBy(b => b.Position, StringComparer.Ordinal)
                .ThenBy(b => b.Text, StringComparer.Ordinal)
                .Select(b => b.Text));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Render(string agreement, string okVersion, string position, AgreementRuleConfig config)
    {
        var text = new StringBuilder();
        text.Append("config|").Append(agreement).Append('|').Append(okVersion).Append('|').Append(position);
        foreach (var property in FingerprintedProperties)
        {
            text.Append('\n')
                .Append(property.Name)
                .Append('=')
                .Append(FormatValue(property, property.GetValue(config)));
        }
        return text.ToString();
    }

    private static string FormatValue(PropertyInfo property, object? value) => value switch
    {
        null => string.Empty,
        decimal d => Normalize(d).ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => s,
        Enum e => e.ToString(),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException(
            $"ConfigFingerprint has no canonical form for AgreementRuleConfig.{property.Name} " +
            $"of type {property.PropertyType.Name}; add one before shipping the property."),
    };

    /// <summary>Removes trailing zeros (37.00 → 37, 1.250 → 1.25) without changing the value.</summary>
    private static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;
}
