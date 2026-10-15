import { useCallback } from 'react';
import { ticketsApi } from '../../../api/tickets';
import { useAsyncData } from '../../../hooks/useAsyncData';

/** Loads the history for one ticket and exposes a reload for post-escalation refreshes. */
export function useTicketEscalations(ticketId: number, enabled = true) {
  const load = useCallback(
    () => (enabled ? ticketsApi.getEscalations(ticketId) : Promise.resolve([])),
    [enabled, ticketId],
  );

  return useAsyncData(load, 'Unable to load escalation history.');
}
