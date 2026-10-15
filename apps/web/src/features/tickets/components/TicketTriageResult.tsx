import { formatDateTime } from '../../../lib/format';
import type { TicketDetail } from '../../../types/api';

interface TicketTriageResultProps {
  ticket: TicketDetail;
}

/** Presents the priority and assignment decision returned by the create endpoint. */
export function TicketTriageResult({ ticket }: TicketTriageResultProps) {
  const triage = ticket.triage;

  return (
    <section aria-labelledby="triage-result-title">
      <h2 id="triage-result-title">Automatic triage</h2>
      <dl className="triage-result">
        <div>
          <dt>Applied priority</dt>
          <dd>{triage?.appliedPriority ?? ticket.priority}</dd>
        </div>
        <div>
          <dt>SLA due</dt>
          <dd>{formatDateTime(ticket.dueAtUtc)}</dd>
        </div>
        <div>
          <dt>Assignment</dt>
          <dd>
            {ticket.assignedAgent?.fullName ??
              (triage
                ? `Unassigned because ${triage.assignmentReason}`
                : 'Unassigned; no triage reason was returned.')}
          </dd>
        </div>
        {triage && (
          <>
            <div>
              <dt>Priority decision</dt>
              <dd>{triage.priorityReason}</dd>
            </div>
            <div>
              <dt>SLA decision</dt>
              <dd>{triage.slaReason}</dd>
            </div>
          </>
        )}
      </dl>
    </section>
  );
}
