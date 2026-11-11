import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchHealth, healthUrl } from './health';

describe('fetchHealth', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('lê a resposta JSON de /api/health', async () => {
    const body = { version: '0.1.0+abc', serverTimeUtc: '2026-11-11T14:30:00+00:00' };
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(fetchHealth()).resolves.toEqual(body);
    expect(fetchMock).toHaveBeenCalledWith(healthUrl, expect.anything());
  });

  it('falha quando o servidor responde com erro HTTP', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 503 })));

    await expect(fetchHealth()).rejects.toThrow('503');
  });
});
