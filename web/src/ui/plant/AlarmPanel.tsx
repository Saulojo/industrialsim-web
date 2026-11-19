import { useTranslation } from 'react-i18next';
import type { ActiveAlarm } from '../../net/contracts.generated';
import { formatNumber, signalLabel } from '../format';

export interface AlarmPanelProps {
  scenarioId: string;
  alarms: readonly ActiveAlarm[];
}

/** Alarmes ativos. ISA-101: a cor de alarme só aparece quando há alarme. */
export function AlarmPanel({ scenarioId, alarms }: AlarmPanelProps) {
  const { t, i18n } = useTranslation();

  return (
    <section className="panel" aria-labelledby="alarms-heading">
      <h3 id="alarms-heading" className="panel__title">
        {t('alarms.heading')}
      </h3>
      {alarms.length === 0 ? (
        <p className="panel__row" data-testid="alarms-none">
          {t('alarms.none')}
        </p>
      ) : (
        <ul className="alarm-list" role="alert">
          {alarms.map((alarm) => (
            <li key={alarm.signal} className="alarm-list__item" data-alarm={alarm.signal}>
              <span>{signalLabel(t, scenarioId, alarm.signal)}</span>
              <span className="alarm-list__since">
                {t('alarms.since', { time: formatNumber(i18n.language, alarm.sinceTime) })}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
