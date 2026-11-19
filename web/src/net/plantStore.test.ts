import { beforeEach, describe, expect, it } from 'vitest';
import { FakeSocket } from './fakeSocket';
import { isMasterConnected, PlantStore } from './plantStore';
import { modbus, plantId, state, welcome } from './testData';

function connectedStore(trendWindowSeconds = 120) {
  const store = new PlantStore(plantId, {
    url: 'ws://teste',
    createSocket: (url) => new FakeSocket(url),
    trendWindowSeconds,
  });
  store.start();
  FakeSocket.last.open();
  FakeSocket.last.receive(welcome);
  return store;
}

describe('PlantStore', () => {
  beforeEach(() => FakeSocket.reset());

  it('guarda welcome, estado, Modbus e alarmes e avisa quem assina', () => {
    const store = connectedStore();
    let notified = 0;
    store.subscribe(() => notified++);

    FakeSocket.last.receive(state(1));
    FakeSocket.last.receive(modbus({ authorizedMasters: ['127.0.0.1'] }));
    FakeSocket.last.receive({
      type: 'alarms',
      active: [{ signal: 'LSLL-101', sinceTime: 0.5, sinceUtc: '2026-11-19T12:00:00Z' }],
    });

    const view = store.getSnapshot();
    expect(view.status).toBe('connected');
    expect(view.welcome?.plant.id).toBe(plantId);
    expect(view.state?.time).toBe(1);
    expect(view.modbus?.authorizedMasters).toEqual(['127.0.0.1']);
    expect(view.alarms.map((a) => a.signal)).toEqual(['LSLL-101']);
    expect(notified).toBe(3);
  });

  it('mantém a tendência na janela de tempo e a recomeça quando a planta é reiniciada', () => {
    const store = connectedStore(1);
    for (let i = 0; i <= 40; i++) {
      FakeSocket.last.receive(state(i * 0.05));
    }

    let trend = store.getSnapshot().trend;
    expect(trend[0]?.time).toBeCloseTo(1.0);
    expect(trend.at(-1)?.time).toBeCloseTo(2.0);
    expect(trend).toHaveLength(21);

    FakeSocket.last.receive(state(0.05, 1));
    trend = store.getSnapshot().trend;
    expect(trend.map((p) => p.time)).toEqual([0.05]);
  });

  it('não perde a tendência quando reconecta à mesma planta', () => {
    const store = connectedStore();
    FakeSocket.last.receive(state(1));
    FakeSocket.last.receive(state(1.05));

    FakeSocket.last.drop();
    FakeSocket.reset();
    store.stop();
    store.start();
    FakeSocket.last.open();
    FakeSocket.last.receive(welcome);
    FakeSocket.last.receive(state(2));

    expect(store.getSnapshot().trend.map((p) => p.time)).toEqual([1, 1.05, 2]);
  });

  it('manda os comandos da IHM no formato do contrato', () => {
    const store = connectedStore();

    store.setDisturbance('FV-103_command', 0.9);
    store.restart();
    store.acceptPairing('a');
    store.refusePairing('b');

    expect(FakeSocket.last.sent.slice(1)).toEqual([
      { type: 'setDisturbance', input: 'FV-103_command', value: 0.9 },
      { type: 'restart' },
      { type: 'acceptPairing', requestId: 'a' },
      { type: 'refusePairing', requestId: 'b' },
    ]);
  });

  it('considera mestre conectado só com conexão autorizada', () => {
    const connection = {
      id: 1,
      address: '127.0.0.1',
      port: 50000,
      connectedUtc: '2026-11-19T12:00:00Z',
      authorized: false,
    };
    expect(isMasterConnected(null)).toBe(false);
    expect(isMasterConnected(modbus({ connections: [connection] }))).toBe(false);
    expect(isMasterConnected(modbus({ connections: [{ ...connection, authorized: true }] }))).toBe(
      true,
    );
  });
});
