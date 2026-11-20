import type {
  ActiveAlarm,
  ForcedInputInfo,
  IncompatibleMessage,
  LogEntryInfo,
  ModbusStatusMessage,
  MonitorLayoutMessage,
  MonitorMessage,
  MonitorWriteInfo,
  ServerMessage,
  SettingsMessage,
  StateMessage,
  UpdateSettingsCommand,
  WelcomeMessage,
} from './contracts.generated';
import {
  PlantSocket,
  plantSocketUrl,
  type ConnectionStatus,
  type PlantSocketOptions,
} from './plantSocket';

/** Um ponto da tendência: tempo simulado (s) e as saídas da planta em SI, na ordem do `welcome`. */
export interface TrendPoint {
  time: number;
  outputs: readonly number[];
}

/** O que a IHM mostra de uma planta; trocado por inteiro a cada mudança (para o React). */
export interface PlantView {
  status: ConnectionStatus;
  welcome: WelcomeMessage | null;
  state: StateMessage | null;
  modbus: ModbusStatusMessage | null;
  alarms: readonly ActiveAlarm[];
  /** Último código de erro recebido do servidor (fatal ou não). */
  errorCode: string | null;
  incompatible: IncompatibleMessage | null;
  /** Pontos da tendência, do mais antigo ao mais novo (janela de `trendWindowSeconds`). */
  trend: readonly TrendPoint[];
  /** Configurações de comunicação (Etapa 8). */
  settings: SettingsMessage | null;
  /** Entradas forçadas. */
  forced: readonly ForcedInputInfo[];
  /** Linhas do monitor de registradores (só com o monitor ligado). */
  monitorLayout: MonitorLayoutMessage | null;
  /** Valores mais recentes do monitor. */
  monitor: MonitorMessage | null;
  /** Última escrita conhecida em cada linha do monitor. */
  monitorWrites: readonly MonitorWriteInfo[];
  /** Log de comunicação, do mais antigo ao mais novo (até `logLimit` eventos). */
  log: readonly LogEntryInfo[];
}

/** Campos de configuração que a tela pode mudar; os ausentes ficam como estão. */
export type SettingsChanges = Partial<Omit<UpdateSettingsCommand, 'type'>>;

/** Eventos do log guardados na IHM, no máximo (o servidor guarda os mesmos 500). */
export const logLimit = 500;

export interface PlantStoreOptions extends PlantSocketOptions {
  /** URL do WebSocket; por padrão, a da planta no host da página. */
  url?: string;
  /** Janela da tendência, em segundos de tempo simulado. */
  trendWindowSeconds?: number;
}

const initialView: PlantView = {
  status: 'connecting',
  welcome: null,
  state: null,
  modbus: null,
  alarms: [],
  errorCode: null,
  incompatible: null,
  trend: [],
  settings: null,
  forced: [],
  monitorLayout: null,
  monitor: null,
  monitorWrites: [],
  log: [],
};

/**
 * Estado de uma planta na IHM, alimentado pelo WebSocket. Não calcula nada da planta (D01): só guarda o que
 * o servidor manda. A tendência recomeça quando a planta é reiniciada (o número do reinício muda).
 */
export class PlantStore {
  private view: PlantView = initialView;
  private readonly listeners = new Set<() => void>();
  private readonly socket: PlantSocket;
  private readonly trendWindowSeconds: number;
  private trend: TrendPoint[] = [];
  private run: number | null = null;
  private monitorEnabled = false;

  constructor(
    readonly plantId: string,
    options: PlantStoreOptions = {},
  ) {
    this.trendWindowSeconds = options.trendWindowSeconds ?? 120;
    this.socket = new PlantSocket(
      options.url ?? plantSocketUrl(plantId),
      {
        onStatus: (status) => this.update({ status }),
        onMessage: (message) => this.handle(message),
      },
      options,
    );
  }

  readonly subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  readonly getSnapshot = (): PlantView => this.view;

  start(): void {
    this.socket.start();
  }

  stop(): void {
    this.socket.stop();
  }

  setDisturbance(input: string, value: number): boolean {
    return this.socket.send({ type: 'setDisturbance', input, value });
  }

  restart(): boolean {
    return this.socket.send({ type: 'restart' });
  }

  acceptPairing(requestId: string): boolean {
    return this.socket.send({ type: 'acceptPairing', requestId });
  }

  refusePairing(requestId: string): boolean {
    return this.socket.send({ type: 'refusePairing', requestId });
  }

