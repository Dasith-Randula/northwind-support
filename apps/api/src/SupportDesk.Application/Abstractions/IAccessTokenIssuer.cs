using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Application.Abstractions;

/// <summary>Creates signed access tokens without coupling application handlers to a JWT library.</summary>
public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(Agent agent);
}

public sealed record IssuedAccessToken(string AccessToken, DateTime ExpiresAtUtc);
