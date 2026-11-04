using System.Globalization;
using StatsTid.Backend.Api.Services;

namespace StatsTid.Tests.Unit.Endpoints;

/// <summary>
/// S147 TIDY-3-T (row 8a, TASK-14723-T) - the Danish sentence the Skema save's 422 <c>absence_full_day_only</c> carries.
///
/// <para>
/// <b>In plain language.</b> When someone books half a care day, the screen says "Omsorgsdag kan kun registreres som hel
/// dag (7,40 timer) den 03-03-2026." A Danish reader expects a decimal comma. Before S147 the number was written with the
/// machine's default format, so on a machine not set to Danish it read "7.40". The sentence now always uses the Danish
/// number format, whatever the machine's language is, and keeps the number's scale (7,40 stays 7,40; 6,00 stays 6,00).
/// The expected texts below are written out from that spec.
/// </para>
/// </summary>
public class AbsenceTypeLabelsFullDayOnlyMessageTests
{
    /// <summary>
    /// Kind N (names <c>FullDayOnlyMessage</c>, which S147 creates). The exact da-DK sentence, asserted under en-US,
    /// invariant and da-DK current cultures, for scales 7.4, 7.40 and 6.00 (the last two pin scale preservation).
    ///
    /// <para><b>Red conditions (N).</b> Mutation: <c>FullDayOnlyMessage</c> interpolates under the current culture
    /// instead of da-DK. Under en-US and invariant the text reads "(7.40 timer)" and the <c>Assert.Equal</c> fails. A
    /// scale-dropping change fails the 7.40 and 6.00 rows. The da-DK row holds under the mutation, so the
    /// en-US and invariant rows are the discriminating ones.</para>
    /// </summary>
    [Theory]
    [InlineData("en-US", "7.4", "Omsorgsdag kan kun registreres som hel dag (7,4 timer) den 03-03-2026.")]
    [InlineData("en-US", "7.40", "Omsorgsdag kan kun registreres som hel dag (7,40 timer) den 03-03-2026.")]
    [InlineData("en-US", "6.00", "Omsorgsdag kan kun registreres som hel dag (6,00 timer) den 03-03-2026.")]
    [InlineData("", "7.40", "Omsorgsdag kan kun registreres som hel dag (7,40 timer) den 03-03-2026.")]
    [InlineData("", "6.00", "Omsorgsdag kan kun registreres som hel dag (6,00 timer) den 03-03-2026.")]
    [InlineData("da-DK", "7.40", "Omsorgsdag kan kun registreres som hel dag (7,40 timer) den 03-03-2026.")]
    public void FullDayOnlyMessage_WritesDecimalComma_AndKeepsScale_UnderAnyCurrentCulture(
        string currentCulture, string hours, string expected)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            // "" is the invariant culture.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(currentCulture);

            var message = AbsenceTypeLabels.FullDayOnlyMessage(
                "CARE_DAY", decimal.Parse(hours, CultureInfo.InvariantCulture), new DateOnly(2026, 3, 3));

            Assert.Equal(expected, message);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
