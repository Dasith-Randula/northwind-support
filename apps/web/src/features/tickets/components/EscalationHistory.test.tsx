import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ticketEscalation } from '../../../test/fixtures';
import { EscalationHistory } from './EscalationHistory';

describe('EscalationHistory', () => {
  it('shows all before-and-after details and preserves newest-first API order', () => {
    const newer = ticketEscalation({ id: 4, toPriority: 'Critical', escalatedBy: 'Alex' });
    const older = ticketEscalation({
      id: 3,
      fromPriority: 'Low',
      toPriority: 'Medium',
      fromAgent: null,
      toAgent: { id: 5, fullName: 'Ben Osei' },
      reason: 'Older escalation reason.',
      escalatedBy: 'Morgan',
    });
    render(
      <EscalationHistory entries={[newer, older]} isLoading={false} onRetry={vi.fn()} />,
    );

    const headings = screen.getAllByRole('heading', { level: 3 });
    expect(headings.map((heading) => heading.textContent)).toEqual([
      'Medium → Critical',
      'Low → Medium',
    ]);
    expect(screen.getByText('Sara Lindqvist → Unassigned')).toBeInTheDocument();
    expect(screen.getByText('Customer impact has increased.')).toBeInTheDocument();
    expect(screen.getByText('Alex')).toBeInTheDocument();
  });

  it('handles null agent and due-date values', () => {
    render(
      <EscalationHistory
        entries={[
          ticketEscalation({
            fromAgent: null,
            toAgent: null,
            fromDueAtUtc: null,
            toDueAtUtc: null,
          }),
        ]}
        isLoading={false}
        onRetry={vi.fn()}
      />,
    );

    expect(screen.getByText('Unassigned → Unassigned')).toBeInTheDocument();
    expect(screen.getByText('- → -')).toBeInTheDocument();
  });

  it('shows loading, empty, and retryable error states', () => {
    const { rerender } = render(
      <EscalationHistory entries={undefined} isLoading onRetry={vi.fn()} />,
    );
    expect(screen.getByRole('status')).toHaveTextContent('Loading escalation history');

    rerender(<EscalationHistory entries={[]} isLoading={false} onRetry={vi.fn()} />);
    expect(screen.getByText('No escalations yet')).toBeInTheDocument();

    const onRetry = vi.fn();
    rerender(
      <EscalationHistory entries={undefined} isLoading={false} error="History unavailable." onRetry={onRetry} />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('History unavailable.');
    within(screen.getByRole('alert')).getByRole('button', { name: 'Try again' }).click();
    expect(onRetry).toHaveBeenCalledOnce();
  });
});
