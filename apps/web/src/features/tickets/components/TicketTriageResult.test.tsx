import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ticketDetail } from '../../../test/fixtures';
import { TicketTriageResult } from './TicketTriageResult';

describe('TicketTriageResult', () => {
  it('shows the returned priority, SLA due date, and reason for an unassigned ticket', () => {
    render(
      <TicketTriageResult
        ticket={ticketDetail({
          assignedAgent: null,
          triage: {
            appliedPriority: 'Critical',
            priorityReason: 'Category policy requires Critical priority.',
            slaWindowHours: 4,
            slaReason: 'Critical window: 4 hours.',
            assignmentReason: 'No eligible agent is available.',
          },
        })}
      />,
    );

    expect(screen.getByText('Critical')).toBeInTheDocument();
    expect(screen.getByText('Unassigned because No eligible agent is available.')).toBeInTheDocument();
    expect(screen.getByText('Category policy requires Critical priority.')).toBeInTheDocument();
    expect(screen.getByText('Critical window: 4 hours.')).toBeInTheDocument();
  });
});
