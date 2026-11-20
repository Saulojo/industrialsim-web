import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test, type Page } from '@playwright/test';
import { evidence, evidenceDir, modbusPort, numberAttribute, openPlant, record } from './evidence';
import { ModbusException, ModbusMaster } from './modbusMaster';

/**
 * Critérios de validação da Etapa 8 (configurações e monitor de comunicação), com o servidor de verdade, um
 * mestre Modbus de teste e a IHM no Chromium. Evidências: EVIDENCE_DIR=../docs/validacao/etapa-08.
 */

/** Abre uma planta, conecta o mestre de teste e aceita o pareamento pela tela. */
async function plantWithMaster(page: Page): Promise<{ plantId: string; master: ModbusMaster }> {
  const plantId = await openPlant(page);
  const master = await ModbusMaster.connect(await modbusPort(page));
  const dialog = page.getByTestId('pairing-dialog');
  await dialog.getByRole('button', { name: 'Aceitar' }).click();
  await expect(page.getByTestId('master-status')).toHaveText('Mestre conectado');
  return { plantId, master };
}

/** Espera até `predicate` valer para o que o mestre lê; devolve o valor e quanto demorou. */
async function untilMaster<T>(
  read: () => Promise<T>,
  predicate: (value: T) => boolean,
  timeoutMs = 5000,
): Promise<{ value: T; afterMs: number }> {
  const started = Date.now();
  for (;;) {
    const value = await read();
    if (predicate(value)) return { value, afterMs: Date.now() - started };
    if (Date.now() - started > timeoutMs) throw new Error(`Não aconteceu em ${timeoutMs} ms.`);
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
}

const signed = (word: number) => (word >= 0x8000 ? word - 0x10000 : word);

test('critério 1: mudar o formato ou o Unit ID na tela altera o que o mestre lê, sem reiniciar', async ({
  page,
}, testInfo) => {
  const { master } = await plantWithMaster(page);
  const [countsBefore = 0] = await master.readInputRegisters(0, 1);
  const levelBefore = (signed(countsBefore) / 27648) * 1.2;
  expect(levelBefore).toBeGreaterThan(0.3);

  await page.getByTestId('tab-communication').click();
  await page.getByTestId('settings-format').selectOption('float32');
  const timeBefore = await numberAttribute(page, 'simulated-time');
  await page.getByTestId('settings-apply').click();
  const format = await untilMaster(
    () => master.readFloat(0),
    (level) => Math.abs(level - levelBefore) < 0.05,
  );
  await expect(page.getByTestId('settings-format')).toHaveValue('float32');
  await evidence(page, testInfo, 'criterio-1-formato');

  await page.getByTestId('settings-unit-id').fill('17');
  await page.getByTestId('settings-apply').click();
  const unitId = await untilMaster(
    async () => {
      try {
        await master.readInputRegisters(0, 2, 1);
        return 0;
      } catch (e) {
        return e instanceof ModbusException ? e.code : -1;
      }
    },
    (code) => code === 0x0b,
  );
  const levelWithNewUnitId = await master.readFloat(0, 17);
  await expect(page.getByTestId('settings-unit-id')).toHaveValue('17');

  const timeAfter = await numberAttribute(page, 'simulated-time');
  expect(await numberAttribute(page, 'run')).toBe(0); // sem reiniciar
  expect(timeAfter).toBeGreaterThan(timeBefore);
  expect(levelWithNewUnitId).toBeGreaterThan(0.3);

  await record(testInfo, 'criterio-1-formato-unit-id', {
    countsBefore: signed(countsBefore),
    levelBeforeM: levelBefore,
    float32AfterMs: format.afterMs,
    levelFloat32M: format.value,
    oldUnitIdException: unitId.value,
    oldUnitIdRefusedAfterMs: unitId.afterMs,
    levelReadWithUnitId17M: levelWithNewUnitId,
    run: 0,
    simulatedTime: [timeBefore, timeAfter],
  });
  master.close();
});

test('critério 2: uma escrita do mestre aparece no monitor em até 200 ms, com o IP de origem', async ({
  page,
}, testInfo) => {
  const { master } = await plantWithMaster(page);
  await page.getByTestId('tab-monitor').click();
  const row = page.getByTestId('monitor-holding-register-FV-101_command-analog');
  await expect(row).toBeVisible();

  const delays: number[] = [];
  for (let i = 0; i < 20; i++) {
    const value = 10000 + i * 500;
    // A página anota a hora (relógio do PC, o mesmo do teste) em que a linha passa a mostrar o valor.
    await page.evaluate((expected) => {
      const target = document.querySelector(
        '[data-testid="monitor-holding-register-FV-101_command-analog"]',
      );
      const w = window as unknown as { seenAt: number | null };
      w.seenAt = null;
      const observer = new MutationObserver(() => {
        if (target?.getAttribute('data-raw') === String(expected) && w.seenAt === null) {
          w.seenAt = Date.now();
          observer.disconnect();
        }
      });
      if (target) observer.observe(target, { attributes: true, attributeFilter: ['data-raw'] });
    }, value);
    const sent = Date.now();
    await master.writeHoldingRegister(0, value);
    const seenAt = await page.waitForFunction(
      () => (window as unknown as { seenAt: number | null }).seenAt,
    );
    delays.push(((await seenAt.jsonValue()) as number) - sent);
    await expect(row).toHaveAttribute('data-author', '127.0.0.1');
  }

  await expect(row).toContainText('127.0.0.1 às');
  await evidence(page, testInfo, 'criterio-2-monitor');
  const sorted = [...delays].sort((a, b) => a - b);
  await record(testInfo, 'criterio-2-escrita-no-monitor', {
    writes: delays.length,
    author: await row.getAttribute('data-author'),
    delaysMs: delays,
    minMs: sorted[0],
    medianMs: sorted[Math.floor(sorted.length / 2)],
    maxMs: sorted.at(-1),
  });
  expect(sorted.at(-1)).toBeLessThanOrEqual(200);
  master.close();
});

test('critério 3: um valor forçado aparece destacado na IHM e no monitor, e deixa de valer ao ser liberado', async ({
  page,
}, testInfo) => {
  const { master } = await plantWithMaster(page);
  await page.getByTestId('tab-monitor').click();
  const row = page.getByTestId('monitor-input-register-LT-101-analog');
  await row.getByTestId('force-value-LT-101').fill('0,3');
  await row.getByTestId('force-LT-101').click();

  await expect(row).toHaveAttribute('data-forced', 'true');
  await expect(row).toContainText('FORÇADO');
  const forcedRead = await untilMaster(
    () => master.readInputRegisters(0, 1),
    ([counts]) => counts === 6912, // 0,3 m em 0–1,2 m = 25 % de 27648
  );
  await evidence(page, testInfo, 'criterio-3-monitor-forcado');
  await page.emulateMedia({ colorScheme: 'dark' });
  await evidence(page, testInfo, 'criterio-3-monitor-forcado-escuro');
  await page.emulateMedia({ colorScheme: 'light' });

  await page.getByTestId('tab-process').click();
  await expect(page.getByTestId('forced-LT-101')).toContainText('0,300 m');
  await expect(page.locator('text[data-signal="LT-101"]')).toHaveAttribute('data-forced', 'true');
  await evidence(page, testInfo, 'criterio-3-ihm-forcado');

  await page.getByTestId('forced-panel').getByRole('button', { name: 'Liberar' }).click();
  await expect(page.getByTestId('forced-panel')).toBeHidden();
  await expect(page.locator('text[data-signal="LT-101"]')).toHaveAttribute('data-forced', 'false');
  const released = await untilMaster(
    () => master.readInputRegisters(0, 1),
    ([counts]) => counts !== undefined && Math.abs(counts - 6912) > 2000,
  );
  await page.getByTestId('tab-monitor').click();
  await expect(row).toHaveAttribute('data-forced', 'false');

  await record(testInfo, 'criterio-3-forcado', {
    forcedValueM: 0.3,
    masterReadWhileForced: forcedRead.value[0],
    forcedReadAfterMs: forcedRead.afterMs,
    masterReadAfterRelease: released.value[0],
    releasedReadAfterMs: released.afterMs,
  });
  master.close();
});

test('critério 4: o mapa de I/O exportado em CSV e em PDF bate com o scenario.json', async ({
  page,
}, testInfo) => {
  const plantId = await openPlant(page);

  const downloadPromise = page.waitForEvent('download');
  await page.getByTestId('io-map-csv').click();
  const download = await downloadPromise;
  const csv = readFileSync(await download.path(), 'utf-8');
  const lines = csv.trimEnd().split('\n');

  // Referência independente: o scenario.json lido aqui, sem o servidor.
  const scenario = JSON.parse(
    readFileSync(
      fileURLToPath(new URL('../../scenarios/caldeira/scenario.json', import.meta.url)),
      'utf-8',
    ),
  ) as {
    io: {
      coils: { address: number; signal: string }[];
      discreteInputs: { address: number; signal: string }[];
      holdingRegisters: { address: number; signal: string }[];
      inputRegisters: { address: number; signal: string }[];
      diagnostics: { address: number; values: { signal: string }[] };
      watchdog: { heartbeatRegister: number; statusDiscreteInput: number };
    };
  };
  const io = scenario.io;
  const expected = [
    ...io.coils.map((p) => `coil,${p.address},${p.address + 1},1,${p.signal}`),
    ...io.discreteInputs.map(
      (p) => `discrete-input,${p.address},${10001 + p.address},1,${p.signal}`,
    ),
    `discrete-input,${io.watchdog.statusDiscreteInput},${10001 + io.watchdog.statusDiscreteInput},1,watchdog`,
    ...io.holdingRegisters.map(
      (p) => `holding-register,${p.address},${40001 + p.address},1,${p.signal}`,
    ),
    `holding-register,${io.watchdog.heartbeatRegister},${40001 + io.watchdog.heartbeatRegister},1,heartbeat`,
    ...io.inputRegisters.map(
      (p) => `input-register,${p.address},${30001 + p.address},1,${p.signal}`,
    ),
    ...io.diagnostics.values.map(
      (v, k) =>
        `input-register,${io.diagnostics.address + 2 * k},${30001 + io.diagnostics.address + 2 * k},2,${v.signal}`,
    ),
  ];
  expect(lines.slice(1).map((l) => l.split(',').slice(0, 5).join(','))).toEqual(expected);

  // PDF: a página de impressão, aberta pelo botão, com as mesmas linhas.
  const popupPromise = page.waitForEvent('popup');
  await page.getByTestId('io-map-pdf').click();
  const printPage = await popupPromise;
  await expect(printPage.getByTestId('io-map-row')).toHaveCount(expected.length);
  const printed = await printPage
    .getByTestId('io-map-row')
    .evaluateAll((rows) =>
      rows.map(
        (r) =>
          `${r.getAttribute('data-area')},${r.getAttribute('data-address')},${r.getAttribute('data-signal')}`,
      ),
    );
  expect(printed).toEqual(expected.map((l) => l.split(',')).map((c) => `${c[0]},${c[1]},${c[4]}`));
  await evidence(printPage, testInfo, 'criterio-4-mapa-pdf');
  const pdf = await printPage.pdf({ format: 'A4', landscape: true, printBackground: true });
  await testInfo.attach('mapa-io.pdf', { body: pdf, contentType: 'application/pdf' });
  if (evidenceDir) {
    writeFileSync(join(evidenceDir, 'mapa-io-caldeira.pdf'), pdf);
    writeFileSync(join(evidenceDir, 'mapa-io-caldeira.csv'), csv);
  }

  await record(testInfo, 'criterio-4-mapa-io', {
    plantId,
    csvFile: download.suggestedFilename(),
    rows: lines.length - 1,
    matchesScenarioJson: true,
    pdfBytes: pdf.length,
  });
});

test('critério 5: pausar congela a planta e o monitor mostra "pausada"; retomar continua', async ({
  page,
}, testInfo) => {
  const { master } = await plantWithMaster(page);
  await page.waitForTimeout(1000);

  await page.getByTestId('pause').click();
  await expect(page.getByTestId('plant-status')).toHaveAttribute('data-status', 'paused');
  const timePaused = await numberAttribute(page, 'simulated-time');
  const readsPaused = [await master.readInputRegisters(0, 6)];
  await page.waitForTimeout(1500);
  readsPaused.push(await master.readInputRegisters(0, 6));
  const timeStill = await numberAttribute(page, 'simulated-time');
  expect(timeStill).toBe(timePaused);
  expect(readsPaused[1]).toEqual(readsPaused[0]); // nem o ruído dos transmissores anda

  await page.getByTestId('tab-monitor').click();
  await expect(page.getByTestId('monitor-paused')).toHaveText('Planta pausada');
  await evidence(page, testInfo, 'criterio-5-pausada');

  await page.getByTestId('pause').click();
  await expect(page.getByTestId('plant-status')).toHaveAttribute('data-status', 'running');
  await expect(page.getByTestId('monitor-paused')).toBeHidden();
  await page.waitForTimeout(1000);
  const timeResumed = await numberAttribute(page, 'simulated-time');
  expect(timeResumed).toBeGreaterThan(timePaused);
  expect(timeResumed - timePaused).toBeLessThan(2);
  expect(await numberAttribute(page, 'run')).toBe(0);

  await record(testInfo, 'criterio-5-pausa', {
    simulatedTimePaused: timePaused,
    simulatedTimeAfter1500msPaused: timeStill,
    masterReadsWhilePaused: readsPaused,
    simulatedTime1sAfterResume: timeResumed,
  });
  master.close();
});

test('critério 6: toda mudança de configuração aparece no log', async ({ page }, testInfo) => {
  await openPlant(page);
  await page.getByTestId('tab-communication').click();
  await page.getByTestId('settings-format').selectOption('counts10000');
  await page.getByTestId('settings-word-order').selectOption('lowWordFirst');
  await page.getByTestId('settings-watchdog').check();
  await page.getByTestId('settings-watchdog-timeout').fill('5');
  await page.getByTestId('settings-diagnostics').uncheck();
  await page.getByTestId('settings-unit-id').fill('3');
  await page.getByTestId('settings-master-policy').selectOption('single');
  await page.getByTestId('settings-apply').click();
  await expect(page.getByTestId('settings-apply')).toBeDisabled();

  // Uma segunda mudança, separada, também vai para o log.
  await page.getByTestId('settings-format').selectOption('float32');
  await page.getByTestId('settings-apply').click();

  await page.getByTestId('tab-log').click();
  const entries = page.locator('[data-testid="log-entry"][data-kind="setting-changed"]');
  await expect(entries).toHaveCount(8);
  const settings = await entries.evaluateAll((rows) =>
    rows.map((r) => r.getAttribute('data-setting')),
  );
  expect([...settings].sort()).toEqual(
    [
      'diagnosticsEnabled',
      'format',
      'format',
      'masterPolicy',
      'unitId',
      'watchdogEnabled',
      'watchdogTimeoutSeconds',
      'wordOrder',
    ].sort(),
  );
  await expect(entries.first()).toContainText('Formato analógico: 0–10000 → float32');
  await evidence(page, testInfo, 'criterio-6-log');

  await record(testInfo, 'criterio-6-log', {
    changesMade: 8,
    logEntries: await entries.evaluateAll((rows) => rows.map((r) => r.textContent)),
  });
});
