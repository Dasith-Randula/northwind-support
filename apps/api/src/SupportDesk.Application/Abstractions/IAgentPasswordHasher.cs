using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Application.Abstractions;

/// <summary>Hashes and verifies agent passwords without coupling the domain to a hash provider.</summary>
public interface IAgentPasswordHasher
{
    string HashPassword(Agent agent, string password);

    bool VerifyPassword(Agent agent, string? passwordHash, string password);
}
