import type { ModbusStatusMessage, StateMessage, WelcomeMessage } from './contracts.generated';

/** Mensagens de exemplo da caldeira, para os testes. */
export const plantId = '7b5c0e0e-1d2a-4f39-9a51-0d6a3f1f2b11';

export const welcome: WelcomeMessage = {
  type: 'welcome',
  protocolVersion: 1,
  serverVersion: '0.1.0',
  plant: {
    id: plantId,
    scenarioId: 'caldeira',
    noiseSeed: 101,
    status: 'running',
    createdUtc: '2026-11-19T12:00:00+00:00',
    lastActivityUtc: '2026-11-19T12:00:00+00:00',
    clientCount: 1,
    recording: true,
    modbusPort: 15003,
  },
  stepSeconds: 0.01,
  statePeriodSeconds: 0.05,
  serverAddress: null,
  inputs: [
    {
      name: 'P-101_run',
      kind: 'digital',
      unit: '',
      unitFactor: 1,
      lrv: null,
      urv: null,
      modbus: null,
    },
    {
      name: 'FV-101_command',
      kind: 'analog',
      unit: '%',
      unitFactor: 100,
      lrv: 0,
      urv: 1,
      modbus: null,
    },
    {
      name: 'FV-102_command',
      kind: 'analog',
      unit: '%',
      unitFactor: 100,
      lrv: 0,
      urv: 1,
      modbus: null,
    },
    {
      name: 'FV-103_command',
      kind: 'analog',
      unit: '%',
      unitFactor: 100,
      lrv: 0,
      urv: 1,
      modbus: null,
    },
  ],
  outputs: [
    { name: 'LT-101', kind: 'analog', unit: 'm', unitFactor: 1, lrv: 0, urv: 1.2, modbus: null },
    {
      name: 'PT-101',
      kind: 'analog',
      unit: 'bar',
      unitFactor: 1e-5,
      lrv: 0,
      urv: 1.6e6,
      modbus: null,
    },
    { name: 'FT-101', kind: 'analog', unit: 'kg/s', unitFactor: 1, lrv: 0, urv: 4, modbus: null },
    { name: 'FT-102', kind: 'analog', unit: 'kg/s', unitFactor: 1, lrv: 0, urv: 5, modbus: null },
    {
      name: 'P-101_running',
      kind: 'digital',
      unit: '',
      unitFactor: 1,
      lrv: null,
      urv: null,
      modbus: null,
    },
  ],
  disturbances: [{ input: 'FV-103_command', min: 0, max: 1, unit: '%', unitFactor: 100 }],
  alarms: ['LSLL-101', 'watchdog'],
};

export function state(time: number, run = 0, outputs = [0.6, 1e6, 2, 2, 1]): StateMessage {
  return {
    type: 'state',
    step: Math.round(time / 0.01),
    time,
    run,
    status: 'running',
    watchdog: false,
    inputs: [1, 0.8228, 0.5, 0.5],
    outputs,
  };
}

export function modbus(changes: Partial<ModbusStatusMessage> = {}): ModbusStatusMessage {
  return {
    type: 'modbus',
    port: 15003,
    unitId: 1,
    authorizedMasters: [],
    pendingRequests: [],
    connections: [],
    ...changes,
  };
}
