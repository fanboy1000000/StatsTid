using System.Globalization;
using StatsTid.Backend.Api.Services;

namespace StatsTid.Tests.Unit.Endpoints;

/// <summary>
/// S147 TIDY-3-T (row 8a, TASK-14723-T) - the Danish sentence the Skema save's 422 <c>absence_full_day_only</c> carries.
///
/// <para>
/// <b>In plain language.</b> When someone books half a care day, the screen says "Omsorgsdag kan kun registreres som hel
/// dag (7,4 timer) den 03-03-2026." A Danish reader expects a decimal comma. The server container sets no language, so
/// before S147 the number was written with the machine's default and read "7.4". The sentence now always uses the Danish
/// number format, whatever the machine's language is. The expected text below is written out from that spec.
/// </para>
/// </summary>
public class AbsenceTypeLabelsFullDayOnlyMessageTests
{
    /// <summary>
    /// Kind N (names <c>FullDayOnlyMessage</c>, which S147 creates). The exact da-DK sentence, asserted while the current
    /// culture is en-US, whose decimal mark is a point, so the fact cannot pass by accident.
    ///
    /// <para><b>Red conditions (N).</b> Mutation: <c>FullDayOnlyMessage</c> interpolates under the current culture
    /// instead of da-DK. Under en-US the text reads "(7.4 timer)" and the <c>Assert.Equal</c> fails.</para>
    /// </summary>
    [Fact]
    public void FullDayOnlyMessage_WritesDecimalComma_EvenUnderEnUsCurrentCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            var message = AbsenceTypeLabels.FullDayOnlyMessage("CARE_DAY", 7.4m, new DateOnly(2026, 3, 3));

            Assert.Equal("Omsorgsdag kan kun registreres som hel dag (7,4 timer) den 03-03-2026.", message);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
