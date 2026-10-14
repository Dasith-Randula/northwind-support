using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Services;

/// <summary>
/// Serializes ticket creation across API instances with a transaction-scoped SQL Server
/// application lock. The lock covers workload reads, selection and persistence.
/// </summary>
public sealed class SqlServerTicketCreationCoordinator(SupportDbContext db) : ITicketCreationCoordinator
{
    private const string LockResource = "SupportDesk:TicketCreation";

    public async Task<TicketDetailDto> ExecuteAsync(
        Func<CancellationToken, Task<TicketDetailDto>> operation,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // A retry must not reuse entities tracked by an abandoned transaction.
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sp_getapplock
                    @Resource = @resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 30000;
                SELECT @result;
                """;

            var resource = command.CreateParameter();
            resource.ParameterName = "@resource";
            resource.DbType = DbType.String;
            resource.Value = LockResource;
            command.Parameters.Add(resource);

            var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
            if (result < 0)
            {
                throw new InvalidOperationException(
                    $"SQL Server could not acquire the ticket creation lock. Result code: {result}.");
            }

            try
            {
                var response = await operation(ct);
                await transaction.CommitAsync(ct);
                return response;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        });
    }
}
