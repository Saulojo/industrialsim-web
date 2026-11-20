import { useTranslation } from 'react-i18next';
import type { LogEntryInfo } from '../../net/contracts.generated';
import { describeLogEntry, logArgument } from '../logText';

/** Argumentos do evento mostrados em colunas próprias. */
const AUTHOR = 'author';
const SETTING = 'setting';

export interface LogPanelProps {
  scenarioId: string;
  log: readonly LogEntryInfo[];
}

/**
 * Log de comunicação (RF05): conexões, pareamentos, exceções Modbus, mudanças de configuração, valores
 * forçados, pausa e reinício, do mais novo para o mais antigo. Eventos repetidos aparecem uma vez, com a
 * contagem.
 */
export function LogPanel({ scenarioId, log }: LogPanelProps) {
  const { t, i18n } = useTranslation();
  const time = new Intl.DateTimeFormat(i18n.language, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
  const entries = [...log].reverse();

  return (
    <section className="panel" aria-labelledby="log-heading" data-testid="log">
      <h3 id="log-heading" className="panel__title">
        {t('log.heading')}
      </h3>
      {entries.length === 0 ? (
        <p className="panel__hint">{t('log.empty')}</p>
      ) : (
        <table className="log">
          <thead>
            <tr>
              <th scope="col">{t('log.columns.time')}</th>
              <th scope="col">{t('log.columns.event')}</th>
              <th scope="col">{t('log.columns.author')}</th>
            </tr>
          </thead>
          <tbody>
            {entries.map((entry) => (
              <tr
                key={entry.id}
                className={
                  entry.kind === 'modbus-exception' ? 'log__row log__row--abnormal' : 'log__row'
                }
                data-testid="log-entry"
                data-kind={entry.kind}
                data-setting={logArgument(entry, SETTING)}
              >
                <td className="value">{time.format(new Date(entry.timeUtc))}</td>
                <td>
                  {describeLogEntry(t, i18n.language, scenarioId, entry)}
                  {entry.count > 1 && (
                    <span className="chip chip--muted log__count">
                      {t('log.count', { count: entry.count })}
                    </span>
                  )}
                </td>
                <td className="value">{logArgument(entry, AUTHOR)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
