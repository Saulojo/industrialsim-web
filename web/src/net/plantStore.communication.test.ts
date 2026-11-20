import { beforeEach, describe, expect, it } from 'vitest';
import { FakeSocket } from './fakeSocket';
import { logLimit, mergeLog, PlantStore } from './plantStore';
import { logEntry, monitor, monitorLayout, plantId, settings, welcome } from './testData';

function connectedStore() {
  const store = new PlantStore(plantId, {
    url: 'ws://teste',
    createSocket: (url) => new FakeSocket(url),
  });
  store.start();
  FakeSocket.last.open();
  FakeSocket.last.receive(welcome);
  return store;
}

describe('PlantStore: configurações, monitor e log (Etapa 8)', () => {
  beforeEach(() => FakeSocket.reset());

  it('guarda configurações e forçados e manda os comandos novos', () => {
    const store = connectedStore();
    FakeSocket.last.receive(settings({ unitId: 3 }));
    FakeSocket.last.receive({
      type: 'forced',
      inputs: [{ area: 'input-register', signal: 'LT-101', value: 0.3 }],
    });

    expect(store.getSnapshot().settings?.settings.unitId).toBe(3);
    expect(store.getSnapshot().forced).toEqual([
      { area: 'input-register', signal: 'LT-101', value: 0.3 },
    ]);

    store.updateSettings({ format: 'float32', unitId: 9 });
    store.forceInput('input-register', 'LT-101', 0.5);
    store.releaseInput('input-register', 'LT-101');
    store.pause();
    store.resume();
    store.revokeMaster('10.0.0.1');

    expect(FakeSocket.last.sent.slice(1)).toEqual([
      {
        type: 'updateSettings',
        format: 'float32',
        wordOrder: null,
        watchdogEnabled: null,
        watchdogTimeoutSeconds: null,
        diagnosticsEnabled: null,
        unitId: 9,
        masterPolicy: null,
      },
      { type: 'forceInput', area: 'input-register', signal: 'LT-101', value: 0.5 },
      { type: 'releaseInput', area: 'input-register', signal: 'LT-101' },
      { type: 'pause' },
      { type: 'resume' },
      { type: 'revokeMaster', address: '10.0.0.1' },
    ]);
  });

  it('liga o monitor, guarda a última escrita conhecida e repete a assinatura ao reconectar', () => {
    const store = connectedStore();
    store.setMonitor(true);
    expect(FakeSocket.last.sent.at(-1)).toEqual({ type: 'subscribeMonitor', enabled: true });

    FakeSocket.last.receive(monitorLayout);
    FakeSocket.last.receive(monitor());
    FakeSocket.last.receive(monitor({ step: 1260, writes: null }));
    expect(store.getSnapshot().monitor?.step).toBe(1260);
    expect(store.getSnapshot().monitorWrites).toEqual([
      { point: 2, author: '192.168.0.20', timeUtc: '2026-11-19T12:00:05Z' },
    ]);

    // Nova conexão: o servidor não sabe do monitor, e a IHM pede de novo.
    const first = FakeSocket.last;
    first.drop();
    FakeSocket.instances.length = 0;
    store.stop();
    store.start();
    FakeSocket.last.open();
    FakeSocket.last.receive(welcome);
    expect(FakeSocket.last.sent).toContainEqual({ type: 'subscribeMonitor', enabled: true });

    store.setMonitor(false);
    expect(FakeSocket.last.sent.at(-1)).toEqual({ type: 'subscribeMonitor', enabled: false });
    expect(store.getSnapshot().monitor).toBeNull();
  });

  it('o log começa do zero no reset e troca eventos agrupados pela versão nova', () => {
    const store = connectedStore();
    FakeSocket.last.receive({
      type: 'log',
      reset: true,
      entries: [logEntry(1, 'paused'), logEntry(2, 'modbus-exception')],
    });
    FakeSocket.last.receive({
      type: 'log',
      reset: false,
      entries: [logEntry(2, 'modbus-exception', {}, { sequence: 3, count: 2 })],
    });

    expect(store.getSnapshot().log.map((e) => [e.id, e.sequence, e.count])).toEqual([
      [1, 1, 1],
      [2, 3, 2],
    ]);

    FakeSocket.last.receive({ type: 'log', reset: true, entries: [logEntry(7, 'resumed')] });
    expect(store.getSnapshot().log.map((e) => e.id)).toEqual([7]);
  });

  it('o log guarda só os eventos mais recentes', () => {
    const entries = Array.from({ length: logLimit + 5 }, (_, i) => logEntry(i + 1, 'paused'));
    const merged = mergeLog([], entries);
    expect(merged).toHaveLength(logLimit);
    expect(merged[0]?.id).toBe(6);
  });
});
