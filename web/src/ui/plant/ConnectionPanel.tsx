import { useTranslation } from 'react-i18next';
import type { ModbusStatusMessage, WelcomeMessage } from '../../net/contracts.generated';
import { isMasterConnected } from '../../net/plantStore';

export interface ConnectionPanelProps {
  welcome: WelcomeMessage;
  modbus: ModbusStatusMessage | null;
}

/** Dados para configurar o mestre Modbus (servidor, porta, Unit ID) e a situação da conexão dele. */
export function ConnectionPanel({ welcome, modbus }: ConnectionPanelProps) {
  const { t } = useTranslation();
  const server = welcome.serverAddress ?? window.location.hostname;
  const port = modbus?.port ?? welcome.plant.modbusPort;
  const connected = isMasterConnected(modbus);
  const waiting = (modbus?.pendingRequests.length ?? 0) > 0;
  const authorized = modbus?.authorizedMasters ?? [];

  return (
    <section className="panel" aria-labelledby="modbus-heading">
      <h3 id="modbus-heading" className="panel__title">
        {t('modbus.heading')}
      </h3>
      <dl className="facts">
        <dt>{t('modbus.server')}</dt>
        <dd className="value" data-testid="modbus-server">
          {server}
        </dd>
        <dt>{t('modbus.port')}</dt>
        <dd className="value" data-testid="modbus-port">
          {port}
        </dd>
        <dt>{t('modbus.unitId')}</dt>
        <dd className="value" data-testid="modbus-unit-id">
          {modbus?.unitId ?? ''}
        </dd>
        <dt>{t('modbus.authorized')}</dt>
        <dd className="value">
          {authorized.length > 0 ? authorized.join(', ') : t('modbus.none')}
        </dd>
      </dl>
      <p
        className={connected ? 'chip' : 'chip chip--muted'}
        data-testid="master-status"
        data-connected={connected}
      >
        {connected
          ? t('modbus.masterConnected')
          : waiting
            ? t('modbus.waiting')
            : t('modbus.noMaster')}
      </p>
      <p className="panel__hint">{t('modbus.hint')}</p>
    </section>
  );
}
