import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import type { EscalateTicketResponse } from '../../../types/api';
import { ticketDetail, ticketEscalation } from '../../../test/fixtures';
import { EscalationPanel } from './EscalationPanel';

const mocks = vi.hoisted(() => ({ escalateTicket: vi.fn() }));

vi.mock('../../../api/tickets', () => ({
  ticketsApi: { escalateTicket: mocks.escalateTicket },
}));

describe('EscalationPanel', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows a validation error for a short reason and does not call the API', () => {
    render(<EscalationPanel ticket={ticketDetail()} onEscalated={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'why' } });
    fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    expect(screen.getByText('Reason must contain between 5 and 500 non-whitespace characters.'))
      .toBeInTheDocument();
    expect(mocks.escalateTicket).not.toHaveBeenCalled();
  });

  it('shows a 409 conflict and does not report a successful ticket update', async () => {
    mocks.escalateTicket.mockRejectedValue(
      new ApiError(409, 'Ticket TCK-0012 is already Critical and cannot be escalated.'),
    );
    const onEscalated = vi.fn();
    render(<EscalationPanel ticket={ticketDetail()} onEscalated={onEscalated} />);
    fireEvent.change(screen.getByLabelText('Reason'), {
      target: { value: 'The customer impact increased.' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ticket TCK-0012 is already Critical and cannot be escalated.',
    );
    expect(onEscalated).not.toHaveBeenCalled();
  });

  it('sends the validated request and returns the successful response to the detail page', async () => {
    const response: EscalateTicketResponse = {
      ticket: ticketDetail({ priority: 'Critical' }),
      escalation: ticketEscalation({ toPriority: 'Critical' }),
    };
    mocks.escalateTicket.mockResolvedValue(response);
    const onEscalated = vi.fn();
    render(<EscalationPanel ticket={ticketDetail()} onEscalated={onEscalated} />);
    fireEvent.change(screen.getByLabelText('Reason'), {
      target: { value: '  The customer impact increased.  ' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    await waitFor(() => expect(onEscalated).toHaveBeenCalledWith(response));
    expect(mocks.escalateTicket).toHaveBeenCalledWith(12, {
      reason: 'The customer impact increased.',
    });
    expect(screen.queryByLabelText(/Escalated by/i)).not.toBeInTheDocument();
  });

  it.each([
    ['Critical', 'Open'],
    ['High', 'Resolved'],
    ['High', 'Closed'],
  ] as const)('disables escalation for a %s, %s ticket', (priority, status) => {
    render(<EscalationPanel ticket={ticketDetail({ priority, status })} onEscalated={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Escalate ticket' })).toBeDisabled();
  });
});
