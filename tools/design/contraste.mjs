#!/usr/bin/env node
/*
 * Verificador de contraste da paleta (Etapa D1).
 *
 * Lê docs/design/tokens.json e confere cada par de cores de "contrastPairs" nos temas claro e escuro,
 * contra o mínimo do par: 4,5:1 para texto normal (WCAG 2.2, critério 1.4.3), 3:1 para texto grande e
 * para elementos gráficos e de interface (critérios 1.4.3 e 1.4.11).
 *
 * Razão de contraste e luminância relativa conforme as definições do WCAG 2.2
 * (https://www.w3.org/TR/WCAG22/#dfn-contrast-ratio e #dfn-relative-luminance); o limiar de
 * linearização usado é 0,04045, o da especificação sRGB (IEC 61966-2-1), como recomenda a nota do
 * próprio WCAG 2.2. A razão não é arredondada antes da comparação: 4,499 reprova num mínimo de 4,5.
 *
 * Uso:
 *   node tools/design/contraste.mjs [tokens.json] [--md saida.md]
 * Sai com código 1 se algum par reprovar.
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/** Converte "#rgb" ou "#rrggbb" em [r, g, b] (0–255). */
export function parseHex(hex) {
  const match = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(String(hex).trim());
  if (!match) {
    throw new Error(`Cor inválida: "${hex}" (use #rgb ou #rrggbb, sem transparência)`);
  }
  let digits = match[1];
  if (digits.length === 3) {
    digits = [...digits].map((d) => d + d).join('');
  }
  return [0, 2, 4].map((i) => parseInt(digits.slice(i, i + 2), 16));
}

/** Luminância relativa (WCAG 2.2). */
export function relativeLuminance(rgb) {
  const [r, g, b] = rgb.map((value) => {
    const c = value / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** Razão de contraste entre duas cores em hexadecimal (de 1 a 21). */
export function contrastRatio(hexA, hexB) {
  const la = relativeLuminance(parseHex(hexA));
  const lb = relativeLuminance(parseHex(hexB));
  const [light, dark] = la >= lb ? [la, lb] : [lb, la];
  return (light + 0.05) / (dark + 0.05);
}

/** Razão truncada em 2 casas, só para exibir (nunca arredonda para cima). */
export function formatRatio(ratio) {
  return (Math.floor(ratio * 100) / 100).toFixed(2);
}

/** Confere todos os pares em todos os temas; devolve uma linha por par e tema. */
export function checkTokens(tokens) {
  const themes = tokens?.color;
  const pairs = tokens?.contrastPairs;
  if (!themes || typeof themes !== 'object' || !Array.isArray(pairs) || pairs.length === 0) {
    throw new Error(
      'tokens.json precisa de "color" (temas) e de "contrastPairs" (lista não vazia)',
    );
  }
  const results = [];
  for (const [theme, colors] of Object.entries(themes)) {
    for (const pair of pairs) {
      const fgHex = colors[pair.fg];
      const bgHex = colors[pair.bg];
      if (fgHex === undefined || bgHex === undefined) {
        throw new Error(`Tema "${theme}": token inexistente no par ${pair.fg} sobre ${pair.bg}`);
      }
      if (typeof pair.min !== 'number' || pair.min < 1) {
        throw new Error(`Par ${pair.fg} sobre ${pair.bg}: "min" deve ser um número ≥ 1`);
      }
      const ratio = contrastRatio(fgHex, bgHex);
      results.push({
        theme,
        fg: pair.fg,
        bg: pair.bg,
        fgHex,
        bgHex,
        ratio,
        min: pair.min,
        pass: ratio >= pair.min,
        use: pair.use ?? '',
      });
    }
  }
  return results;
}

/** Tabela em Markdown com o resultado, por tema. */
export function toMarkdown(results) {
  const lines = [];
  const themes = [...new Set(results.map((r) => r.theme))];
  for (const theme of themes) {
    lines.push(`### Tema ${theme}`, '');
    lines.push('| Frente | Fundo | Razão | Mínimo | Resultado | Uso |');
    lines.push('| --- | --- | --- | --- | --- | --- |');
    for (const r of results.filter((x) => x.theme === theme)) {
      lines.push(
        `| \`${r.fg}\` ${r.fgHex} | \`${r.bg}\` ${r.bgHex} | ${formatRatio(r.ratio)}:1 | ${r.min}:1 | ${r.pass ? 'passa' : '**reprova**'} | ${r.use} |`,
      );
    }
    lines.push('');
  }
  const failed = results.filter((r) => !r.pass).length;
  lines.push(
    `Total: ${results.length} pares conferidos, ${results.length - failed} passam, ${failed} reprovam.`,
  );
  return lines.join('\n') + '\n';
}

function main(argv) {
  const args = [...argv];
  let mdOut = null;
  const mdIndex = args.indexOf('--md');
  if (mdIndex >= 0) {
    mdOut = args[mdIndex + 1];
    args.splice(mdIndex, 2);
  }
  const here = fileURLToPath(new URL('.', import.meta.url));
  const tokensPath = resolve(args[0] ?? resolve(here, '../../docs/design/tokens.json'));
  const tokens = JSON.parse(readFileSync(tokensPath, 'utf8'));
  const results = checkTokens(tokens);
  const markdown = toMarkdown(results);
  if (mdOut) {
    writeFileSync(mdOut, markdown, 'utf8');
  }
  process.stdout.write(markdown);
  return results.every((r) => r.pass) ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(2));
}
