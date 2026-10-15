using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public sealed class EscalateTicketRequestValidatorTests
{
    private readonly EscalateTicketRequestValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no")]
    public void RejectsShortOrMissingReason(string? reason)
    {
        var result = _validator.Validate(new EscalateTicketRequest(reason, "lead"));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(EscalateTicketRequest.Reason));
    }

    [Fact]
    public void RejectsMissingActor()
    {
        var result = _validator.Validate(new EscalateTicketRequest("Customer impact", null));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(EscalateTicketRequest.EscalatedBy));
    }
}
