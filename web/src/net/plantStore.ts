import type {
  ActiveAlarm,
  IncompatibleMessage,
  ModbusStatusMessage,
  ServerMessage,
  StateMessage,
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
}

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

  private handle(message: ServerMessage): void {
    switch (message.type) {
      case 'welcome':
        this.update({ welcome: message, errorCode: null });
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

/** Se há mestre Modbus autorizado e conectado. */
export function isMasterConnected(modbus: ModbusStatusMessage | null): boolean {
  return modbus?.connections.some((c) => c.authorized) ?? false;
}
