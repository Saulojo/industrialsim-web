import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiRequestError, createPlant, fetchScenarios } from '../net/api';
import type { PlantSummary, ScenarioSummary } from '../net/contracts.generated';
import { apiErrorText, translateDynamic } from './format';
import { navigate, plantPath } from './navigation';

export interface ScenarioPickerProps {
  /** Substituíveis nos testes. */
  load?: (signal: AbortSignal) => Promise<ScenarioSummary[]>;
  create?: (scenarioId: string) => Promise<PlantSummary>;
  onCreated?: (plant: PlantSummary) => void;
}

type LoadState =
  | { kind: 'loading' }
  | { kind: 'ok'; scenarios: ScenarioSummary[] }
  | { kind: 'error'; code: string };

/** Lista os cenários do servidor e cria uma planta do escolhido. */
export function ScenarioPicker({
  load = fetchScenarios,
  create = createPlant,
  onCreated = (plant) => navigate(plantPath(plant.id)),
}: ScenarioPickerProps) {
  const { t } = useTranslation();
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [opening, setOpening] = useState<string | null>(null);
  const [createError, setCreateError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal).then(
      (scenarios) => setState({ kind: 'ok', scenarios }),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setState({
            kind: 'error',
            code: error instanceof ApiRequestError ? error.code : 'network',
          });
        }
      },
    );
    return () => controller.abort();
  }, [load]);

  async function open(scenarioId: string) {
    setOpening(scenarioId);
    setCreateError(null);
    try {
      onCreated(await create(scenarioId));
    } catch (error) {
      setCreateError(error instanceof ApiRequestError ? error.code : 'network');
      setOpening(null);
    }
  }

  return (
    <section className="panel" aria-labelledby="scenarios-heading">
      <h2 id="scenarios-heading" className="panel__title">
        {t('scenarios.heading')}
      </h2>
      <p className="panel__row">{t('scenarios.intro')}</p>
      {state.kind === 'loading' && <p className="panel__row">{t('scenarios.loading')}</p>}
      {state.kind === 'error' && (
        <p role="alert" className="panel__alarm">
          {apiErrorText(t, state.code)}
        </p>
      )}
      {state.kind === 'ok' && state.scenarios.length === 0 && (
        <p className="panel__row">{t('scenarios.empty')}</p>
      )}
      {state.kind === 'ok' && (
        <ul className="scenario-list">
          {state.scenarios.map((scenario) => (
            <li key={scenario.id} className="scenario-list__item">
              <div>
                <h3 className="scenario-list__name">
                  {translateDynamic(t, `scenarios.names.${scenario.id}`, scenario.id)}
                </h3>
                <p className="scenario-list__description">
                  {translateDynamic(t, `scenarios.descriptions.${scenario.id}`, '')}
                </p>
              </div>
              <button
                type="button"
                className="button button--primary"
                disabled={opening !== null}
                onClick={() => void open(scenario.id)}
              >
                {opening === scenario.id ? t('scenarios.opening') : t('scenarios.open')}
              </button>
            </li>
          ))}
        </ul>
      )}
      {createError && (
        <p role="alert" className="panel__alarm">
          {apiErrorText(t, createError)}
        </p>
      )}
    </section>
  );
}
