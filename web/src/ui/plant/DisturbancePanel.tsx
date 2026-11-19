import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { StateMessage, WelcomeMessage } from '../../net/contracts.generated';
import { engineeringValue, formatNumber, signalLabel } from '../format';

export interface DisturbancePanelProps {
  welcome: WelcomeMessage;
  state: StateMessage;
  disabled: boolean;
  onChange(input: string, value: number): void;
}

/**
 * Distúrbios da seção `hmi` do cenário (D39), como controles deslizantes em unidade de engenharia. Cada
 * movimento vai ao servidor; até o estado da planta mostrar o valor pedido, o controle mostra o pedido.
 */
export function DisturbancePanel({ welcome, state, disabled, onChange }: DisturbancePanelProps) {
  const { t, i18n } = useTranslation();
  const [requested, setRequested] = useState<Record<string, number>>({});

  if (welcome.disturbances.length === 0) return null;

  return (
    <section className="panel" aria-labelledby="disturbances-heading">
      <h3 id="disturbances-heading" className="panel__title">
        {t('disturbances.heading')}
      </h3>
      {welcome.disturbances.map((disturbance) => {
        const index = welcome.inputs.findIndex((i) => i.name === disturbance.input);
        const current = state.inputs[index] ?? disturbance.min;
        const pending = requested[disturbance.input];
        // O estado vem em float32: compara com folga.
        const reached =
          pending === undefined ||
          Math.abs(current - pending) <= 1e-5 * Math.max(1, Math.abs(pending));
        const shown = engineeringValue(reached ? current : pending, disturbance);
        const min = engineeringValue(disturbance.min, disturbance);
        const max = engineeringValue(disturbance.max, disturbance);
        const id = `disturbance-${disturbance.input}`;

        return (
          <div key={disturbance.input} className="field">
            <label htmlFor={id} className="field__label">
              {signalLabel(t, welcome.plant.scenarioId, disturbance.input)}
            </label>
            <div className="field__row">
              <input
                id={id}
                type="range"
                className="field__range"
                min={Math.min(min, max)}
                max={Math.max(min, max)}
                step={Math.abs(max - min) / 100}
                value={shown}
                disabled={disabled}
                data-testid={id}
                onChange={(event) => {
                  const si = Number(event.target.value) / disturbance.unitFactor;
                  setRequested((r) => ({ ...r, [disturbance.input]: si }));
                  onChange(disturbance.input, si);
                }}
              />
              <output
                htmlFor={id}
                className="value field__value"
                data-testid={`${id}-value`}
                data-value={current}
              >
                {formatNumber(i18n.language, shown)} {disturbance.unit}
              </output>
            </div>
          </div>
        );
      })}
    </section>
  );
}
