using SupportDesk.Application.Contracts.Tickets;

namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Runs ticket creation under the persistence boundary required to keep assignment capacity
/// consistent across API instances.
/// </summary>
public interface ITicketCreationCoordinator
{
    Task<TicketDetailDto> ExecuteAsync(
        Func<CancellationToken, Task<TicketDetailDto>> operation,
        CancellationToken ct);
}
