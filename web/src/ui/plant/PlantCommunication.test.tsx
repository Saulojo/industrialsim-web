import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { FakeSocket } from '../../net/fakeSocket';
import {
  logEntry,
  modbus,
  monitor,
  monitorLayout,
  plantId,
  settings,
  state,
  welcome,
} from '../../net/testData';
import { rawText } from './MonitorPanel';
import { PlantPage } from './PlantPage';
import { settingsChanges } from './SettingsPanel';

function renderPlant() {
  window.location.hash = '';
  render(
    <PlantPage
      plantId={plantId}
      storeOptions={{ url: 'ws://teste', createSocket: (url) => new FakeSocket(url) }}
    />,
  );
  const socket = FakeSocket.last;
  act(() => {
    socket.open();
    socket.receive(welcome);
    socket.receive(state(12.5));
    socket.receive(modbus());
    socket.receive(settings());
    socket.receive({ type: 'forced', inputs: [] });
    socket.receive({ type: 'log', reset: true, entries: [] });
  });
  return socket;
}

describe('PlantPage: configurações, monitor, log e pausa (Etapa 8)', () => {
  beforeEach(() => {
    FakeSocket.reset();
    window.localStorage.clear();
  });

  it('aplica só as configurações mudadas', () => {
    const socket = renderPlant();
    fireEvent.click(screen.getByTestId('tab-communication'));

    expect(screen.getByTestId('settings-port')).toHaveTextContent('15003');
    expect(screen.getByTestId('settings-apply')).toBeDisabled();
    fireEvent.change(screen.getByTestId('settings-format'), { target: { value: 'float32' } });
    fireEvent.change(screen.getByTestId('settings-unit-id'), { target: { value: '17' } });
    fireEvent.click(screen.getByTestId('settings-apply'));

    expect(socket.sent.at(-1)).toEqual({
      type: 'updateSettings',
      format: 'float32',
      wordOrder: null,
      watchdogEnabled: null,
      watchdogTimeoutSeconds: null,
      diagnosticsEnabled: null,
      unitId: 17,
      masterPolicy: null,
    });

    // O servidor confirma: a tela passa a mostrar o que vale, sem nada pendente.
    act(() => socket.receive(settings({ format: 'float32', unitId: 17 })));
    expect(screen.getByTestId('settings-format')).toHaveValue('float32');
    expect(screen.getByTestId('settings-apply')).toBeDisabled();
  });

  it('não aplica um Unit ID fora de 1 a 255', () => {
    renderPlant();
    fireEvent.click(screen.getByTestId('tab-communication'));
    fireEvent.change(screen.getByTestId('settings-unit-id'), { target: { value: '300' } });
    expect(screen.getByTestId('settings-apply')).toBeDisabled();

    const current = settings().settings;
    expect(
      settingsChanges(current, { ...current, unitId: '0', watchdogTimeoutSeconds: '2' }, 600),
    ).toBeNull();
    expect(
      settingsChanges(current, { ...current, unitId: '1', watchdogTimeoutSeconds: '2,5' }, 600),
    ).toEqual({ watchdogTimeoutSeconds: 2.5 });
  });

  it('o monitor liga ao abrir, mostra autor e notação, destaca o forçado e força uma entrada', () => {
    const socket = renderPlant();
    fireEvent.click(screen.getByTestId('tab-monitor'));
    expect(socket.sent.at(-1)).toEqual({ type: 'subscribeMonitor', enabled: true });

    act(() => {
      socket.receive(monitorLayout);
      socket.receive(monitor({ forced: [3] }));
    });

    const command = screen.getByTestId('monitor-holding-register-FV-101_command-analog');
    expect(command).toHaveAttribute('data-author', '192.168.0.20');
    expect(command).toHaveTextContent('192.168.0.20 às');
    expect(within(command).getAllByRole('cell')[0]).toHaveTextContent('0');
    fireEvent.click(screen.getByTestId('notation-classic'));
    expect(within(command).getAllByRole('cell')[0]).toHaveTextContent('40001');
    expect(window.localStorage.getItem('industrialsim.addressNotation')).toBe('classic');

    const level = screen.getByTestId('monitor-input-register-LT-101-analog');
    expect(level).toHaveAttribute('data-forced', 'true');
    expect(level).toHaveClass('monitor__row--forced');
    expect(level).toHaveTextContent('FORÇADO');
    fireEvent.click(within(level).getByRole('button', { name: 'Liberar' }));
    expect(socket.sent.at(-1)).toEqual({
      type: 'releaseInput',
      area: 'input-register',
      signal: 'LT-101',
    });

    const lshh = screen.getByTestId('monitor-discrete-input-LSHH-101-digital');
    fireEvent.click(within(lshh).getByRole('button', { name: 'Forçar' }));
    expect(socket.sent.at(-1)).toEqual({
      type: 'forceInput',
      area: 'discrete-input',
      signal: 'LSHH-101',
      value: 1,
    });

    fireEvent.click(screen.getByTestId('tab-process'));
    expect(socket.sent.at(-1)).toEqual({ type: 'subscribeMonitor', enabled: false });
  });

  it('um valor forçado aparece destacado no sinóptico e no painel de forçados', () => {
    const socket = renderPlant();
    act(() =>
      socket.receive({
        type: 'forced',
        inputs: [{ area: 'input-register', signal: 'LT-101', value: 0.3 }],
      }),
    );

    expect(screen.getByTestId('forced-LT-101')).toHaveTextContent(
      'LT-101 · Nível do tubulão: 0,300 m',
    );
    const reading = document.querySelector('text[data-signal="LT-101"]');
    expect(reading).toHaveAttribute('data-forced', 'true');
    expect(reading).toHaveTextContent('FORÇADO');
  });

  it('pausar manda o comando; pausada, a planta mostra o aviso e o botão retoma', () => {
    const socket = renderPlant();
    fireEvent.click(screen.getByTestId('pause'));
    expect(socket.sent.at(-1)).toEqual({ type: 'pause' });

    act(() => socket.receive({ ...state(12.5), status: 'paused' }));
    expect(screen.getByTestId('plant-status')).toHaveTextContent('Pausada');
    expect(screen.getByTestId('paused-notice')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('tab-monitor'));
    expect(screen.getByTestId('monitor-paused')).toHaveTextContent('Planta pausada');

    fireEvent.click(screen.getByTestId('pause'));
    expect(socket.sent.at(-1)).toEqual({ type: 'resume' });
  });

  it('o log mostra os eventos traduzidos, o mais novo primeiro, com autor e contagem', () => {
    const socket = renderPlant();
    fireEvent.click(screen.getByTestId('tab-log'));
    expect(screen.getByTestId('log')).toHaveTextContent('Nenhum evento ainda.');

    act(() =>
      socket.receive({
        type: 'log',
        reset: false,
        entries: [
          logEntry(1, 'setting-changed', {
            setting: 'unitId',
            old: '1',
            new: '5',
            author: '10.0.0.9',
          }),
          logEntry(
            2,
            'modbus-exception',
            { address: '10.0.0.5', function: '3', code: '2' },
            { count: 4 },
          ),
        ],
      }),
    );

    const rows = screen.getAllByTestId('log-entry');
    expect(rows[0]).toHaveTextContent('Exceção 2 (endereço ilegal) para 10.0.0.5 na função 3.');
    expect(rows[0]).toHaveTextContent('×4');
    expect(rows[1]).toHaveTextContent('Unit ID: 1 → 5.');
    expect(rows[1]).toHaveTextContent('10.0.0.9');
  });

  it('no modo um mestre por vez, o pedido avisa qual mestre será desconectado', () => {
    const socket = renderPlant();
    act(() => {
      socket.receive(settings({ masterPolicy: 'single' }));
      socket.receive(
        modbus({
          authorizedMasters: ['10.0.0.1'],
          pendingRequests: [
            {
              id: 'a1',
              address: '10.0.0.2',
              requestedUtc: '2026-11-19T12:00:00Z',
              expiresUtc: '2026-11-19T12:01:00Z',
              waitingConnections: 1,
            },
          ],
        }),
      );
    });

    expect(screen.getByTestId('pairing-replaces')).toHaveTextContent(
      'aceitar desconecta o mestre 10.0.0.1',
    );
  });

  it('exporta o mapa de I/O em CSV e abre a página do PDF', () => {
    renderPlant();
    expect(screen.getByTestId('io-map-csv')).toHaveAttribute(
      'href',
      `/api/plants/${plantId}/io-map.csv`,
    );
    expect(screen.getByTestId('io-map-pdf')).toHaveAttribute('href', `/plants/${plantId}/io-map`);
  });
});

describe('rawText', () => {
  it('mostra INT com sinal, UINT no heartbeat e as palavras do float32 em hexadecimal', () => {
    const [, , , analog, diagnostic] = monitorLayout.points;
    if (!analog || !diagnostic) throw new Error('Linhas de exemplo faltando.');
    expect(rawText(analog, [0xed00])).toBe('-4864');
    expect(rawText({ ...analog, kind: 'heartbeat' }, [0xed00])).toBe('60672');
    expect(rawText(diagnostic, [0x3f19, 0x999a])).toBe('0x3F19 0x999A');
  });
});
