using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Presentation.Security;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Data;

public sealed class SupportDbSeederTests
{
    [Fact]
    public async Task SeedAsync_RequiresDevelopmentPasswordBeforeWritingSeedData()
    {
        await using var database = await TestDatabase.CreateAsync(configureDevelopmentPassword: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.CreateSeeder().SeedAsync());

        Assert.Contains(SupportDbSeeder.DevelopmentPasswordConfigurationKey, exception.Message);
        Assert.Empty(await database.Db.Set<Agent>().ToListAsync());
        Assert.Empty(await database.Db.Set<Category>().ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_FreshDatabaseCreatesSixAgentsWithUsablePasswordHashes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seeder = database.CreateSeeder();

        await seeder.SeedAsync();

        var agents = await database.Db.Set<Agent>().OrderBy(agent => agent.Email).ToListAsync();
        Assert.Equal(6, agents.Count);
        Assert.All(agents, agent =>
        {
            Assert.False(string.IsNullOrWhiteSpace(agent.PasswordHash));
            Assert.NotEqual(database.DevelopmentPassword, agent.PasswordHash);
            Assert.True(database.PasswordHasher.VerifyPassword(
                agent,
                agent.PasswordHash!,
                database.DevelopmentPassword));
        });
        Assert.False(agents.Single(agent => agent.Email == "yuki.tanaka@northwind-support.example").IsActive);
        Assert.Equal(40, await database.Db.Set<Ticket>().CountAsync());
    }

    [Fact]
    public async Task SeedAsync_BackfillsKnownAgentsWithExistingTicketsWithoutChangingTheirDataOrHashes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var existing = await database.AddExistingSeedAgentsAndTicketAsync();
        var seeder = database.CreateSeeder();

        await seeder.SeedAsync();

        var firstHashes = await database.Db.Set<Agent>()
            .OrderBy(agent => agent.Email)
            .Select(agent => agent.PasswordHash)
            .ToListAsync();
        Assert.Equal(6, firstHashes.Count);
        Assert.All(firstHashes, hash => Assert.False(string.IsNullOrWhiteSpace(hash)));
        Assert.Equal(1, await database.Db.Set<Ticket>().CountAsync());
        Assert.Equal(existing.AgentId, (await database.Db.Set<Ticket>().SingleAsync()).AssignedAgentId);
        Assert.Equal(6, await database.Db.Set<AgentSpecialization>().CountAsync());
        Assert.False((await database.Db.Set<Agent>().SingleAsync(agent =>
            agent.Email == "yuki.tanaka@northwind-support.example")).IsActive);

        var alex = await database.Db.Set<Agent>().SingleAsync(agent =>
            agent.Email == "alex.turner@northwind-support.example");
        var preexistingHash = database.PasswordHasher.HashPassword(alex, "changed-before-next-startup");
        alex.SetPasswordHash(preexistingHash);
        await database.Db.SaveChangesAsync();

        await seeder.SeedAsync();

        Assert.Equal(preexistingHash, (await database.Db.Set<Agent>().SingleAsync(agent =>
            agent.Email == "alex.turner@northwind-support.example")).PasswordHash);
        Assert.Equal(1, await database.Db.Set<Ticket>().CountAsync());
        Assert.Equal(existing.AgentId, (await database.Db.Set<Ticket>().SingleAsync()).AssignedAgentId);
        Assert.Equal(6, await database.Db.Set<AgentSpecialization>().CountAsync());
    }

    private sealed class TestDatabase(
        SqliteConnection connection,
        SupportDbContext db,
        IConfiguration configuration,
        string developmentPassword) : IAsyncDisposable
    {
        public SupportDbContext Db { get; } = db;

        public string DevelopmentPassword { get; } = developmentPassword;

        public IAgentPasswordHasher PasswordHasher { get; } = new IdentityAgentPasswordHasher();

        public SupportDbSeeder CreateSeeder() => new(
            Db,
            new FixedClock(),
            PasswordHasher,
            configuration,
            NullLogger<SupportDbSeeder>.Instance);

        public static async Task<TestDatabase> CreateAsync(bool configureDevelopmentPassword = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SupportDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SupportDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var developmentPassword = Guid.NewGuid().ToString("N");
            var settings = configureDevelopmentPassword
                ? new Dictionary<string, string?>
                {
                    [SupportDbSeeder.DevelopmentPasswordConfigurationKey] = developmentPassword
                }
                : new Dictionary<string, string?>();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new TestDatabase(connection, db, configuration, developmentPassword);
        }

        public async Task<(int AgentId, int TicketId)> AddExistingSeedAgentsAndTicketAsync()
        {
            var now = FixedClock.DefaultNow;
            var category = new Category("General", false, false);
            var customer = new Customer("Contoso Ltd", "contoso@example.test", null, CustomerTier.Premium, now);
            var agents = new[]
            {
                new Agent("Alex Turner", "alex.turner@northwind-support.example", 10, now),
                new Agent("Ben Osei", "ben.osei@northwind-support.example", 8, now),
                new Agent("Priya Nair", "priya.nair@northwind-support.example", 8, now),
                new Agent("Sara Lindqvist", "sara.lindqvist@northwind-support.example", 6, now),
                new Agent("Marcus Doyle", "marcus.doyle@northwind-support.example", 6, now),
                new Agent("Yuki Tanaka", "yuki.tanaka@northwind-support.example", 8, now)
            };
            agents[^1].Deactivate();
            Db.AddRange(category, customer);
            Db.AddRange(agents);
            await Db.SaveChangesAsync();

            foreach (var agent in agents)
            {
                agent.AddSpecialization(category.Id);
            }

            var ticket = Ticket.Raise(
                "TCK-0001",
                "Existing ticket stays intact",
                "Its assignment and history must survive credential backfill.",
                customer.Id,
                category.Id,
                TicketPriority.Medium,
                now,
                now.AddHours(24),
                agents[0].Id);
            Db.Add(ticket);
            await Db.SaveChangesAsync();

            return (agents[0].Id, ticket.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
