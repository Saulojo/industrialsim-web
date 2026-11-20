import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ModbusAreas,
  MonitorPointKinds,
  type MonitorLayoutMessage,
  type MonitorMessage,
  type MonitorPointInfo,
  type MonitorWriteInfo,
} from '../../net/contracts.generated';
import { formatNumber, signalLabel, translateDynamic } from '../format';

/** Notação dos endereços no monitor, escolhida por usuário (descritivo: "Notação de endereços exibida"). */
export type AddressNotation = 'base0' | 'classic';

const NOTATION_KEY = 'industrialsim.addressNotation';
const NOTATIONS: readonly AddressNotation[] = ['base0', 'classic'];

/** Notação guardada no navegador; base 0 se não houver (ou se o armazenamento estiver bloqueado). */
export function loadNotation(): AddressNotation {
  try {
    return window.localStorage.getItem(NOTATION_KEY) === 'classic' ? 'classic' : 'base0';
  } catch {
    return 'base0';
  }
}

function saveNotation(notation: AddressNotation): void {
  try {
    window.localStorage.setItem(NOTATION_KEY, notation);
  } catch {
    // Sem armazenamento: a escolha vale só nesta página.
  }
}

/** Valor bruto de uma linha, como o mestre o vê: bit, `INT`, `UINT` ou as duas palavras do `float32`. */
export function rawText(point: MonitorPointInfo, words: readonly number[]): string {
  if (point.kind === MonitorPointKinds.digital || point.kind === MonitorPointKinds.watchdogStatus) {
    return String(words[0] ?? 0);
  }
  if (point.length === 2) {
    return words.map((w) => `0x${w.toString(16).toUpperCase().padStart(4, '0')}`).join(' ');
  }
  const word = words[0] ?? 0;
  return String(
    point.kind === MonitorPointKinds.heartbeat || word < 0x8000 ? word : word - 0x10000,
  );
}

/** Se o mestre lê este ponto e a IHM pode forçá-lo (entradas discretas e medições analógicas). */
export function isForceable(point: MonitorPointInfo): boolean {
  return (
    (point.area === ModbusAreas.discreteInput &&
      (point.kind === MonitorPointKinds.digital ||
        point.kind === MonitorPointKinds.watchdogStatus)) ||
    (point.area === ModbusAreas.inputRegister && point.kind === MonitorPointKinds.analog)
  );
}

export interface MonitorPanelProps {
  scenarioId: string;
  layout: MonitorLayoutMessage | null;
  monitor: MonitorMessage | null;
  writes: readonly MonitorWriteInfo[];
  paused: boolean;
  disabled: boolean;
  /** Liga o monitor ao montar e desliga ao desmontar. */
  onSubscribe: (enabled: boolean) => void;
  onForce: (area: string, signal: string, value: number) => void;
  onRelease: (area: string, signal: string) => void;
}

const AREAS = [
  ModbusAreas.coil,
  ModbusAreas.discreteInput,
  ModbusAreas.holdingRegister,
  ModbusAreas.inputRegister,
];

/**
 * Monitor de registradores (RF05): cada ponto do mapa ao vivo, com valor bruto, valor de engenharia, unidade e
 * a última escrita (IP do mestre e horário), em base 0 ou na notação clássica. Entradas podem ser forçadas e
 * liberadas daqui; um ponto forçado aparece destacado.
 */
