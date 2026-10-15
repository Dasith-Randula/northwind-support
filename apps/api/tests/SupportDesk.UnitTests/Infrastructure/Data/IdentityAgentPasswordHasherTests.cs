using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Presentation.Security;

namespace SupportDesk.UnitTests.Infrastructure.Data;

public sealed class IdentityAgentPasswordHasherTests
{
    [Fact]
    public void HashPassword_ProducesSaltedHashThatVerifiesOnlyTheCorrectPassword()
    {
        var agent = new Agent(
            "Alex Turner",
            "alex.turner@northwind-support.example",
            10,
            DateTime.UtcNow);
        var hasher = new IdentityAgentPasswordHasher();

        var password = Guid.NewGuid().ToString("N");
        var hash = hasher.HashPassword(agent, password);

        Assert.NotEqual(password, hash);
        Assert.True(hasher.VerifyPassword(agent, hash, password));
        Assert.False(hasher.VerifyPassword(agent, hash, "incorrect-password"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-password-hash")]
    [InlineData("AAAA")]
    public void VerifyPassword_RejectsMissingOrUnusableHashes(string? hash)
    {
        var agent = new Agent(
            "Alex Turner",
            "alex.turner@northwind-support.example",
            10,
            DateTime.UtcNow);

        Assert.False(new IdentityAgentPasswordHasher().VerifyPassword(agent, hash, "any-password"));
    }
}
