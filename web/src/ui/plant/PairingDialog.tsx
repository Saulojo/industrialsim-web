import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import type { PairingRequestResponse } from '../../net/contracts.generated';

export interface PairingDialogProps {
  request: PairingRequestResponse;
  onAccept(): void;
  onRefuse(): void;
}

/** Pergunta se o IP do mestre pode conectar à planta (D38). */
export function PairingDialog({ request, onAccept, onRefuse }: PairingDialogProps) {
  const { t, i18n } = useTranslation();
  const acceptRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    acceptRef.current?.focus();
  }, [request.id]);

  const expires = new Intl.DateTimeFormat(i18n.language, { timeStyle: 'medium' }).format(
    new Date(request.expiresUtc),
  );

  return (
    <div className="dialog-backdrop">
      <div
        className="dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="pairing-title"
        aria-describedby="pairing-question"
        data-testid="pairing-dialog"
      >
        <h3 id="pairing-title" className="panel__title">
          {t('pairing.title')}
        </h3>
        <p id="pairing-question" className="panel__row">
          {t('pairing.question', { address: request.address })}
        </p>
        <p className="panel__hint">{t('pairing.hint', { time: expires })}</p>
        <div className="dialog__actions">
          <button type="button" className="button" onClick={onRefuse}>
            {t('pairing.refuse')}
          </button>
          <button
            ref={acceptRef}
            type="button"
            className="button button--primary"
            onClick={onAccept}
          >
            {t('pairing.accept')}
          </button>
        </div>
      </div>
    </div>
  );
}
