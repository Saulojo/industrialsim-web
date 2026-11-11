import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { HealthResponse } from '../net/health';
import { HealthStatus } from './HealthStatus';

describe('HealthStatus', () => {
  it('mostra a versão vinda do servidor', async () => {
    const health: HealthResponse = {
      version: '0.1.0+abc123',
      serverTimeUtc: '2026-11-11T14:30:00+00:00',
    };

    render(<HealthStatus load={() => Promise.resolve(health)} />);

    expect(screen.getByText('Consultando o servidor…')).toBeInTheDocument();
    expect(await screen.findByTestId('health-version')).toHaveTextContent('Versão: 0.1.0+abc123');
    expect(screen.getByTestId('health-time')).toHaveTextContent('Hora do servidor: 11/11/2026');
  });

  it('mostra alarme quando o servidor não responde', async () => {
    render(<HealthStatus load={() => Promise.reject(new Error('offline'))} />);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Não foi possível falar com o servidor.',
    );
  });
});
