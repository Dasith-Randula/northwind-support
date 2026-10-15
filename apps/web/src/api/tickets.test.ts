import { afterEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from './tickets';
import { page } from '../test/fixtures';
import { emptyFilters } from '../features/tickets/hooks/useTicketList';

describe('ticketsApi', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('sends the SLA status with the other ticket filters', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(page([])), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await ticketsApi.getTickets({ ...emptyFilters, status: 'New', slaStatus: 'AtRisk' });

    const requestedUrl = new URL(String(fetchMock.mock.calls[0]?.[0]), 'http://localhost');
    expect(requestedUrl.pathname).toBe('/api/tickets');
    expect(requestedUrl.searchParams.get('status')).toBe('New');
    expect(requestedUrl.searchParams.get('slaStatus')).toBe('AtRisk');
  });
});
