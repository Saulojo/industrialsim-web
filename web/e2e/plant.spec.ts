import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { recordingsDir } from '../playwright.config';
import { ModbusMaster } from './modbusMaster';

/**
 * Critérios de validação da Etapa 7, com o servidor de verdade. Com EVIDENCE_DIR definido (ex.:
 * ../docs/validacao/etapa-07), as medidas são gravadas lá, em JSON, e as capturas em ../img, com o nome da
 * pasta como prefixo (ex.: img/etapa-07-criterio-1-tendencia.png).
 */
const evidenceDir = process.env.EVIDENCE_DIR;

async function evidence(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  const shot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: shot, contentType: 'image/png' });
  if (evidenceDir) {
    const images = join(evidenceDir, '..', 'img');
    mkdirSync(images, { recursive: true });
    writeFileSync(join(images, `${basename(evidenceDir)}-${name}.png`), shot);
  }
}

async function record(testInfo: TestInfo, name: string, data: object): Promise<void> {
  const text = JSON.stringify(data, null, 2);
  console.log(`${name}: ${text}`);
  await testInfo.attach(name, { body: text, contentType: 'application/json' });
  if (evidenceDir) {
    mkdirSync(evidenceDir, { recursive: true });
    writeFileSync(join(evidenceDir, `${name}.json`), text + '\n');
  }
}

/** Abre uma planta nova da caldeira pela tela inicial e espera a IHM conectar. */
async function openPlant(page: Page): Promise<string> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Abrir planta' }).click();
  await page.waitForURL(/\/plants\/[0-9a-f-]+$/);
  await expect(page.getByTestId('connection-status')).toHaveAttribute('data-status', 'connected');
  return new URL(page.url()).pathname.split('/').at(-1) ?? '';
}

async function numberAttribute(page: Page, testId: string, name = 'data-value'): Promise<number> {
  return Number(await page.getByTestId(testId).getAttribute(name));
}

async function modbusPort(page: Page): Promise<number> {
  return Number(await page.getByTestId('modbus-port').textContent());
}

/** Leva o distúrbio FV-103 a `percent` pelo teclado, como um operador faria. */
async function moveSteamValve(page: Page, percent: number): Promise<void> {
  const slider = page.getByTestId('disturbance-FV-103_command');
  await slider.focus();
  const current = Number(await slider.inputValue());
  const steps = Math.round(percent - current);
  for (let i = 0; i < Math.abs(steps); i++) {
    await page.keyboard.press(steps > 0 ? 'ArrowRight' : 'ArrowLeft');
  }
  await expect(page.getByTestId('disturbance-FV-103_command-value')).toHaveAttribute(
    'data-value',
    /^0\.\d+/,
  );
}

test('critério 1: os valores mudam e o distúrbio move vazão de vapor e pressão na tendência', async ({
  page,
}, testInfo) => {
  await openPlant(page);

  // Os valores mudam: o tempo simulado anda e o nível (com ruído de medição) varia.
  const time0 = await numberAttribute(page, 'simulated-time');
  const levels = new Set<number>();
  for (let i = 0; i < 10; i++) {
    levels.add(await numberAttribute(page, 'trend-value-LT-101'));
    await page.waitForTimeout(100);
  }
  const time1 = await numberAttribute(page, 'simulated-time');
  expect(time1).toBeGreaterThan(time0 + 0.5);
  expect(levels.size).toBeGreaterThan(1);

  await page.waitForTimeout(3000);
  const flowBefore = await numberAttribute(page, 'trend-value-FT-102');
  const pressureBefore = await numberAttribute(page, 'trend-value-PT-101');
  const pointsBefore = await numberAttribute(page, 'trend-value-FT-102', 'data-points');

  await moveSteamValve(page, 90);

  await expect
    .poll(() => numberAttribute(page, 'trend-value-FT-102'), { timeout: 20_000 })
    .toBeGreaterThan(flowBefore + 0.5);
  await expect
    .poll(() => numberAttribute(page, 'trend-value-PT-101'), { timeout: 20_000 })
    .toBeLessThan(pressureBefore - 0.3e5);
  await page.waitForTimeout(4000);

  const flowAfter = await numberAttribute(page, 'trend-value-FT-102');
  const pressureAfter = await numberAttribute(page, 'trend-value-PT-101');
  const pointsAfter = await numberAttribute(page, 'trend-value-FT-102', 'data-points');
  expect(pointsAfter).toBeGreaterThan(pointsBefore);
  await expect(page.locator('polyline[data-signal="FT-102"]')).toHaveAttribute('points', /,/);

  await record(testInfo, 'criterio-1-disturbio', {
    simulatedTime: [time0, time1],
    distinctLevelReadings: levels.size,
    steamFlowKgPerS: [flowBefore, flowAfter],
    pressureBar: [pressureBefore / 1e5, pressureAfter / 1e5],
    trendPoints: [pointsBefore, pointsAfter],
  });
  await evidence(page, testInfo, 'criterio-1-tendencia');
});

