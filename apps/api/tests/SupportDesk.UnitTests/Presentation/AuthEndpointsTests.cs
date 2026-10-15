using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Common;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Services;
using SupportDesk.Presentation.Controllers;
using SupportDesk.Presentation.Security;

namespace SupportDesk.UnitTests.Presentation;

public sealed class AuthEndpointsTests
{
    private const string TestPassword = "phase-two-test-password";
    private const string JwtIssuer = "NorthwindSupportDesk.Tests";
    private const string JwtAudience = "NorthwindSupportDesk.Tests.Client";
    private const int TokenLifetimeMinutes = 60;

    [Fact]
    public async Task Login_returns_signed_token_and_me_returns_the_authenticated_agent()
    {
        var agent = CreateAgent(1, "alex@example.test", active: true, TestPassword);
        await using var host = await TestApiHost.StartAsync(agent);

        var response = await host.Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(agent.Email, TestPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        Assert.Equal(agent.Id, login.Agent.Id);
        Assert.Equal(agent.FullName, login.Agent.FullName);
        Assert.Equal(agent.Email, login.Agent.Email);

        var parsedToken = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        Assert.Equal(agent.Id.ToString(), parsedToken.Claims.Single(claim => claim.Type == "sub").Value);
        Assert.Equal(JwtIssuer, parsedToken.Issuer);
        Assert.Contains(JwtAudience, parsedToken.Audiences);
        Assert.Equal(
            new DateTimeOffset(parsedToken.ValidTo).ToUnixTimeSeconds(),
            new DateTimeOffset(login.ExpiresAtUtc).ToUnixTimeSeconds());

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var meResponse = await host.Client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var authenticatedAgent = await meResponse.Content.ReadFromJsonAsync<AuthenticatedAgentDto>();
        Assert.Equal(new AuthenticatedAgentDto(agent.Id, agent.FullName, agent.Email), authenticatedAgent);
    }

    [Fact]
    public async Task Login_returns_identical_generic_unauthorized_responses_for_unknown_email_and_wrong_password()
    {
        var agent = CreateAgent(1, "alex@example.test", active: true, TestPassword);
        await using var host = await TestApiHost.StartAsync(agent);

        var wrongPassword = await host.Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(agent.Email, "incorrect-password"));
        var unknownEmail = await host.Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("unknown@example.test", TestPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownEmail.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await wrongPassword.Content.ReadAsStringAsync());
        Assert.Equal("Invalid email or password.", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_rejects_inactive_agent_even_when_password_is_correct()
    {
        var yuki = CreateAgent(
            6,
            "yuki.tanaka@northwind-support.example",
            active: false,
            TestPassword,
            fullName: "Yuki Tanaka");
        await using var host = await TestApiHost.StartAsync(yuki);

        var response = await host.Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(yuki.Email, TestPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Invalid email or password.", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("malformed-hash")]
    public async Task Login_rejects_missing_or_unusable_password_hash(string? hash)
    {
        var agent = CreateAgent(1, "alex@example.test", active: true, password: null);
        if (hash is not null)
        {
            agent.SetPasswordHash(hash);
        }

        await using var host = await TestApiHost.StartAsync(agent);
        var response = await host.Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(agent.Email, TestPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_rejects_missing_invalid_and_expired_tokens()
    {
        var agent = CreateAgent(1, "alex@example.test", active: true, TestPassword);
        await using var host = await TestApiHost.StartAsync(agent);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/auth/me")).StatusCode);

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/auth/me")).StatusCode);

        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            host.CreateExpiredToken(agent.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Me_returns_unauthorized_when_token_agent_no_longer_exists()
    {
        var agent = CreateAgent(1, "alex@example.test", active: true, TestPassword);
        await using var host = await TestApiHost.StartAsync(agent);
        var loginResponse = await host.Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(agent.Email, TestPassword));
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);

        host.AgentRepository.Remove(agent.Id);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/auth/me")).StatusCode);
    }

    private static Agent CreateAgent(int id, string email, bool active, string? password, string fullName = "Test Agent")
    {
        var agent = new Agent(fullName, email, 5, DateTime.UtcNow);
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(agent, id);
        if (!active)
        {
            agent.Deactivate();
        }

        if (password is not null)
        {
            agent.SetPasswordHash(new IdentityAgentPasswordHasher().HashPassword(agent, password));
        }

        return agent;
    }

    private sealed class TestApiHost(WebApplication app, AgentRepository agentRepository, HttpClient client)
        : IAsyncDisposable
    {
        public AgentRepository AgentRepository { get; } = agentRepository;

        public HttpClient Client { get; } = client;

        public string CreateExpiredToken(int agentId)
        {
            var settings = app.Services.GetRequiredService<JwtSettings>();
            var signingKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(settings.SigningKey));
            var token = new JwtSecurityToken(
                settings.Issuer,
                settings.Audience,
                [new Claim(JwtRegisteredClaimNames.Sub, agentId.ToString())],
                DateTime.UtcNow.AddMinutes(-10),
                DateTime.UtcNow.AddMinutes(-5),
                new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static async Task<TestApiHost> StartAsync(Agent agent)
        {
            var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:SigningKey"] = key,
                ["Jwt:TokenLifetimeMinutes"] = TokenLifetimeMinutes.ToString()
            };
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Services
                .AddControllers()
                .AddApplicationPart(typeof(AuthController).Assembly);
            builder.Services.AddApplication();
            builder.Services.AddJwtAuthentication(builder.Configuration);
            builder.Services.AddSingleton<IAgentRepository>(new AgentRepository(agent));
            builder.Services.AddSingleton<IAgentPasswordHasher, IdentityAgentPasswordHasher>();
            builder.Services.AddSingleton<IClock, SystemClock>();

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();

            return new TestApiHost(app, (AgentRepository)app.Services.GetRequiredService<IAgentRepository>(), app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class AgentRepository(Agent agent) : IAgentRepository
    {
        private readonly Dictionary<int, Agent> agents = new() { [agent.Id] = agent };

        public Task<Agent?> GetByIdAsync(int id, CancellationToken ct) =>
            Task.FromResult(agents.GetValueOrDefault(id));

        public Task<Agent?> GetByEmailAsync(string email, CancellationToken ct) =>
            Task.FromResult(agents.Values.SingleOrDefault(candidate => candidate.Email == email));

        public Task<IReadOnlyList<Agent>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Agent>>(agents.Values.ToArray());

        public void Remove(int id) => agents.Remove(id);
    }
}
