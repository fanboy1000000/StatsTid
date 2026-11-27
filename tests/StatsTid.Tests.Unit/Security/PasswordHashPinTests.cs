namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// The login pin L-11 (S149 TASK-14901), kind R (a regression pin): BCrypt behaves as the seeded demo
/// logins and the password endpoints rely on. The seed password is "password"
/// (<c>docker/postgres/init.sql:1131</c>, shared by every demo user); the seeded hash is the one
/// at <c>init.sql:1162</c>. Frozen after CI-W0; names no runtime version (ADR-045).
/// </summary>
public sealed class PasswordHashPinTests
{
    private const string SeededHash = "$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te";

    /// <summary>The seeded hash verifies the seed password and rejects a wrong one.</summary>
    [Fact]
    public void L11_SeededHash_VerifiesSeedPassword_AndRejectsWrongOne()
    {
        Assert.True(BCrypt.Net.BCrypt.Verify("password", SeededHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("not-the-password", SeededHash));
    }

    /// <summary>A freshly made hash round-trips, is a bcrypt hash and is not the password itself.</summary>
    [Fact]
    public void L11_HashPassword_ThenVerify_RoundTrips()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("a-new-Password-1");
        Assert.NotEqual("a-new-Password-1", hash);
        Assert.StartsWith("$2", hash);
        Assert.True(BCrypt.Net.BCrypt.Verify("a-new-Password-1", hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("a-new-Password-2", hash));
    }
}
