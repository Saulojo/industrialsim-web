import { fileURLToPath } from 'node:url';
import { defineConfig, devices } from '@playwright/test';

/**
 * Testes de ponta a ponta (Etapa 7): o servidor .NET de verdade, servindo o build do web (rode `npm run build`
 * antes), com o laço de tempo real, o Modbus e o WebSocket. Porta e faixa Modbus próprias, para não colidir
 * com um servidor de desenvolvimento aberto.
 */
const port = 5180;
export const recordingsDir = fileURLToPath(new URL('./test-results/recordings', import.meta.url));

export default defineConfig({
  testDir: './e2e',
  timeout: 120_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    locale: 'pt-BR',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], locale: 'pt-BR' } }],
  webServer: {
    // Roda a DLL com o dotnet (e não o .exe gerado), como o CI: o Controle de Aplicativo do Windows pode
    // bloquear o executável recém-compilado. A pasta do projeto é a raiz de conteúdo (wwwroot).
    command: `dotnet build -c Release && dotnet bin/Release/net10.0/IndustrialSim.Server.dll --urls http://127.0.0.1:${port}`,
    cwd: fileURLToPath(new URL('../server/src/IndustrialSim.Server', import.meta.url)),
    url: `http://127.0.0.1:${port}/api/health`,
    timeout: 240_000,
    reuseExistingServer: false,
    stdout: 'ignore',
    env: {
      ASPNETCORE_ENVIRONMENT: 'Production',
      Simulation__Modbus__PortRangeStart: '15500',
      Simulation__Modbus__PortRangeEnd: '15599',
      Simulation__Instances__RecordingDirectory: recordingsDir,
    },
  },
});
