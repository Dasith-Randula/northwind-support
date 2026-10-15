using Microsoft.AspNetCore.Identity;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Presentation.Security;

/// <summary>Adapts ASP.NET Core Identity's salted password hasher for agent credentials.</summary>
public sealed class IdentityAgentPasswordHasher : IAgentPasswordHasher
{
    private readonly PasswordHasher<Agent> hasher = new();

    public string HashPassword(Agent agent, string password) => hasher.HashPassword(agent, password);

    public bool VerifyPassword(Agent agent, string? passwordHash, string password)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        try
        {
            return hasher.VerifyHashedPassword(agent, passwordHash, password) is
                PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            // Corrupt or unusable stored hashes must fail closed during future login checks.
            return false;
        }
    }
}
