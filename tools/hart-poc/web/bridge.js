// Ponte da prova de conceito (Etapa 2): repassa bytes crus entre a Web Serial e o WebSocket.
// Não interpreta HART; só conta, mede e mostra em hexadecimal.
'use strict';

const SERIAL_OPTIONS = { baudRate: 1200, dataBits: 8, parity: 'odd', stopBits: 1, flowControl: 'none', bufferSize: 4096 };
const MAX_LOG_LINES = 5000;

const $ = (id) => document.getElementById(id);
const ui = {
  unsupported: $('unsupported'),
  wsUrl: $('ws-url'),
  mode: $('mode'),
  wsState: $('ws-state'),
  connect: $('connect'),
  disconnect: $('disconnect'),
  dtr: $('dtr'),
  rts: $('rts'),
  serialState: $('serial-state'),
  signals: $('signals'),
  countUp: $('count-up'),
  countDown: $('count-down'),
  countErrors: $('count-errors'),
  countReplies: $('count-replies'),
  roundTrip: $('round-trip'),
  uptime: $('uptime'),
  log: $('log'),
  clear: $('clear'),
  save: $('save'),
  autoscroll: $('autoscroll'),
};

const state = {
  port: null,
  reader: null,
  writer: null,
  socket: null,
  running: false,
  connectedAt: 0,
  bytesUp: 0,
  bytesDown: 0,
  readErrors: 0,
  lastUpAt: null,
  replies: 0,
  rtMin: Infinity,
  rtMax: 0,
  rtSum: 0,
  lastSignals: '',
  logLines: [],
  timers: [],
};

function defaultUrl() {
  const scheme = location.protocol === 'https:' ? 'wss' : 'ws';
  return `${scheme}://${location.host}/ws/${ui.mode.value}`;
}

function hex(bytes) {
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0').toUpperCase()).join(' ');
}

function stamp() {
  const now = new Date();
  const pad = (n, size = 2) => String(n).padStart(size, '0');
  return `${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}.${pad(now.getMilliseconds(), 3)}`;
}

function log(text) {
  state.logLines.push(`${stamp()} ${text}`);
  if (state.logLines.length > MAX_LOG_LINES) {
    state.logLines.splice(0, state.logLines.length - MAX_LOG_LINES);
  }
  ui.log.textContent = state.logLines.join('\n');
  if (ui.autoscroll.checked) {
    ui.log.scrollTop = ui.log.scrollHeight;
  }
}

function renderCounters() {
  ui.countUp.textContent = `${state.bytesUp} bytes`;
  ui.countDown.textContent = `${state.bytesDown} bytes`;
  ui.countErrors.textContent = String(state.readErrors);
  ui.countReplies.textContent = String(state.replies);
  ui.roundTrip.textContent =
    state.replies === 0
      ? '—'
      : `${state.rtMin.toFixed(1)} / ${(state.rtSum / state.replies).toFixed(1)} / ${state.rtMax.toFixed(1)} ms`;
  if (state.running) {
    const seconds = Math.floor((performance.now() - state.connectedAt) / 1000);
    ui.uptime.textContent = `${Math.floor(seconds / 60)} min ${seconds % 60} s`;
  }
}

function openSocket(url) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url);
    socket.binaryType = 'arraybuffer';
    socket.onopen = () => resolve(socket);
    socket.onerror = () => reject(new Error(`não foi possível conectar em ${url}`));
  });
}

async function onSocketMessage(event) {
  const bytes = new Uint8Array(event.data);
  if (state.lastUpAt !== null) {
    // Do último pedaço mandado ao servidor até a resposta chegar: tempo gasto fora da serial.
    const elapsed = performance.now() - state.lastUpAt;
    state.lastUpAt = null;
    state.replies += 1;
    state.rtSum += elapsed;
    state.rtMin = Math.min(state.rtMin, elapsed);
    state.rtMax = Math.max(state.rtMax, elapsed);
  }
  state.bytesDown += bytes.length;
  log(`servidor → serial  ${hex(bytes)}`);
  try {
    await state.writer.write(bytes);
  } catch (error) {
    log(`ERRO ao escrever na serial: ${error.message}`);
  }
  renderCounters();
}

