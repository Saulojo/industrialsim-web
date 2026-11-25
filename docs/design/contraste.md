# Contraste da paleta (WCAG 2.2 AA)

Etapa D1, parte C. Resultado do verificador `tools/design/contraste.mjs` sobre `docs/design/tokens.json`, nos temas claro (`light`) e escuro (`dark`).

## Critérios

- **4,5:1** para texto normal (WCAG 2.2, critério 1.4.3, nível AA).
- **3:1** para elementos gráficos e componentes de interface que precisam ser percebidos: bordas de campos, indicador de foco, traços de equipamento, séries de tendência, indicadores de alarme (critério 1.4.11, nível AA). Texto grande também pede 3:1, mas a paleta usa 4,5:1 em todo texto.
- A razão segue as definições de luminância relativa e de razão de contraste do WCAG 2.2, com o limiar de linearização 0,04045 da especificação sRGB. A razão não é arredondada antes da comparação (4,499 reprova num mínimo de 4,5); a tabela mostra o valor truncado em 2 casas.
- Indicadores de alarme de prioridade 2 e 3 (laranja e amarelo) não alcançam 3:1 contra o fundo claro. No tema claro eles têm contorno na cor do texto (`alarm-2-edge` e `alarm-3-edge`); no escuro, o próprio preenchimento já contrasta. Os pares `*-edge` conferem isso. A prioridade também é indicada por forma e número, nunca só por cor (critério 1.4.1).
- Cores que não carregam informação sozinhas ficam fora da lista: `border` (divisórias decorativas), `water` (preenchimento do tubulão; o nível é dado pela linha em `equipment-on`, conferida contra a água, e pelo valor numérico), `text-disabled` (controles desabilitados são isentos pelo critério 1.4.3) e `overlay`.

## Como reproduzir

```powershell
node tools/design/contraste.mjs
```

```powershell
node --test tools/design/
```

