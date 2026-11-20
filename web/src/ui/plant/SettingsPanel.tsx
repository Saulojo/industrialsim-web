import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  AnalogFormats,
  MasterPolicies,
  WordOrders,
  type ModbusStatusMessage,
  type PlantSettingsInfo,
  type SettingsMessage,
  type WelcomeMessage,
} from '../../net/contracts.generated';
import type { SettingsChanges } from '../../net/plantStore';
import { translateDynamic } from '../format';

export interface SettingsPanelProps {
  welcome: WelcomeMessage;
  modbus: ModbusStatusMessage | null;
  settings: SettingsMessage;
  disabled: boolean;
  onApply: (changes: SettingsChanges) => void;
}

const FORMATS = Object.values(AnalogFormats);
const ORDERS = Object.values(WordOrders);
const POLICIES = Object.values(MasterPolicies);

/** Rascunho da tela: o que o usuário está editando, ainda não enviado. */
type Draft = Omit<PlantSettingsInfo, 'unitId' | 'watchdogTimeoutSeconds'> & {
  unitId: string;
  watchdogTimeoutSeconds: string;
};

function toDraft(settings: PlantSettingsInfo): Draft {
  return {
    ...settings,
    unitId: String(settings.unitId),
    watchdogTimeoutSeconds: String(settings.watchdogTimeoutSeconds),
  };
}

/** Só os campos que mudaram; nulo se algum número for inválido. */
export function settingsChanges(
  current: PlantSettingsInfo,
  draft: Draft,
  maxTimeout: number,
): SettingsChanges | null {
  const unitId = Number(draft.unitId);
  const timeout = Number(draft.watchdogTimeoutSeconds.replace(',', '.'));
  if (!Number.isInteger(unitId) || unitId < 1 || unitId > 255) return null;
  if (!Number.isFinite(timeout) || timeout <= 0 || timeout > maxTimeout) return null;

  const changes: SettingsChanges = {};
  if (draft.format !== current.format) changes.format = draft.format;
  if (draft.wordOrder !== current.wordOrder) changes.wordOrder = draft.wordOrder;
  if (draft.watchdogEnabled !== current.watchdogEnabled)
    changes.watchdogEnabled = draft.watchdogEnabled;
  if (timeout !== current.watchdogTimeoutSeconds) changes.watchdogTimeoutSeconds = timeout;
  if (draft.diagnosticsEnabled !== current.diagnosticsEnabled)
    changes.diagnosticsEnabled = draft.diagnosticsEnabled;
  if (unitId !== current.unitId) changes.unitId = unitId;
  if (draft.masterPolicy !== current.masterPolicy) changes.masterPolicy = draft.masterPolicy;
  return changes;
}

/**
 * Painel de conexão da Etapa 8: IP e porta (só leitura), Unit ID, formato analógico, ordem das palavras,
 * watchdog, bloco de diagnóstico e mestres permitidos. As mudanças só vão ao servidor em "Aplicar".
 */
