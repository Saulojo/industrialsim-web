import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { FakeSocket } from '../../net/fakeSocket';
import { modbus, plantId, state, welcome } from '../../net/testData';
import { PlantPage } from './PlantPage';

function renderPlant() {
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
  });
  return socket;
}

describe('PlantPage', () => {
  beforeEach(() => FakeSocket.reset());

  it('mostra conexão, dados do mestre, valores, tendência e alarmes', () => {
    const socket = renderPlant();

    expect(screen.getByTestId('connection-status')).toHaveTextContent('Conectado');
    expect(screen.getByTestId('simulated-time')).toHaveTextContent('12,50 s');
    expect(screen.getByTestId('modbus-port')).toHaveTextContent('15003');
    expect(screen.getByTestId('modbus-unit-id')).toHaveTextContent('1');
    expect(screen.getByTestId('master-status')).toHaveTextContent('Nenhum mestre conectado');
    expect(screen.getByTestId('trend-value-PT-101')).toHaveTextContent('10,00 bar');
    expect(screen.getByTestId('trend-value-FT-102')).toHaveAttribute('data-value', '2');
    expect(screen.getByTestId('alarms-none')).toHaveTextContent('Nenhum alarme ativo');
    expect(screen.getByTestId('download-csv')).toHaveAttribute(
      'href',
      `/api/plants/${plantId}/recording`,
    );

    act(() =>
      socket.receive({
        type: 'alarms',
        active: [{ signal: 'LSLL-101', sinceTime: 3.2, sinceUtc: '2026-11-19T12:00:00Z' }],
      }),
    );
    expect(screen.getByRole('alert')).toHaveTextContent('LSLL-101 · Nível muito baixo');
  });

  it('pergunta pelo pareamento e mostra "Mestre conectado" depois do aceite', () => {
    const socket = renderPlant();
    const request = {
      id: 'b3c1',
      address: '127.0.0.1',
      requestedUtc: '2026-11-19T12:00:00Z',
      expiresUtc: '2026-11-19T12:01:00Z',
      waitingConnections: 1,
    };

    act(() => socket.receive(modbus({ pendingRequests: [request] })));
    const dialog = screen.getByTestId('pairing-dialog');
    expect(dialog).toHaveTextContent('Aceitar conexão de 127.0.0.1?');

    fireEvent.click(within(dialog).getByRole('button', { name: 'Aceitar' }));
    expect(socket.sent.at(-1)).toEqual({ type: 'acceptPairing', requestId: 'b3c1' });

    act(() =>
      socket.receive(
        modbus({
          authorizedMasters: ['127.0.0.1'],
          connections: [
            {
              id: 1,
              address: '127.0.0.1',
              port: 50000,
              connectedUtc: '2026-11-19T12:00:00Z',
              authorized: true,
            },
          ],
        }),
      ),
    );
    expect(screen.queryByTestId('pairing-dialog')).not.toBeInTheDocument();
    expect(screen.getByTestId('master-status')).toHaveTextContent('Mestre conectado');
  });

  it('recusa o pareamento pelo botão Recusar', () => {
    const socket = renderPlant();
    const request = {
      id: 'c4d2',
      address: '10.0.0.7',
      requestedUtc: '2026-11-19T12:00:00Z',
      expiresUtc: '2026-11-19T12:01:00Z',
      waitingConnections: 1,
    };
    act(() => socket.receive(modbus({ pendingRequests: [request] })));

    fireEvent.click(screen.getByRole('button', { name: 'Recusar' }));

    expect(socket.sent.at(-1)).toEqual({ type: 'refusePairing', requestId: 'c4d2' });
  });

  it('manda o distúrbio em SI quando o controle muda', () => {
    const socket = renderPlant();

    fireEvent.change(screen.getByTestId('disturbance-FV-103_command'), { target: { value: '90' } });

    expect(socket.sent.at(-1)).toEqual({
      type: 'setDisturbance',
      input: 'FV-103_command',
      value: 0.9,
    });
  });

  it('mostra a incompatibilidade de versão com as duas versões', () => {
    render(
      <PlantPage
        plantId={plantId}
        storeOptions={{
          url: 'ws://teste',
          createSocket: (url) => new FakeSocket(url),
          protocolVersion: 2,
        }}
      />,
    );
    act(() => {
      FakeSocket.last.open();
      FakeSocket.last.receive({
        type: 'incompatible',
        serverProtocolVersion: 1,
        clientProtocolVersion: 2,
      });
    });

    expect(screen.getByTestId('connection-status')).toHaveTextContent('Versão incompatível');
    expect(screen.getByTestId('incompatible')).toHaveTextContent(
      'Esta página usa a versão 2 do protocolo, mas o servidor usa a versão 1.',
    );
  });

  it('mostra a reconexão quando a conexão cai', () => {
    const socket = renderPlant();

    act(() => socket.drop());

    expect(screen.getByTestId('connection-status')).toHaveTextContent(
      'Conexão perdida; reconectando…',
    );
  });
});