test('critério 2: o pareamento aparece, aceitar conecta o mestre e recusar fecha a conexão', async ({
  page,
}, testInfo) => {
  await openPlant(page);
  await expect(page.getByTestId('master-status')).toHaveText('Nenhum mestre conectado');

  const master = await ModbusMaster.connect(await modbusPort(page));
  const dialog = page.getByTestId('pairing-dialog');
  await expect(dialog).toContainText('Aceitar conexão de 127.0.0.1?');
  await evidence(page, testInfo, 'criterio-2-pedido');

  await dialog.getByRole('button', { name: 'Aceitar' }).click();
  await expect(page.getByTestId('master-status')).toHaveText('Mestre conectado');
  await expect(dialog).toBeHidden();
  const [command] = await master.readHoldingRegisters(0, 1);
  expect(command).toBe(22749); // FV-101 em 82,28% no formato 0–27648
  await evidence(page, testInfo, 'criterio-2-mestre-conectado');
  master.close();

  // Outra planta: recusar fecha a conexão do mestre.
  await openPlant(page);
  const refused = await ModbusMaster.connect(await modbusPort(page));
  await expect(dialog).toContainText('Aceitar conexão de 127.0.0.1?');
  const started = Date.now();
  await dialog.getByRole('button', { name: 'Recusar' }).click();
  await refused.closed;
  const closedAfterMs = Date.now() - started;
  await expect(dialog).toBeHidden();
  await expect(page.getByTestId('master-status')).toHaveText('Nenhum mestre conectado');
  expect(closedAfterMs).toBeLessThan(5000);
  await record(testInfo, 'criterio-2-pareamento', {
    acceptedRead: command,
    refusedConnectionClosedAfterMs: closedAfterMs,
  });
});

test('critério 3: depois de uma queda do WebSocket, a IHM reconecta sozinha em menos de 5 s', async ({
  page,
}, testInfo) => {
  // O Playwright fica entre a página e o servidor e derruba a conexão do lado da página.
  const connections: { close: () => Promise<void> }[] = [];
  await page.routeWebSocket(/\/api\/plants\/.+\/ws$/, (ws) => {
    ws.connectToServer();
    connections.push(ws);
  });

  const plantId = await openPlant(page);
  await page.waitForTimeout(2000);
  const timeBefore = await numberAttribute(page, 'simulated-time');
  const pointsBefore = await numberAttribute(page, 'trend-value-LT-101', 'data-points');

  const dropped = Date.now();
  await connections.at(-1)?.close();
  await expect(page.getByTestId('connection-status')).toHaveAttribute(
    'data-status',
    'reconnecting',
  );
  await expect(page.getByTestId('connection-status')).toHaveAttribute('data-status', 'connected', {
    timeout: 5000,
  });
  const reconnectedAfterMs = Date.now() - dropped;
  await page.waitForTimeout(500);

  const timeAfter = await numberAttribute(page, 'simulated-time');
  expect(reconnectedAfterMs).toBeLessThan(5000);
  expect(connections).toHaveLength(2);
  expect(page.url()).toContain(plantId);
  expect(timeAfter).toBeGreaterThan(timeBefore); // a mesma planta, que continuou rodando
  expect(await numberAttribute(page, 'run')).toBe(0);
  expect(await numberAttribute(page, 'trend-value-LT-101', 'data-points')).toBeGreaterThan(
    pointsBefore,
  );
  await record(testInfo, 'criterio-3-reconexao', {
    plantId,
    reconnectedAfterMs,
    simulatedTime: [timeBefore, timeAfter],
  });
});

test('critério 4: um cliente com outra versão do protocolo recebe a mensagem de incompatibilidade', async ({
  page,
}, testInfo) => {
  // A página anuncia a versão 99 (o servidor de verdade responde).
  await page.routeWebSocket(/\/api\/plants\/.+\/ws$/, (ws) => {
    const server = ws.connectToServer();
    ws.onMessage((message) => {
      const parsed = JSON.parse(String(message)) as { type: string; protocolVersion?: number };
      if (parsed.type === 'hello') parsed.protocolVersion = 99;
      server.send(JSON.stringify(parsed));
    });
  });

  await page.goto('/');
  await page.getByRole('button', { name: 'Abrir planta' }).click();
  await expect(page.getByTestId('connection-status')).toHaveText('Versão incompatível');
  await expect(page.getByTestId('incompatible')).toHaveText(
    'Esta página usa a versão 99 do protocolo, mas o servidor usa a versão 1. Recarregue a página (Ctrl+F5) para carregar a versão atual.',
  );
  await page.waitForTimeout(3000);
  await expect(page.getByTestId('connection-status')).toHaveText('Versão incompatível'); // não insiste
  await evidence(page, testInfo, 'criterio-4-incompativel');
});

