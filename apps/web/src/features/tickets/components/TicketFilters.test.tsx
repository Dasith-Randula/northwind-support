import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { emptyFilters } from '../hooks/useTicketList';
import { TicketFilters } from './TicketFilters';

describe('TicketFilters SLA status', () => {
  it('offers all SLA states and reports a selection to the list hook', () => {
    const onChange = vi.fn();

    render(
      <TicketFilters
        filters={emptyFilters}
        agents={[]}
        categories={[]}
        customers={[]}
        onChange={onChange}
        onReset={vi.fn()}
      />,
    );

    const slaFilter = screen.getByLabelText('SLA status');
    expect(slaFilter).toHaveTextContent('Within SLA');
    expect(slaFilter).toHaveTextContent('At risk');
    expect(slaFilter).toHaveTextContent('Breached');
    expect(slaFilter).toHaveTextContent('Met');
    expect(slaFilter).toHaveTextContent('No SLA');

    fireEvent.change(slaFilter, { target: { value: 'AtRisk' } });

    expect(onChange).toHaveBeenCalledWith({ slaStatus: 'AtRisk' });
  });
});
