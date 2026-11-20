import { mkdirSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { expect, type Page, type TestInfo } from '@playwright/test';

/**
 * Evidências dos critérios de validação. Com EVIDENCE_DIR definido (ex.: ../docs/validacao/etapa-08), as
 * medidas são gravadas lá, em JSON, e as capturas em ../img, com o nome da pasta como prefixo (ex.:
 * img/etapa-08-criterio-1-formato.png).
 */
export const evidenceDir = process.env.EVIDENCE_DIR;
export async function evidence(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  const shot = await page.screenshot({ fullPage: true });
  await testInfo.attach(name, { body: shot, contentType: 'image/png' });
  if (evidenceDir) {
    const images = join(evidenceDir, '..', 'img');
    mkdirSync(images, { recursive: true });
    writeFileSync(join(images, `${basename(evidenceDir)}-${name}.png`), shot);
  }
}

export async function record(testInfo: TestInfo, name: string, data: object): Promise<void> {
  const text = JSON.stringify(data, null, 2);
  console.log(`${name}: ${text}`);
  await testInfo.attach(name, { body: text, contentType: 'application/json' });
  if (evidenceDir) {
    mkdirSync(evidenceDir, { recursive: true });
    writeFileSync(join(evidenceDir, `${name}.json`), text + '\n');
  }
}

/** Abre uma planta nova da caldeira pela tela inicial e espera a IHM conectar. */
export async function openPlant(page: Page): Promise<string> {
  await page.goto('/');
  await page.getByRole('button', { name: 'Abrir planta' }).click();
  await page.waitForURL(/\/plants\/[0-9a-f-]+$/);
  await expect(page.getByTestId('connection-status')).toHaveAttribute('data-status', 'connected');
  return new URL(page.url()).pathname.split('/').at(-1) ?? '';
}

export async function numberAttribute(
  page: Page,
  testId: string,
  name = 'data-value',
): Promise<number> {
  return Number(await page.getByTestId(testId).getAttribute(name));
}

export async function modbusPort(page: Page): Promise<number> {
  return Number(await page.getByTestId('modbus-port').textContent());
}
