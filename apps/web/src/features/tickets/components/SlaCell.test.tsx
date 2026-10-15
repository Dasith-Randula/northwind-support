import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SlaStatus } from '../../../types/api';
import { SlaCell } from './SlaCell';

const labels: { status: SlaStatus; label: string }[] = [
  { status: 'WithinSla', label: 'Within SLA' },
  { status: 'AtRisk', label: 'At risk' },
  { status: 'Breached', label: 'Breached' },
  { status: 'Met', label: 'Met' },
  { status: 'NotApplicable', label: 'No SLA' },
];

describe('SlaCell', () => {
  it.each(labels)('renders $label as text for $status', ({ status, label }) => {
    render(<SlaCell dueAtUtc={null} slaStatus={status} />);

    expect(screen.getByText(label)).toBeInTheDocument();
  });
});
