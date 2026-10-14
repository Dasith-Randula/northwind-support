using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.BusinessRules;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.Infrastructure.Repositories;
using SupportDesk.Infrastructure.Services;

namespace SupportDesk.Infrastructure;

/// <summary>
/// Wires the domain's repositories and the application's abstractions to their SQL Server /
/// system implementations.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. See README.md for local setup.");

        services.AddDbContext<SupportDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SupportDbContext>());

        services.AddSingleton<IClock, SystemClock>();

        var slaSettings = new SlaPolicySettings
        {
            BaseWindowHours = new()
            {
                [TicketPriority.Low] = ReadSlaNumber(configuration, "BaseWindowHours:Low", 72),
                [TicketPriority.Medium] = ReadSlaNumber(configuration, "BaseWindowHours:Medium", 24),
                [TicketPriority.High] = ReadSlaNumber(configuration, "BaseWindowHours:High", 8),
                [TicketPriority.Critical] = ReadSlaNumber(configuration, "BaseWindowHours:Critical", 4)
            },
            PremiumCustomerMultiplier = ReadSlaNumber(configuration, "PremiumCustomerMultiplier", 0.5),
            MinimumWindowHours = ReadSlaNumber(configuration, "MinimumWindowHours", 1),
            AtRiskThresholdPercent = ReadSlaNumber(configuration, "AtRiskThresholdPercent", 25)
        };
        slaSettings.Validate();
        services.AddSingleton(slaSettings);
        services.AddSingleton<SlaCalculator>();

        services.AddScoped<ITicketCreationCoordinator, SqlServerTicketCreationCoordinator>();
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();

        services.AddScoped<ITicketQueries, TicketQueries>();
        services.AddScoped<ICustomerQueries, CustomerQueries>();
        services.AddScoped<IAgentQueries, AgentQueries>();
        services.AddScoped<ICategoryQueries, CategoryQueries>();

        services.AddScoped<SupportDbSeeder>();

        return services;
    }

    private static double ReadSlaNumber(IConfiguration configuration, string key, double defaultValue)
    {
        var value = configuration[$"Sla:{key}"];
        if (value is null)
        {
            return defaultValue;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new InvalidOperationException($"SLA configuration value 'Sla:{key}' is invalid.");
        }

        return number;
    }
}
