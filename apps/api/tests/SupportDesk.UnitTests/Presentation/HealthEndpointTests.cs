using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Presentation.Extensions;
using SupportDesk.Presentation.Security;

namespace SupportDesk.UnitTests.Presentation;

public sealed class HealthEndpointTests
{
    private const string SigningKey = "health-endpoint-test-signing-key-at-least-32-bytes";

    [Fact]
    public async Task Health_returns_200_without_authentication_when_database_is_available()
    {
        await using var host = await HealthHost.StartAsync(useSqlite: true);

        var response = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"Healthy\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/protected-test")).StatusCode);
    }

    [Fact]
    public async Task Health_returns_503_without_sensitive_details_when_database_is_unavailable()
    {
        await using var host = await HealthHost.StartAsync(useSqlite: false);

        var response = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    private sealed class HealthHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<HealthHost> StartAsync(bool useSqlite)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "HealthEndpointTests",
                ["Jwt:Audience"] = "HealthEndpointTests",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:TokenLifetimeMinutes"] = "60"
            });
            builder.Services.AddJwtAuthentication(builder.Configuration);
            builder.Services.AddDbContext<SupportDbContext>(options =>
            {
                if (useSqlite)
                {
                    options.UseSqlite("Data Source=:memory:");
                }
                else
                {
                    options.UseSqlServer(
                        "Server=127.0.0.1,1;Database=SupportDeskUnavailable;User Id=test;Password=not-a-secret;TrustServerCertificate=True;Encrypt=False;Connect Timeout=1");
                }
            });

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapDatabaseHealthEndpoint();
            app.MapGet("/api/protected-test", () => Results.Ok());
            await app.StartAsync();

            return new HealthHost(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
