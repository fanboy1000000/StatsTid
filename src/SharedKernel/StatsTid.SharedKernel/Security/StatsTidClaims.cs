namespace StatsTid.SharedKernel.Security;

public static class StatsTidClaims
{
    public const string EmployeeId = "employee_id";
    public const string Role = "role";
    public const string AgreementCode = "agreement_code";
    public const string OrgId = "org_id";           // NEW: primary org
    public const string Scopes = "scopes";           // NEW: JSON array of role scopes
}
