# Design — trilha D1

Entregáveis da Etapa D1 (prototipagem e teste com usuários), conforme `docs/plano.md`. O que sai daqui é a referência da Etapa D2 (sistema de design aplicado no código).

## Entregáveis

| Parte | Entregável | Onde | Situação |
| --- | --- | --- | --- |
| A | Perfis e tarefas | [`perfis-e-tarefas.md`](perfis-e-tarefas.md) | pronto |
| A | Mapa de telas e fluxos | [`mapa-de-telas.md`](mapa-de-telas.md) | pronto |
| B | Wireframes de baixa fidelidade | Artifact (link abaixo) | pronto, aguardando revisão |
| C | Guia de identidade visual | `identidade-visual.md` | a fazer |
| C | Tokens (fonte para a D2) | `tokens.json` | a fazer |
| C | Verificação de contraste WCAG 2.2 AA | `contraste.md` e `tools/design/contraste.mjs` | a fazer |
| D | Protótipo navegável de alta fidelidade | Artifact (link abaixo) | a fazer |
| D | Revisão ISA-101 das telas de IHM | `isa-101.md` | a fazer |
| E | Roteiro, tarefas cronometradas e termo de consentimento | `teste/` | a fazer |
| E | Formulário do moderador | Artifact (link abaixo) e `teste/resultados.csv` | a fazer |
| F | Relatório do teste e correções | `teste/relatorio.md` | depois do teste |

## Links

Os Artifacts são páginas privadas no claude.ai; quem os publicou decide com quem compartilhar.

- Wireframes (32 pranchas em 4 páginas: Fase 1, Fase 2, Fases 3 e 4, Componentes; revistos em 23/11 com abas na planta, aba Gráficos e compartilhamento por código): https://claude.ai/artifact/5mtQasLaySPNUq3wMcSQLY
- Protótipo: *(preenchido na parte D)*
- Formulário do moderador: *(preenchido na parte E)*

## Convenções

- Telas identificadas por T01…T25 e estados por E01…E10 (`mapa-de-telas.md`); tarefas por T1…T5 (principais) e S1…S13 (`perfis-e-tarefas.md`).
- Dados do protótipo tirados do repositório: tags, faixas e mapa Modbus de `scenarios/caldeira/scenario.json`; textos de `web/src/locales/pt-BR/translation.json`.
- Nenhum código de produção nesta etapa: `web/src` só muda na D2.
