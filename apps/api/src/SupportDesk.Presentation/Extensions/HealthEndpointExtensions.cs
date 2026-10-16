using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Presentation.Extensions;

public static class HealthEndpointExtensions
{
    public static IEndpointRouteBuilder MapDatabaseHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", async (SupportDbContext db, CancellationToken cancellationToken) =>
            {
                try
                {
                    return await db.Database.CanConnectAsync(cancellationToken)
                        ? Results.Ok(new { status = "Healthy" })
                        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
                catch (Exception exception) when (
                    exception is DbException or TimeoutException or RetryLimitExceededException)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
            })
            .AllowAnonymous();

        return endpoints;
    }
}
