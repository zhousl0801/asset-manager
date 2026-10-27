using AssetKeeper.Api.Security;

namespace AssetKeeper.Tests.Security;

public sealed class AccessPasswordHasherTests
{
    [Fact]
    public void Verify_AcceptsMatchingPassword()
    {
        var hash = AccessPasswordHasher.Hash("correct horse battery staple");

        Assert.True(AccessPasswordHasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var hash = AccessPasswordHasher.Hash("correct horse battery staple");

        Assert.False(AccessPasswordHasher.Verify("correct horse battery stapl", hash));
        Assert.False(AccessPasswordHasher.Verify("", hash));
        Assert.False(AccessPasswordHasher.Verify(null, hash));
    }

    [Fact]
    public void Hash_UsesDistinctSaltsAndDocumentsIterations()
    {
        var first = AccessPasswordHasher.Hash("same password");
        var second = AccessPasswordHasher.Hash("same password");

        Assert.NotEqual(first, second);
        Assert.StartsWith("pbkdf2-sha256$210000$", first);
        Assert.Equal(4, first.Split('$').Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plain-text")]
    [InlineData("pbkdf2-sha256$210000$not-base64$not-base64")]
    [InlineData("pbkdf2-sha256$100$c2FsdHNhbHRzYWx0c2FsdA==$c2FsdHNhbHRzYWx0c2FsdHNhbHRzYWx0c2E=")]
    [InlineData("pbkdf2-sha256$2000000$c2FsdHNhbHRzYWx0c2FsdA==$c2FsdHNhbHRzYWx0c2FsdHNhbHRzYWx0c2E=")]
    public void Verify_RejectsMalformedHash(string? encodedHash)
    {
        Assert.False(AccessPasswordHasher.Verify("password", encodedHash));
    }
}
