import { useSyncExternalStore } from 'react';

/**
 * Navegação mínima pelo caminho da URL (sem biblioteca de rotas): `/` escolhe o cenário,
 * `/plants/{id}` mostra a planta e `/plants/{id}/io-map` é o mapa de I/O para imprimir ou salvar em PDF.
 * O servidor devolve a página para qualquer caminho fora de `/api`.
 */
const navigationEvent = 'industrialsim:navigate';

function subscribe(listener: () => void): () => void {
  window.addEventListener('popstate', listener);
  window.addEventListener(navigationEvent, listener);
  return () => {
    window.removeEventListener('popstate', listener);
    window.removeEventListener(navigationEvent, listener);
  };
}

export function usePath(): string {
  return useSyncExternalStore(subscribe, () => window.location.pathname);
}

export function navigate(path: string): void {
  window.history.pushState(null, '', path);
  window.dispatchEvent(new Event(navigationEvent));
}

export function plantPath(plantId: string): string {
  return `/plants/${encodeURIComponent(plantId)}`;
}

/** Identificador da planta num caminho `/plants/{id}`; nulo nos outros caminhos. */
export function plantIdFromPath(path: string): string | null {
  const match = /^\/plants\/([^/]+)\/?$/.exec(path);
  return match?.[1] ? decodeURIComponent(match[1]) : null;
}

/** Página de impressão do mapa de I/O de uma planta. */
export function ioMapPath(plantId: string): string {
  return `${plantPath(plantId)}/io-map`;
}

/** Identificador da planta num caminho `/plants/{id}/io-map`; nulo nos outros caminhos. */
export function ioMapPlantIdFromPath(path: string): string | null {
  const match = /^\/plants\/([^/]+)\/io-map\/?$/.exec(path);
  return match?.[1] ? decodeURIComponent(match[1]) : null;
}
