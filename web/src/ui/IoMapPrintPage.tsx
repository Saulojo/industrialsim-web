import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiRequestError, fetchIoMap } from '../net/api';
import type { IoMapResponse, IoMapRow } from '../net/contracts.generated';
import { apiErrorText, signalLabel, translateDynamic } from './format';
import { navigate, plantPath } from './navigation';

export interface IoMapPrintPageProps {
  plantId: string;
}

/**
 * Mapa de I/O da planta numa página própria para imprimir ou salvar em PDF pelo navegador (D45): a mesma
 * lista do CSV, com os nomes traduzidos, sem os controles da IHM na impressão.
 */
export function IoMapPrintPage({ plantId }: IoMapPrintPageProps) {
  const { t, i18n } = useTranslation();
  const [map, setMap] = useState<IoMapResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const abort = new AbortController();
    fetchIoMap(plantId, abort.signal)
      .then(setMap)
      .catch((e: unknown) => {
        if (abort.signal.aborted) return;
        setError(e instanceof ApiRequestError ? e.code : 'network');
      });
    return () => abort.abort();
  }, [plantId]);

  const number = new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 6 });
  const range = (row: IoMapRow) =>
    row.lrv === null || row.urv === null
      ? ''
      : `${number.format(row.lrv)} – ${number.format(row.urv)}`;
  const raw = (row: IoMapRow) => (row.rawMin === null ? '' : `${row.rawMin} – ${row.rawMax}`);

  return (
    <div className="app app--wide io-map-page">
      <p className="panel__row no-print">
        <button type="button" className="button" onClick={() => navigate(plantPath(plantId))}>
          {t('ioMap.back')}
        </button>{' '}
        <button
          type="button"
          className="button button--primary"
          disabled={!map}
          data-testid="io-map-print"
          onClick={() => window.print()}
        >
          {t('ioMap.print')}
        </button>
      </p>
      {error && (
        <p role="alert" className="panel__alarm">
          {apiErrorText(t, error)}
        </p>
      )}
      {!map && !error && <p className="panel__hint">{t('ioMap.loading')}</p>}
      {map && (
        <>
          <h1 className="app__title">
            {t('ioMap.heading', {
              scenario: translateDynamic(t, `scenarios.names.${map.scenarioId}`, map.scenarioId),
            })}
          </h1>
          <p className="app__subtitle" data-testid="io-map-facts">
            {t('ioMap.facts', {
              format: translateDynamic(t, `settings.values.format.${map.format}`, map.format),
              wordOrder: translateDynamic(
                t,
                `settings.values.wordOrder.${map.wordOrder}`,
                map.wordOrder,
              ),
              unitId: map.unitId,
              port: map.port,
            })}
          </p>
          <table className="io-map" data-testid="io-map-table">
            <thead>
              <tr>
                <th scope="col">{t('ioMap.columns.area')}</th>
                <th scope="col">{t('ioMap.columns.address')}</th>
                <th scope="col">{t('ioMap.columns.classic')}</th>
                <th scope="col">{t('ioMap.columns.length')}</th>
                <th scope="col">{t('ioMap.columns.signal')}</th>
                <th scope="col">{t('ioMap.columns.access')}</th>
                <th scope="col">{t('ioMap.columns.dataType')}</th>
                <th scope="col">{t('ioMap.columns.raw')}</th>
                <th scope="col">{t('ioMap.columns.range')}</th>
                <th scope="col">{t('ioMap.columns.unit')}</th>
              </tr>
            </thead>
            <tbody>
              {map.rows.map((row) => (
                <tr
                  key={`${row.area}-${row.address}`}
                  data-testid="io-map-row"
                  data-area={row.area}
                  data-address={row.address}
                  data-signal={row.signal}
                >
                  <td>{translateDynamic(t, `monitor.areas.${row.area}`, row.area)}</td>
                  <td className="value">{row.address}</td>
                  <td className="value">{row.classicAddress}</td>
                  <td className="value">{row.length}</td>
                  <td>{signalLabel(t, map.scenarioId, row.signal)}</td>
                  <td>{translateDynamic(t, `ioMap.access.${row.access}`, row.access)}</td>
                  <td className="value">{row.dataType}</td>
                  <td className="value">{raw(row)}</td>
                  <td className="value">{range(row)}</td>
                  <td>{row.unit}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </div>
  );
}
