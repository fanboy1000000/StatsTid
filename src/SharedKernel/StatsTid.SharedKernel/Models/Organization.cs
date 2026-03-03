namespace StatsTid.SharedKernel.Models;

public sealed class Organization
{
    public required string OrgId { get; init; }
    public required string OrgName { get; init; }
    public required string OrgType { get; init; }  // MINISTRY, STYRELSE, AFDELING, TEAM
    public string? ParentOrgId { get; init; }
    public required string MaterializedPath { get; init; }  // e.g. "/MIN01/STY01/"
    public required string AgreementCode { get; init; }  // default agreement for org
    public required string OkVersion { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
