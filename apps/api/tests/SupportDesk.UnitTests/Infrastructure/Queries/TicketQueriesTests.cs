using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.BusinessRules;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Infrastructure.Queries;

public sealed class TicketQueriesTests
{
    [Fact]
    public async Task GetPagedAsync_FiltersEverySlaStatusInDatabaseBeforeCountAndPaging()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = FixedClock.DefaultNow;
        var slaTickets = new[]
        {
            Ticket.Raise("TCK-1001", "No SLA", "No due date", database.FabrikamId, database.GeneralId,
                TicketPriority.Low, now.AddDays(-2)),
            Ticket.Raise("TCK-1002", "Met SLA", "Resolved on time", database.FabrikamId, database.GeneralId,
                TicketPriority.High, now.AddDays(-1), now.AddHours(1)),
            Ticket.Raise("TCK-1003", "Breached SLA", "Past due", database.FabrikamId, database.GeneralId,
                TicketPriority.High, now.AddDays(-1), now.AddTicks(-1)),
            Ticket.Raise("TCK-1004", "At risk SLA", "At configured threshold", database.FabrikamId, database.GeneralId,
                TicketPriority.High, now.AddDays(-1), now.AddHours(4)),
            Ticket.Raise("TCK-1005", "Premium at risk SLA", "Premium threshold", database.ContosoId, database.GeneralId,
                TicketPriority.High, now.AddDays(-1), now.AddHours(2)),
            Ticket.Raise("TCK-1006", "Within SLA", "More time remains", database.FabrikamId, database.GeneralId,
                TicketPriority.High, now.AddDays(-1), now.AddHours(6))
        };
        slaTickets[1].ChangeStatus(TicketStatus.Resolved, now);
        slaTickets[3].ChangeStatus(TicketStatus.InProgress, now);
        slaTickets[4].ChangeStatus(TicketStatus.InProgress, now);
        await database.Db.AddRangeAsync(slaTickets);
        await database.Db.SaveChangesAsync();

        var notApplicable = await database.Queries.GetPagedAsync(
            new TicketQuery { SlaStatus = SlaStatus.NotApplicable, Search = "TCK-1001" }, CancellationToken.None);
        var met = await database.Queries.GetPagedAsync(
            new TicketQuery { SlaStatus = SlaStatus.Met, Search = "TCK-1002" }, CancellationToken.None);
        var breached = await database.Queries.GetPagedAsync(
            new TicketQuery { SlaStatus = SlaStatus.Breached, Search = "TCK-1003" }, CancellationToken.None);
        var atRiskPage1 = await database.Queries.GetPagedAsync(
            new TicketQuery
            {
                SlaStatus = SlaStatus.AtRisk,
                Page = 1,
                PageSize = 1,
                SortBy = "createdAtUtc",
                SortDirection = "asc"
            }, CancellationToken.None);
        var atRiskPage2 = await database.Queries.GetPagedAsync(
            new TicketQuery
            {
                SlaStatus = SlaStatus.AtRisk,
                Page = 2,
                PageSize = 1,
                SortBy = "createdAtUtc",
                SortDirection = "asc"
            }, CancellationToken.None);
        var within = await database.Queries.GetPagedAsync(
            new TicketQuery { SlaStatus = SlaStatus.WithinSla, Search = "TCK-1006" }, CancellationToken.None);
        var combined = await database.Queries.GetPagedAsync(
            new TicketQuery
            {
                Status = TicketStatus.InProgress,
                Priority = TicketPriority.High,
                SlaStatus = SlaStatus.AtRisk
            }, CancellationToken.None);
        var withoutSlaFilter = await database.Queries.GetPagedAsync(
            new TicketQuery { Search = "TCK-1001" }, CancellationToken.None);

