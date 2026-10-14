using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.BusinessRules;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.BusinessRules;

public sealed class PriorityAndTriagePolicyTests
{
    [Fact]
    public void Determine_WithoutRequestedPriority_DefaultsToMedium()
    {
        var result = PriorityPolicy.Determine(null, Category("General", forcesCritical: false));

        Assert.Equal(TicketPriority.Medium, result.Priority);
        Assert.Contains("default Medium", result.Reason);
    }

    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.High)]
    [InlineData(TicketPriority.Critical)]
    public void Determine_UsesRequestedPriorityWhenCategoryDoesNotForceCritical(
        TicketPriority requestedPriority)
    {
        var result = PriorityPolicy.Determine(
            requestedPriority,
            Category("General", forcesCritical: false));

        Assert.Equal(requestedPriority, result.Priority);
        Assert.Contains(requestedPriority.ToString(), result.Reason);
    }

    [Fact]
    public void Determine_ForcedCriticalCategoryOverridesRequestedPriority()
    {
        var result = PriorityPolicy.Determine(
            TicketPriority.Low,
            Category("Security", forcesCritical: true));

        Assert.Equal(TicketPriority.Critical, result.Priority);
        Assert.Contains("forces Critical", result.Reason);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, 4)]
    [InlineData(TicketPriority.High, 8)]
    [InlineData(TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.Low, 72)]
    public void Calculate_UsesConfiguredStandardWindows(TicketPriority priority, double hours)
    {
        var from = FixedClock.DefaultNow;
        var calculator = new SlaCalculator(new SlaPolicySettings());

        var result = calculator.Calculate(priority, CustomerTier.Standard, from);

        Assert.Equal(TimeSpan.FromHours(hours), result.EffectiveWindow);
        Assert.Equal(from.AddHours(hours), result.DueAtUtc);
        Assert.Contains($"{hours:0.##}-hour", result.Reason);
    }

    [Theory]
    [InlineData(TicketPriority.Critical, 2)]
    [InlineData(TicketPriority.High, 4)]
    [InlineData(TicketPriority.Medium, 12)]
    [InlineData(TicketPriority.Low, 36)]
    public void Calculate_HalvesPremiumWindows(TicketPriority priority, double hours)
    {
        var result = new SlaCalculator(new SlaPolicySettings())
            .Calculate(priority, CustomerTier.Premium, FixedClock.DefaultNow);

        Assert.Equal(TimeSpan.FromHours(hours), result.EffectiveWindow);
    }

    [Fact]
    public void Calculate_UsesConfiguredValuesAndMinimumWindow()
    {
        var settings = new SlaPolicySettings
        {
            BaseWindowHours = new()
            {
                [TicketPriority.Low] = 10,
                [TicketPriority.Medium] = 10,
                [TicketPriority.High] = 10,
                [TicketPriority.Critical] = 10
            },
            PremiumCustomerMultiplier = 0.1,
            MinimumWindowHours = 2,
            AtRiskThresholdPercent = 30
        };

        var result = new SlaCalculator(settings)
            .Calculate(TicketPriority.High, CustomerTier.Premium, FixedClock.DefaultNow);

        Assert.Equal(TimeSpan.FromHours(2), result.EffectiveWindow);
        Assert.Equal(FixedClock.DefaultNow.AddHours(2), result.DueAtUtc);
    }

    [Fact]
    public void Calculate_AppliesMinimumWindowToPremiumCustomer()
    {
        var settings = new SlaPolicySettings
        {
            BaseWindowHours = new()
            {
                [TicketPriority.Low] = 1,
                [TicketPriority.Medium] = 1,
                [TicketPriority.High] = 1,
                [TicketPriority.Critical] = 1
            },
            PremiumCustomerMultiplier = 0.5,
            MinimumWindowHours = 1
        };

        var result = new SlaCalculator(settings)
            .Calculate(TicketPriority.Medium, CustomerTier.Premium, FixedClock.DefaultNow);

        Assert.Equal(TimeSpan.FromHours(1), result.EffectiveWindow);
        Assert.Equal(FixedClock.DefaultNow.AddHours(1), result.DueAtUtc);
    }

    [Fact]
    public void Calculate_RejectsInvalidConfiguration()
    {
        var settings = new SlaPolicySettings { PremiumCustomerMultiplier = 1.1 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new SlaCalculator(settings).Calculate(
                TicketPriority.Medium,
                CustomerTier.Standard,
                FixedClock.DefaultNow));

        Assert.Contains("multiplier", exception.Message);
    }

    [Theory]
    [MemberData(nameof(NonFiniteSettings))]
    public void Calculate_RejectsNonFiniteConfiguration(SlaPolicySettings settings)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new SlaCalculator(settings).Calculate(
                TicketPriority.Medium,
                CustomerTier.Standard,
                FixedClock.DefaultNow));

        Assert.Contains("SLA", exception.Message);
    }

    public static IEnumerable<object[]> NonFiniteSettings()
    {
        yield return
        [
            new SlaPolicySettings
            {
                BaseWindowHours = new()
                {
                    [TicketPriority.Low] = double.NaN,
                    [TicketPriority.Medium] = 24,
                    [TicketPriority.High] = 8,
                    [TicketPriority.Critical] = 4
                }
            }
        ];
        yield return [new SlaPolicySettings { PremiumCustomerMultiplier = double.PositiveInfinity }];
        yield return [new SlaPolicySettings { MinimumWindowHours = double.NegativeInfinity }];
        yield return [new SlaPolicySettings { AtRiskThresholdPercent = double.NaN }];
    }

    [Fact]
    public void Select_ChoosesEligibleAgentWithFewestOpenTickets()
    {
        var category = Category("General", requiresSpecialist: false);
        var first = Agent("First", 1, maxOpenTickets: 5);
        var second = Agent("Second", 2, maxOpenTickets: 5);

        var result = AgentSelectionPolicy.Select(
            category,
            [first, second],
            new Dictionary<int, int> { [first.Id] = 3, [second.Id] = 1 });

        Assert.Equal(second.Id, result.AgentId);
    }

    [Fact]
    public void Select_ExcludesInactiveAndFullOrOverCapacityAgents()
    {
        var category = Category("General", requiresSpecialist: false);
        var inactive = Agent("Inactive", 1, maxOpenTickets: 5);
        inactive.Deactivate();
        var full = Agent("Full", 2, maxOpenTickets: 2);
        var overCapacity = Agent("Over", 3, maxOpenTickets: 1);
        var available = Agent("Available", 4, maxOpenTickets: 5);

        var result = AgentSelectionPolicy.Select(
            category,
            [inactive, full, overCapacity, available],
            new Dictionary<int, int>
            {
                [inactive.Id] = 0,
                [full.Id] = 2,
                [overCapacity.Id] = 2,
                [available.Id] = 1
            });

        Assert.Equal(available.Id, result.AgentId);
    }

    [Fact]
    public void CountOpenTickets_ExcludesResolvedAndClosedTickets()
    {
        var agent = Agent("Agent", 1, maxOpenTickets: 5);
        var tickets = new[]
        {
            new TicketBuilder().AssignedTo(agent.Id).WithStatus(TicketStatus.Open).Build(),
            new TicketBuilder().AssignedTo(agent.Id).WithStatus(TicketStatus.Resolved).Build(),
            new TicketBuilder().AssignedTo(agent.Id).WithStatus(TicketStatus.Closed).Build()
        };

        var counts = AgentSelectionPolicy.CountOpenTickets([agent], tickets);

        Assert.Equal(1, counts[agent.Id]);
    }

    [Fact]
    public void Select_RequiresSpecializationOnlyWhenCategoryRequiresIt()
    {
        var specialistCategory = Category("Security", id: 1, requiresSpecialist: true);
        var generalCategory = Category("General", id: 2, requiresSpecialist: false);
        var specialist = Agent("Specialist", 1, maxOpenTickets: 5);
        specialist.AddSpecialization(specialistCategory.Id);
        var generalist = Agent("Generalist", 2, maxOpenTickets: 5);

        var specialistResult = AgentSelectionPolicy.Select(
            specialistCategory,
            [generalist, specialist],
            new Dictionary<int, int> { [generalist.Id] = 0, [specialist.Id] = 1 });
        var generalResult = AgentSelectionPolicy.Select(
            generalCategory,
            [generalist, specialist],
            new Dictionary<int, int> { [generalist.Id] = 0, [specialist.Id] = 1 });

        Assert.Equal(specialist.Id, specialistResult.AgentId);
        Assert.Equal(generalist.Id, generalResult.AgentId);
    }

    [Fact]
    public void Select_BreaksWorkloadTiesByLowestAgentId()
    {
        var category = Category("General", requiresSpecialist: false);
        var lowerId = Agent("Lower", 1, maxOpenTickets: 5);
        var higherId = Agent("Higher", 2, maxOpenTickets: 5);

        var result = AgentSelectionPolicy.Select(
            category,
            [higherId, lowerId],
            new Dictionary<int, int> { [lowerId.Id] = 1, [higherId.Id] = 1 });

        Assert.Equal(lowerId.Id, result.AgentId);
    }

    [Fact]
    public void Select_ReturnsUnassignedResultWhenNoAgentIsEligible()
    {
        var category = Category("Security", requiresSpecialist: true);
        var agent = Agent("Unavailable", 1, maxOpenTickets: 1);
        var result = AgentSelectionPolicy.Select(
            category,
            [agent],
            new Dictionary<int, int> { [agent.Id] = 1 });

        Assert.Null(result.AgentId);
        Assert.Contains("unassigned", result.Reason);
    }

    [Fact]
    public void WorkedExample_AdventureWorksOutage_AppliesCriticalTwoHourSlaAndSelectsBen()
    {
        AssertWorkedExample(
            categoryName: "Outage",
            requiresSpecialist: true,
            forcesCritical: true,
            customerTier: CustomerTier.Premium,
            requestedPriority: TicketPriority.Medium,
            expectedPriority: TicketPriority.Critical,
            expectedWindowHours: 2,
            expectedAgentId: 2);
    }

    [Fact]
    public void WorkedExample_ContosoSecurity_AppliesCriticalTwoHourSlaAndSelectsPriya()
    {
        AssertWorkedExample(
            categoryName: "Security",
            requiresSpecialist: true,
            forcesCritical: true,
            customerTier: CustomerTier.Premium,
            requestedPriority: TicketPriority.Low,
            expectedPriority: TicketPriority.Critical,
            expectedWindowHours: 2,
            expectedAgentId: 3);
    }

    [Fact]
    public void WorkedExample_FabrikamBilling_AppliesHighEightHourSlaAndSelectsSara()
    {
        AssertWorkedExample(
            categoryName: "Billing",
            requiresSpecialist: true,
            forcesCritical: false,
            customerTier: CustomerTier.Standard,
            requestedPriority: TicketPriority.High,
            expectedPriority: TicketPriority.High,
            expectedWindowHours: 8,
            expectedAgentId: 4);
    }

    [Fact]
    public void WorkedExample_TailspinTechnical_AppliesMediumTwentyFourHourSlaAndSelectsSara()
    {
        AssertWorkedExample(
            categoryName: "Technical",
            requiresSpecialist: false,
            forcesCritical: false,
            customerTier: CustomerTier.Standard,
            requestedPriority: null,
            expectedPriority: TicketPriority.Medium,
            expectedWindowHours: 24,
            expectedAgentId: 4);
    }

    [Fact]
    public void WorkedExample_WideWorldImportersGeneral_AppliesLowThirtySixHourSlaAndSelectsSara()
    {
        AssertWorkedExample(
            categoryName: "General",
            requiresSpecialist: false,
            forcesCritical: false,
            customerTier: CustomerTier.Premium,
            requestedPriority: TicketPriority.Low,
            expectedPriority: TicketPriority.Low,
            expectedWindowHours: 36,
            expectedAgentId: 4);
    }

    private static void AssertWorkedExample(
        string categoryName,
        bool requiresSpecialist,
        bool forcesCritical,
        CustomerTier customerTier,
        TicketPriority? requestedPriority,
        TicketPriority expectedPriority,
        double expectedWindowHours,
        int expectedAgentId)
    {
        var categoryId = categoryName switch
        {
            "Security" => 10,
            "Outage" => 11,
            "Billing" => 12,
            "Technical" => 13,
            _ => 14
        };
        var category = Category(categoryName, categoryId, requiresSpecialist, forcesCritical);
        var priority = PriorityPolicy.Determine(requestedPriority, category);
        var sla = new SlaCalculator(new SlaPolicySettings())
            .Calculate(priority.Priority, customerTier, FixedClock.DefaultNow);
        var agents = SeedExampleAgents(category.Id);
        var selection = AgentSelectionPolicy.Select(
            category,
            agents,
            new Dictionary<int, int>
            {
                [1] = 5,
                [2] = 3,
                [3] = 4,
                [4] = 2,
                [5] = 6,
                [6] = 0
            });

        Assert.Equal(expectedPriority, priority.Priority);
        Assert.Equal(TimeSpan.FromHours(expectedWindowHours), sla.EffectiveWindow);
        Assert.Equal(
            FixedClock.DefaultNow.AddHours(expectedWindowHours),
            sla.DueAtUtc);
        Assert.Equal(expectedAgentId, selection.AgentId);
    }

    private static Agent[] SeedExampleAgents(int categoryId)
    {
        var alex = Agent("Alex Turner", 1, 10);
        var ben = Agent("Ben Osei", 2, 8);
        var priya = Agent("Priya Nair", 3, 8);
        var sara = Agent("Sara Lindqvist", 4, 6);
        var marcus = Agent("Marcus Doyle", 5, 6);
        var yuki = Agent("Yuki Tanaka", 6, 8);
        yuki.Deactivate();

        switch (categoryId)
        {
            case 10:
                priya.AddSpecialization(categoryId);
                marcus.AddSpecialization(categoryId);
                break;
            case 11:
                ben.AddSpecialization(categoryId);
                priya.AddSpecialization(categoryId);
                break;
            case 12:
                sara.AddSpecialization(categoryId);
                marcus.AddSpecialization(categoryId);
                break;
            case 13:
                alex.AddSpecialization(categoryId);
                ben.AddSpecialization(categoryId);
                yuki.AddSpecialization(categoryId);
                break;
            case 14:
                alex.AddSpecialization(categoryId);
                sara.AddSpecialization(categoryId);
                break;
        }

        return [alex, ben, priya, sara, marcus, yuki];
    }

    private static Category Category(
        string name,
        int id = 0,
        bool requiresSpecialist = false,
        bool forcesCritical = false)
    {
        var category = new Category(name, requiresSpecialist, forcesCritical);
        SetId(category, id);
        return category;
    }

    private static Agent Agent(string name, int id, int maxOpenTickets) =>
        SetId(new Agent(name, $"{name}@example.test", maxOpenTickets, FixedClock.DefaultNow), id);

    private static T SetId<T>(T entity, int id)
    {
        typeof(SupportDesk.Domain.Common.Entity)
            .GetProperty(nameof(SupportDesk.Domain.Common.Entity.Id))!
            .SetValue(entity, id);
        return entity;
    }
}
