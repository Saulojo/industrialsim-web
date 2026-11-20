import { useCallback, useEffect, useState, useSyncExternalStore } from 'react';
import { useTranslation } from 'react-i18next';
import { ioMapCsvUrl, recordingUrl } from '../../net/api';
import { PlantStore, type PlantStoreOptions } from '../../net/plantStore';
import { formatNumber, socketErrorText, translateDynamic } from '../format';
import { ioMapPath, navigate } from '../navigation';
import { AlarmPanel } from './AlarmPanel';
import { CaldeiraSynoptic } from './CaldeiraSynoptic';
import { ConnectionPanel } from './ConnectionPanel';
import { DisturbancePanel } from './DisturbancePanel';
import { ForcedPanel } from './ForcedPanel';
import { LogPanel } from './LogPanel';
import { MastersPanel } from './MastersPanel';
import { MonitorPanel } from './MonitorPanel';
import { PairingDialog } from './PairingDialog';
import { SettingsPanel } from './SettingsPanel';
import { SignalTable } from './SignalTable';
import { TrendChart } from './TrendChart';

export interface PlantPageProps {
  plantId: string;
  /** Opções do cliente (os testes trocam o WebSocket). */
  storeOptions?: PlantStoreOptions;
}

/** Sinais mostrados na tendência, quando o cenário os tem. */
const trendSignals = ['LT-101', 'PT-101', 'FT-101', 'FT-102'];

/** Seções da página da planta; a escolhida fica no fragmento da URL (#monitor, #log…). */
const TABS = ['process', 'communication', 'monitor', 'log'] as const;
export type PlantTab = (typeof TABS)[number];

function tabFromHash(): PlantTab {
  const hash = window.location.hash.replace('#', '');
  return (TABS as readonly string[]).includes(hash) ? (hash as PlantTab) : 'process';
}

/**
 * IHM de uma planta: na seção do processo, sinóptico, tendências, alarmes, distúrbios e valores forçados; na
 * de comunicação, as configurações e os mestres; o monitor de registradores e o log de comunicação (Etapa 8).
 */
export function PlantPage({ plantId, storeOptions }: PlantPageProps) {
  const { t, i18n } = useTranslation();
  const [store] = useState(() => new PlantStore(plantId, storeOptions));
  const view = useSyncExternalStore(store.subscribe, store.getSnapshot);
  const [tab, setTab] = useState<PlantTab>(tabFromHash);
  const subscribeMonitor = useCallback((enabled: boolean) => store.setMonitor(enabled), [store]);
  const release = useCallback(
    (area: string, signal: string) => store.releaseInput(area, signal),
    [store],
  );

  useEffect(() => {
    store.start();
    return () => store.stop();
  }, [store]);

  const { welcome, state, status } = view;
  const scenarioId = welcome?.plant.scenarioId ?? '';
  const fatal = status === 'failed' || status === 'incompatible';
  const connected = status === 'connected';
  const paused = state?.status === 'paused';
  const pending = view.modbus?.pendingRequests[0] ?? null;
  const replaces =
    view.settings?.settings.masterPolicy === 'single' ? (view.modbus?.authorizedMasters ?? []) : [];

  function chooseTab(next: PlantTab) {
    setTab(next);
    window.history.replaceState(null, '', `#${next}`);
  }

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
          className={`chip ${connected ? '' : 'chip--abnormal'}`}
          data-testid="connection-status"
          data-status={status}
          role="status"
        >
          {t(`connection.status.${status}`)}
        </span>
        {state && (
          <>
            <span className="plant__fact">
              {t('plant.statusLabel')}:{' '}
              <span
                className={paused ? 'chip chip--abnormal' : ''}
                data-testid="plant-status"
                data-status={state.status}
              >
                {t(`plant.status.${state.status as 'running'}`)}
              </span>
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
          <a className="button" href={ioMapCsvUrl(plantId)} download data-testid="io-map-csv">
            {t('plant.ioMapCsv')}
          </a>
          <a
            className="button"
            href={ioMapPath(plantId)}
            target="_blank"
            rel="noopener"
            data-testid="io-map-pdf"
          >
            {t('plant.ioMapPdf')}
          </a>
          <button
            type="button"
            className="button"
            disabled={!connected || !state}
            data-testid="pause"
            onClick={() => (paused ? store.resume() : store.pause())}
          >
            {paused ? t('plant.resume') : t('plant.pause')}
          </button>
          <button type="button" className="button" disabled={!connected} onClick={restart}>
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
      {paused && (
        <p role="status" className="panel__alarm" data-testid="paused-notice">
          {t('plant.pausedNotice')}
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
        <>
          <div className="tabs" role="tablist" aria-label={t('plant.tabsLabel')}>
            {TABS.map((name) => (
              <button
                key={name}
                type="button"
                role="tab"
                id={`tab-${name}`}
                aria-selected={tab === name}
                aria-controls={`panel-${name}`}
                className={tab === name ? 'tabs__tab tabs__tab--active' : 'tabs__tab'}
                data-testid={`tab-${name}`}
                onClick={() => chooseTab(name)}
              >
                {t(`plant.tabs.${name}`)}
              </button>
            ))}
          </div>

          <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`}>
            {tab === 'process' && (
              <div className="plant__grid">
                <div className="stack">
                  {scenarioId === 'caldeira' ? (
                    <CaldeiraSynoptic welcome={welcome} state={state} forced={view.forced} />
                  ) : (
                    <SignalTable welcome={welcome} state={state} forced={view.forced} />
                  )}
                  <TrendChart
                    welcome={welcome}
                    trend={view.trend}
                    signals={trendSignals.filter((name) =>
                      welcome.outputs.some((o) => o.name === name),
                    )}
                  />
                </div>
                <div className="stack">
                  <ForcedPanel
                    welcome={welcome}
                    forced={view.forced}
                    disabled={!connected}
                    onRelease={release}
                  />
                  <ConnectionPanel welcome={welcome} modbus={view.modbus} />
                  <AlarmPanel scenarioId={scenarioId} alarms={view.alarms} />
                  <DisturbancePanel
                    welcome={welcome}
                    state={state}
                    disabled={!connected}
                    onChange={(input, value) => store.setDisturbance(input, value)}
                  />
                </div>
              </div>
            )}

            {tab === 'communication' && (
              <div className="plant__grid">
                {view.settings && (
                  <SettingsPanel
                    welcome={welcome}
                    modbus={view.modbus}
                    settings={view.settings}
                    disabled={!connected}
                    onApply={(changes) => store.updateSettings(changes)}
                  />
                )}
                <MastersPanel
                  modbus={view.modbus}
                  disabled={!connected}
                  onRevoke={(address) => store.revokeMaster(address)}
                />
              </div>
            )}

            {tab === 'monitor' && (
              <MonitorPanel
                scenarioId={scenarioId}
                layout={view.monitorLayout}
                monitor={view.monitor}
                writes={view.monitorWrites}
                paused={paused}
                disabled={!connected}
                onSubscribe={subscribeMonitor}
                onForce={(area, signal, value) => store.forceInput(area, signal, value)}
                onRelease={release}
              />
            )}

            {tab === 'log' && <LogPanel scenarioId={scenarioId} log={view.log} />}
          </div>
        </>
      )}

      {pending && connected && (
        <PairingDialog
          request={pending}
          replaces={replaces}
          onAccept={() => store.acceptPairing(pending.id)}
          onRefuse={() => store.refusePairing(pending.id)}
        />
      )}
    </div>
  );
}
