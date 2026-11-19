import { useTranslation } from 'react-i18next';
import type { SignalInfo, StateMessage, WelcomeMessage } from '../../net/contracts.generated';
import { fractionOfRange } from '../format';
import { useSignalText } from './SignalTable';

type Anchor = 'start' | 'middle' | 'end';

/** Onde cada valor aparece no desenho (tags do cenário, não texto de interface). */
const READINGS: readonly { signal: string; x: number; y: number; anchor: Anchor }[] = [
  { signal: 'FV-101_position', x: 180, y: 262, anchor: 'middle' },
  { signal: 'FT-101', x: 240, y: 220, anchor: 'middle' },
  { signal: 'LT-101', x: 510, y: 150, anchor: 'start' },
  { signal: 'FV-103_position', x: 560, y: 70, anchor: 'middle' },
  { signal: 'FT-102', x: 460, y: 30, anchor: 'middle' },
  { signal: 'PT-101', x: 300, y: 60, anchor: 'start' },
  { signal: 'FV-102_position', x: 180, y: 325, anchor: 'middle' },
];

/** Chaves e válvula de alívio: a tag fica na cor de alarme quando ativa. */
const SWITCHES: readonly { signal: string; x: number; y: number }[] = [
  { signal: 'LSHH-101', x: 510, y: 110 },
  { signal: 'LSLL-101', x: 510, y: 190 },
  { signal: 'PSH-101', x: 300, y: 30 },
  { signal: 'PSV-101_open', x: 446, y: 72 },
];

const PUMP_RUNNING = 'P-101_running';
const LEVEL = 'LT-101';

export interface CaldeiraSynopticProps {
  welcome: WelcomeMessage;
  state: StateMessage;
}

/**
 * Sinóptico 2D simples da caldeira (Etapa 7), no estilo ISA-101: traço neutro, cor só para condição anormal.
 * O desenho é provisório; a trilha de design (D1, D2) e o 3D (Etapa 14) o substituem.
 */
export function CaldeiraSynoptic({ welcome, state }: CaldeiraSynopticProps) {
  const { t } = useTranslation();
  const text = useSignalText();

  const find = (name: string): { info: SignalInfo; value: number } | null => {
    const index = welcome.outputs.findIndex((o) => o.name === name);
    const info = welcome.outputs[index];
    return info ? { info, value: state.outputs[index] ?? 0 } : null;
  };

  const active = (name: string) => (find(name)?.value ?? 0) >= 0.5;
  const level = find(LEVEL);
  const levelFraction = Math.min(
    Math.max(level ? (fractionOfRange(level.value, level.info) ?? 0) : 0, 0),
    1,
  );

  /** Valor de uma saída, como texto do SVG, com os dados para os testes. */
  const reading = (name: string, x: number, y: number, anchor: Anchor) => {
    const signal = find(name);
    if (!signal) return null;
    return (
      <text
        x={x}
        y={y}
        textAnchor={anchor}
        className="synoptic__value"
        data-signal={name}
        data-value={signal.value}
      >
        <tspan className="synoptic__tag">{name.replace(/_.*$/, '')} </tspan>
        {text(signal.value, signal.info)}
      </text>
    );
  };

  const switchTag = (name: string, x: number, y: number) => (
    <text
      x={x}
      y={y}
      className={active(name) ? 'synoptic__switch synoptic__switch--active' : 'synoptic__switch'}
      data-signal={name}
      data-value={active(name) ? 1 : 0}
    >
      {name.replace(/_.*$/, '')}
    </text>
  );

  const drumTop = 80;
  const drumHeight = 120;
  const waterTop = drumTop + drumHeight * (1 - levelFraction);

  return (
    <section className="panel" aria-labelledby="synoptic-heading">
      <h3 id="synoptic-heading" className="panel__title">
        {t('synoptic.caldeira.title')}
      </h3>
      <svg className="synoptic" viewBox="0 0 640 330" role="img" aria-labelledby="synoptic-heading">
        <defs>
          <clipPath id="drum-clip">
            <rect x={300} y={drumTop} width={200} height={drumHeight} rx={50} />
          </clipPath>
        </defs>

        {/* Água de alimentação: P-101 → FV-101 → tubulão */}
        <text x={10} y={215} className="synoptic__label">
          {t('synoptic.caldeira.feedwater')}
        </text>
        <polyline className="synoptic__pipe" points="10,230 300,230 330,195" />
        <circle
          cx={90}
          cy={230}
          r={16}
          className={
            active(PUMP_RUNNING)
              ? 'synoptic__equipment synoptic__equipment--on'
              : 'synoptic__equipment'
          }
        />
        <text x={90} y={262} textAnchor="middle" className="synoptic__label">
          {t('synoptic.caldeira.pump')}
        </text>
        <Valve x={180} y={230} />

        {/* Tubulão */}
        <rect
          x={300}
          y={drumTop}
          width={200}
          height={drumHeight}
          rx={50}
          className="synoptic__vessel"
        />
        <rect
          x={300}
          y={waterTop}
          width={200}
          height={drumTop + drumHeight - waterTop}
          className="synoptic__water"
          clipPath="url(#drum-clip)"
        />
        <text x={400} y={drumTop + drumHeight / 2} textAnchor="middle" className="synoptic__label">
          {t('synoptic.caldeira.drum')}
        </text>

        {/* Vapor: tubulão → FV-103 → consumo */}
        <polyline className="synoptic__pipe" points="400,80 400,40 630,40" />
        <Valve x={540} y={40} />
        <text x={630} y={30} textAnchor="end" className="synoptic__label">
          {t('synoptic.caldeira.steam')}
        </text>
        <polyline className="synoptic__pipe" points="440,82 440,60" />

        {READINGS.map((r) => (
          <g key={r.signal}>{reading(r.signal, r.x, r.y, r.anchor)}</g>
        ))}
        {SWITCHES.map((s) => (
          <g key={s.signal}>{switchTag(s.signal, s.x, s.y)}</g>
        ))}

        {/* Combustível: FV-102 → queimador */}
        <text x={10} y={285} className="synoptic__label">
          {t('synoptic.caldeira.fuel')}
        </text>
        <polyline className="synoptic__pipe" points="10,300 330,300" />
        <Valve x={180} y={300} />
        <rect x={330} y={280} width={140} height={40} className="synoptic__equipment" />
        <text x={400} y={305} textAnchor="middle" className="synoptic__label">
          {t('synoptic.caldeira.burner')}
        </text>
        <polyline className="synoptic__pipe" points="400,280 400,200" />
      </svg>
    </section>
  );
}

/** Válvula (gravata) centrada em (x, y). */
function Valve({ x, y }: { x: number; y: number }) {
  return (
    <polygon
      className="synoptic__valve"
      points={`${x - 12},${y - 9} ${x + 12},${y + 9} ${x + 12},${y - 9} ${x - 12},${y + 9}`}
    />
  );
}
