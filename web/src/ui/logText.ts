import type { TFunction } from 'i18next';
import type { LogEntryInfo } from '../net/contracts.generated';
import { signalLabel, translateDynamic } from './format';

/** Número do log no formato do idioma, sem casas decimais sobrando (2 → "2"; 3.5 → "3,5"). */
function plainNumber(language: string, value: number): string {
  return new Intl.NumberFormat(language, { maximumFractionDigits: 6 }).format(value);
}

/** Valor de um argumento do evento, ou texto vazio. */
export function logArgument(entry: LogEntryInfo, name: string): string {
  return entry.arguments.find((a) => a.name === name)?.value ?? '';
}

/** Texto de um valor de configuração do log, pelo nome da configuração (D16: termos traduzidos). */
function settingValue(t: TFunction, language: string, setting: string, value: string): string {
  switch (setting) {
    case 'format':
      return translateDynamic(t, `settings.values.format.${value}`, value);
    case 'wordOrder':
      return translateDynamic(t, `settings.values.wordOrder.${value}`, value);
    case 'masterPolicy':
      return translateDynamic(t, `settings.values.masterPolicy.${value}`, value);
    case 'watchdogEnabled':
    case 'diagnosticsEnabled':
      return translateDynamic(t, `settings.values.bool.${value}`, value);
    case 'watchdogTimeoutSeconds':
      return t('log.seconds', { value: plainNumber(language, Number(value)) });
    default:
      return value;
  }
}

/**
 * Frase de um evento do log de comunicação, montada só com os arquivos de idioma (RNF11): o servidor manda o
 * tipo e os argumentos.
 */
export function describeLogEntry(
  t: TFunction,
  language: string,
  scenarioId: string,
  entry: LogEntryInfo,
): string {
  const args: Record<string, string> = {};
  for (const argument of entry.arguments) args[argument.name] = argument.value;

  let key = `log.kinds.${entry.kind}`;
  switch (entry.kind) {
    case 'modbus-exception':
      args.codeName = translateDynamic(
        t,
        `log.exceptions.${args.code ?? ''}`,
        t('log.unknownException'),
      );
      if (args.register !== undefined) key = 'log.kinds.modbus-exception-register';
      break;
    case 'setting-changed': {
      const setting = args.setting ?? '';
      args.setting = translateDynamic(t, `log.settings.${setting}`, setting);
      args.old = settingValue(t, language, setting, args.old ?? '');
      args.new = settingValue(t, language, setting, args.new ?? '');
      break;
    }
    case 'input-forced':
    case 'input-released': {
      const area = args.area ?? '';
      const value = Number(args.value);
      args.signal = signalLabel(t, scenarioId, args.signal ?? '');
      args.value = area === 'discrete-input' ? (args.value ?? '') : plainNumber(language, value);
      args.area = translateDynamic(t, `log.areas.${area}`, area);
      break;
    }
  }

  return translateDynamic(t, key, entry.kind, args);
}
