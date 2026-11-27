using System.Text.RegularExpressions;

namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// The login pin L-11 (S149 TASK-14901), kind R (a regression pin): BCrypt behaves as the seeded demo
/// logins and the password endpoints rely on. The seed password is "password": see the users seed in
/// <c>docker/postgres/init.sql</c>, whose comment states the shared dev password; the hash below is the one
/// that seed stores for every demo user. L-11 calls the library directly, so no production line can be
/// mutated; its <c>Assert.False</c> halves are what let it fail. Frozen after CI-W0; names no runtime
/// version (ADR-045).
/// </summary>
public sealed class PasswordHashPinTests
{
    private const string SeededHash = "$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te";

    /// <summary>Kind R. The seeded hash verifies the seed password and rejects a wrong one.</summary>
    [Fact]
    public void L11_SeededHash_VerifiesSeedPassword_AndRejectsWrongOne()
    {
        Assert.True(BCrypt.Net.BCrypt.Verify("password", SeededHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("not-the-password", SeededHash));
    }

    /// <summary>Kind R. A freshly made hash round-trips, is not the password itself, and carries cost
    /// factor 11 (the revision letter is not pinned).</summary>
    [Fact]
    public void L11_HashPassword_ThenVerify_RoundTrips()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("a-new-Password-1");
        Assert.NotEqual("a-new-Password-1", hash);
        Assert.Matches(new Regex(@"^\$2[aby]\$11\$"), hash);
        Assert.True(BCrypt.Net.BCrypt.Verify("a-new-Password-1", hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("a-new-Password-2", hash));
    }
}
