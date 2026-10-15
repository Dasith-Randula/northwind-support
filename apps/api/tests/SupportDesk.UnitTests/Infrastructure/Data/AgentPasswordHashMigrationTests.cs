using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Presentation.Security;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Data;

public sealed class AgentPasswordHashMigrationTests
{
    [Fact]
    public async Task Migration_CreatesPasswordHashColumnOnFreshDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var db = new SupportDbContext(options);

        await db.Database.MigrateAsync();

        Assert.Equal(0, await db.Set<Agent>().CountAsync());
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('Agents')";
        await using var reader = await command.ExecuteReaderAsync();
        var passwordHashColumnExists = false;
        while (await reader.ReadAsync())
        {
            passwordHashColumnExists |= reader.GetString(1) == "PasswordHash";
        }

        Assert.True(passwordHashColumnExists);
    }

    [Fact]
    public async Task Migration_AddsNullableHashToExistingAgentsAndPreservesAssignedTicketData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SupportDbContext>()
            .UseSqlite(connection)
            // The production migrations target SQL Server; SQLite's model differs only in provider annotations.
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var db = new SupportDbContext(options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20260926183612_InitialCreate");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Agents (Id, FullName, Email, IsActive, MaxOpenTickets, CreatedAtUtc)
            VALUES (1, 'Alex Turner', 'alex.turner@northwind-support.example', 1, 10, '2026-01-01T00:00:00.000');
            INSERT INTO Categories (Id, Name, IsActive, RequiresSpecialist, ForcesCriticalPriority)
            VALUES (1, 'General', 1, 0, 0);
            INSERT INTO Customers (Id, Name, Email, Phone, Tier, CreatedAtUtc)
            VALUES (1, 'Contoso Ltd', 'contoso@example.test', NULL, 1, '2026-01-01T00:00:00.000');
            INSERT INTO AgentSpecializations (AgentId, CategoryId) VALUES (1, 1);
            INSERT INTO Tickets (Id, Reference, Title, Description, CustomerId, CategoryId, AssignedAgentId,
                Priority, Status, CreatedAtUtc, UpdatedAtUtc, DueAtUtc, ResolvedAtUtc)
            VALUES (1, 'TCK-0001', 'Keep this ticket', 'Keep its assignment and history intact.', 1, 1, 1,
                2, 1, '2026-01-01T00:00:00.000', '2026-01-01T00:00:00.000', NULL, NULL);
            """);

        await db.Database.MigrateAsync();

        var existingAgent = await db.Set<Agent>().SingleAsync();
        Assert.Null(existingAgent.PasswordHash);
        var existingTicket = await db.Set<Ticket>().SingleAsync();
        Assert.Equal(existingAgent.Id, existingTicket.AssignedAgentId);
        Assert.Single((await db.Set<Agent>().Include(agent => agent.Specializations).SingleAsync()).Specializations);

        IAgentPasswordHasher hasher = new IdentityAgentPasswordHasher();
        var developmentPassword = Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SupportDbSeeder.DevelopmentPasswordConfigurationKey] = developmentPassword
            })
            .Build();
        var seeder = new SupportDbSeeder(
            db,
            new FixedClock(),
            hasher,
            configuration,
            NullLogger<SupportDbSeeder>.Instance);
        await seeder.SeedAsync();

        existingAgent = await db.Set<Agent>().Include(agent => agent.Specializations).SingleAsync();
        Assert.True(hasher.VerifyPassword(
            existingAgent,
            existingAgent.PasswordHash!,
            developmentPassword));
        Assert.Equal(existingAgent.Id, (await db.Set<Ticket>().SingleAsync()).AssignedAgentId);
        Assert.Single(existingAgent.Specializations);
        Assert.Equal(1, await db.Set<Ticket>().CountAsync());
    }
}
