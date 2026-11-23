// Testes do verificador de contraste: node --test tools/design/
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import {
  checkTokens,
  contrastRatio,
  formatRatio,
  parseHex,
  relativeLuminance,
} from './contraste.mjs';

test('lê #rgb e #rrggbb', () => {
  assert.deepEqual(parseHex('#fff'), [255, 255, 255]);
  assert.deepEqual(parseHex('#1D5A8E'), [29, 90, 142]);
  assert.throws(() => parseHex('rgb(0 0 0)'));
  assert.throws(() => parseHex('#12345678'));
});

test('luminância relativa nos extremos', () => {
  assert.equal(relativeLuminance([0, 0, 0]), 0);
  assert.equal(relativeLuminance([255, 255, 255]), 1);
});

test('preto sobre branco é 21:1 e cor sobre ela mesma é 1:1', () => {
  assert.equal(contrastRatio('#000000', '#ffffff'), 21);
  assert.equal(contrastRatio('#ffffff', '#000000'), 21);
  assert.equal(contrastRatio('#777777', '#777777'), 1);
});

test('valores de referência conhecidos', () => {
  // #777 sobre branco: 4,48:1, o caso clássico de cinza que reprova em 4,5 (WebAIM Contrast Checker).
  assert.equal(formatRatio(contrastRatio('#777777', '#ffffff')), '4.47');
  assert.ok(contrastRatio('#777777', '#ffffff') < 4.5);
  // #767676 sobre branco: 4,54:1, o cinza mais claro que passa em 4,5.
  assert.ok(contrastRatio('#767676', '#ffffff') >= 4.5);
});

test('formatRatio trunca, nunca arredonda para cima', () => {
  assert.equal(formatRatio(4.499), '4.49');
  assert.equal(formatRatio(21), '21.00');
});

test('checkTokens marca o par que reprova e exige tokens existentes', () => {
  const tokens = {
    color: { claro: { a: '#000000', b: '#ffffff', c: '#777777' } },
    contrastPairs: [
      { fg: 'a', bg: 'b', min: 4.5 },
      { fg: 'c', bg: 'b', min: 4.5 },
    ],
  };
  const results = checkTokens(tokens);
  assert.deepEqual(
    results.map((r) => r.pass),
    [true, false],
  );
  assert.throws(() => checkTokens({ ...tokens, contrastPairs: [{ fg: 'x', bg: 'b', min: 3 }] }));
});

test('a paleta do projeto passa em todos os pares, nos dois temas', () => {
  const tokens = JSON.parse(readFileSync(new URL('../../docs/design/tokens.json', import.meta.url), 'utf8'));
  const results = checkTokens(tokens);
  const failed = results.filter((r) => !r.pass).map((r) => `${r.theme}: ${r.fg} sobre ${r.bg}`);
  assert.deepEqual(failed, []);
  assert.deepEqual(Object.keys(tokens.color).sort(), ['dark', 'light']);
});
