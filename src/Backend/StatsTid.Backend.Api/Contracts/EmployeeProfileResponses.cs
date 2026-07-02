namespace StatsTid.Backend.Api.Contracts;

// S112 / TASK-11201 (Fork B retrofit, PAT-010/PAT-012) — the named response record for the
// employee-profile admin read/edit (GET 200 + PUT 200 on /api/admin/employee-profiles/{employeeId}
// — both handlers previously returned the SAME anonymous 5-field shape). EXACT shape-copy: member
// order mirrors the prior anonymous order (employeeId … version), serialized camelCase via the
// .NET 8 JsonSerializerDefaults.Web default — NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON.

/// <summary>The employee-profile admin body (GET read / PUT edit result). <paramref name="Position"/>
/// null = no position override (base agreement config applies); <paramref name="IsPartTime"/> is
/// derived (<c>partTimeFraction &lt; 1.0</c>); <paramref name="Version"/> matches the ETag
/// header.</summary>
public sealed record EmployeeProfileResponse(
    string EmployeeId,
    decimal PartTimeFraction,
    string? Position,
    bool IsPartTime,
    long Version);
