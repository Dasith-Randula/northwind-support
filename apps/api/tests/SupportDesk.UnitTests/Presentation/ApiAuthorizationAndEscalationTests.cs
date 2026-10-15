using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
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
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.BusinessRules;
using SupportDesk.Domain.Common;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Services;
using SupportDesk.Presentation.Controllers;
using SupportDesk.Presentation.Filters;
using SupportDesk.Presentation.Middleware;
using SupportDesk.Presentation.Security;
using SupportDesk.UnitTests.Application.Features.Tickets.Commands;

namespace SupportDesk.UnitTests.Presentation;

public sealed class ApiAuthorizationAndEscalationTests
{
    private const string Password = "phase-three-test-password";
    private const string Issuer = "NorthwindSupportDesk.Tests";
    private const string Audience = "NorthwindSupportDesk.Tests.Client";
    private const string SigningKey = "test-only-signing-key-32-bytes-minimum";

    [Fact]
    public async Task Ticket_list_requires_a_valid_token_and_works_with_one()
    {
        var agent = CreateAgent(1, active: true);
        await using var host = await ApiHost.StartAsync(agent);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/tickets")).StatusCode);

        var login = await host.LoginAsync(agent);
        var response = await host.GetWithTokenAsync("/api/tickets", login.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<TicketListItemDto>>();
        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("GET", "/api/tickets/41")]
    [InlineData("POST", "/api/tickets")]
    [InlineData("PATCH", "/api/tickets/41/status")]
    [InlineData("PATCH", "/api/tickets/41/assignment")]
    [InlineData("POST", "/api/tickets/41/escalate")]
    [InlineData("GET", "/api/tickets/41/escalations")]
    [InlineData("GET", "/api/customers")]
    [InlineData("GET", "/api/customers/1")]
    [InlineData("GET", "/api/agents")]
    [InlineData("GET", "/api/categories")]
    public async Task Existing_api_routes_reject_anonymous_requests(string method, string path)
    {
        var agent = CreateAgent(1, active: true);
        await using var host = await ApiHost.StartAsync(agent);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PATCH")
        {
            request.Content = JsonContent.Create(new { reason = "Valid test escalation reason" });
        }

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_and_expired_tokens_are_rejected_by_the_fallback_policy()
    {
        var agent = CreateAgent(1, active: true);
        await using var host = await ApiHost.StartAsync(agent);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await host.GetWithTokenAsync("/api/tickets", "not-a-jwt")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await host.GetWithTokenAsync("/api/tickets", host.CreateToken(agent.Id, expired: true))).StatusCode);
    }

