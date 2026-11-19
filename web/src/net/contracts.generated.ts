// Gerado por IndustrialSim.Contracts.TypeScriptWriter a partir do contrato C# (D05, D40).
// Não edite à mão: rode `npm run contracts` na pasta web.

export interface AcceptPairingCommand {
  type: 'acceptPairing';
  requestId: string;
}

export interface ActiveAlarm {
  signal: string;
  sinceTime: number;
  sinceUtc: string;
}

export const AlarmSignals = {
  watchdog: 'watchdog',
} as const;

export interface AlarmsMessage {
  type: 'alarms';
  active: ActiveAlarm[];
}

export interface ApiError {
  code: string;
}

export const ApiErrorCodes = {
  scenarioNotFound: 'scenario-not-found',
  plantNotFound: 'plant-not-found',
  instanceLimitReached: 'instance-limit-reached',
  recordingNotFound: 'recording-not-found',
  invalidRequest: 'invalid-request',
  modbusPortsExhausted: 'modbus-ports-exhausted',
  pairingRequestNotFound: 'pairing-request-not-found',
  masterNotFound: 'master-not-found',
} as const;

export type ClientMessage =
  | HelloMessage
  | SetDisturbanceCommand
  | RestartCommand
  | AcceptPairingCommand
  | RefusePairingCommand;

export interface DisturbanceInfo {
  input: string;
  min: number;
  max: number;
  unit: string;
  unitFactor: number;
}

export interface DurationStats {
  count: number;
  meanMs: number;
  p50Ms: number;
  p99Ms: number;
  maxMs: number;
}

export interface HealthResponse {
  version: string;
  serverTimeUtc: string;
}

export interface HelloMessage {
  type: 'hello';
  protocolVersion: number;
}

export interface IncompatibleMessage {
  type: 'incompatible';
  serverProtocolVersion: number;
  clientProtocolVersion: number;
}

export interface LoopMetricsResponse {
  threadCount: number;
  plantCount: number;
  waiterKind: string;
  cycles: number;
  steps: number;
  lateCycles: number;
  droppedCycles: number;
  overruns: number;
  stepLateness: DurationStats;
  stepCost: DurationStats;
}

export interface MasterAuthorizeRequest {
  address: string;
}

export interface MasterConnectionInfo {
  id: number;
  address: string;
  port: number;
  connectedUtc: string;
  authorized: boolean;
}

export const ModbusAreas = {
  coil: 'coil',
  discreteInput: 'discrete-input',
  holdingRegister: 'holding-register',
  inputRegister: 'input-register',
} as const;

export interface ModbusConnectionResponse {
  id: number;
  address: string;
  port: number;
  connectedUtc: string;
  authorized: boolean;
  requests: number;
}

export interface ModbusInfoResponse {
  port: number;
  unitId: number;
  authorizedMasters: string[];
  pendingRequests: PairingRequestResponse[];
  connections: ModbusConnectionResponse[];
  requests: number;
  exceptionResponses: number;
}

export interface ModbusPointInfo {
  area: string;
  address: number;
  classicAddress: number;
}

export interface ModbusStatusMessage {
  type: 'modbus';
  port: number;
  unitId: number;
  authorizedMasters: string[];
  pendingRequests: PairingRequestResponse[];
  connections: MasterConnectionInfo[];
}

export interface ModbusWriteResponse {
  area: string;
  address: number;
  classicAddress: number;
  author: string;
  timeUtc: string;
}

export interface PairingRequestResponse {
  id: string;
  address: string;
  requestedUtc: string;
  expiresUtc: string;
  waitingConnections: number;
}

export interface PlantCreateRequest {
  scenarioId: string;
  noiseSeed: number | null;
  record: boolean;
}

export const PlantSocketProtocol = {
  version: 1,
  path: '/api/plants/{id}/ws',
} as const;

export interface PlantStateResponse {
  plant: PlantSummary;
  stepCount: number;
  simulatedSeconds: number;
  watchdogTripped: boolean;
  faultMessage: string | null;
  inputs: SignalReading[];
  outputs: SignalReading[];
  realTime: RealTimeStats;
}

export const PlantStatuses = {
  running: 'running',
  faulted: 'faulted',
  ended: 'ended',
} as const;

export interface PlantSummary {
  id: string;
  scenarioId: string;
  noiseSeed: number;
  status: string;
  createdUtc: string;
  lastActivityUtc: string;
  clientCount: number;
  recording: boolean;
  modbusPort: number;
}

export interface RealTimeStats {
  loopThread: number;
  wallSeconds: number;
  simulatedSeconds: number;
  drift: number;
}

export interface RefusePairingCommand {
  type: 'refusePairing';
  requestId: string;
}

export interface RestartCommand {
  type: 'restart';
}

export interface ScenarioSummary {
  id: string;
  stepSeconds: number;
  noiseSeed: number;
  inputs: string[];
  outputs: string[];
}

export type ServerMessage =
  | WelcomeMessage
  | IncompatibleMessage
  | SocketErrorMessage
  | StateMessage
  | ModbusStatusMessage
  | AlarmsMessage;

export interface SetDisturbanceCommand {
  type: 'setDisturbance';
  input: string;
  value: number;
}

export interface SignalInfo {
  name: string;
  kind: string;
  unit: string;
  unitFactor: number;
  lrv: number | null;
  urv: number | null;
  modbus: ModbusPointInfo | null;
}

export const SignalKinds = {
  analog: 'analog',
  digital: 'digital',
} as const;

export interface SignalReading {
  name: string;
  value: number;
}

export const SocketErrorCodes = {
  plantNotFound: 'plant-not-found',
  plantEnded: 'plant-ended',
  helloExpected: 'hello-expected',
  invalidMessage: 'invalid-message',
  disturbanceNotFound: 'disturbance-not-found',
  pairingRequestNotFound: 'pairing-request-not-found',
  plantNotRunning: 'plant-not-running',
} as const;

export interface SocketErrorMessage {
  type: 'error';
  code: string;
  fatal: boolean;
}

export interface StateMessage {
  type: 'state';
  step: number;
  time: number;
  run: number;
  status: string;
  watchdog: boolean;
  inputs: number[];
  outputs: number[];
}

export interface UnitIdRequest {
  unitId: number;
}

export interface WelcomeMessage {
  type: 'welcome';
  protocolVersion: number;
  serverVersion: string;
  plant: PlantSummary;
  stepSeconds: number;
  statePeriodSeconds: number;
  serverAddress: string | null;
  inputs: SignalInfo[];
  outputs: SignalInfo[];
  disturbances: DisturbanceInfo[];
  alarms: string[];
}