test('critério 5: o CSV baixado tem os mesmos dados da gravação do servidor', async ({
  page,
}, testInfo) => {
  const plantId = await openPlant(page);
  await moveSteamValve(page, 70);
  await page.waitForTimeout(5000);

  const downloading = page.waitForEvent('download');
  await page.getByTestId('download-csv').click();
  const download = await downloading;
  const path = await download.path();
  const downloaded = readFileSync(path);

  // Encerra a planta: o servidor completa o arquivo, que deve começar exatamente com o que foi baixado.
  const ended = await page.request.delete(`/api/plants/${plantId}`);
  expect(ended.status()).toBe(204);
  const server = readFileSync(join(recordingsDir, `${plantId.replaceAll('-', '')}.csv`));
  const prefix = server.subarray(0, downloaded.length);

  const sha = (data: Buffer) => createHash('sha256').update(data).digest('hex');
  const lines = downloaded.toString('utf8').split('\n');
  const header = (lines.find((l) => l.startsWith('step,')) ?? '').split(',');
  const disturbanceColumn = header.indexOf('FV-103_command');
  expect(sha(prefix)).toBe(sha(downloaded));
  expect(lines[0]).toBe('#format=industrialsim-recording/1');
  expect(lines.some((l) => /^\d/.test(l) && l.split(',')[disturbanceColumn] === '0.7')).toBe(true);
  expect(download.suggestedFilename()).toBe(`caldeira-${plantId.replaceAll('-', '')}.csv`);

  await record(testInfo, 'criterio-5-csv', {
    suggestedFilename: download.suggestedFilename(),
    downloadedBytes: downloaded.length,
    serverBytesAfterEnd: server.length,
    downloadedRows: lines.filter((l) => /^\d/.test(l)).length,
    sha256Downloaded: sha(downloaded),
    sha256ServerPrefix: sha(prefix),
  });
});

test('critério 6: a banda do WebSocket por cliente fica abaixo de 100 kbit/s', async ({
  page,
}, testInfo) => {
  test.setTimeout(180_000);
  const frames = {
    received: 0,
    receivedBytes: 0,
    sent: 0,
    sentBytes: 0,
    byType: {} as Record<string, number>,
  };
  page.on('websocket', (ws) => {
    ws.on('framereceived', (frame) => {
      const text = String(frame.payload);
      frames.received++;
      frames.receivedBytes += Buffer.byteLength(text);
      const type = (JSON.parse(text) as { type: string }).type;
      frames.byType[type] = (frames.byType[type] ?? 0) + 1;
    });
    ws.on('framesent', (frame) => {
      frames.sent++;
      frames.sentBytes += Buffer.byteLength(String(frame.payload));
    });
  });

  await openPlant(page);
  // Cenário de uso: mestre pareado escrevendo comando e heartbeat, e o operador mexendo no distúrbio.
  const master = await ModbusMaster.connect(await modbusPort(page));
  await page.getByTestId('pairing-dialog').getByRole('button', { name: 'Aceitar' }).click();
  await expect(page.getByTestId('master-status')).toHaveText('Mestre conectado');

  Object.assign(frames, { received: 0, receivedBytes: 0, sent: 0, sentBytes: 0, byType: {} });
  const started = Date.now();
  let heartbeat = 0;
  while (Date.now() - started < 60_000) {
    await master.writeHoldingRegister(10, ++heartbeat & 0xffff);
    if (heartbeat % 40 === 0) await moveSteamValve(page, heartbeat % 80 === 0 ? 50 : 60);
    await page.waitForTimeout(250);
  }
  const seconds = (Date.now() - started) / 1000;
  master.close();

  // Cabeçalho de quadro do servidor (sem máscara): 2 bytes até 125 de carga, 4 bytes até 65535.
  const headerBytes = frames.received * 4;
  const payloadKbit = (frames.receivedBytes * 8) / 1000 / seconds;
  const withHeadersKbit = ((frames.receivedBytes + headerBytes) * 8) / 1000 / seconds;
  expect(withHeadersKbit).toBeLessThanOrEqual(100);

  await record(testInfo, 'criterio-6-banda', {
    seconds,
    framesReceived: frames.received,
    statesPerSecond: (frames.byType.state ?? 0) / seconds,
    messagesByType: frames.byType,
    payloadBytes: frames.receivedBytes,
    payloadKbitPerSecond: payloadKbit,
    withFrameHeadersKbitPerSecond: withHeadersKbit,
    sentFrames: frames.sent,
    sentKbitPerSecond: ((frames.sentBytes + frames.sent * 8) * 8) / 1000 / seconds,
  });
});