        Assert.Equal("TCK-1001", Assert.Single(notApplicable.Items).Reference);
        Assert.Equal("TCK-1002", Assert.Single(met.Items).Reference);
        Assert.Equal("TCK-1003", Assert.Single(breached.Items).Reference);
        Assert.Equal(2, atRiskPage1.TotalCount);
        Assert.Equal(2, atRiskPage1.TotalPages);
        Assert.Equal("TCK-1005", Assert.Single(atRiskPage1.Items).Reference);
        Assert.Equal("TCK-1004", Assert.Single(atRiskPage2.Items).Reference);
        Assert.Equal("TCK-1006", Assert.Single(within.Items).Reference);
        Assert.Equal(2, combined.TotalCount);
        Assert.All(combined.Items, x => Assert.Equal(TicketStatus.InProgress, x.Status));
        Assert.Equal("TCK-1001", Assert.Single(withoutSlaFilter.Items).Reference);
        Assert.All(notApplicable.Items, x => Assert.Equal(SlaStatus.NotApplicable, x.SlaStatus));
        Assert.All(met.Items, x => Assert.Equal(SlaStatus.Met, x.SlaStatus));
        Assert.All(breached.Items, x => Assert.Equal(SlaStatus.Breached, x.SlaStatus));
        Assert.All(atRiskPage1.Items, x => Assert.Equal(SlaStatus.AtRisk, x.SlaStatus));
        Assert.All(atRiskPage2.Items, x => Assert.Equal(SlaStatus.AtRisk, x.SlaStatus));
        Assert.All(within.Items, x => Assert.Equal(SlaStatus.WithinSla, x.SlaStatus));
        Assert.All(combined.Items, x => Assert.Equal(SlaStatus.AtRisk, x.SlaStatus));
    }

    [Fact]
    public async Task GetPagedAsync_AtRiskCutoffUsesConfiguredMinimumSlaWindow()
    {
        var settings = new SlaPolicySettings
        {
            PremiumCustomerMultiplier = 0.1,
            MinimumWindowHours = 2,
            AtRiskThresholdPercent = 50
        };
        await using var database = await TestDatabase.CreateAsync(settings);
        var now = FixedClock.DefaultNow;
        var premiumCriticalTicket = Ticket.Raise(
            "TCK-1010",
            "Premium critical SLA",
            "The minimum SLA window determines the at-risk cutoff.",
            database.ContosoId,
            database.GeneralId,
            TicketPriority.Critical,
            now.AddMinutes(-30),
            now.AddHours(1));
        await database.Db.AddAsync(premiumCriticalTicket);
        await database.Db.SaveChangesAsync();

        var withConfiguredFloor = await database.Queries.GetPagedAsync(
            new TicketQuery { SlaStatus = SlaStatus.AtRisk, Search = "TCK-1010" },
            CancellationToken.None);
        var withoutFloor = new TicketQueries(
            database.Db,
            new FixedClock(),
            new SlaPolicySettings
            {
                PremiumCustomerMultiplier = 0.1,
                MinimumWindowHours = 1,
                AtRiskThresholdPercent = 50
            });
        var belowFloorResult = await withoutFloor.GetPagedAsync(
            new TicketQuery { SlaStatus = SlaStatus.AtRisk, Search = "TCK-1010" },
            CancellationToken.None);

        var ticket = Assert.Single(withConfiguredFloor.Items);
        Assert.Equal(SlaStatus.AtRisk, ticket.SlaStatus);
        Assert.Equal(0, belowFloorResult.TotalCount);
        Assert.Empty(belowFloorResult.Items);
    }

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
            int fabrikamId,
            int generalId,
            int alexId)
        {
            Connection = connection;
            Db = db;
            Queries = queries;
            ContosoId = contosoId;
            FabrikamId = fabrikamId;
            GeneralId = generalId;
            AlexId = alexId;
        }

        public SqliteConnection Connection { get; }

        public SupportDbContext Db { get; }

        public TicketQueries Queries { get; }

        public int ContosoId { get; }

        public int FabrikamId { get; }

        public int GeneralId { get; }

        public int AlexId { get; }

        public static async Task<TestDatabase> CreateAsync(SlaPolicySettings? slaSettings = null)
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
                new TicketQueries(
                    db,
                    new FixedClock(),
                    slaSettings ?? new SlaPolicySettings { AtRiskThresholdPercent = 50 }),
                contoso.Id,
                fabrikam.Id,
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