async function readLoop() {
  // Erros de paridade, quadro ou estouro não fecham a porta: a leitura é retomada com um leitor novo.
  while (state.running && state.port.readable) {
    state.reader = state.port.readable.getReader();
    try {
      for (;;) {
        const { value, done } = await state.reader.read();
        if (done) {
          break;
        }
        state.bytesUp += value.length;
        state.lastUpAt = performance.now();
        log(`serial → servidor  ${hex(value)}`);
        if (state.socket.readyState === WebSocket.OPEN) {
          state.socket.send(value);
        }
        renderCounters();
      }
    } catch (error) {
      state.readErrors += 1;
      log(`ERRO de leitura da serial (${error.name}): ${error.message}`);
      renderCounters();
    } finally {
      state.reader.releaseLock();
    }
  }
}

async function pollSignals() {
  if (!state.running) {
    return;
  }
  try {
    const s = await state.port.getSignals();
    const text = `CTS=${+s.clearToSend} DSR=${+s.dataSetReady} DCD=${+s.dataCarrierDetect} RI=${+s.ringIndicator}`;
    ui.signals.textContent = text;
    if (text !== state.lastSignals) {
      state.lastSignals = text;
      log(`sinais: ${text}`);
    }
  } catch {
    // A porta pode estar fechando; a próxima leitura resolve.
  }
}

async function applyOutputSignals() {
  if (!state.running) {
    return;
  }
  await state.port.setSignals({ dataTerminalReady: ui.dtr.checked, requestToSend: ui.rts.checked });
  log(`DTR=${+ui.dtr.checked} RTS=${+ui.rts.checked} (definidos por esta página)`);
}

async function connect() {
  ui.connect.disabled = true;
  try {
    const url = ui.wsUrl.value.trim();
    state.socket = await openSocket(url);
    ui.wsState.textContent = `conectado a ${url}`;
    state.socket.onmessage = onSocketMessage;
    state.socket.onclose = () => {
      ui.wsState.textContent = 'desconectado';
      if (state.running) {
        log('WebSocket fechado pelo servidor ou pela rede');
        disconnect();
      }
    };

    state.port = await navigator.serial.requestPort();
    await state.port.open(SERIAL_OPTIONS);
    state.writer = state.port.writable.getWriter();
    state.running = true;
    state.connectedAt = performance.now();
    const info = state.port.getInfo();
    ui.serialState.textContent = 'aberta (1200 8O1)';
    log(`porta serial aberta: ${JSON.stringify(info)} — ${JSON.stringify(SERIAL_OPTIONS)}`);
    await applyOutputSignals();

    state.timers.push(setInterval(pollSignals, 250), setInterval(renderCounters, 1000));
    ui.disconnect.disabled = false;
    ui.wsUrl.disabled = true;
    ui.mode.disabled = true;
    readLoop();
  } catch (error) {
    log(`ERRO ao conectar: ${error.message}`);
    await disconnect();
  }
}

async function disconnect() {
  state.running = false;
  state.timers.forEach(clearInterval);
  state.timers = [];
  try {
    if (state.reader) {
      await state.reader.cancel();
    }
    if (state.writer) {
      state.writer.releaseLock();
    }
    if (state.port) {
      await state.port.close();
      log('porta serial fechada');
    }
  } catch (error) {
    log(`ERRO ao fechar a serial: ${error.message}`);
  }
  if (state.socket && state.socket.readyState <= WebSocket.OPEN) {
    state.socket.close();
  }
  state.port = null;
  state.reader = null;
  state.writer = null;
  state.socket = null;
  ui.serialState.textContent = 'fechada';
  ui.wsState.textContent = 'desconectado';
  ui.connect.disabled = !('serial' in navigator);
  ui.disconnect.disabled = true;
  ui.wsUrl.disabled = false;
  ui.mode.disabled = false;
}

function saveLog() {
  const blob = new Blob([state.logLines.join('\r\n')], { type: 'text/plain' });
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = `ponte-hart-${new Date().toISOString().replace(/[:.]/g, '-')}.log`;
  link.click();
  URL.revokeObjectURL(link.href);
}

ui.wsUrl.value = defaultUrl();
ui.mode.addEventListener('change', () => {
  // Troca só o caminho, para preservar um servidor remoto digitado à mão.
  ui.wsUrl.value = ui.wsUrl.value.replace(/\/ws\/\w+$/, `/ws/${ui.mode.value}`);
});
ui.connect.addEventListener('click', connect);
ui.disconnect.addEventListener('click', disconnect);
ui.dtr.addEventListener('change', applyOutputSignals);
ui.rts.addEventListener('change', applyOutputSignals);
ui.clear.addEventListener('click', () => {
  state.logLines = [];
  ui.log.textContent = '';
});
ui.save.addEventListener('click', saveLog);

if (!('serial' in navigator)) {
  ui.unsupported.hidden = false;
  ui.connect.disabled = true;
}