    [Fact]
    public async Task Login_is_public_me_is_protected_and_swagger_documentation_is_public()
    {
        var agent = CreateAgent(1, active: true);
        await using var host = await ApiHost.StartAsync(agent, enableSwagger: true);

        var loginResponse = await host.Client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(agent.Email, Password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/swagger/index.html")).StatusCode);
        var documentResponse = await host.Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        using var document = JsonDocument.Parse(await documentResponse.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/tickets").GetProperty("get").GetProperty("security").GetArrayLength() > 0);
        Assert.False(paths.GetProperty("/api/auth/login").GetProperty("post").TryGetProperty("security", out _));
        Assert.True(paths.GetProperty("/api/auth/me").GetProperty("get").GetProperty("security").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Escalation_uses_authenticated_agent_and_ignores_a_spoofed_actor_field()
    {
        var agent = CreateAgent(1, active: true, fullName: "Alex Turner");
        var commandContext = new TicketCommandTestContext();
        commandContext.ExistingTicket(CreateTicket());
        commandContext.Agents.Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([agent]);
        commandContext.Tickets.Setup(repository => repository.ExistsAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        commandContext.Tickets.Setup(repository => repository.GetEscalationsAsync(41, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => commandContext.AddedEscalation is { } saved
                ? [saved]
                : Array.Empty<TicketEscalation>());
        commandContext.Agents.Setup(repository => repository.GetByEmailAsync(agent.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        commandContext.Agents.Setup(repository => repository.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        await using var host = await ApiHost.StartEscalationAsync(agent, commandContext);
        var token = host.CreateToken(agent.Id);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tickets/41/escalate")
        {
            Content = new StringContent(
                "{\"reason\":\"Customer impact increased\",\"escalatedBy\":\"Mallory Spoof\"}",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await host.Client.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Alex Turner", body.RootElement.GetProperty("escalation").GetProperty("escalatedBy").GetString());
        Assert.Equal("Alex Turner", commandContext.AddedEscalation!.EscalatedBy);

        var historyResponse = await host.GetWithTokenAsync("/api/tickets/41/escalations", token);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<IReadOnlyList<TicketEscalationDto>>();
        Assert.NotNull(history);
        Assert.Single(history);
        Assert.Equal("Alex Turner", history[0].EscalatedBy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-agent-id")]
    public async Task Escalation_rejects_a_valid_token_without_a_usable_agent_identity(string? subject)
    {
        var agent = CreateAgent(1, active: true);
        var commandContext = new TicketCommandTestContext();
        await using var host = await ApiHost.StartEscalationAsync(agent, commandContext);
        var response = await host.PostWithTokenAsync(
            "/api/tickets/41/escalate",
            "{\"reason\":\"Customer impact increased\"}",
            host.CreateToken(subject));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Escalation_rejects_a_token_for_an_agent_that_does_not_exist()
    {
        var agent = CreateAgent(1, active: true);
        var commandContext = new TicketCommandTestContext();
        commandContext.ExistingTicket(CreateTicket());
        await using var host = await ApiHost.StartEscalationAsync(agent, commandContext);
        var response = await host.PostWithTokenAsync(
            "/api/tickets/41/escalate",
            "{\"reason\":\"Customer impact increased\"}",
            host.CreateToken(agentId: 999));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Escalation_rejects_a_token_for_an_inactive_agent()
    {
        var agent = CreateAgent(1, active: true);
        agent.Deactivate();
        var commandContext = new TicketCommandTestContext();
        commandContext.ExistingTicket(CreateTicket());
        commandContext.Agents.Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([agent]);
        await using var host = await ApiHost.StartEscalationAsync(agent, commandContext);
        var response = await host.PostWithTokenAsync(
            "/api/tickets/41/escalate",
            "{\"reason\":\"Customer impact increased\"}",
            host.CreateToken(agent.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Ticket CreateTicket()
    {
        var ticket = Ticket.Raise(
            "TCK-0041", "Cannot log in", "The customer cannot access the portal.",
            1, 3, TicketPriority.Low, DateTime.UtcNow, DateTime.UtcNow.AddHours(72));
        return SetId(ticket, 41);
    }

    private static Agent CreateAgent(int id, bool active, string fullName = "Test Agent")
    {
        var agent = new Agent(fullName, $"agent{id}@example.test", 5, DateTime.UtcNow);
        SetId(agent, id);
        if (!active)
        {
            agent.Deactivate();
        }

        agent.SetPasswordHash(new IdentityAgentPasswordHasher().HashPassword(agent, Password));
        return agent;
    }

    private static T SetId<T>(T entity, int id)
    {
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(entity, id);
        return entity;
    }

    private sealed class ApiHost(WebApplication app, HttpClient client, Agent agent) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async Task<LoginResponse> LoginAsync(Agent? loginAgent = null)
        {
            var response = await Client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest((loginAgent ?? agent).Email, Password));
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        }

        public Task<HttpResponseMessage> GetWithTokenAsync(string path, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Client.SendAsync(request);
        }

        public Task<HttpResponseMessage> PostWithTokenAsync(string path, string json, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Client.SendAsync(request);
        }

        public string CreateToken(int agentId, bool expired = false) => CreateToken(agentId.ToString(), expired);

        public string CreateToken(string? subject, bool expired = false)
        {
            var now = DateTime.UtcNow;
            var claims = subject is null
                ? Array.Empty<Claim>()
                : [new Claim(JwtRegisteredClaimNames.Sub, subject)];
            var token = new JwtSecurityToken(
                Issuer,
                Audience,
                claims,
                expired ? now.AddMinutes(-10) : now.AddMinutes(-1),
                expired ? now.AddMinutes(-5) : now.AddMinutes(30),
                new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static Task<ApiHost> StartAsync(Agent agent, bool enableSwagger = false) =>
            StartCoreAsync(agent, enableSwagger, escalationContext: null);

        public static Task<ApiHost> StartEscalationAsync(Agent agent, TicketCommandTestContext context) =>
            StartCoreAsync(agent, enableSwagger: false, context);

        private static async Task<ApiHost> StartCoreAsync(
            Agent agent,
            bool enableSwagger,
            TicketCommandTestContext? escalationContext)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:TokenLifetimeMinutes"] = "60"
            });
            builder.Services
                .AddControllers()
                .AddApplicationPart(typeof(AuthController).Assembly)
                .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNameCaseInsensitive = true);
            builder.Services.AddApplication();
            builder.Services.AddJwtAuthentication(builder.Configuration);
            builder.Services.AddSingleton<IAgentPasswordHasher, IdentityAgentPasswordHasher>();
            builder.Services.AddSingleton<IClock, SystemClock>();
            builder.Services.AddSingleton<SlaPolicySettings>();
            builder.Services.AddSingleton<SlaCalculator>();

            if (escalationContext is null)
            {
                builder.Services.AddSingleton<IAgentRepository>(new AgentRepository(agent));
                var ticketQueries = new Mock<ITicketQueries>();
                ticketQueries.Setup(queries => queries.GetPagedAsync(
                        It.IsAny<TicketQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PagedResult<TicketListItemDto>([], 1, 20, 0));
                builder.Services.AddSingleton(ticketQueries.Object);
            }
            else
            {
                builder.Services.AddSingleton<IAgentRepository>(escalationContext.Agents.Object);
                builder.Services.AddSingleton<ITicketRepository>(escalationContext.Tickets.Object);
                builder.Services.AddSingleton<ICustomerRepository>(escalationContext.Customers.Object);
                builder.Services.AddSingleton<ICategoryRepository>(escalationContext.Categories.Object);
                builder.Services.AddSingleton<IUnitOfWork>(escalationContext.UnitOfWork.Object);
                builder.Services.AddSingleton<ITicketQueries>(escalationContext.TicketQueries.Object);
                builder.Services.AddSingleton<IClock>(escalationContext.Clock);
            }

            if (enableSwagger)
            {
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen(options =>
                {
                    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
                    {
                        Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT"
                    });
                    options.OperationFilter<BearerAuthorizationOperationFilter>();
                });
            }

            var app = builder.Build();
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            if (enableSwagger)
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();
            return new ApiHost(app, app.GetTestClient(), agent);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class AgentRepository(Agent agent) : IAgentRepository
    {
        public Task<Agent?> GetByIdAsync(int id, CancellationToken ct) =>
            Task.FromResult<Agent?>(id == agent.Id ? agent : null);

        public Task<Agent?> GetByEmailAsync(string email, CancellationToken ct) =>
            Task.FromResult<Agent?>(email == agent.Email ? agent : null);

        public Task<IReadOnlyList<Agent>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Agent>>([agent]);
    }
}
