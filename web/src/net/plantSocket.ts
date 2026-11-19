import {
  PlantSocketProtocol,
  type ClientMessage,
  type ServerMessage,
  type WelcomeMessage,
} from './contracts.generated';

/**
 * Situação da conexão com a planta:
 * - `connecting`: primeira tentativa, ainda sem `welcome`;
 * - `connected`: `welcome` recebido, estados chegando;
 * - `reconnecting`: a conexão caiu e o cliente tenta de novo sozinho;
 * - `incompatible`: o servidor fala outra versão do protocolo (não tenta de novo);
 * - `failed`: erro fatal do servidor, como planta inexistente ou encerrada (não tenta de novo);
 * - `closed`: fechada por quem usa o cliente.
 */
export type ConnectionStatus =
  'connecting' | 'connected' | 'reconnecting' | 'incompatible' | 'failed' | 'closed';

/** O mínimo de `WebSocket` que o cliente usa; substituível nos testes. */
export interface SocketLike {
  onopen: ((event: Event) => void) | null;
  onmessage: ((event: MessageEvent) => void) | null;
  onclose: ((event: CloseEvent) => void) | null;
  onerror: ((event: Event) => void) | null;
  readonly readyState: number;
  send(data: string): void;
  close(code?: number, reason?: string): void;
}

export type SocketFactory = (url: string) => SocketLike;

export interface PlantSocketOptions {
  /** Cria o WebSocket; por padrão, o do navegador. */
  createSocket?: SocketFactory;
  /** Versão do protocolo anunciada no `hello` (só os testes mudam). */
  protocolVersion?: number;
  /** Primeira espera antes de reconectar, em ms; dobra a cada falha seguida. */
  reconnectInitialMs?: number;
  /** Maior espera antes de reconectar, em ms. */
  reconnectMaxMs?: number;
  /** Sem nenhuma mensagem por este tempo (ms), a conexão é dada como morta e refeita. */
  staleAfterMs?: number;
}

export interface PlantSocketHandlers {
  onStatus(status: ConnectionStatus): void;
  onMessage(message: ServerMessage): void;
}

const OPEN = 1;

/** URL do WebSocket de uma planta, no mesmo host e porta da página (ws em http, wss em https). */
export function plantSocketUrl(plantId: string, location: Location = window.location): string {
  const scheme = location.protocol === 'https:' ? 'wss:' : 'ws:';
  return `${scheme}//${location.host}${PlantSocketProtocol.path.replace('{id}', encodeURIComponent(plantId))}`;
}

/**
 * Cliente do WebSocket da planta (D05). Abre com `hello`, repassa as mensagens e, se a conexão cair, reconecta
 * sozinho com espera crescente (0,25 s, 0,5 s, 1 s, 2 s, 2 s…), sem trocar de planta. Não reconecta depois de
 * `incompatible` nem de erro fatal.
 */
export class PlantSocket {
  private readonly createSocket: SocketFactory;
  private readonly protocolVersion: number;
  private readonly reconnectInitialMs: number;
  private readonly reconnectMaxMs: number;
  private readonly staleAfterMs: number;
  private socket: SocketLike | null = null;
  private status: ConnectionStatus = 'connecting';
  private failures = 0;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private staleTimer: ReturnType<typeof setTimeout> | null = null;
  private stopped = false;

  constructor(
    private readonly url: string,
    private readonly handlers: PlantSocketHandlers,
    options: PlantSocketOptions = {},
  ) {
    this.createSocket = options.createSocket ?? ((u) => new WebSocket(u));
    this.protocolVersion = options.protocolVersion ?? PlantSocketProtocol.version;
    this.reconnectInitialMs = options.reconnectInitialMs ?? 250;
    this.reconnectMaxMs = options.reconnectMaxMs ?? 2000;
    this.staleAfterMs = options.staleAfterMs ?? 3000;
  }

  get currentStatus(): ConnectionStatus {
    return this.status;
  }

  start(): void {
    this.stopped = false;
    this.setStatus('connecting');
    this.open();
  }

  /** Manda um comando; falso se a conexão não estiver pronta. */
  send(message: ClientMessage): boolean {
    if (this.status !== 'connected' || this.socket?.readyState !== OPEN) {
      return false;
    }
    this.socket.send(JSON.stringify(message));
    return true;
  }

  stop(): void {
    this.stopped = true;
    this.clearTimers();
    const socket = this.socket;
    this.socket = null;
    socket?.close(1000);
    this.setStatus('closed');
  }

  private open(): void {
    const socket = this.createSocket(this.url);
    this.socket = socket;

    socket.onopen = () => {
      if (this.socket !== socket) return;
      socket.send(JSON.stringify({ type: 'hello', protocolVersion: this.protocolVersion }));
      this.armStaleTimer();
    };

    socket.onmessage = (event: MessageEvent) => {
      if (this.socket !== socket) return;
      this.armStaleTimer();
      let message: ServerMessage;
      try {
        message = JSON.parse(String(event.data)) as ServerMessage;
      } catch {
        return;
      }
      this.handle(message as ServerMessage | WelcomeMessage);
    };

    socket.onclose = () => {
      if (this.socket !== socket) return;
      this.socket = null;
      this.clearTimers();
      if (this.stopped || this.status === 'incompatible' || this.status === 'failed') {
        return;
      }
      this.scheduleReconnect();
    };

    socket.onerror = () => {
      // O navegador fecha o socket depois do erro; a reconexão sai do onclose.
    };
  }

  private handle(message: ServerMessage): void {
    switch (message.type) {
      case 'welcome':
        this.failures = 0;
        this.setStatus('connected');
        break;
      case 'incompatible':
        this.setStatus('incompatible');
        break;
      case 'error':
        if (message.fatal) {
          this.setStatus('failed');
        }
        break;
    }
    this.handlers.onMessage(message);
  }

  private scheduleReconnect(): void {
    const delay = Math.min(this.reconnectInitialMs * 2 ** this.failures, this.reconnectMaxMs);
    this.failures++;
    this.setStatus('reconnecting');
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      if (!this.stopped) {
        this.open();
      }
    }, delay);
  }

  /** Sem mensagens por `staleAfterMs`, fecha e deixa o onclose reconectar. */
  private armStaleTimer(): void {
    if (this.staleTimer !== null) clearTimeout(this.staleTimer);
    this.staleTimer = setTimeout(() => {
      this.staleTimer = null;
      const socket = this.socket;
      if (socket) {
        // Alguns navegadores demoram a disparar onclose numa conexão morta: reconecta já.
        this.socket = null;
        socket.close(4000, 'stale');
        if (!this.stopped && this.status !== 'incompatible' && this.status !== 'failed') {
          this.scheduleReconnect();
        }
      }
    }, this.staleAfterMs);
  }

  private clearTimers(): void {
    if (this.reconnectTimer !== null) clearTimeout(this.reconnectTimer);
    if (this.staleTimer !== null) clearTimeout(this.staleTimer);
    this.reconnectTimer = null;
    this.staleTimer = null;
  }

  private setStatus(status: ConnectionStatus): void {
    if (this.status === status) return;
    this.status = status;
    this.handlers.onStatus(status);
  }
}
