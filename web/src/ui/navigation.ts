import { useSyncExternalStore } from 'react';

/**
 * Navegação mínima pelo caminho da URL (sem biblioteca de rotas): `/` escolhe o cenário e
 * `/plants/{id}` mostra a planta. O servidor devolve a página para qualquer caminho fora de `/api`.
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
