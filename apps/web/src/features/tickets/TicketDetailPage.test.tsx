import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../api/client';
import type { EscalateTicketResponse } from '../../types/api';
import { ticketDetail, ticketEscalation } from '../../test/fixtures';
import { TicketDetailPage } from './TicketDetailPage';

const mocks = vi.hoisted(() => ({
  ticket: null as unknown,
  history: [] as unknown[],
  setData: vi.fn(),
  reload: vi.fn(),
  reloadHistory: vi.fn(),
  escalateTicket: vi.fn(),
}));

vi.mock('./hooks/useTicket', () => ({
  useTicket: () => ({
    data: mocks.ticket,
    isLoading: false,
    error: undefined,
    reload: mocks.reload,
    setData: mocks.setData,
  }),
}));

vi.mock('./hooks/useTicketEscalations', () => ({
  useTicketEscalations: () => ({
    data: mocks.history,
    isLoading: false,
    error: undefined,
    reload: mocks.reloadHistory,
  }),
}));

vi.mock('../../hooks/useReferenceData', () => ({
  useReferenceData: () => ({ data: { agents: [] }, isLoading: false, error: undefined, reload: vi.fn() }),
}));

vi.mock('../../api/tickets', () => ({
  ticketsApi: { escalateTicket: mocks.escalateTicket },
}));

function renderTicketDetailPage() {
  return render(
    <MemoryRouter initialEntries={['/tickets/12']}>
      <Routes>
        <Route path="/tickets/:id" element={<TicketDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

function enterValidEscalation() {
  fireEvent.change(screen.getByLabelText('Reason'), {
    target: { value: 'The customer impact increased.' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));
}

describe('TicketDetailPage escalation integration', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.ticket = ticketDetail();
    mocks.history = [];
    mocks.reloadHistory.mockResolvedValue(undefined);
  });

  it('shows a 409 response without changing ticket information', async () => {
    mocks.escalateTicket.mockRejectedValue(
      new ApiError(409, 'Ticket TCK-0012 is already Critical and cannot be escalated.'),
    );
    renderTicketDetailPage();

    expect(screen.getByText('High')).toBeInTheDocument();
    enterValidEscalation();

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ticket TCK-0012 is already Critical and cannot be escalated.',
    );
    expect(mocks.setData).not.toHaveBeenCalled();
    expect(screen.getByText('High')).toBeInTheDocument();
  });

  it('updates ticket details and reloads escalation history after success', async () => {
    const updatedTicket = ticketDetail({ priority: 'Critical' });
    const response: EscalateTicketResponse = {
      ticket: updatedTicket,
      escalation: ticketEscalation({ toPriority: 'Critical' }),
    };
    mocks.escalateTicket.mockResolvedValue(response);
    renderTicketDetailPage();
    enterValidEscalation();

    await waitFor(() => {
      expect(mocks.setData).toHaveBeenCalledWith(updatedTicket);
      expect(mocks.reloadHistory).toHaveBeenCalledOnce();
    });
  });
});
