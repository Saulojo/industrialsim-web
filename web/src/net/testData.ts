import type {
  LogEntryInfo,
  ModbusStatusMessage,
  MonitorLayoutMessage,
  MonitorMessage,
  PlantSettingsInfo,
  SettingsMessage,
  StateMessage,
  WelcomeMessage,
} from './contracts.generated';

/** Mensagens de exemplo da caldeira, para os testes. */
export const plantId = '7b5c0e0e-1d2a-4f39-9a51-0d6a3f1f2b11';

export const welcome: WelcomeMessage = {
  type: 'welcome',
  protocolVersion: 2,
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
    requestsPerSecond: 0,
    requests: 0,
    exceptionResponses: 0,
    ...changes,
  };
}

export function settings(changes: Partial<PlantSettingsInfo> = {}): SettingsMessage {
  return {
    type: 'settings',
    settings: {
      format: 'counts27648',
      wordOrder: 'highWordFirst',
      watchdogEnabled: false,
      watchdogTimeoutSeconds: 2,
      diagnosticsEnabled: true,
      unitId: 1,
      masterPolicy: 'multiple',
      ...changes,
    },
    maxWatchdogTimeoutSeconds: 600,
  };
}

/** Linhas do monitor: um coil, uma entrada discreta, um holding register e duas medições de LT-101. */
export const monitorLayout: MonitorLayoutMessage = {
  type: 'monitorLayout',
  format: 'counts27648',
  wordOrder: 'highWordFirst',
  points: [
    {
      area: 'coil',
      address: 0,
      classicAddress: 1,
      length: 1,
      signal: 'P-101_run',
      unit: '',
      kind: 'digital',
    },
    {
      area: 'discrete-input',
      address: 2,
      classicAddress: 10003,
      length: 1,
      signal: 'LSHH-101',
      unit: '',
      kind: 'digital',
    },
    {
      area: 'holding-register',
      address: 0,
      classicAddress: 40001,
      length: 1,
      signal: 'FV-101_command',
      unit: '%',
      kind: 'analog',
    },
    {
      area: 'input-register',
      address: 0,
      classicAddress: 30001,
      length: 1,
      signal: 'LT-101',
      unit: 'm',
      kind: 'analog',
    },
    {
      area: 'input-register',
      address: 100,
      classicAddress: 30101,
      length: 2,
      signal: 'LT-101',
      unit: 'm',
      kind: 'diagnostic',
    },
  ],
};

export function monitor(changes: Partial<MonitorMessage> = {}): MonitorMessage {
  return {
    type: 'monitor',
    step: 1250,
    raw: [1, 0, 22749, 13824, 0x3f19, 0x999a],
    values: [1, 0, 82.28, 0.6, 0.6],
    forced: [],
    writes: [{ point: 2, author: '192.168.0.20', timeUtc: '2026-11-19T12:00:05Z' }],
    ...changes,
  };
}

export function logEntry(
  id: number,
  kind: string,
  args: Record<string, string> = {},
  changes: Partial<LogEntryInfo> = {},
): LogEntryInfo {
  return {
    id,
    sequence: id,
    kind,
    firstUtc: '2026-11-19T12:00:00Z',
    timeUtc: '2026-11-19T12:00:00Z',
    count: 1,
    arguments: Object.entries(args).map(([name, value]) => ({ name, value })),
    ...changes,
  };
}
