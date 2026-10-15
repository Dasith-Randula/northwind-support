namespace SupportDesk.Presentation.Security;

public sealed record JwtSettings(
    string Issuer,
    string Audience,
    string SigningKey,
    int TokenLifetimeMinutes);
