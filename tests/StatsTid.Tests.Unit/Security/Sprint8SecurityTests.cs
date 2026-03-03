using System.IdentityModel.Tokens.Jwt;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// Tests for Sprint 8: RBAC role hierarchy, organizational scoping,
/// JWT token enhancements, domain models, and event serialization.
/// </summary>
public class Sprint8SecurityTests
{
    private static JwtSettings TestSettings => new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = "TestKey_MustBeAtLeast32BytesLong_ForHmacSha256!",
        ExpirationMinutes = 60
    };

    // ---------------------------------------------------------------
    // 1. RoleScope.CoversOrg Tests (5 tests)
    // ---------------------------------------------------------------

    [Fact]
    public void RoleScope_GlobalScope_CoversEverything()
    {
        var scope = new RoleScope("GlobalAdmin", null, "GLOBAL");

        Assert.True(scope.CoversOrg("/MIN01/STY02/AFD01/", null));
        Assert.True(scope.CoversOrg("/MIN01/", null));
        Assert.True(scope.CoversOrg("/ANOTHER/ORG/PATH/", null));
    }

    [Fact]
    public void RoleScope_OrgAndDescendants_CoversChildOrgs()
    {
        var scope = new RoleScope("LocalHR", "MIN01", "ORG_AND_DESCENDANTS");

        Assert.True(scope.CoversOrg("/MIN01/STY02/AFD01/", "/MIN01/"));
        Assert.True(scope.CoversOrg("/MIN01/STY01/", "/MIN01/"));
        Assert.True(scope.CoversOrg("/MIN01/", "/MIN01/"));
    }

    [Fact]
    public void RoleScope_OrgAndDescendants_DoesNotCoverSiblingSubtrees()
    {
        var scope = new RoleScope("LocalAdmin", "STY02", "ORG_AND_DESCENDANTS");

        // /MIN01/STY01/ does NOT start with /MIN01/STY02/
        Assert.False(scope.CoversOrg("/MIN01/STY01/", "/MIN01/STY02/"));
        // A completely different subtree
        Assert.False(scope.CoversOrg("/MIN02/STY01/", "/MIN01/STY02/"));
    }

    [Fact]
    public void RoleScope_OrgOnly_CoversExactMatchOnly()
    {
        var scope = new RoleScope("Employee", "AFD01", "ORG_ONLY");

        Assert.True(scope.CoversOrg("/MIN01/STY02/AFD01/", "/MIN01/STY02/AFD01/"));
        // Parent org path does not match
        Assert.False(scope.CoversOrg("/MIN01/STY02/", "/MIN01/STY02/AFD01/"));
        // Child org path does not match
        Assert.False(scope.CoversOrg("/MIN01/STY02/AFD01/TEAM01/", "/MIN01/STY02/AFD01/"));
    }

    [Fact]
    public void RoleScope_NullPaths_ReturnsFalseForNonGlobalScopes()
    {
        var orgAndDescScope = new RoleScope("LocalLeader", "AFD01", "ORG_AND_DESCENDANTS");
        var orgOnlyScope = new RoleScope("Employee", "AFD01", "ORG_ONLY");

        Assert.False(orgAndDescScope.CoversOrg(null, "/MIN01/STY02/AFD01/"));
        Assert.False(orgAndDescScope.CoversOrg("/MIN01/STY02/AFD01/", null));
        Assert.False(orgOnlyScope.CoversOrg(null, "/MIN01/STY02/AFD01/"));
        Assert.False(orgOnlyScope.CoversOrg("/MIN01/STY02/AFD01/", null));
    }

    // ---------------------------------------------------------------
    // 2. StatsTidRoles Hierarchy Tests (4 tests)
    // ---------------------------------------------------------------

    [Fact]
    public void StatsTidRoles_HierarchyLevels_AreCorrect()
    {
        Assert.Equal(1, StatsTidRoles.GetHierarchyLevel("GlobalAdmin"));
        Assert.Equal(2, StatsTidRoles.GetHierarchyLevel("LocalAdmin"));
        Assert.Equal(3, StatsTidRoles.GetHierarchyLevel("LocalHR"));
        Assert.Equal(4, StatsTidRoles.GetHierarchyLevel("LocalLeader"));
        Assert.Equal(5, StatsTidRoles.GetHierarchyLevel("Employee"));
    }

    [Fact]
    public void StatsTidRoles_IsAtLeast_HigherPrivilege_ReturnsTrue()
    {
        Assert.True(StatsTidRoles.IsAtLeast("GlobalAdmin", "Employee"));
        Assert.True(StatsTidRoles.IsAtLeast("GlobalAdmin", "LocalAdmin"));
        Assert.True(StatsTidRoles.IsAtLeast("LocalHR", "LocalLeader"));
        Assert.True(StatsTidRoles.IsAtLeast("LocalAdmin", "LocalHR"));
        // Same role should also return true
        Assert.True(StatsTidRoles.IsAtLeast("Employee", "Employee"));
    }

    [Fact]
    public void StatsTidRoles_IsAtLeast_LowerPrivilege_ReturnsFalse()
    {
        Assert.False(StatsTidRoles.IsAtLeast("Employee", "LocalLeader"));
        Assert.False(StatsTidRoles.IsAtLeast("LocalLeader", "LocalAdmin"));
        Assert.False(StatsTidRoles.IsAtLeast("LocalHR", "LocalAdmin"));
        Assert.False(StatsTidRoles.IsAtLeast("LocalAdmin", "GlobalAdmin"));
    }

    [Fact]
    public void StatsTidRoles_LegacyRoles_MapToCorrectHierarchyLevels()
    {
        // Legacy "Admin" maps to GlobalAdmin level (1)
        Assert.Equal(1, StatsTidRoles.GetHierarchyLevel("Admin"));
        // Legacy "Manager" maps to LocalLeader level (4)
        Assert.Equal(4, StatsTidRoles.GetHierarchyLevel("Manager"));
        // Legacy "ReadOnly" is below Employee (6)
        Assert.Equal(6, StatsTidRoles.GetHierarchyLevel("ReadOnly"));
        // Unknown role returns int.MaxValue
        Assert.Equal(int.MaxValue, StatsTidRoles.GetHierarchyLevel("NonExistentRole"));
    }

    // ---------------------------------------------------------------
    // 3. JWT Token with Scopes Tests (3 tests)
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateToken_WithOrgId_IncludesOrgIdClaim()
    {
        var sut = new JwtTokenService(TestSettings);

        var token = sut.GenerateToken("EMP001", "Test User", "GlobalAdmin", "AC", "MIN01");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var orgIdClaim = jwt.Claims.First(c => c.Type == StatsTidClaims.OrgId);

        Assert.Equal("MIN01", orgIdClaim.Value);
    }

    [Fact]
    public void GenerateToken_WithScopes_IncludesScopesClaim()
    {
        var sut = new JwtTokenService(TestSettings);
        var scopes = new List<RoleScope>
        {
            new("GlobalAdmin", null, "GLOBAL"),
            new("LocalHR", "STY02", "ORG_AND_DESCENDANTS")
        };

        var token = sut.GenerateToken("admin01", "Admin User", "GlobalAdmin", "AC", "MIN01", scopes);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var scopesClaim = jwt.Claims.First(c => c.Type == StatsTidClaims.Scopes);

        Assert.Contains("GlobalAdmin", scopesClaim.Value);
        Assert.Contains("GLOBAL", scopesClaim.Value);
        Assert.Contains("LocalHR", scopesClaim.Value);
        Assert.Contains("ORG_AND_DESCENDANTS", scopesClaim.Value);
    }

    [Fact]
    public void GenerateToken_WithoutOrgIdOrScopes_BackwardCompatible()
    {
        var sut = new JwtTokenService(TestSettings);

        var token = sut.GenerateToken("EMP001", "Test User", "Employee", "AC");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // org_id claim should not be present
        Assert.Null(jwt.Claims.FirstOrDefault(c => c.Type == StatsTidClaims.OrgId));
        // scopes claim should not be present
        Assert.Null(jwt.Claims.FirstOrDefault(c => c.Type == StatsTidClaims.Scopes));
        // Standard claims should still be present
        Assert.Equal("EMP001", jwt.Subject);
        Assert.Equal("Employee", jwt.Claims.First(c => c.Type == StatsTidClaims.Role).Value);
        Assert.Equal("AC", jwt.Claims.First(c => c.Type == StatsTidClaims.AgreementCode).Value);
    }

    // ---------------------------------------------------------------
    // 4. Domain Model Tests (3 tests)
    // ---------------------------------------------------------------

    [Fact]
    public void Organization_Model_ConstructsCorrectly()
    {
        var org = new Organization
        {
            OrgId = "MIN01",
            OrgName = "Finansministeriet",
            OrgType = "MINISTRY",
            MaterializedPath = "/MIN01/",
            AgreementCode = "AC",
            OkVersion = "OK24"
        };

        Assert.Equal("MIN01", org.OrgId);
        Assert.Equal("Finansministeriet", org.OrgName);
        Assert.Equal("MINISTRY", org.OrgType);
        Assert.Null(org.ParentOrgId);
        Assert.Equal("/MIN01/", org.MaterializedPath);
        Assert.Equal("AC", org.AgreementCode);
        Assert.Equal("OK24", org.OkVersion);
        Assert.True(org.IsActive);
    }

    [Fact]
    public void ApprovalPeriod_Model_ConstructsWithStatus()
    {
        var periodId = Guid.NewGuid();
        var period = new ApprovalPeriod
        {
            PeriodId = periodId,
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            PeriodType = "WEEKLY",
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal(periodId, period.PeriodId);
        Assert.Equal("EMP001", period.EmployeeId);
        Assert.Equal("AFD01", period.OrgId);
        Assert.Equal(new DateOnly(2024, 6, 3), period.PeriodStart);
        Assert.Equal(new DateOnly(2024, 6, 9), period.PeriodEnd);
        Assert.Equal("WEEKLY", period.PeriodType);
        Assert.Equal("DRAFT", period.Status);
        Assert.Null(period.ApprovedBy);
        Assert.Null(period.ApprovedAt);
        Assert.Null(period.RejectionReason);
    }

    [Fact]
    public void LocalConfiguration_Model_ConstructsCorrectly()
    {
        var configId = Guid.NewGuid();
        var config = new LocalConfiguration
        {
            ConfigId = configId,
            OrgId = "STY02",
            ConfigArea = "FLEX_RULES",
            ConfigKey = "maxFlexBalance",
            ConfigValue = "{\"value\": 80}",
            EffectiveFrom = new DateOnly(2024, 4, 1),
            AgreementCode = "HK",
            OkVersion = "OK24",
            CreatedBy = "ladm01"
        };

        Assert.Equal(configId, config.ConfigId);
        Assert.Equal("STY02", config.OrgId);
        Assert.Equal("FLEX_RULES", config.ConfigArea);
        Assert.Equal("maxFlexBalance", config.ConfigKey);
        Assert.Equal("{\"value\": 80}", config.ConfigValue);
        Assert.Equal(new DateOnly(2024, 4, 1), config.EffectiveFrom);
        Assert.Null(config.EffectiveTo);
        Assert.Equal(1, config.Version);
        Assert.True(config.IsActive);
        Assert.Null(config.ApprovedBy);
        Assert.Equal("ladm01", config.CreatedBy);
    }

    // ---------------------------------------------------------------
    // 5. Event Type Registration Tests (2 tests)
    // ---------------------------------------------------------------

    [Fact]
    public void EventSerializer_Sprint8Events_RoundtripCorrectly()
    {
        // OrganizationCreated
        var orgCreated = new OrganizationCreated
        {
            OrgId = "TEST01",
            OrgName = "Test Ministry",
            OrgType = "MINISTRY",
            MaterializedPath = "/TEST01/",
            AgreementCode = "AC",
            OkVersion = "OK24"
        };
        var orgJson = EventSerializer.Serialize(orgCreated);
        var orgDeserialized = EventSerializer.Deserialize("OrganizationCreated", orgJson);
        Assert.IsType<OrganizationCreated>(orgDeserialized);
        Assert.Equal("TEST01", ((OrganizationCreated)orgDeserialized).OrgId);

        // UserCreated
        var userCreated = new UserCreated
        {
            UserId = "USR01",
            Username = "testuser",
            DisplayName = "Test User",
            PrimaryOrgId = "MIN01",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };
        var userJson = EventSerializer.Serialize(userCreated);
        var userDeserialized = EventSerializer.Deserialize("UserCreated", userJson);
        Assert.IsType<UserCreated>(userDeserialized);
        Assert.Equal("USR01", ((UserCreated)userDeserialized).UserId);

        // RoleAssignmentGranted
        var assignmentId = Guid.NewGuid();
        var roleGranted = new RoleAssignmentGranted
        {
            AssignmentId = assignmentId,
            UserId = "USR01",
            RoleId = "GlobalAdmin",
            OrgId = null,
            ScopeType = "GLOBAL"
        };
        var roleJson = EventSerializer.Serialize(roleGranted);
        var roleDeserialized = EventSerializer.Deserialize("RoleAssignmentGranted", roleJson);
        Assert.IsType<RoleAssignmentGranted>(roleDeserialized);
        Assert.Equal(assignmentId, ((RoleAssignmentGranted)roleDeserialized).AssignmentId);

        // RoleAssignmentRevoked
        var revokeId = Guid.NewGuid();
        var roleRevoked = new RoleAssignmentRevoked
        {
            AssignmentId = revokeId,
            UserId = "USR01",
            RoleId = "LocalHR",
            Reason = "Role expired"
        };
        var revokeJson = EventSerializer.Serialize(roleRevoked);
        var revokeDeserialized = EventSerializer.Deserialize("RoleAssignmentRevoked", revokeJson);
        Assert.IsType<RoleAssignmentRevoked>(revokeDeserialized);
        Assert.Equal("Role expired", ((RoleAssignmentRevoked)revokeDeserialized).Reason);

        // LocalConfigurationChanged
        var configId = Guid.NewGuid();
        var configChanged = new LocalConfigurationChanged
        {
            ConfigId = configId,
            OrgId = "STY02",
            ConfigArea = "FLEX_RULES",
            ConfigKey = "maxFlexBalance",
            ConfigValue = "{\"value\": 80}",
            PreviousValue = "{\"value\": 60}",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };
        var configJson = EventSerializer.Serialize(configChanged);
        var configDeserialized = EventSerializer.Deserialize("LocalConfigurationChanged", configJson);
        Assert.IsType<LocalConfigurationChanged>(configDeserialized);
        Assert.Equal(configId, ((LocalConfigurationChanged)configDeserialized).ConfigId);

        // PeriodSubmitted
        var periodId = Guid.NewGuid();
        var submitted = new PeriodSubmitted
        {
            PeriodId = periodId,
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            PeriodType = "WEEKLY"
        };
        var subJson = EventSerializer.Serialize(submitted);
        var subDeserialized = EventSerializer.Deserialize("PeriodSubmitted", subJson);
        Assert.IsType<PeriodSubmitted>(subDeserialized);
        Assert.Equal(periodId, ((PeriodSubmitted)subDeserialized).PeriodId);

        // PeriodApproved
        var approvedId = Guid.NewGuid();
        var approved = new PeriodApproved
        {
            PeriodId = approvedId,
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            ApprovedBy = "leader01"
        };
        var appJson = EventSerializer.Serialize(approved);
        var appDeserialized = EventSerializer.Deserialize("PeriodApproved", appJson);
        Assert.IsType<PeriodApproved>(appDeserialized);
        Assert.Equal("leader01", ((PeriodApproved)appDeserialized).ApprovedBy);

        // PeriodRejected
        var rejectedId = Guid.NewGuid();
        var rejected = new PeriodRejected
        {
            PeriodId = rejectedId,
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            RejectedBy = "leader01",
            RejectionReason = "Incomplete entries"
        };
        var rejJson = EventSerializer.Serialize(rejected);
        var rejDeserialized = EventSerializer.Deserialize("PeriodRejected", rejJson);
        Assert.IsType<PeriodRejected>(rejDeserialized);
        Assert.Equal("Incomplete entries", ((PeriodRejected)rejDeserialized).RejectionReason);
    }

    [Fact]
    public void EventSerializer_UnknownEventType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EventSerializer.Deserialize("NonExistentEvent", "{}"));
    }

    // ---------------------------------------------------------------
    // 6. Additional model coverage tests
    // ---------------------------------------------------------------

    [Fact]
    public void User_Model_ConstructsWithDefaults()
    {
        var user = new User
        {
            UserId = "USR01",
            Username = "jdoe",
            PasswordHash = "hashed_pw",
            DisplayName = "Jane Doe",
            PrimaryOrgId = "MIN01",
            AgreementCode = "AC",
            OkVersion = "OK24"
        };

        Assert.Equal("USR01", user.UserId);
        Assert.Equal("jdoe", user.Username);
        Assert.Equal("Jane Doe", user.DisplayName);
        Assert.Null(user.Email);
        Assert.Equal("Standard", user.EmploymentCategory);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void RoleAssignment_Model_ConstructsCorrectly()
    {
        var assignmentId = Guid.NewGuid();
        var assignment = new RoleAssignment
        {
            AssignmentId = assignmentId,
            UserId = "USR01",
            RoleId = "LocalHR",
            OrgId = "STY02",
            ScopeType = "ORG_AND_DESCENDANTS",
            AssignedBy = "admin01"
        };

        Assert.Equal(assignmentId, assignment.AssignmentId);
        Assert.Equal("USR01", assignment.UserId);
        Assert.Equal("LocalHR", assignment.RoleId);
        Assert.Equal("STY02", assignment.OrgId);
        Assert.Equal("ORG_AND_DESCENDANTS", assignment.ScopeType);
        Assert.Equal("admin01", assignment.AssignedBy);
        Assert.Null(assignment.ExpiresAt);
        Assert.True(assignment.IsActive);
    }

    [Fact]
    public void RoleAssignment_GlobalScope_HasNullOrgId()
    {
        var assignment = new RoleAssignment
        {
            AssignmentId = Guid.NewGuid(),
            UserId = "USR01",
            RoleId = "GlobalAdmin",
            OrgId = null,
            ScopeType = "GLOBAL",
            AssignedBy = "system"
        };

        Assert.Null(assignment.OrgId);
        Assert.Equal("GLOBAL", assignment.ScopeType);
    }

    [Fact]
    public void Organization_WithParent_SetsParentOrgId()
    {
        var org = new Organization
        {
            OrgId = "STY01",
            OrgName = "Styrelse 1",
            OrgType = "STYRELSE",
            ParentOrgId = "MIN01",
            MaterializedPath = "/MIN01/STY01/",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal("MIN01", org.ParentOrgId);
        Assert.Equal("/MIN01/STY01/", org.MaterializedPath);
        Assert.Equal("STYRELSE", org.OrgType);
    }
}
