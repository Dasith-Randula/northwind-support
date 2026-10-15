import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { formatDateTime } from '../../../lib/format';
import { ticketDetail } from '../../../test/fixtures';
import { TicketSummary } from './TicketSummary';

describe('TicketSummary SLA display', () => {
  it('shows the relative countdown for an active ticket', () => {
    render(<TicketSummary ticket={ticketDetail({ status: 'InProgress', slaStatus: 'WithinSla' })} />);

    expect(screen.getByText(/^Due /)).toBeInTheDocument();
  });

  it.each(['Resolved', 'Closed'] as const)(
    'keeps the SLA status and absolute due date without a countdown for %s tickets',
    (status) => {
      const ticket = ticketDetail({
        status,
        slaStatus: 'Met',
        resolvedAtUtc: '2026-09-25T10:03:00Z',
      });
      render(<TicketSummary ticket={ticket} />);

      expect(screen.getByText('Met')).toBeInTheDocument();
      expect(screen.getByText(formatDateTime(ticket.dueAtUtc))).toBeInTheDocument();
      expect(screen.queryByText(/^Due /)).not.toBeInTheDocument();
    },
  );
});
