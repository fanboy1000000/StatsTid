namespace StatsTid.Backend.Api.Contracts;

public sealed class LoginResponse
{
    public required string Token { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required string EmployeeId { get; init; }
    public required string Role { get; init; }
    public string? OrgId { get; init; }
}
