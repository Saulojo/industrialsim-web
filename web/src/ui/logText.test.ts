import i18n from 'i18next';
import { beforeAll, describe, expect, it } from 'vitest';
import { initI18n } from '../locales';
import { logEntry } from '../net/testData';
import { describeLogEntry } from './logText';

describe('describeLogEntry', () => {
  beforeAll(async () => {
    await initI18n('pt-BR');
  });

  const describe_ = (entry: ReturnType<typeof logEntry>) =>
    describeLogEntry(i18n.t, 'pt-BR', 'caldeira', entry);

  it('traduz a configuração e os valores antigo e novo', () => {
    expect(
      describe_(
        logEntry(1, 'setting-changed', { setting: 'format', old: 'counts27648', new: 'float32' }),
      ),
    ).toBe('Formato analógico: 0–27648 (Siemens) → float32 em unidades de engenharia.');
    expect(
      describe_(
        logEntry(2, 'setting-changed', { setting: 'watchdogTimeoutSeconds', old: '2', new: '3.5' }),
      ),
    ).toBe('Tempo limite do watchdog: 2 s → 3,5 s.');
    expect(
      describe_(logEntry(3, 'setting-changed', { setting: 'unitId', old: '1', new: '17' })),
    ).toBe('Unit ID: 1 → 17.');
  });

  it('dá nome à exceção Modbus e mostra o endereço pedido', () => {
    expect(
      describe_(
        logEntry(4, 'modbus-exception', {
          address: '10.0.0.5',
          function: '4',
          code: '2',
          register: '200',
        }),
      ),
    ).toBe('Exceção 2 (endereço ilegal) para 10.0.0.5 na função 4, endereço 200.');
    expect(
      describe_(
        logEntry(5, 'modbus-exception', { address: '10.0.0.5', function: '43', code: '1' }),
      ),
    ).toBe('Exceção 1 (função ilegal) para 10.0.0.5 na função 43.');
  });

  it('mostra o sinal forçado pelo nome de tela', () => {
    expect(
      describe_(
        logEntry(6, 'input-forced', { area: 'input-register', signal: 'LT-101', value: '0.9' }),
      ),
    ).toBe('LT-101 · Nível do tubulão (input register) forçado em 0,9.');
    expect(describe_(logEntry(7, 'paused', { step: '300' }))).toBe('Planta pausada no passo 300.');
  });
});
