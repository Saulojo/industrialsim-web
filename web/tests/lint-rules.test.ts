// @vitest-environment node
import { fileURLToPath } from 'node:url';
import { ESLint } from 'eslint';
import stylelint from 'stylelint';
import { describe, expect, it } from 'vitest';

// Garante que as regras de i18n (RNF11) e de tokens de design (D19) continuam valendo.
const webRoot = fileURLToPath(new URL('..', import.meta.url));

async function eslintRules(code: string): Promise<string[]> {
  const eslint = new ESLint({ cwd: webRoot });
  const [result] = await eslint.lintText(code, { filePath: 'src/ui/LintFixture.tsx' });
  return (result?.messages ?? []).map((m) => m.ruleId ?? m.message);
}

async function stylelintRules(code: string): Promise<string[]> {
  const { results } = await stylelint.lint({
    code,
    codeFilename: `${webRoot}src/ui/fixture.css`,
    cwd: webRoot,
  });
  return (results[0]?.warnings ?? []).map((w) => w.rule);
}

describe('regra de texto fixo (i18next/no-literal-string)', () => {
  // Obs.: o plugin ignora strings em constantes com nome todo em maiúsculas (const X = ...),
  // por isso os exemplos usam nomes de componente normais.
  it('reprova texto fixo dentro do JSX', async () => {
    const rules = await eslintRules(
      'export function Fixture() {\n  return <p>Texto fixo</p>;\n}\n',
    );
    expect(rules).toContain('i18next/no-literal-string');
  });

  it('reprova texto fixo em atributo visível ao usuário', async () => {
    const rules = await eslintRules(
      'export function Fixture() {\n  return <button aria-label="Fechar" />;\n}\n',
    );
    expect(rules).toContain('i18next/no-literal-string');
  });

  it('aceita texto vindo de t() e atributos técnicos', async () => {
    const code = [
      "import { useTranslation } from 'react-i18next';",
      'export const Fixture = () => {',
      '  const { t } = useTranslation();',
      '  return <p className="panel__row" data-testid="x">{t(\'app.title\')}</p>;',
      '};',
      '',
    ].join('\n');
    expect(await eslintRules(code)).not.toContain('i18next/no-literal-string');
  });
});

describe('regra de tokens de design (stylelint)', () => {
  it('reprova cor fixa fora dos tokens', async () => {
    const rules = await stylelintRules('a {\n  color: #fff;\n}\n');
    expect(rules).toContain('scale-unlimited/declaration-strict-value');
  });

  it('reprova espaçamento fixo fora dos tokens', async () => {
    const rules = await stylelintRules('a {\n  padding: 12px;\n}\n');
    expect(rules).toContain('scale-unlimited/declaration-strict-value');
  });

  it('aceita valores vindos de tokens', async () => {
    const rules = await stylelintRules(
      'a {\n  color: var(--color-text);\n  padding: var(--space-2);\n}\n',
    );
    expect(rules).toEqual([]);
  });
});