export function SettingsPanel({
  welcome,
  modbus,
  settings,
  disabled,
  onApply,
}: SettingsPanelProps) {
  const { t } = useTranslation();
  const current = settings.settings;
  const [draft, setDraft] = useState<Draft>(() => toDraft(current));
  const [base, setBase] = useState<PlantSettingsInfo>(current);

  // Outra pessoa (ou este navegador) mudou as configurações: a tela volta a mostrar o que vale.
  if (base !== current) {
    setBase(current);
    setDraft(toDraft(current));
  }

  const changes = settingsChanges(current, draft, settings.maxWatchdogTimeoutSeconds);
  const dirty = changes === null || Object.keys(changes).length > 0;
  const server = welcome.serverAddress ?? window.location.hostname;
  const port = modbus?.port ?? welcome.plant.modbusPort;
  const set = (changes: Partial<Draft>) => setDraft({ ...draft, ...changes });

  return (
    <section className="panel" aria-labelledby="settings-heading">
      <h3 id="settings-heading" className="panel__title">
        {t('settings.heading')}
      </h3>
      <form
        className="settings"
        onSubmit={(event) => {
          event.preventDefault();
          if (changes) onApply(changes);
        }}
      >
        <dl className="facts">
          <dt>{t('settings.serverAddress')}</dt>
          <dd className="value" data-testid="settings-server">
            {server}
          </dd>
          <dt>{t('settings.port')}</dt>
          <dd className="value" data-testid="settings-port">
            {port}
          </dd>
        </dl>

        <label className="settings__field">
          <span className="field__label">{t('settings.unitId')}</span>
          <input
            className="settings__input value"
            type="number"
            min={1}
            max={255}
            step={1}
            value={draft.unitId}
            disabled={disabled}
            data-testid="settings-unit-id"
            onChange={(e) => set({ unitId: e.target.value })}
          />
        </label>

        <label className="settings__field">
          <span className="field__label">{t('settings.format')}</span>
          <select
            className="settings__input"
            value={draft.format}
            disabled={disabled}
            data-testid="settings-format"
            onChange={(e) => set({ format: e.target.value })}
          >
            {FORMATS.map((f) => (
              <option key={f} value={f}>
                {translateDynamic(t, `settings.values.format.${f}`, f)}
              </option>
            ))}
          </select>
        </label>

        <label className="settings__field">
          <span className="field__label">{t('settings.wordOrder')}</span>
          <select
            className="settings__input"
            value={draft.wordOrder}
            disabled={disabled}
            data-testid="settings-word-order"
            onChange={(e) => set({ wordOrder: e.target.value })}
          >
            {ORDERS.map((o) => (
              <option key={o} value={o}>
                {translateDynamic(t, `settings.values.wordOrder.${o}`, o)}
              </option>
            ))}
          </select>
        </label>

        <fieldset className="settings__group">
          <legend className="field__label">{t('settings.watchdog')}</legend>
          <label className="settings__check">
            <input
              type="checkbox"
              checked={draft.watchdogEnabled}
              disabled={disabled}
              data-testid="settings-watchdog"
              onChange={(e) => set({ watchdogEnabled: e.target.checked })}
            />
            {t('settings.watchdogEnabled')}
          </label>
          <label className="settings__check">
            {t('settings.watchdogTimeout')}
            <input
              className="settings__input settings__input--short value"
              type="number"
              min={0.1}
              max={settings.maxWatchdogTimeoutSeconds}
              step={0.1}
              value={draft.watchdogTimeoutSeconds}
              disabled={disabled}
              data-testid="settings-watchdog-timeout"
              onChange={(e) => set({ watchdogTimeoutSeconds: e.target.value })}
            />
          </label>
        </fieldset>

        <fieldset className="settings__group">
          <legend className="field__label">{t('settings.diagnostics')}</legend>
          <label className="settings__check">
            <input
              type="checkbox"
              checked={draft.diagnosticsEnabled}
              disabled={disabled}
              data-testid="settings-diagnostics"
              onChange={(e) => set({ diagnosticsEnabled: e.target.checked })}
            />
            {t('settings.diagnosticsEnabled')}
          </label>
        </fieldset>

        <label className="settings__field">
          <span className="field__label">{t('settings.masterPolicy')}</span>
          <select
            className="settings__input"
            value={draft.masterPolicy}
            disabled={disabled}
            data-testid="settings-master-policy"
            onChange={(e) => set({ masterPolicy: e.target.value })}
          >
            {POLICIES.map((p) => (
              <option key={p} value={p}>
                {translateDynamic(t, `settings.values.masterPolicy.${p}`, p)}
              </option>
            ))}
          </select>
        </label>
        {draft.masterPolicy === MasterPolicies.single && (
          <p className="panel__hint">{t('settings.singleHint')}</p>
        )}

        <p className="panel__row settings__actions">
          <button
            type="submit"
            className="button button--primary"
            disabled={disabled || !dirty || changes === null}
            data-testid="settings-apply"
          >
            {t('settings.apply')}
          </button>
          <button
            type="button"
            className="button"
            disabled={!dirty}
            onClick={() => setDraft(toDraft(current))}
          >
            {t('settings.discard')}
          </button>
        </p>
        <p className="panel__hint">{t('settings.hint')}</p>
      </form>
    </section>
  );
}
