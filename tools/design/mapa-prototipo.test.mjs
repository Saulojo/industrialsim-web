// Testes da conferência mapa de telas × protótipo: node --test tools/design/
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import {
  checkAll,
  effectiveLinks,
  parseBoard,
  parseScreens,
  parseStateBoards,
  reachable,
} from './mapa-prototipo.mjs';

const row = (id, wf, pt) =>
  `| ${id} | Tela ${id} | /x | D2 | todos | — | A | não | wf: \`${wf}\`${pt ? ` · pt: \`${pt}\`` : ''} |`;
const allRows = (fn) =>
  [...Array(25)]
    .map((_, i) => 'T' + String(i + 1).padStart(2, '0'))
    .map(fn)
    .join('\n');
const statesPara =
  'Pranchas dos estados no protótipo: E01 em `A`; E02 em `A`; E03, E04 e E10 em `A`; E05 e E09 em `A`; E06 e E08 em `A`; E07 em `A` (ajuste `estado`).';

function artifact(names, htmlByName = {}) {
  const parsed = {};
  for (const n of names) parsed[n] = parseBoard(htmlByName[n] || '');
  return { boards: new Set(names), files: new Set(names), parsed };
}

test('lê as linhas da tabela de telas com wf: e pt:', () => {
  const screens = parseScreens(row('T01', 'Main', 'Main') + '\n' + row('T02', 'T02-Menu'));
  assert.deepEqual(
    screens.map((s) => [s.id, s.wf, s.pt]),
    [
      ['T01', 'Main', 'Main'],
      ['T02', 'T02-Menu', null],
    ],
  );
});

test('lê as pranchas de cada estado e ignora o nome do ajuste', () => {
  const states = parseStateBoards(
    'Pranchas dos estados no protótipo: E01 em `X`; E03 e E10 em `Y` e `Z`; E07 em `W` (ajuste `estado`).',
  );
  assert.deepEqual(states.E01, ['X']);
  assert.deepEqual(states.E10, ['Y', 'Z']);
  assert.deepEqual(states.E07, ['W']);
});

test('links do HTML e do código; componentes importados somam links', () => {
  const page = parseBoard('<a href="B.dc.html">b</a><dc-import name="Bar"></dc-import>');
  const bar = parseBoard("<script>const defs = { c: ['C', 'C.dc.html'] };</script>");
  assert.deepEqual(page.links, ['B']);
  assert.deepEqual(page.imports, ['Bar']);
  const links = effectiveLinks({ Page: page, Bar: bar });
  assert.deepEqual([...links.Page].sort(), ['B', 'C']);
  assert.deepEqual([...reachable(links, 'Page')].sort(), ['B', 'C', 'Page']);
});

test('aponta tela sem prancha no protótipo, link quebrado e tarefa inalcançável', () => {
  const md = allRows((id) => row(id, 'A', id === 'T07' ? null : 'A')) + '\n' + statesPara;
  const proto = artifact(['A', 'Main'], { Main: '<a href="Fantasma.dc.html">x</a>' });
  const result = checkAll(md, artifact(['A']), proto);
  const byName = Object.fromEntries(result.checks.map((c) => [c.name, c]));
  assert.equal(byName['Cada tela tem prancha no protótipo que existe'].ok, false);
  assert.match(byName['Cada tela tem prancha no protótipo que existe'].detail, /T07/);
  assert.equal(byName['Nenhum link do protótipo aponta para prancha inexistente'].ok, false);
  assert.equal(byName['Da página inicial se chega ao fim de cada tarefa principal'].ok, false);
});

test('aponta estado sem prancha', () => {
  const md =
    allRows((id) => row(id, 'A', 'A')) + '\nPranchas dos estados no protótipo: E01 em `A`.';
  const result = checkAll(md, artifact(['A']), artifact(['A']));
  const check = result.checks.find((c) => c.name.startsWith('Cada estado'));
  assert.equal(check.ok, false);
  assert.match(check.detail, /E02 sem prancha/);
});

test('o mapa e o protótipo do repositório passam em todas as conferências', async () => {
  const { checkAll: run } = await import('./mapa-prototipo.mjs');
  const md = readFileSync(new URL('../../docs/design/mapa-de-telas.md', import.meta.url), 'utf8');
  const { readdirSync } = await import('node:fs');
  const load = (dir) => {
    const base = new URL(`../../docs/design/fontes/${dir}/project/`, import.meta.url);
    const canvas = JSON.parse(readFileSync(new URL('canvas.json', base), 'utf8'));
    const files = readdirSync(base)
      .filter((f) => f.endsWith('.dc.html'))
      .map((f) => f.replace('.dc.html', ''));
    const parsed = {};
    for (const f of files)
      parsed[f] = parseBoard(readFileSync(new URL(f + '.dc.html', base), 'utf8'));
    return {
      boards: new Set(Object.keys(canvas.boards).map((b) => b.replace('.dc.html', ''))),
      files: new Set(files),
      parsed,
    };
  };
  const result = run(md, load('wireframes'), load('prototipo'));
  for (const c of result.checks) assert.ok(c.ok, `${c.name}: ${c.detail}`);
});
