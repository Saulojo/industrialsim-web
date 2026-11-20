import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PlantSocketProtocol, type ServerMessage } from './contracts.generated';
import { FakeSocket } from './fakeSocket';
import { PlantSocket, plantSocketUrl, type ConnectionStatus } from './plantSocket';
import { welcome } from './testData';

function start(options = {}) {
  const statuses: ConnectionStatus[] = [];
  const messages: ServerMessage[] = [];
  const socket = new PlantSocket(
    'ws://teste/api/plants/x/ws',
    { onStatus: (s) => statuses.push(s), onMessage: (m) => messages.push(m) },
    { createSocket: (url) => new FakeSocket(url), ...options },
  );
  socket.start();
  return { socket, statuses, messages };
}

describe('PlantSocket', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    FakeSocket.reset();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('monta a URL no mesmo host da página, com wss em https', () => {
    expect(plantSocketUrl('abc', { protocol: 'https:', host: 'sim.exemplo:443' } as Location)).toBe(
      'wss://sim.exemplo:443/api/plants/abc/ws',
    );
    expect(plantSocketUrl('abc', { protocol: 'http:', host: 'localhost:5080' } as Location)).toBe(
      'ws://localhost:5080/api/plants/abc/ws',
    );
  });

  it('abre com hello na versão do protocolo e fica conectado com o welcome', () => {
    const { statuses, messages } = start();

    FakeSocket.last.open();
    expect(FakeSocket.last.sent).toEqual([
      { type: 'hello', protocolVersion: PlantSocketProtocol.version },
    ]);

    FakeSocket.last.receive(welcome);
    expect(statuses).toEqual(['connected']);
    expect(messages).toEqual([welcome]);
  });

  it('reconecta sozinho depois de uma queda, com espera crescente e limitada a 2 s', () => {
    const { statuses } = start();
    FakeSocket.last.open();
    FakeSocket.last.receive(welcome);

    const delays: number[] = [];
    for (let attempt = 0; attempt < 5; attempt++) {
      const before = FakeSocket.instances.length;
      FakeSocket.last.drop();
      let waited = 0;
      while (FakeSocket.instances.length === before) {
        vi.advanceTimersByTime(50);
        waited += 50;
      }
      delays.push(waited);
    }

    expect(delays).toEqual([250, 500, 1000, 2000, 2000]);
    expect(statuses.at(-1)).toBe('reconnecting');

    // A nova conexão volta à mesma planta e zera a espera.
    expect(FakeSocket.last.url).toBe('ws://teste/api/plants/x/ws');
    FakeSocket.last.open();
    FakeSocket.last.receive(welcome);
    expect(statuses.at(-1)).toBe('connected');
    FakeSocket.last.drop();
    vi.advanceTimersByTime(250);
    expect(FakeSocket.instances).toHaveLength(7);
  });

  it('refaz uma conexão que parou de mandar mensagens', () => {
    const { statuses } = start({ staleAfterMs: 3000 });
    FakeSocket.last.open();
    FakeSocket.last.receive(welcome);
    const first = FakeSocket.last;

    vi.advanceTimersByTime(3000);

    expect(first.closedByClient).toBe(true);
    expect(statuses.at(-1)).toBe('reconnecting');
    vi.advanceTimersByTime(250);
    expect(FakeSocket.instances).toHaveLength(2);
  });

  it('não reconecta quando o servidor fala outra versão do protocolo', () => {
    const { statuses, messages } = start({ protocolVersion: 99 });
    FakeSocket.last.open();
    expect(FakeSocket.last.sent).toEqual([{ type: 'hello', protocolVersion: 99 }]);

    FakeSocket.last.receive({
      type: 'incompatible',
      serverProtocolVersion: 1,
      clientProtocolVersion: 99,
    });
    FakeSocket.last.drop();
    vi.advanceTimersByTime(10_000);

    expect(statuses.at(-1)).toBe('incompatible');
    expect(messages.at(-1)?.type).toBe('incompatible');
    expect(FakeSocket.instances).toHaveLength(1);
  });

  it('não reconecta depois de erro fatal, mas segue depois de erro comum', () => {
    const { statuses } = start();
    FakeSocket.last.open();
    FakeSocket.last.receive(welcome);

    FakeSocket.last.receive({ type: 'error', code: 'invalid-message', fatal: false });
    expect(statuses.at(-1)).toBe('connected');

    FakeSocket.last.receive({ type: 'error', code: 'plant-ended', fatal: true });
    FakeSocket.last.drop();
    vi.advanceTimersByTime(10_000);
    expect(statuses.at(-1)).toBe('failed');
    expect(FakeSocket.instances).toHaveLength(1);
  });

  it('só manda comandos com a conexão pronta e para de vez com stop', () => {
    const { socket, statuses } = start();
    expect(socket.send({ type: 'restart' })).toBe(false);

    FakeSocket.last.open();
    FakeSocket.last.receive(welcome);
    expect(socket.send({ type: 'restart' })).toBe(true);
    expect(FakeSocket.last.sent.at(-1)).toEqual({ type: 'restart' });

    socket.stop();
    vi.advanceTimersByTime(10_000);
    expect(statuses.at(-1)).toBe('closed');
    expect(FakeSocket.instances).toHaveLength(1);
  });
});
