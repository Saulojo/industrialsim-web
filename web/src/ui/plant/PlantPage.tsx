import { useEffect, useState, useSyncExternalStore } from 'react';
import { useTranslation } from 'react-i18next';
import { recordingUrl } from '../../net/api';
import { PlantStore, type PlantStoreOptions } from '../../net/plantStore';
import { formatNumber, socketErrorText, translateDynamic } from '../format';
import { navigate } from '../navigation';
import { AlarmPanel } from './AlarmPanel';
import { CaldeiraSynoptic } from './CaldeiraSynoptic';
import { ConnectionPanel } from './ConnectionPanel';
import { DisturbancePanel } from './DisturbancePanel';
import { PairingDialog } from './PairingDialog';
import { SignalTable } from './SignalTable';
import { TrendChart } from './TrendChart';

export interface PlantPageProps {
  plantId: string;
  /** Opções do cliente (os testes trocam o WebSocket). */
  storeOptions?: PlantStoreOptions;
}

/** Sinais mostrados na tendência, quando o cenário os tem. */
const trendSignals = ['LT-101', 'PT-101', 'FT-101', 'FT-102'];

/** IHM de uma planta: conexão, pareamento, sinóptico, tendências, alarmes, distúrbios e ensaio. */
export function PlantPage({ plantId, storeOptions }: PlantPageProps) {
  const { t, i18n } = useTranslation();
  const [store] = useState(() => new PlantStore(plantId, storeOptions));
  const view = useSyncExternalStore(store.subscribe, store.getSnapshot);

  useEffect(() => {
    store.start();
    return () => store.stop();
  }, [store]);

  const { welcome, state, status } = view;
  const scenarioId = welcome?.plant.scenarioId ?? '';
  const fatal = status === 'failed' || status === 'incompatible';
  const pending = view.modbus?.pendingRequests[0] ?? null;

  function restart() {
    if (window.confirm(t('plant.restartConfirm'))) {
      store.restart();
    }
  }

  return (
    <div className="plant" data-plant-id={plantId}>
      <div className="plant__bar">
        <h2 className="plant__title">
          {t('plant.heading', {
            scenario: translateDynamic(t, `scenarios.names.${scenarioId}`, scenarioId),
          })}
        </h2>
        <span
          className={`chip ${status === 'connected' ? '' : 'chip--abnormal'}`}
          data-testid="connection-status"
          data-status={status}
          role="status"
        >
          {t(`connection.status.${status}`)}
        </span>
        {state && (
          <>
            <span className="plant__fact">
              {t('plant.statusLabel')}: {t(`plant.status.${state.status as 'running'}`)}
            </span>
            <span className="plant__fact">
              {t('plant.simulatedTime')}:{' '}
              <span className="value" data-testid="simulated-time" data-value={state.time}>
                {t('plant.seconds', { value: formatNumber(i18n.language, state.time) })}
              </span>
            </span>
            <span className="plant__fact">
              {t('plant.run')}:{' '}
              <span className="value" data-testid="run" data-value={state.run}>
                {state.run}
              </span>
            </span>
          </>
        )}
        <span className="plant__actions">
          <a className="button" href={recordingUrl(plantId)} download data-testid="download-csv">
            {t('plant.downloadCsv')}
          </a>
          <button
            type="button"
            className="button"
            disabled={status !== 'connected'}
            onClick={restart}
          >
            {t('plant.restart')}
          </button>
        </span>
      </div>

      {view.incompatible && (
        <p role="alert" className="panel__alarm" data-testid="incompatible">
          {t('connection.incompatible', {
            client: view.incompatible.clientProtocolVersion,
            server: view.incompatible.serverProtocolVersion,
          })}
        </p>
      )}
      {view.errorCode && (
        <p role="alert" className="panel__alarm" data-testid="socket-error">
          {socketErrorText(t, view.errorCode)}
        </p>
      )}
      {fatal && (
        <p className="panel__row">
          <button type="button" className="button" onClick={() => navigate('/')}>
            {t('connection.backHome')}
          </button>
        </p>
      )}

      {welcome && state && (
        <div className="plant__grid">
          <div className="stack">
            {scenarioId === 'caldeira' ? (
              <CaldeiraSynoptic welcome={welcome} state={state} />
            ) : (
              <SignalTable welcome={welcome} state={state} />
            )}
            <TrendChart
              welcome={welcome}
              trend={view.trend}
              signals={trendSignals.filter((name) => welcome.outputs.some((o) => o.name === name))}
            />
          </div>
          <div className="stack">
            <ConnectionPanel welcome={welcome} modbus={view.modbus} />
            <AlarmPanel scenarioId={scenarioId} alarms={view.alarms} />
            <DisturbancePanel
              welcome={welcome}
              state={state}
              disabled={status !== 'connected'}
              onChange={(input, value) => store.setDisturbance(input, value)}
            />
          </div>
        </div>
      )}

      {pending && status === 'connected' && (
        <PairingDialog
          request={pending}
          onAccept={() => store.acceptPairing(pending.id)}
          onRefuse={() => store.refusePairing(pending.id)}
        />
      )}
    </div>
  );
}