  revokeMaster(address: string): boolean {
    return this.socket.send({ type: 'revokeMaster', address });
  }

  /** Muda as configurações dadas; o servidor responde com `settings` (ou com um erro). */
  updateSettings(changes: SettingsChanges): boolean {
    return this.socket.send({
      type: 'updateSettings',
      format: changes.format ?? null,
      wordOrder: changes.wordOrder ?? null,
      watchdogEnabled: changes.watchdogEnabled ?? null,
      watchdogTimeoutSeconds: changes.watchdogTimeoutSeconds ?? null,
      diagnosticsEnabled: changes.diagnosticsEnabled ?? null,
      unitId: changes.unitId ?? null,
      masterPolicy: changes.masterPolicy ?? null,
    });
  }

  forceInput(area: string, signal: string, value: number): boolean {
    return this.socket.send({ type: 'forceInput', area, signal, value });
  }

  releaseInput(area: string, signal: string): boolean {
    return this.socket.send({ type: 'releaseInput', area, signal });
  }

  pause(): boolean {
    return this.socket.send({ type: 'pause' });
  }

  resume(): boolean {
    return this.socket.send({ type: 'resume' });
  }

  /**
   * Liga ou desliga o monitor de registradores. Fica ligado também depois de uma reconexão (o pedido é
   * repetido a cada `welcome`).
   */
  setMonitor(enabled: boolean): void {
    this.monitorEnabled = enabled;
    this.socket.send({ type: 'subscribeMonitor', enabled });
    if (!enabled) this.update({ monitorLayout: null, monitor: null, monitorWrites: [] });
  }

  private handle(message: ServerMessage): void {
    switch (message.type) {
      case 'welcome':
        this.update({ welcome: message, errorCode: null });
        if (this.monitorEnabled) this.socket.send({ type: 'subscribeMonitor', enabled: true });
        break;
      case 'settings':
        this.update({ settings: message });
        break;
      case 'forced':
        this.update({ forced: message.inputs });
        break;
      case 'monitorLayout':
        this.update({ monitorLayout: message, monitor: null, monitorWrites: [] });
        break;
      case 'monitor':
        this.update(
          message.writes === null
            ? { monitor: message }
            : { monitor: message, monitorWrites: message.writes },
        );
        break;
      case 'log':
        this.update({ log: mergeLog(message.reset ? [] : this.view.log, message.entries) });
        break;
      case 'state':
        this.addState(message);
        break;
      case 'modbus':
        this.update({ modbus: message });
        break;
      case 'alarms':
        this.update({ alarms: message.active });
        break;
      case 'incompatible':
        this.update({ incompatible: message });
        break;
      case 'error':
        this.update({ errorCode: message.code });
        break;
    }
  }

  private addState(state: StateMessage): void {
    if (this.run !== state.run) {
      this.run = state.run;
      this.trend = [];
    }

    const last = this.trend.at(-1);
    if (last === undefined || state.time > last.time) {
      this.trend.push({ time: state.time, outputs: state.outputs });
      const start = state.time - this.trendWindowSeconds;
      let drop = 0;
      while (drop < this.trend.length && (this.trend[drop]?.time ?? 0) < start) drop++;
      if (drop > 0) this.trend.splice(0, drop);
    }

    this.update({ state, trend: [...this.trend] });
  }

  private update(changes: Partial<PlantView>): void {
    this.view = { ...this.view, ...changes };
    for (const listener of this.listeners) listener();
  }
}

/**
 * Junta eventos novos ao log: um evento agrupado de novo (mesmo `id`) sai da posição antiga e vai para o fim.
 * Guarda os `logLimit` mais recentes, em ordem de sequência.
 */
export function mergeLog(
  current: readonly LogEntryInfo[],
  entries: readonly LogEntryInfo[],
): LogEntryInfo[] {
  const ids = new Set(entries.map((e) => e.id));
  const merged = [...current.filter((e) => !ids.has(e.id)), ...entries].sort(
    (a, b) => a.sequence - b.sequence,
  );
  return merged.length > logLimit ? merged.slice(merged.length - logLimit) : merged;
}

/** Se a entrada está forçada. */
export function isForced(
  forced: readonly ForcedInputInfo[],
  area: string,
  signal: string,
): ForcedInputInfo | undefined {
  return forced.find((f) => f.area === area && f.signal === signal);
}

/** Se há mestre Modbus autorizado e conectado. */
export function isMasterConnected(modbus: ModbusStatusMessage | null): boolean {
  return modbus?.connections.some((c) => c.authorized) ?? false;
}
