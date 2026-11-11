import { fileURLToPath } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// O build vai para o wwwroot do servidor ASP.NET Core, que serve o site (Etapa 0).
const serverWwwroot = fileURLToPath(
  new URL('../server/src/IndustrialSim.Server/wwwroot', import.meta.url),
);

// Porta do servidor em desenvolvimento (server/src/IndustrialSim.Server/Properties/launchSettings.json).
const devServerUrl = 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': devServerUrl,
    },
  },
  build: {
    outDir: serverWwwroot,
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./tests/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}', 'tests/**/*.test.{ts,tsx}'],
  },
});