O primeiro comando imprime a tabela abaixo e sai com código 1 se algum par reprovar; o segundo roda os testes do verificador (valores de referência, como preto sobre branco = 21:1 e #777 sobre branco < 4,5:1) e confere de novo a paleta.

## Resultado (23/11/2026, Node 26.13.0)

Saída: código 0. Testes: 7 de 7 passam.

### Tema light

| Frente | Fundo | Razão | Mínimo | Resultado | Uso |
| --- | --- | --- | --- | --- | --- |
| `text` #1A1D21 | `bg` #E9EBED | 14.15:1 | 4.5:1 | passa | Texto sobre o fundo da IHM |
| `text` #1A1D21 | `surface` #FFFFFF | 16.91:1 | 4.5:1 | passa | Texto em painéis |
| `text` #1A1D21 | `surface-sunken` #F3F4F6 | 15.36:1 | 4.5:1 | passa | Texto em áreas rebaixadas (tabelas, campos) |
| `text-muted` #4D555D | `bg` #E9EBED | 6.33:1 | 4.5:1 | passa | Rótulos e textos secundários sobre o fundo |
| `text-muted` #4D555D | `surface` #FFFFFF | 7.57:1 | 4.5:1 | passa | Rótulos e textos secundários em painéis |
| `text-muted` #4D555D | `surface-sunken` #F3F4F6 | 6.88:1 | 4.5:1 | passa | Rótulos em áreas rebaixadas |
| `accent` #1C5A91 | `surface` #FFFFFF | 7.19:1 | 4.5:1 | passa | Links em painéis |
| `accent` #1C5A91 | `bg` #E9EBED | 6.01:1 | 4.5:1 | passa | Links sobre o fundo |
| `on-accent` #FFFFFF | `accent` #1C5A91 | 7.19:1 | 4.5:1 | passa | Texto do botão principal |
| `on-accent` #FFFFFF | `accent-hover` #154773 | 9.62:1 | 4.5:1 | passa | Texto do botão principal sob o ponteiro |
| `border-strong` #6E7780 | `surface` #FFFFFF | 4.55:1 | 3:1 | passa | Borda de campos e controles (1.4.11) |
| `border-strong` #6E7780 | `bg` #E9EBED | 3.80:1 | 3:1 | passa | Borda de controles sobre o fundo (1.4.11) |
| `focus` #1C5A91 | `surface` #FFFFFF | 7.19:1 | 3:1 | passa | Indicador de foco em painéis (1.4.11) |
| `focus` #1C5A91 | `bg` #E9EBED | 6.01:1 | 3:1 | passa | Indicador de foco sobre o fundo (1.4.11) |
| `equipment` #6E7780 | `surface` #FFFFFF | 4.55:1 | 3:1 | passa | Traço de equipamento e tubulação no sinóptico |
| `equipment` #6E7780 | `bg` #E9EBED | 3.80:1 | 3:1 | passa | Traço de equipamento sobre o fundo |
| `equipment-on` #3B434B | `surface` #FFFFFF | 10.04:1 | 3:1 | passa | Equipamento em operação (preenchimento escuro) |
| `equipment-on` #3B434B | `water` #C3D0DC | 6.39:1 | 3:1 | passa | Linha do nível sobre a água no tubulão |
| `alarm-1` #C8102E | `surface` #FFFFFF | 5.88:1 | 3:1 | passa | Indicador de alarme de prioridade 1 |
| `alarm-1` #C8102E | `bg` #E9EBED | 4.92:1 | 3:1 | passa | Indicador de alarme de prioridade 1 sobre o fundo |
| `on-alarm-1` #FFFFFF | `alarm-1` #C8102E | 5.88:1 | 4.5:1 | passa | Número ou texto dentro do indicador de prioridade 1 |
| `alarm-1-text` #B00020 | `surface` #FFFFFF | 7.32:1 | 4.5:1 | passa | Texto de alarme de prioridade 1 em painéis |
| `alarm-1-text` #B00020 | `alarm-1-surface` #FDE8EA | 6.24:1 | 4.5:1 | passa | Texto na linha de alarme de prioridade 1 |
| `text` #1A1D21 | `alarm-1-surface` #FDE8EA | 14.42:1 | 4.5:1 | passa | Texto comum na linha de alarme de prioridade 1 |
| `on-alarm-2` #1A1D21 | `alarm-2` #F28C00 | 6.86:1 | 4.5:1 | passa | Número ou texto dentro do indicador de prioridade 2 |
| `alarm-2-text` #954700 | `surface` #FFFFFF | 6.60:1 | 4.5:1 | passa | Texto de alarme de prioridade 2 em painéis |
| `alarm-2-text` #954700 | `alarm-2-surface` #FFF0DC | 5.89:1 | 4.5:1 | passa | Texto na linha de alarme de prioridade 2 |
| `text` #1A1D21 | `alarm-2-surface` #FFF0DC | 15.10:1 | 4.5:1 | passa | Texto comum na linha de alarme de prioridade 2 |
| `on-alarm-3` #1A1D21 | `alarm-3` #F5D000 | 11.19:1 | 4.5:1 | passa | Número ou texto dentro do indicador de prioridade 3 |
| `alarm-3-text` #6B5800 | `surface` #FFFFFF | 6.95:1 | 4.5:1 | passa | Texto de alarme de prioridade 3 em painéis |
| `alarm-3-text` #6B5800 | `alarm-3-surface` #FFF8D6 | 6.50:1 | 4.5:1 | passa | Texto na linha de alarme de prioridade 3 |
| `text` #1A1D21 | `alarm-3-surface` #FFF8D6 | 15.82:1 | 4.5:1 | passa | Texto comum na linha de alarme de prioridade 3 |
| `alarm-2-edge` #1A1D21 | `surface` #FFFFFF | 16.91:1 | 3:1 | passa | Borda do indicador de prioridade 2 (claro: contorno escuro; escuro: o próprio preenchimento) |
| `alarm-2-edge` #1A1D21 | `bg` #E9EBED | 14.15:1 | 3:1 | passa | Borda do indicador de prioridade 2 sobre o fundo |
| `alarm-3-edge` #1A1D21 | `surface` #FFFFFF | 16.91:1 | 3:1 | passa | Borda do indicador de prioridade 3 (claro: contorno escuro; escuro: o próprio preenchimento) |
| `alarm-3-edge` #1A1D21 | `bg` #E9EBED | 14.15:1 | 3:1 | passa | Borda do indicador de prioridade 3 sobre o fundo |
| `forced` #8E2A7A | `surface` #FFFFFF | 7.60:1 | 4.5:1 | passa | Marca FORÇADO e valor forçado em painéis |
| `forced` #8E2A7A | `bg` #E9EBED | 6.36:1 | 4.5:1 | passa | Valor forçado no sinóptico, sobre o fundo |
| `forced` #8E2A7A | `forced-surface` #F6E6F3 | 6.34:1 | 4.5:1 | passa | Texto no destaque de valor forçado |
| `text` #1A1D21 | `forced-surface` #F6E6F3 | 14.11:1 | 4.5:1 | passa | Texto comum no destaque de valor forçado |
| `fault` #006B73 | `surface` #FFFFFF | 6.26:1 | 4.5:1 | passa | Marca FALHA (instrumento em falha) em painéis |
| `fault` #006B73 | `bg` #E9EBED | 5.24:1 | 4.5:1 | passa | Marca FALHA no sinóptico, sobre o fundo |
| `fault` #006B73 | `fault-surface` #E0F2F3 | 5.42:1 | 4.5:1 | passa | Texto no destaque de falha de instrumento |
| `text` #1A1D21 | `fault-surface` #E0F2F3 | 14.62:1 | 4.5:1 | passa | Texto comum no destaque de falha |
| `trend-1` #1C5A91 | `surface` #FFFFFF | 7.19:1 | 3:1 | passa | Série 1 das tendências |
| `trend-2` #6B4C9A | `surface` #FFFFFF | 6.71:1 | 3:1 | passa | Série 2 das tendências |
| `trend-3` #2F7A3E | `surface` #FFFFFF | 5.28:1 | 3:1 | passa | Série 3 das tendências |
| `trend-4` #7A5C3A | `surface` #FFFFFF | 6.14:1 | 3:1 | passa | Série 4 das tendências |
| `trend-5` #4D555D | `surface` #FFFFFF | 7.57:1 | 3:1 | passa | Série 5 das tendências |

### Tema dark

| Frente | Fundo | Razão | Mínimo | Resultado | Uso |
| --- | --- | --- | --- | --- | --- |
| `text` #E7EAED | `bg` #121518 | 15.17:1 | 4.5:1 | passa | Texto sobre o fundo da IHM |
| `text` #E7EAED | `surface` #1C2024 | 13.57:1 | 4.5:1 | passa | Texto em painéis |
| `text` #E7EAED | `surface-sunken` #16191C | 14.61:1 | 4.5:1 | passa | Texto em áreas rebaixadas (tabelas, campos) |
| `text-muted` #A7AFB7 | `bg` #121518 | 8.25:1 | 4.5:1 | passa | Rótulos e textos secundários sobre o fundo |
| `text-muted` #A7AFB7 | `surface` #1C2024 | 7.38:1 | 4.5:1 | passa | Rótulos e textos secundários em painéis |
| `text-muted` #A7AFB7 | `surface-sunken` #16191C | 7.94:1 | 4.5:1 | passa | Rótulos em áreas rebaixadas |
| `accent` #8CC2F2 | `surface` #1C2024 | 8.67:1 | 4.5:1 | passa | Links em painéis |
| `accent` #8CC2F2 | `bg` #121518 | 9.69:1 | 4.5:1 | passa | Links sobre o fundo |
| `on-accent` #0B1A28 | `accent` #8CC2F2 | 9.31:1 | 4.5:1 | passa | Texto do botão principal |
| `on-accent` #0B1A28 | `accent-hover` #A9D3F7 | 11.19:1 | 4.5:1 | passa | Texto do botão principal sob o ponteiro |
| `border-strong` #7C858E | `surface` #1C2024 | 4.37:1 | 3:1 | passa | Borda de campos e controles (1.4.11) |
| `border-strong` #7C858E | `bg` #121518 | 4.88:1 | 3:1 | passa | Borda de controles sobre o fundo (1.4.11) |
| `focus` #8CC2F2 | `surface` #1C2024 | 8.67:1 | 3:1 | passa | Indicador de foco em painéis (1.4.11) |
| `focus` #8CC2F2 | `bg` #121518 | 9.69:1 | 3:1 | passa | Indicador de foco sobre o fundo (1.4.11) |
| `equipment` #7C858E | `surface` #1C2024 | 4.37:1 | 3:1 | passa | Traço de equipamento e tubulação no sinóptico |
| `equipment` #7C858E | `bg` #121518 | 4.88:1 | 3:1 | passa | Traço de equipamento sobre o fundo |
| `equipment-on` #C6CDD4 | `surface` #1C2024 | 10.21:1 | 3:1 | passa | Equipamento em operação (preenchimento escuro) |
| `equipment-on` #C6CDD4 | `water` #2D4357 | 6.37:1 | 3:1 | passa | Linha do nível sobre a água no tubulão |
| `alarm-1` #FF5C5C | `surface` #1C2024 | 5.41:1 | 3:1 | passa | Indicador de alarme de prioridade 1 |
| `alarm-1` #FF5C5C | `bg` #121518 | 6.05:1 | 3:1 | passa | Indicador de alarme de prioridade 1 sobre o fundo |
| `on-alarm-1` #121518 | `alarm-1` #FF5C5C | 6.05:1 | 4.5:1 | passa | Número ou texto dentro do indicador de prioridade 1 |
| `alarm-1-text` #FF8A80 | `surface` #1C2024 | 7.17:1 | 4.5:1 | passa | Texto de alarme de prioridade 1 em painéis |
| `alarm-1-text` #FF8A80 | `alarm-1-surface` #3D1A1C | 6.74:1 | 4.5:1 | passa | Texto na linha de alarme de prioridade 1 |
| `text` #E7EAED | `alarm-1-surface` #3D1A1C | 12.75:1 | 4.5:1 | passa | Texto comum na linha de alarme de prioridade 1 |
| `on-alarm-2` #121518 | `alarm-2` #FF9F1A | 8.92:1 | 4.5:1 | passa | Número ou texto dentro do indicador de prioridade 2 |
| `alarm-2-text` #FFB45C | `surface` #1C2024 | 9.31:1 | 4.5:1 | passa | Texto de alarme de prioridade 2 em painéis |
| `alarm-2-text` #FFB45C | `alarm-2-surface` #3A2610 | 8.14:1 | 4.5:1 | passa | Texto na linha de alarme de prioridade 2 |
| `text` #E7EAED | `alarm-2-surface` #3A2610 | 11.87:1 | 4.5:1 | passa | Texto comum na linha de alarme de prioridade 2 |
| `on-alarm-3` #121518 | `alarm-3` #F5D000 | 12.13:1 | 4.5:1 | passa | Número ou texto dentro do indicador de prioridade 3 |
| `alarm-3-text` #F2D64B | `surface` #1C2024 | 11.31:1 | 4.5:1 | passa | Texto de alarme de prioridade 3 em painéis |
| `alarm-3-text` #F2D64B | `alarm-3-surface` #33300F | 9.23:1 | 4.5:1 | passa | Texto na linha de alarme de prioridade 3 |
| `text` #E7EAED | `alarm-3-surface` #33300F | 11.07:1 | 4.5:1 | passa | Texto comum na linha de alarme de prioridade 3 |
| `alarm-2-edge` #FF9F1A | `surface` #1C2024 | 7.98:1 | 3:1 | passa | Borda do indicador de prioridade 2 (claro: contorno escuro; escuro: o próprio preenchimento) |
| `alarm-2-edge` #FF9F1A | `bg` #121518 | 8.92:1 | 3:1 | passa | Borda do indicador de prioridade 2 sobre o fundo |
| `alarm-3-edge` #F5D000 | `surface` #1C2024 | 10.85:1 | 3:1 | passa | Borda do indicador de prioridade 3 (claro: contorno escuro; escuro: o próprio preenchimento) |
| `alarm-3-edge` #F5D000 | `bg` #121518 | 12.13:1 | 3:1 | passa | Borda do indicador de prioridade 3 sobre o fundo |
| `forced` #E59AD6 | `surface` #1C2024 | 7.74:1 | 4.5:1 | passa | Marca FORÇADO e valor forçado em painéis |
| `forced` #E59AD6 | `bg` #121518 | 8.65:1 | 4.5:1 | passa | Valor forçado no sinóptico, sobre o fundo |
| `forced` #E59AD6 | `forced-surface` #3A1D35 | 7.05:1 | 4.5:1 | passa | Texto no destaque de valor forçado |
| `text` #E7EAED | `forced-surface` #3A1D35 | 12.35:1 | 4.5:1 | passa | Texto comum no destaque de valor forçado |
| `fault` #5CCFD8 | `surface` #1C2024 | 8.87:1 | 4.5:1 | passa | Marca FALHA (instrumento em falha) em painéis |
| `fault` #5CCFD8 | `bg` #121518 | 9.92:1 | 4.5:1 | passa | Marca FALHA no sinóptico, sobre o fundo |
| `fault` #5CCFD8 | `fault-surface` #12343A | 7.20:1 | 4.5:1 | passa | Texto no destaque de falha de instrumento |
| `text` #E7EAED | `fault-surface` #12343A | 11.02:1 | 4.5:1 | passa | Texto comum no destaque de falha |
| `trend-1` #8CC2F2 | `surface` #1C2024 | 8.67:1 | 3:1 | passa | Série 1 das tendências |
| `trend-2` #B9A3DC | `surface` #1C2024 | 7.28:1 | 3:1 | passa | Série 2 das tendências |
| `trend-3` #86C98F | `surface` #1C2024 | 8.40:1 | 3:1 | passa | Série 3 das tendências |
| `trend-4` #C8A27E | `surface` #1C2024 | 6.96:1 | 3:1 | passa | Série 4 das tendências |
| `trend-5` #A7AFB7 | `surface` #1C2024 | 7.38:1 | 3:1 | passa | Série 5 das tendências |

Total: 98 pares conferidos, 98 passam, 0 reprovam.
