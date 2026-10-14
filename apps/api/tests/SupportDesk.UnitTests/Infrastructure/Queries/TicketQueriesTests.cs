using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests
{
    [Fact]
    public async Task GetPagedAsync_FiltersByPriorityBeforeCounting()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery { Priority = TicketPriority.High },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, ticket => Assert.Equal(TicketPriority.High, ticket.Priority));
        Assert.DoesNotContain(result.Items, ticket => ticket.Priority == TicketPriority.Critical);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByStatusBeforeCounting()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery { Status = TicketStatus.InProgress },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, ticket => Assert.Equal(TicketStatus.InProgress, ticket.Status));
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByCategoryBeforeCounting()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery { CategoryId = database.GeneralId },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, ticket => Assert.Equal(database.GeneralId, ticket.Category.Id));
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByCustomerBeforeCounting()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery { CustomerId = database.ContosoId },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, ticket => Assert.Equal(database.ContosoId, ticket.Customer.Id));
    }

    [Fact]
    public async Task GetPagedAsync_CombinesFiltersWithAnd()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery
            {
                Status = TicketStatus.InProgress,
                Priority = TicketPriority.High,
                CustomerId = database.ContosoId,
                CategoryId = database.GeneralId
            },
            CancellationToken.None);

        var ticket = Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(database.ContosoId, ticket.Customer.Id);
    }

    [Fact]
    public async Task GetPagedAsync_SearchesCustomerNameInTheDatabase()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery { Search = "  contoso  " },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, ticket => Assert.Equal("Contoso Ltd", ticket.Customer.Name));
    }

    [Fact]
    public async Task GetPagedAsync_AppliesExactReferenceAndTitleSearches()
    {
        await using var database = await TestDatabase.CreateAsync();

        var referenceResult = await database.Queries.GetPagedAsync(
            new TicketQuery { Search = "TCK-0002" },
            CancellationToken.None);
        var titleResult = await database.Queries.GetPagedAsync(
            new TicketQuery { Search = "invoice export" },
            CancellationToken.None);

        Assert.Equal("TCK-0002", Assert.Single(referenceResult.Items).Reference);
        Assert.Equal("Invoice export fails", Assert.Single(titleResult.Items).Title);
    }

    [Fact]
    public async Task GetPagedAsync_IgnoresWhitespaceOnlySearch()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery { Search = "   " },
            CancellationToken.None);

        Assert.Equal(4, result.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_AppliesAssignmentAndUnassignedFilters()
    {
        await using var database = await TestDatabase.CreateAsync();

        var assigned = await database.Queries.GetPagedAsync(
            new TicketQuery { AssignedAgentId = database.AlexId },
            CancellationToken.None);
        var unassigned = await database.Queries.GetPagedAsync(
            new TicketQuery { UnassignedOnly = true },
            CancellationToken.None);

        Assert.Equal(2, assigned.TotalCount);
        Assert.All(assigned.Items, ticket => Assert.Equal(database.AlexId, ticket.AssignedAgent!.Id));
        Assert.Single(unassigned.Items);
        Assert.Null(unassigned.Items[0].AssignedAgent);
    }

    [Fact]
    public async Task GetPagedAsync_CombiningAssignedAgentAndUnassignedOnlyReturnsNoMatches()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery
            {
                AssignedAgentId = database.AlexId,
                UnassignedOnly = true
            },
            CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsEmptyPageWhenNoTicketsMatch()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery { CustomerId = 999999 },
            CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersBeforePagingAndReportsFilteredTotalPages()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery
            {
                Priority = TicketPriority.High,
                Page = 2,
                PageSize = 1,
                SortBy = "createdAtUtc",
                SortDirection = "asc"
            },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Page);
        Assert.Single(result.Items);
        Assert.Equal("TCK-0004", result.Items[0].Reference);
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsEmptyPageBeyondFilteredPageRange()
    {
        await using var database = await TestDatabase.CreateAsync();

        var result = await database.Queries.GetPagedAsync(
            new TicketQuery
            {
                Priority = TicketPriority.High,
                Page = 3,
                PageSize = 1
            },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(3, result.Page);
        Assert.Empty(result.Items);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(
            SqliteConnection connection,
            SupportDbContext db,
            TicketQueries queries,
            int contosoId,
            int generalId,
            int alexId)
        {
            Connection = connection;
            Db = db;
            Queries = queries;
            ContosoId = contosoId;
            GeneralId = generalId;
            AlexId = alexId;
        }

        public SqliteConnection Connection { get; }

        public SupportDbContext Db { get; }

        public TicketQueries Queries { get; }

        public int ContosoId { get; }

        public int GeneralId { get; }

        public int AlexId { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<SupportDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new SupportDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var now = FixedClock.DefaultNow;
            var contoso = new Customer("Contoso Ltd", "contoso@example.test", null, CustomerTier.Premium, now);
            var fabrikam = new Customer("Fabrikam Inc", "fabrikam@example.test", null, CustomerTier.Standard, now);
            var general = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);
            var technical = new Category("Technical", requiresSpecialist: false, forcesCriticalPriority: false);
            var alex = new Agent("Alex Turner", "alex@example.test", 10, now);
            var ben = new Agent("Ben Osei", "ben@example.test", 8, now);

            db.AddRange(contoso, fabrikam, general, technical, alex, ben);
            await db.SaveChangesAsync();

            var tickets = new[]
            {
                Ticket.Raise("TCK-0001", "Portal login fails", "Users cannot log in.", contoso.Id, general.Id,
                    TicketPriority.High, now.AddMinutes(-30)),
                Ticket.Raise("TCK-0002", "Invoice export fails", "The invoice export is empty.", contoso.Id, technical.Id,
                    TicketPriority.Critical, now.AddMinutes(-20)),
                Ticket.Raise("TCK-0003", "Update billing address", "Please update our billing address.", fabrikam.Id, general.Id,
                    TicketPriority.Low, now.AddMinutes(-10)),
                Ticket.Raise("TCK-0004", "API export fails", "The API export returns an error.", fabrikam.Id, technical.Id,
                    TicketPriority.High, now.AddMinutes(-5))
            };

            tickets[0].AssignTo(alex.Id, now);
            tickets[0].ChangeStatus(TicketStatus.InProgress, now);
            tickets[1].ChangeStatus(TicketStatus.Open, now);
            tickets[3].AssignTo(alex.Id, now);
            tickets[3].ChangeStatus(TicketStatus.InProgress, now);
            tickets[2].AssignTo(ben.Id, now);
            tickets[2].ChangeStatus(TicketStatus.Resolved, now);

            db.AddRange(tickets);
            await db.SaveChangesAsync();

            return new TestDatabase(
                connection,
                db,
                new TicketQueries(db, new FixedClock()),
                contoso.Id,
                general.Id,
                alex.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
