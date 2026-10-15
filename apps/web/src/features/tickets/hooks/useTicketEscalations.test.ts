import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketEscalation } from '../../../test/fixtures';
import { useTicketEscalations } from './useTicketEscalations';

const mocks = vi.hoisted(() => ({ getEscalations: vi.fn() }));

vi.mock('../../../api/tickets', () => ({
  ticketsApi: { getEscalations: mocks.getEscalations },
}));

describe('useTicketEscalations', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getEscalations.mockResolvedValue([ticketEscalation()]);
  });

  it('loads a ticket history and can refresh it after an escalation', async () => {
    const { result } = renderHook(() => useTicketEscalations(12));
    await waitFor(() => expect(result.current.data).toHaveLength(1));
    expect(mocks.getEscalations).toHaveBeenCalledWith(12);

    await act(async () => {
      await result.current.reload();
    });

    expect(mocks.getEscalations).toHaveBeenCalledTimes(2);
  });
});
