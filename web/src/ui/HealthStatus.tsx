import { useEffect, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { fetchHealth, type HealthResponse } from '../net/health';

type HealthState = { kind: 'loading' } | { kind: 'ok'; health: HealthResponse } | { kind: 'error' };

export interface HealthStatusProps {
  /** Função que consulta o servidor; substituível nos testes. */
  load?: (signal: AbortSignal) => Promise<HealthResponse>;
}

/** Mostra a versão e a hora do servidor, lidas de GET /api/health. */
export function HealthStatus({ load = fetchHealth }: HealthStatusProps) {
  const { t, i18n } = useTranslation();
  const [state, setState] = useState<HealthState>({ kind: 'loading' });

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal).then(
      (health) => setState({ kind: 'ok', health }),
      () => {
        if (!controller.signal.aborted) {
          setState({ kind: 'error' });
        }
      },
    );
    return () => controller.abort();
  }, [load]);

  const value = <span className="panel__value" />;

  return (
    <section className="panel" aria-labelledby="health-heading">
      <h2 id="health-heading" className="panel__title">
        {t('health.heading')}
      </h2>
      {state.kind === 'loading' && <p className="panel__row">{t('health.loading')}</p>}
      {state.kind === 'error' && (
        <p role="alert" className="panel__alarm">
          {t('health.error')}
        </p>
      )}
      {state.kind === 'ok' && (
        <>
          <p className="panel__row" data-testid="health-version">
            <Trans
              i18nKey="health.version"
              values={{ version: state.health.version }}
              components={{ value }}
            />
          </p>
          <p className="panel__row" data-testid="health-time">
            <Trans
              i18nKey="health.serverTime"
              values={{
                time: new Intl.DateTimeFormat(i18n.language, {
                  dateStyle: 'short',
                  timeStyle: 'medium',
                }).format(new Date(state.health.serverTimeUtc)),
              }}
              components={{ value }}
            />
          </p>
        </>
      )}
    </section>
  );
}
