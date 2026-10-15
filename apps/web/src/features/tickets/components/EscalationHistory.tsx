import { EmptyState } from '../../../components/EmptyState';
import { ErrorMessage } from '../../../components/ErrorMessage';
import { Spinner } from '../../../components/Spinner';
import { formatDateTime } from '../../../lib/format';
import type { TicketEscalation } from '../../../types/api';

interface EscalationHistoryProps {
  entries?: TicketEscalation[];
  isLoading: boolean;
  error?: string;
  onRetry: () => void;
}

/** Shows the immutable before-and-after record for each escalation. */
export function EscalationHistory({
  entries,
  isLoading,
  error,
  onRetry,
}: EscalationHistoryProps) {
  return (
    <section className="card" aria-labelledby="escalation-history-title">
      <h2 id="escalation-history-title">Escalation history</h2>

      {isLoading && <Spinner label="Loading escalation history" />}
      {error && <ErrorMessage message={error} onRetry={onRetry} />}
      {!isLoading && !error && entries?.length === 0 && (
        <EmptyState
          title="No escalations yet"
          description="Changes made through escalation will appear here."
        />
      )}
      {!isLoading && !error && entries && entries.length > 0 && (
        <ol className="escalation-history">
          {entries.map((entry) => (
            <li className="escalation-history__entry" key={entry.id}>
              <h3>
                {entry.fromPriority} → {entry.toPriority}
              </h3>
              <dl className="escalation-history__details">
                <div>
                  <dt>Assigned agent</dt>
                  <dd>
                    {entry.fromAgent?.fullName ?? 'Unassigned'} →{' '}
                    {entry.toAgent?.fullName ?? 'Unassigned'}
                  </dd>
                </div>
                <div>
                  <dt>SLA due</dt>
                  <dd>
                    {formatDateTime(entry.fromDueAtUtc)} → {formatDateTime(entry.toDueAtUtc)}
                  </dd>
                </div>
                <div>
                  <dt>Reason</dt>
                  <dd>{entry.reason}</dd>
                </div>
                <div>
                  <dt>Escalated by</dt>
                  <dd>{entry.escalatedBy}</dd>
                </div>
                <div>
                  <dt>When</dt>
                  <dd>{formatDateTime(entry.escalatedAtUtc)}</dd>
                </div>
              </dl>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
