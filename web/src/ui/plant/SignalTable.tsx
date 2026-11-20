import { useTranslation } from 'react-i18next';
import type {
  ForcedInputInfo,
  SignalInfo,
  StateMessage,
  WelcomeMessage,
} from '../../net/contracts.generated';
import { formatSignal, signalLabel } from '../format';

export interface SignalTableProps {
  welcome: WelcomeMessage;
  state: StateMessage;
  /** Entradas forçadas, destacadas na tabela. */
  forced?: readonly ForcedInputInfo[];
}

/** Valor de um sinal para leitura: digital como ligado/desligado, analógico com unidade. */
export function useSignalText() {
  const { t, i18n } = useTranslation();
  return (si: number, signal: SignalInfo): string =>
    signal.kind === 'digital'
      ? si >= 0.5
        ? t('values.on')
        : t('values.off')
      : formatSignal(i18n.language, si, signal);
}

/** Tabela das saídas da planta; é o sinóptico dos cenários que ainda não têm um desenho próprio. */
export function SignalTable({ welcome, state, forced = [] }: SignalTableProps) {
  const { t } = useTranslation();
  const text = useSignalText();

  return (
    <section className="panel" aria-labelledby="values-heading">
      <h3 id="values-heading" className="panel__title">
        {t('plant.values')}
      </h3>
      <table className="signal-table">
        <tbody>
          {welcome.outputs.map((signal, i) => (
            <tr key={signal.name}>
              <th scope="row">{signalLabel(t, welcome.plant.scenarioId, signal.name)}</th>
              <td
                className="value"
                data-signal={signal.name}
                data-value={state.outputs[i]}
                data-forced={forced.some((f) => f.signal === signal.name)}
              >
                {text(state.outputs[i] ?? 0, signal)}
                {forced.some((f) => f.signal === signal.name) && (
                  <span className="chip chip--forced monitor__flag">{t('monitor.forced')}</span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}
