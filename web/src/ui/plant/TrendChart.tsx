import { useTranslation } from 'react-i18next';
import type { WelcomeMessage } from '../../net/contracts.generated';
import type { TrendPoint } from '../../net/plantStore';
import { formatSignal, fractionOfRange, signalLabel } from '../format';

export interface TrendChartProps {
  welcome: WelcomeMessage;
  trend: readonly TrendPoint[];
  /** Saídas desenhadas, pelo nome. */
  signals: readonly string[];
  /** Janela mostrada, em segundos de tempo simulado. */
  windowSeconds?: number;
}

const width = 600;
const height = 200;

/**
 * Tendências das saídas escolhidas, cada uma em % da faixa do seu instrumento (LRV a URV), para caberem no
 * mesmo eixo. Os pontos são os estados recebidos do servidor; nada é calculado além da escala do desenho.
 */
export function TrendChart({ welcome, trend, signals, windowSeconds = 120 }: TrendChartProps) {
  const { t, i18n } = useTranslation();
  const series = signals
    .flatMap((name) => {
      const index = welcome.outputs.findIndex((o) => o.name === name);
      const info = welcome.outputs[index];
      return info ? [{ name, index, info }] : [];
    })
    .map((s, n) => ({ ...s, color: n % 4 }));

  const last = trend.at(-1);
  const end = last?.time ?? 0;
  const start = end - windowSeconds;
  const x = (time: number) => ((time - start) / windowSeconds) * width;
  const y = (fraction: number) => height - Math.min(Math.max(fraction, -0.05), 1.05) * height;

  return (
    <section className="panel" aria-labelledby="trends-heading">
      <h3 id="trends-heading" className="panel__title">
        {t('trends.heading')}
      </h3>
      <p className="panel__hint">{t('trends.window', { seconds: windowSeconds })}</p>
      {last === undefined ? (
        <p className="panel__row">{t('trends.empty')}</p>
      ) : (
        <svg
          className="trend"
          viewBox={`0 0 ${width} ${height}`}
          preserveAspectRatio="none"
          role="img"
          aria-labelledby="trends-heading"
        >
          {[0.25, 0.5, 0.75].map((f) => (
            <line key={f} className="trend__grid" x1={0} x2={width} y1={y(f)} y2={y(f)} />
          ))}
          {series.map((s) => {
            const points = trend
              .map((p) => {
                const fraction = fractionOfRange(p.outputs[s.index] ?? 0, s.info);
                return fraction === null
                  ? null
                  : `${x(p.time).toFixed(1)},${y(fraction).toFixed(1)}`;
              })
              .filter((p) => p !== null)
              .join(' ');
            return (
              <polyline
                key={s.name}
                className={`trend__line trend__line--${s.color + 1}`}
                points={points}
                data-signal={s.name}
              />
            );
          })}
        </svg>
      )}
      <ul className="trend-legend">
        {series.map((s) => {
          const value = last?.outputs[s.index];
          return (
            <li key={s.name} className="trend-legend__item">
              <span className={`trend-legend__swatch trend-legend__swatch--${s.color + 1}`} />
              <span>{signalLabel(t, welcome.plant.scenarioId, s.name)}</span>
              <span
                className="value"
                data-testid={`trend-value-${s.name}`}
                data-value={value ?? ''}
                data-points={trend.length}
              >
                {value === undefined ? '' : formatSignal(i18n.language, value, s.info)}
              </span>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
