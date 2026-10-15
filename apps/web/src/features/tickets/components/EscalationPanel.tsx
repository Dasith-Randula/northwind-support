import { useRef, useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { EscalateTicketResponse, TicketDetail } from '../../../types/api';

interface EscalationPanelProps {
  ticket: TicketDetail;
  onEscalated: (response: EscalateTicketResponse) => void | Promise<void>;
}

/** Collects an escalation reason and submits one request. */
export function EscalationPanel({ ticket, onEscalated }: EscalationPanelProps) {
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitError, setSubmitError] = useState<string>();
  const [isSaving, setIsSaving] = useState(false);
  const isSubmitting = useRef(false);
  const canEscalate =
    ticket.priority !== 'Critical' && ticket.status !== 'Resolved' && ticket.status !== 'Closed';

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (isSubmitting.current) {
      return;
    }

    const trimmedReason = reason.trim();
    const validationErrors: Record<string, string> = {};

    if (trimmedReason.length < 5 || trimmedReason.length > 500) {
      validationErrors.reason = 'Reason must contain between 5 and 500 non-whitespace characters.';
    }
    setErrors(validationErrors);
    setSubmitError(undefined);
    if (Object.keys(validationErrors).length > 0 || !canEscalate) {
      return;
    }

    isSubmitting.current = true;
    setIsSaving(true);

    try {
      const response = await ticketsApi.escalateTicket(ticket.id, {
        reason: trimmedReason,
      });
      await onEscalated(response);
      setReason('');
      setErrors({});
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 400) {
        setErrors((current) => ({ ...current, ...mapFieldErrors(caught.fieldErrors) }));
      }

      const fallback =
        caught instanceof ApiError && caught.status === 404
          ? 'This ticket no longer exists.'
          : caught instanceof ApiError && caught.status === 409
            ? 'This ticket can no longer be escalated.'
            : 'Could not escalate the ticket. Check your connection and try again.';
      setSubmitError(toErrorMessage(caught, fallback));
    } finally {
      isSubmitting.current = false;
      setIsSaving(false);
    }
  };

  return (
    <section className="card" aria-labelledby="escalation-panel-title">
      <h2 id="escalation-panel-title">Escalate ticket</h2>
      {!canEscalate && (
        <p className="field__hint">
          Critical, Resolved, and Closed tickets cannot be escalated.
        </p>
      )}
      <form className="form" onSubmit={submit} noValidate>
        <Field
          id="escalation-reason"
          label="Reason"
          error={errors.reason}
          hint="Enter 5–500 non-whitespace characters."
        >
          {(fieldProps) => (
            <textarea
              {...fieldProps}
              rows={3}
              maxLength={500}
              value={reason}
              disabled={isSaving || !canEscalate}
              onChange={(event) => setReason(event.target.value)}
            />
          )}
        </Field>
        {submitError && (
          <p className="field__error" role="alert">
            {submitError}
          </p>
        )}
        <button
          type="submit"
          className="button button--primary"
          disabled={isSaving || !canEscalate}
        >
          {isSaving ? 'Escalating...' : 'Escalate ticket'}
        </button>
      </form>
    </section>
  );
}

function mapFieldErrors(fieldErrors: Record<string, string[]>): Record<string, string> {
  return Object.fromEntries(
    Object.entries(fieldErrors).map(([field, messages]) => [
      field.charAt(0).toLowerCase() + field.slice(1),
      messages[0],
    ]),
  );
}
