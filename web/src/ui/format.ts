import type { TFunction } from 'i18next';
import type { SignalInfo } from '../net/contracts.generated';

/**
 * `t` sem a checagem de chaves em tempo de compilação, para chaves montadas com dados do servidor (tag de
 * sinal, código de erro). Se a chave não existir, mostra `fallback`.
 */
export function translateDynamic(
  t: TFunction,
  key: string,
  fallback: string,
  options: Record<string, unknown> = {},
): string {
  const loose = t as unknown as (k: string, o: Record<string, unknown>) => string;
  return loose(key, { ...options, defaultValue: fallback });
}

/** Nome de tela de um sinal: `signals.<cenário>.<tag>` nos arquivos de idioma, ou a própria tag. */
export function signalLabel(t: TFunction, scenarioId: string, name: string): string {
  if (name === 'watchdog') return t('signals.watchdog');
  if (name === 'heartbeat') return t('signals.heartbeat');
  return translateDynamic(t, `signals.${scenarioId}.${name}`, name);
}

/** Texto de um código de erro da API REST (D35). */
export function apiErrorText(t: TFunction, code: string): string {
  return translateDynamic(t, `errors.api.${code}`, t('errors.unknown', { code }));
}

/** Texto de um código de erro do WebSocket da planta. */
export function socketErrorText(t: TFunction, code: string): string {
  return translateDynamic(t, `errors.socket.${code}`, t('errors.unknown', { code }));
}

/** Valor de engenharia = valor em SI × fator (D29). Só conversão de unidade, não cálculo de planta (D01). */
export function engineeringValue(si: number, signal: Pick<SignalInfo, 'unitFactor'>): number {
  return si * signal.unitFactor;
}

/** Posição do valor na faixa do instrumento, de 0 a 1 (pode passar dos limites); nulo sem faixa. */
export function fractionOfRange(
  si: number,
  signal: Pick<SignalInfo, 'lrv' | 'urv'>,
): number | null {
  if (signal.lrv === null || signal.urv === null || signal.urv === signal.lrv) return null;
  return (si - signal.lrv) / (signal.urv - signal.lrv);
}

/** Número no formato do idioma, com casas decimais pela ordem de grandeza. */
export function formatNumber(language: string, value: number): string {
  const magnitude = Math.abs(value);
  const digits = magnitude >= 100 ? 1 : magnitude >= 10 ? 2 : 3;
  return new Intl.NumberFormat(language, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(value);
}

/** Valor com unidade, para leitura (ex.: "10,003 bar"). */
export function formatSignal(language: string, si: number, signal: SignalInfo): string {
  const text = formatNumber(language, engineeringValue(si, signal));
  return signal.unit ? `${text} ${signal.unit}` : text;
}
