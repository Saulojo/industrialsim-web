#!/usr/bin/env node
/*
 * Conferência do mapa de telas contra os wireframes e o protótipo (Etapa D1).
 *
 * Critério da D1: "Todas as telas do mapa existem no protótipo e cobrem os fluxos do visitante, do aluno
 * e do professor". O script lê docs/design/mapa-de-telas.md e as fontes dos Artifacts em
 * docs/design/fontes/ e confere:
 *   1. a tabela de telas tem T01 a T25 (e T05b), cada uma com a prancha de wireframe (wf:) e de protótipo (pt:);
 *   2. cada prancha citada existe no canvas.json do Artifact e tem o arquivo .dc.html;
 *   3. cada estado E01 a E10 tem prancha no protótipo (parágrafo "Pranchas dos estados no protótipo");
 *   4. nenhum link entre pranchas ("X.dc.html", no HTML ou no código) aponta para arquivo que não existe;
 *   5. a partir da página inicial (Main), seguindo links, chega-se ao fim de cada tarefa principal (T1 a T5).
 * Os links de um componente importado (dc-import) contam para a prancha que o importa.
 *
 * Uso:
 *   node tools/design/mapa-prototipo.mjs [--md saida.md]
 * Sai com código 1 se alguma conferência falhar.
 */
import { existsSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/** Fim de cada tarefa principal no protótipo (docs/design/teste/roteiro.md, seção 5). */
export const TASK_ENDS = {
  T1: 'E01-SemMestre',
  T2: 'T06-IHM',
  T3: 'T08-Configuracoes',
  T4: 'T15-Compartilhar',
  T5: 'T18-Painel',
};

/** Lê a tabela "Telas" do mapa: [{ id, name, wf, pt }]. */
export function parseScreens(markdown) {
  const screens = [];
  for (const line of markdown.split(/\r?\n/)) {
    const match = /^\| (T\d{2}b?) \| (.+?) \|/.exec(line);
    if (!match) continue;
    const wf = /wf: `([^`]+)`/.exec(line);
    const pt = /pt: `([^`]+)`/.exec(line);
    screens.push({ id: match[1], name: match[2], wf: wf ? wf[1] : null, pt: pt ? pt[1] : null });
  }
  return screens;
}

/** Lê o parágrafo "Pranchas dos estados no protótipo": { E01: ['E01-SemMestre'], ... }. */
export function parseStateBoards(markdown) {
  const para = markdown
    .split(/\r?\n/)
    .find((l) => l.startsWith('Pranchas dos estados no protótipo'));
  const states = {};
  if (!para) return states;
  // Cada trecho entre ';' liga um ou mais estados a uma ou mais pranchas.
  for (const part of para.split(';')) {
    const ids = [...part.matchAll(/\bE(\d{2})\b/g)].map((m) => 'E' + m[1]);
    const boards = [...part.matchAll(/`([A-Za-z0-9-]+)`/g)]
      .map((m) => m[1])
      .filter((b) => !/^(estado)$/.test(b));
    for (const id of ids) states[id] = [...(states[id] || []), ...boards];
  }
  return states;
}

/** Pranchas de um canvas.json (nomes sem .dc.html). */
export function canvasBoards(canvas) {
  return Object.keys(canvas.boards || {}).map((f) => f.replace(/\.dc\.html$/, ''));
}

/**
 * Links e importações de uma prancha. Conta como link todo endereço "X.dc.html" entre aspas, no HTML
 * (href) ou montado no código da prancha (abas e menus calculados), inclusive os que só aparecem em
 * certos estados; por isso é uma aproximação por cima dos caminhos possíveis.
 */
export function parseBoard(html) {
  const links = [...html.matchAll(/["']([A-Za-z0-9-]+)\.dc\.html["']/g)].map((m) => m[1]);
  const imports = [...html.matchAll(/<dc-import name="([A-Za-z0-9-]+)"/g)].map((m) => m[1]);
  return { links: [...new Set(links)], imports: [...new Set(imports)] };
}

/** Links efetivos de cada prancha, somando os dos componentes que ela importa (recursivo). */
export function effectiveLinks(boards) {
  const memo = {};
  const visit = (name, stack) => {
    if (memo[name]) return memo[name];
    const b = boards[name];
    if (!b) return new Set();
    const out = new Set(b.links);
    for (const imp of b.imports) {
      if (stack.includes(imp)) continue;
      for (const l of visit(imp, [...stack, imp])) out.add(l);
    }
    memo[name] = out;
    return out;
  };
  const result = {};
  for (const name of Object.keys(boards)) result[name] = visit(name, [name]);
  return result;
}

/** Pranchas alcançáveis a partir de `start`, seguindo links. */
export function reachable(links, start) {
  const seen = new Set([start]);
  const queue = [start];
  while (queue.length) {
    const cur = queue.shift();
    for (const next of links[cur] || []) {
      if (!seen.has(next)) {
        seen.add(next);
        queue.push(next);
      }
    }
  }
  return seen;
}

function loadArtifact(dir) {
  const canvas = JSON.parse(readFileSync(join(dir, 'project', 'canvas.json'), 'utf8'));
  const files = new Set(
    readdirSync(join(dir, 'project'))
      .filter((f) => f.endsWith('.dc.html'))
      .map((f) => f.replace(/\.dc\.html$/, '')),
  );
  const boards = {};
  for (const name of files)
    boards[name] = parseBoard(readFileSync(join(dir, 'project', name + '.dc.html'), 'utf8'));
  return { boards: new Set(canvasBoards(canvas)), files, parsed: boards };
}

/** Faz todas as conferências; devolve { checks: [{ name, ok, detail }], unreachable }. */
export function checkAll(markdown, wireframes, prototype) {
  const checks = [];
  const add = (name, problems, okDetail) =>
    checks.push({
      name,
      ok: problems.length === 0,
      detail: problems.length ? problems.join('; ') : okDetail,
    });

  const screens = parseScreens(markdown);
  const expected = [...Array(25)].map((_, i) => 'T' + String(i + 1).padStart(2, '0'));
  const ids = new Set(screens.map((s) => s.id));
  add(
    'Tabela tem T01 a T25',
    expected.filter((id) => !ids.has(id)).map((id) => id + ' ausente'),
    `${screens.length} linhas (T01 a T25 e T05b)`,
  );

  const exists = (art, name) => art.boards.has(name) && art.files.has(name);
  add(
    'Cada tela tem prancha de wireframe que existe',
    screens
      .filter((s) => !s.wf || !exists(wireframes, s.wf))
      .map((s) => `${s.id}: ${s.wf ?? 'sem wf:'}`),
    `${screens.length} de ${screens.length}`,
  );
  add(
    'Cada tela tem prancha no protótipo que existe',
    screens
      .filter((s) => !s.pt || !exists(prototype, s.pt))
      .map((s) => `${s.id}: ${s.pt ?? 'sem pt:'}`),
    `${screens.length} de ${screens.length}`,
  );

  const states = parseStateBoards(markdown);
  const stateIds = [...Array(10)].map((_, i) => 'E' + String(i + 1).padStart(2, '0'));
  add(
    'Cada estado E01 a E10 tem prancha no protótipo',
    stateIds
      .filter((id) => !(states[id] || []).some((b) => exists(prototype, b)))
      .map((id) => id + ' sem prancha'),
    '10 de 10',
  );

  const missingFiles = [...prototype.boards].filter((b) => !prototype.files.has(b));
  add(
    'Toda prancha do canvas do protótipo tem arquivo',
    missingFiles,
    `${prototype.boards.size} pranchas`,
  );

  const broken = [];
  for (const [name, b] of Object.entries(prototype.parsed)) {
    for (const l of b.links) if (!prototype.files.has(l)) broken.push(`${name} → ${l}`);
  }
  add('Nenhum link do protótipo aponta para prancha inexistente', broken, 'nenhum link quebrado');

  const links = effectiveLinks(prototype.parsed);
  const fromMain = reachable(links, 'Main');
  add(
    'Da página inicial se chega ao fim de cada tarefa principal',
    Object.entries(TASK_ENDS)
      .filter(([, end]) => !fromMain.has(end))
      .map(([t, end]) => `${t} (${end}) inalcançável`),
    Object.entries(TASK_ENDS)
      .map(([t, end]) => `${t} → ${end}`)
      .join(', '),
  );

  const components = new Set(['GlobalBar', 'PlantBar', 'Icon']);
  const unreachable = [...prototype.boards]
    .filter((b) => !components.has(b) && !fromMain.has(b))
    .sort();
  return {
    checks,
    unreachable,
    reachableCount: [...prototype.boards].filter((b) => fromMain.has(b)).length,
  };
}

export function toMarkdown(result) {
  const lines = ['| Conferência | Resultado | Detalhe |', '| --- | --- | --- |'];
  for (const c of result.checks)
    lines.push(`| ${c.name} | ${c.ok ? 'passa' : '**falha**'} | ${c.detail} |`);
  lines.push('');
  lines.push(
    `Pranchas alcançáveis a partir da página inicial, só por links: ${result.reachableCount}. ` +
      `Abertas só pelo canvas (variantes de estado e telas de outras fases): ${result.unreachable.map((b) => '`' + b + '`').join(', ') || 'nenhuma'}.`,
  );
  return lines.join('\n') + '\n';
}

function main(args) {
  const here = fileURLToPath(new URL('.', import.meta.url));
  const root = resolve(here, '..', '..');
  const markdown = readFileSync(join(root, 'docs', 'design', 'mapa-de-telas.md'), 'utf8');
  const fontes = join(root, 'docs', 'design', 'fontes');
  if (!existsSync(join(fontes, 'prototipo'))) {
    console.error('Fontes do protótipo não encontradas em docs/design/fontes/prototipo.');
    return 1;
  }
  const result = checkAll(
    markdown,
    loadArtifact(join(fontes, 'wireframes')),
    loadArtifact(join(fontes, 'prototipo')),
  );
  const md = toMarkdown(result);
  console.log(md);
  const mdIndex = args.indexOf('--md');
  if (mdIndex >= 0 && args[mdIndex + 1]) writeFileSync(args[mdIndex + 1], md);
  return result.checks.every((c) => c.ok) ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(2));
}
