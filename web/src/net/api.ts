import type {
  ApiError,
  IoMapResponse,
  PlantCreateRequest,
  PlantSummary,
  ScenarioSummary,
} from './contracts.generated';

/** Erro da API REST: só o código (D35), traduzido pela interface. */
export class ApiRequestError extends Error {
  constructor(
    readonly code: string,
    readonly status: number,
  ) {
    super(`API respondeu HTTP ${status} (${code})`);
  }
}

async function check(response: Response): Promise<Response> {
  if (response.ok) return response;
  let code = 'unknown';
  try {
    code = ((await response.json()) as ApiError).code ?? code;
  } catch {
    // corpo vazio ou não JSON
  }
  throw new ApiRequestError(code, response.status);
}

export async function fetchScenarios(signal?: AbortSignal): Promise<ScenarioSummary[]> {
  const response = await check(await fetch('/api/scenarios', { signal: signal ?? null }));
  return (await response.json()) as ScenarioSummary[];
}

/** Cria uma planta; a IHM sempre grava o ensaio, para o CSV poder ser baixado (RF07). */
export async function createPlant(scenarioId: string): Promise<PlantSummary> {
  const body: PlantCreateRequest = { scenarioId, noiseSeed: null, record: true };
  const response = await check(
    await fetch('/api/plants', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),
  );
  return (await response.json()) as PlantSummary;
}

/** Gravação do ensaio em CSV, desde o último reinício (D34). */
export function recordingUrl(plantId: string): string {
  return `/api/plants/${encodeURIComponent(plantId)}/recording`;
}

/** Mapa de I/O da planta em CSV, no formato e com as faixas atuais (Etapa 8). */
export function ioMapCsvUrl(plantId: string): string {
  return `/api/plants/${encodeURIComponent(plantId)}/io-map.csv`;
}

/** Mapa de I/O da planta, para a página de impressão (PDF). */
export async function fetchIoMap(plantId: string, signal?: AbortSignal): Promise<IoMapResponse> {
  const response = await check(
    await fetch(`/api/plants/${encodeURIComponent(plantId)}/io-map`, { signal: signal ?? null }),
  );
  return (await response.json()) as IoMapResponse;
}
