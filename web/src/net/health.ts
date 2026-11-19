import type { HealthResponse } from './contracts.generated';

export type { HealthResponse };

export const healthUrl = '/api/health';

export async function fetchHealth(signal?: AbortSignal): Promise<HealthResponse> {
  const response = await fetch(healthUrl, { signal: signal ?? null });
  if (!response.ok) {
    throw new Error(`GET ${healthUrl} respondeu HTTP ${response.status}`);
  }
  return (await response.json()) as HealthResponse;
}
