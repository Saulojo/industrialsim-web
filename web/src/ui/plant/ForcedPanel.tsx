import { useTranslation } from 'react-i18next';
import type { ForcedInputInfo, WelcomeMessage } from '../../net/contracts.generated';
import { formatNumber, signalLabel } from '../format';

export interface ForcedPanelProps {
  welcome: WelcomeMessage;
  forced: readonly ForcedInputInfo[];
  disabled: boolean;
  onRelease: (area: string, signal: string) => void;
}

/**
 * Valores forçados, na tela do processo: destacados como estado anormal (ISA-101), para ninguém esquecer que o
 * mestre está lendo um valor que não é o da planta. Some quando nada está forçado.
 */
export function ForcedPanel({ welcome, forced, disabled, onRelease }: ForcedPanelProps) {
  const { t, i18n } = useTranslation();
  if (forced.length === 0) return null;

  const unitOf = (signal: string) => welcome.outputs.find((o) => o.name === signal)?.unit ?? '';

  return (
    <section
      className="panel panel--forced"
      aria-labelledby="forced-heading"
      data-testid="forced-panel"
    >
      <h3 id="forced-heading" className="panel__title">
        {t('forced.heading')}
      </h3>
      <ul className="plain-list">
        {forced.map((f) => {
          const digital = f.area === 'discrete-input';
          const value = digital ? String(f.value) : formatNumber(i18n.language, f.value);
          const unit = digital ? '' : unitOf(f.signal);
          return (
            <li
              key={`${f.area}-${f.signal}`}
              className="plain-list__item"
              data-testid={`forced-${f.signal}`}
              data-value={f.value}
            >
              <span className="chip chip--forced">
                {t('forced.item', {
                  signal: signalLabel(t, welcome.plant.scenarioId, f.signal),
                  value: unit ? `${value} ${unit}` : value,
                })}
              </span>
              <button
                type="button"
                className="button"
                disabled={disabled}
                onClick={() => onRelease(f.area, f.signal)}
              >
                {t('forced.release')}
              </button>
            </li>
          );
        })}
      </ul>
      <p className="panel__hint">{t('forced.hint')}</p>
    </section>
  );
}
