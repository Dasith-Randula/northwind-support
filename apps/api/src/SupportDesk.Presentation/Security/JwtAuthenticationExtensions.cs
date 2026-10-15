using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;

namespace SupportDesk.Presentation.Security;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = ReadSettings(configuration);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey));

        services.AddSingleton(settings);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = ClaimTypes.Name
                };
            });
        services.AddAuthorization();

        return services;
    }

    private static JwtSettings ReadSettings(IConfiguration configuration)
    {
        var issuer = Required(configuration, "Jwt:Issuer");
        var audience = Required(configuration, "Jwt:Audience");
        var signingKey = Required(configuration, "Jwt:SigningKey");
        if (Encoding.UTF8.GetByteCount(signingKey) < 32 ||
            signingKey.StartsWith("replace-with-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "JWT signing key 'Jwt:SigningKey' must be replaced with a secret of at least 32 bytes.");
        }

        var lifetimeValue = Required(configuration, "Jwt:TokenLifetimeMinutes");
        if (!int.TryParse(lifetimeValue, out var lifetimeMinutes) || lifetimeMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException(
                "JWT token lifetime 'Jwt:TokenLifetimeMinutes' must be between 1 and 1440 minutes.");
        }

        return new JwtSettings(issuer, audience, signingKey, lifetimeMinutes);
    }

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"Required JWT configuration '{key}' is missing.");
}
