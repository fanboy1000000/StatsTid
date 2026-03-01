using StatsTid.Backend.Api.Validation;

namespace StatsTid.Tests.Unit.Validation;

public class RequestValidatorTests
{
    [Fact]
    public void TimeEntry_MissingEmployeeId_ReturnsError()
    {
        var (isValid, error) = RequestValidator.ValidateTimeEntry(null, 8m, "AC", "OK24");

        Assert.False(isValid);
        Assert.Contains("EmployeeId", error);
    }

    [Fact]
    public void TimeEntry_NegativeHours_ReturnsError()
    {
        var (isValid, error) = RequestValidator.ValidateTimeEntry("EMP001", -1m, "AC", "OK24");

        Assert.False(isValid);
        Assert.Contains("Hours", error);
    }

    [Fact]
    public void TimeEntry_HoursAbove24_ReturnsError()
    {
        var (isValid, error) = RequestValidator.ValidateTimeEntry("EMP001", 25m, "AC", "OK24");

        Assert.False(isValid);
        Assert.Contains("Hours", error);
    }

    [Fact]
    public void TimeEntry_InvalidAgreementCode_ReturnsError()
    {
        var (isValid, error) = RequestValidator.ValidateTimeEntry("EMP001", 8m, "INVALID", "OK24");

        Assert.False(isValid);
        Assert.Contains("AgreementCode", error);
    }

    [Fact]
    public void TimeEntry_InvalidOkVersion_ReturnsError()
    {
        var (isValid, error) = RequestValidator.ValidateTimeEntry("EMP001", 8m, "AC", "OK99");

        Assert.False(isValid);
        Assert.Contains("OkVersion", error);
    }

    [Fact]
    public void TimeEntry_ValidRequest_NoError()
    {
        var (isValid, error) = RequestValidator.ValidateTimeEntry("EMP001", 8m, "AC", "OK24");

        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void Absence_InvalidAbsenceType_ReturnsError()
    {
        var (isValid, error) = RequestValidator.ValidateAbsence("EMP001", 7.4m, "INVALID_TYPE", "AC", "OK24");

        Assert.False(isValid);
        Assert.Contains("AbsenceType", error);
    }

    [Fact]
    public void Absence_ValidRequest_NoError()
    {
        var (isValid, error) = RequestValidator.ValidateAbsence("EMP001", 7.4m, "VACATION", "AC", "OK24");

        Assert.True(isValid);
        Assert.Null(error);
    }
}
