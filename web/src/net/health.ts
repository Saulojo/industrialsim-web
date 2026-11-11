/**
 * Espelho de IndustrialSim.Contracts.HealthResponse.
 * Escrito à mão na Etapa 0; passa a ser gerado do contrato C# na Etapa 7 (D05).
 */
export interface HealthResponse {
  version: string;
  /** Hora do servidor em UTC, ISO 8601. */
  serverTimeUtc: string;
}

export const healthUrl = '/api/health';

export async function fetchHealth(signal?: AbortSignal): Promise<HealthResponse> {
  const response = await fetch(healthUrl, { signal: signal ?? null });
  if (!response.ok) {
    throw new Error(`GET ${healthUrl} respondeu HTTP ${response.status}`);
  }
  return (await response.json()) as HealthResponse;
}