export function MonitorPanel(props: MonitorPanelProps) {
  const { scenarioId, layout, monitor, writes, paused, disabled, onSubscribe } = props;
  const { t, i18n } = useTranslation();
  const [notation, setNotation] = useState<AddressNotation>(loadNotation);

  useEffect(() => {
    onSubscribe(true);
    return () => onSubscribe(false);
  }, [onSubscribe]);

  const chooseNotation = (value: AddressNotation) => {
    setNotation(value);
    saveNotation(value);
  };

  const time = new Intl.DateTimeFormat(i18n.language, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });

  // Posição de cada linha nos valores brutos (cada linha ocupa `length` palavras).
  const offsets: number[] = [];
  let offset = 0;
  for (const point of layout?.points ?? []) {
    offsets.push(offset);
    offset += point.length;
  }

  return (
    <section className="panel" aria-labelledby="monitor-heading" data-testid="monitor">
      <div className="panel__heading-row">
        <h3 id="monitor-heading" className="panel__title">
          {t('monitor.heading')}
        </h3>
        {paused && (
          <span className="chip chip--abnormal" data-testid="monitor-paused" role="status">
            {t('monitor.paused')}
          </span>
        )}
        <fieldset className="segmented">
          <legend className="segmented__legend">{t('monitor.notation')}</legend>
          {NOTATIONS.map((value) => (
            <label key={value} className="segmented__option">
              <input
                type="radio"
                name="address-notation"
                value={value}
                checked={notation === value}
                data-testid={`notation-${value}`}
                onChange={() => chooseNotation(value)}
              />
              {t(`monitor.${value}`)}
            </label>
          ))}
        </fieldset>
      </div>

      {!layout || !monitor ? (
        <p className="panel__hint">{t('monitor.waiting')}</p>
      ) : (
        <table className="monitor" data-step={monitor.step}>
          <thead>
            <tr>
              <th scope="col">{t('monitor.columns.address')}</th>
              <th scope="col">{t('monitor.columns.signal')}</th>
              <th scope="col">{t('monitor.columns.raw')}</th>
              <th scope="col">{t('monitor.columns.value')}</th>
              <th scope="col">{t('monitor.columns.lastWrite')}</th>
              <th scope="col">{t('monitor.columns.force')}</th>
            </tr>
          </thead>
          {AREAS.map((area) => {
            const rows = layout.points
              .map((point, index) => ({ point, index }))
              .filter((r) => r.point.area === area);
            if (rows.length === 0) return null;
            return (
              <tbody key={area}>
                <tr>
                  <th scope="rowgroup" colSpan={6} className="monitor__area">
                    {translateDynamic(t, `monitor.areas.${area}`, area)}
                  </th>
                </tr>
                {rows.map(({ point, index }) => {
                  const start = offsets[index] ?? 0;
                  const words = monitor.raw.slice(start, start + point.length);
                  const value = monitor.values[index] ?? 0;
                  const forced = monitor.forced.includes(index);
                  const write = writes.find((w) => w.point === index);
                  const address = notation === 'classic' ? point.classicAddress : point.address;
                  const digital =
                    point.kind === MonitorPointKinds.digital ||
                    point.kind === MonitorPointKinds.watchdogStatus;
                  return (
                    <tr
                      key={`${point.area}-${point.address}`}
                      className={forced ? 'monitor__row monitor__row--forced' : 'monitor__row'}
                      data-testid={`monitor-${point.area}-${point.signal}-${point.kind}`}
                      data-raw={words.join(',')}
                      data-value={value}
                      data-forced={forced}
                      data-author={write?.author ?? ''}
                    >
                      <td className="value">
                        {address}
                        {point.length === 2 && `–${address + 1}`}
                      </td>
                      <td>{signalLabel(t, scenarioId, point.signal)}</td>
                      <td className="value">{rawText(point, words)}</td>
                      <td className="value">
                        {digital ? String(value) : formatNumber(i18n.language, value)}
                        {point.unit && !digital && ` ${point.unit}`}
                        {forced && (
                          <span className="chip chip--forced monitor__flag">
                            {t('monitor.forced')}
                          </span>
                        )}
                      </td>
                      <td className="monitor__write">
                        {write &&
                          t('monitor.writtenBy', {
                            author: write.author,
                            time: time.format(new Date(write.timeUtc)),
                          })}
                      </td>
                      <td>
                        {isForceable(point) && (
                          <ForceControl
                            point={point}
                            current={value}
                            forced={forced}
                            disabled={disabled}
                            onForce={(v) => props.onForce(point.area, point.signal, v)}
                            onRelease={() => props.onRelease(point.area, point.signal)}
                          />
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            );
          })}
        </table>
      )}
    </section>
  );
}

interface ForceControlProps {
  point: MonitorPointInfo;
  current: number;
  forced: boolean;
  disabled: boolean;
  onForce: (value: number) => void;
  onRelease: () => void;
}

/** Forçar (com o valor a forçar) ou liberar uma entrada. */
function ForceControl({ point, current, forced, disabled, onForce, onRelease }: ForceControlProps) {
  const { t, i18n } = useTranslation();
  const digital = point.area === ModbusAreas.discreteInput;
  const [text, setText] = useState('');

  if (forced) {
    return (
      <button
        type="button"
        className="button"
        disabled={disabled}
        data-testid={`release-${point.signal}`}
        onClick={onRelease}
      >
        {t('monitor.release')}
      </button>
    );
  }

  const parsed = digital
    ? Number(text || (current >= 0.5 ? '0' : '1'))
    : Number(text.replace(',', '.'));
  const valid = (digital || text.trim() !== '') && Number.isFinite(parsed);
  return (
    <form
      className="force"
      onSubmit={(event) => {
        event.preventDefault();
        if (valid) onForce(parsed);
      }}
    >
      {digital ? (
        <select
          className="settings__input"
          aria-label={t('monitor.forceValue', { signal: point.signal })}
          value={text || (current >= 0.5 ? '0' : '1')}
          disabled={disabled}
          data-testid={`force-value-${point.signal}`}
          onChange={(e) => setText(e.target.value)}
        >
          <option value="0">{t('monitor.off')}</option>
          <option value="1">{t('monitor.on')}</option>
        </select>
      ) : (
        <input
          className="settings__input settings__input--short value"
          inputMode="decimal"
          aria-label={t('monitor.forceValue', { signal: point.signal })}
          placeholder={formatNumber(i18n.language, current)}
          value={text}
          disabled={disabled}
          data-testid={`force-value-${point.signal}`}
          onChange={(e) => setText(e.target.value)}
        />
      )}
      <button
        type="submit"
        className="button"
        disabled={disabled || !valid}
        data-testid={`force-${point.signal}`}
      >
        {t('monitor.force')}
      </button>
    </form>
  );
}
