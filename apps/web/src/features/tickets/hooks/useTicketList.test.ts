import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { page, ticketListItem } from '../../../test/fixtures';
import { useTicketList } from './useTicketList';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    getTickets: vi.fn(),
  },
}));

const getTickets = vi.mocked(ticketsApi.getTickets);

describe('useTicketList', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    getTickets.mockResolvedValue(page([ticketListItem()]));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('searches with the text the user typed, once the typing settles', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });

    // Still inside the debounce window: no second request yet.
    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'invoice', page: 1 }),
    );

    expect(result.current.isLoading).toBe(false);
    expect(result.current.data?.items).toHaveLength(1);
  });

  it('requests immediately when the status filter changes', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    getTickets.mockClear();

    act(() => {
      result.current.updateFilters({ status: 'InProgress' });
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'InProgress' }),
    );
  });

  it.each([
    ['priority', { priority: 'Critical' }, { priority: 'Critical' }],
    ['category', { categoryId: 5 }, { categoryId: 5 }],
    ['customer', { customerId: 3 }, { customerId: 3 }],
    ['assigned agent', { assignedAgentId: 4 }, { assignedAgentId: 4 }],
    ['unassigned-only', { unassignedOnly: true }, { unassignedOnly: true }],
    ['SLA status', { slaStatus: 'AtRisk' }, { slaStatus: 'AtRisk' }],
    ['sort', { sortBy: 'priority', sortDirection: 'asc' }, { sortBy: 'priority', sortDirection: 'asc' }],
  ] as const)('requests immediately when the %s filter changes', async (_name, patch, expected) => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    getTickets.mockClear();

    act(() => {
      result.current.updateFilters(patch);
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining(expected));
  });

  it('preserves selected filters when a debounced search changes', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    act(() => {
      result.current.updateFilters({ status: 'Resolved' });
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    getTickets.mockClear();

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });

    expect(getTickets).not.toHaveBeenCalled();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'invoice', status: 'Resolved' }),
    );
  });

  it('uses the latest search and filter values when a filter changes during debounce', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    getTickets.mockClear();

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });

    act(() => {
      result.current.updateFilters({ status: 'Resolved' });
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'invoice', status: 'Resolved' }),
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);
  });

  it('refreshes with default filters when filters are cleared', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    act(() => {
      result.current.updateFilters({ status: 'Resolved', search: 'invoice' });
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    getTickets.mockClear();

    act(() => {
      result.current.resetFilters();
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({
        search: '',
        status: undefined,
        page: 1,
        sortBy: 'createdAtUtc',
        sortDirection: 'desc',
      }),
    );
  });

  it('combines the SLA filter with existing filters and resets pagination', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    act(() => {
      result.current.updateFilters({ status: 'New', page: 4 });
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    getTickets.mockClear();

    act(() => {
      result.current.updateFilters({ slaStatus: 'AtRisk' });
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'New', slaStatus: 'AtRisk', page: 1 }),
    );
  });
});
