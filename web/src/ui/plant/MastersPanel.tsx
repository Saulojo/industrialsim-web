import { useTranslation } from 'react-i18next';
import type { ModbusStatusMessage } from '../../net/contracts.generated';

export interface MastersPanelProps {
  modbus: ModbusStatusMessage | null;
  disabled: boolean;
  onRevoke: (address: string) => void;
}

/** IPs autorizados (com "Retirar"), conexões abertas e contadores de requisições do Modbus da planta. */
export function MastersPanel({ modbus, disabled, onRevoke }: MastersPanelProps) {
  const { t, i18n } = useTranslation();
  const rate = new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 1 });
  const authorized = modbus?.authorizedMasters ?? [];
  const connections = modbus?.connections ?? [];

  function revoke(address: string) {
    if (window.confirm(t('masters.revokeConfirm', { address }))) onRevoke(address);
  }

  return (
    <section className="panel" aria-labelledby="masters-heading">
      <h3 id="masters-heading" className="panel__title">
        {t('masters.heading')}
      </h3>
      <dl className="facts">
        <dt>{t('masters.requestsPerSecond')}</dt>
        <dd className="value" data-testid="modbus-rate" data-value={modbus?.requestsPerSecond ?? 0}>
          {rate.format(modbus?.requestsPerSecond ?? 0)}
        </dd>
        <dt>{t('masters.requests')}</dt>
        <dd className="value">{modbus?.requests ?? 0}</dd>
        <dt>{t('masters.exceptions')}</dt>
        <dd className="value">{modbus?.exceptionResponses ?? 0}</dd>
      </dl>

      <h4 className="panel__subtitle">{t('masters.authorized')}</h4>
      {authorized.length === 0 ? (
        <p className="panel__hint">{t('masters.none')}</p>
      ) : (
        <ul className="plain-list">
          {authorized.map((address) => (
            <li key={address} className="plain-list__item" data-testid={`master-${address}`}>
              <span className="value">{address}</span>
              <button
                type="button"
                className="button"
                disabled={disabled}
                onClick={() => revoke(address)}
              >
                {t('masters.revoke')}
              </button>
            </li>
          ))}
        </ul>
      )}

      <h4 className="panel__subtitle">{t('masters.connections')}</h4>
      {connections.length === 0 ? (
        <p className="panel__hint">{t('masters.none')}</p>
      ) : (
        <ul className="plain-list">
          {connections.map((c) => (
            <li key={c.id} className="plain-list__item">
              <span className="value">
                {c.address}:{c.port}
              </span>
              {!c.authorized && <span className="chip chip--muted">{t('masters.pending')}</span>}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
