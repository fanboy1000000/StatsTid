namespace StatsTid.SharedKernel.Security;

public sealed class JwtSettings
{
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string SigningKey { get; init; }
    public int ExpirationMinutes { get; init; } = 480;
}
