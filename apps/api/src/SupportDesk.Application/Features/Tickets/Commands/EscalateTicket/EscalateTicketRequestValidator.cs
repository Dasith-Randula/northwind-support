using FluentValidation;
using SupportDesk.Application.Contracts.Tickets;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketRequestValidator : AbstractValidator<EscalateTicketRequest>
{
    public EscalateTicketRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .Must(reason => reason is not null && reason.Trim().Length is >= 5 and <= 500)
            .WithMessage("Reason must contain between 5 and 500 non-whitespace characters.");
        RuleFor(x => x.EscalatedBy).NotEmpty().MaximumLength(100);
    }
}
