using StatsTid.Infrastructure.Outbox;

namespace StatsTid.Tests.Unit.Outbox;

/// <summary>
/// S23 / TASK-2302 — pure unit coverage for <see cref="OutboxCorrelationParser"/>.
///
/// <para>
/// The publisher's old inline ternary silently dropped unparseable
/// correlation_id values. The parser now surfaces three explicit outcomes
/// (Null / Parsed / ParseFailure); the publisher logs a warning on
/// ParseFailure before binding DBNull, so the audit-chain breadcrumb
/// survives in logs even though it cannot survive in the canonical
/// <c>events.correlation_id UUID</c> column.
/// </para>
/// </summary>
public sealed class OutboxCorrelationParserTests
{
    [Fact]
    public void Parse_NullInput_ReturnsNullOutcome_WithDBNull()
    {
        var (outcome, dbValue) = OutboxCorrelationParser.Parse(null);

        Assert.Equal(CorrelationParseOutcome.Null, outcome);
        Assert.Equal(DBNull.Value, dbValue);
    }

    [Fact]
    public void Parse_ValidGuid_ReturnsParsedOutcome_WithBoxedGuid()
    {
        var raw = "11111111-2222-3333-4444-555555555555";

        var (outcome, dbValue) = OutboxCorrelationParser.Parse(raw);

        Assert.Equal(CorrelationParseOutcome.Parsed, outcome);
        Assert.IsType<Guid>(dbValue);
        Assert.Equal(Guid.Parse(raw), (Guid)dbValue);
    }

    [Fact]
    public void Parse_BraceFormGuid_ReturnsParsedOutcome()
    {
        // Guid.TryParse accepts both unbraced and `{...}` forms; the parser
        // inherits that contract.
        var raw = "{11111111-2222-3333-4444-555555555555}";

        var (outcome, dbValue) = OutboxCorrelationParser.Parse(raw);

        Assert.Equal(CorrelationParseOutcome.Parsed, outcome);
        Assert.Equal(Guid.Parse(raw), (Guid)dbValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("11111111-2222-3333-4444-55555555555")]   // 11 chars in last group
    [InlineData("zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz")] // valid shape, invalid hex
    public void Parse_NonGuidInput_ReturnsParseFailure_WithDBNull(string raw)
    {
        var (outcome, dbValue) = OutboxCorrelationParser.Parse(raw);

        Assert.Equal(CorrelationParseOutcome.ParseFailure, outcome);
        Assert.Equal(DBNull.Value, dbValue);
    }
}
