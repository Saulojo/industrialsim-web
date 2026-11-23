# Design — trilha D1

Entregáveis da Etapa D1 (prototipagem e teste com usuários), conforme `docs/plano.md`. O que sai daqui é a referência da Etapa D2 (sistema de design aplicado no código).

## Entregáveis

| Parte | Entregável | Onde | Situação |
| --- | --- | --- | --- |
| A | Perfis e tarefas | [`perfis-e-tarefas.md`](perfis-e-tarefas.md) | pronto |
| A | Mapa de telas e fluxos | [`mapa-de-telas.md`](mapa-de-telas.md) | pronto |
| B | Wireframes de baixa fidelidade | Artifact (link abaixo) | pronto, aguardando revisão |
| C | Guia de identidade visual | [`identidade-visual.md`](identidade-visual.md) e Artifact (link abaixo) | pronto |
| C | Logotipo | [`logo/`](logo/) | conceito B escolhido em 23/11; falta converter o nome em curvas (autor, Inkscape ou Figma) |
| C | Tokens (fonte para a D2) | [`tokens.json`](tokens.json) | pronto |
| C | Verificação de contraste WCAG 2.2 AA | [`contraste.md`](contraste.md) e `tools/design/contraste.mjs` | 98 pares, todos passam |
| D | Protótipo navegável de alta fidelidade | Artifact (link abaixo) | todas as telas T01–T25 e estados E01–E10; aguardando revisão |
| D | Revisão ISA-101 das telas de IHM | [`isa-101.md`](isa-101.md) | pronta; 3 problemas corrigidos no protótipo, 4 ressalvas para o orientador |
| D | Chaves de idioma dos textos novos | [`mapa-de-telas.md`](mapa-de-telas.md#textos-novos) | 475 chaves propostas para a D2 (telas de fidelidade média ficam para as etapas delas) |
| E | Roteiro, tarefas cronometradas e termo de consentimento | [`teste/roteiro.md`](teste/roteiro.md), [`teste/termo-de-consentimento.md`](teste/termo-de-consentimento.md) | pronto; falta o teste piloto, e o termo precisa dos nomes e contatos |
| E | Formulário do moderador | Artifact (link abaixo) e [`teste/resultados.csv`](teste/resultados.csv) | pronto e conferido pelo autor no claude.ai em 23/11 (o CSV só tem o cabeçalho até o teste) |
| F | Conferência do mapa contra o protótipo | `tools/design/mapa-prototipo.mjs`; fontes em [`fontes/`](fontes/) | pronta; todas as conferências passam |
| F | Validação da etapa e decisões novas (D46 a D50) | [`docs/validacao/etapa-d1.md`](../validacao/etapa-d1.md) | rascunho; decisões ainda não estão no descritivo |
| F | Relatório do teste e correções | `teste/relatorio.md` | depois do teste |

## Links

Os Artifacts são páginas no claude.ai; quem os publicou decide com quem compartilhar. O protótipo está com o link público (23/11): foi conferido sem login, e a navegação funciona. Só aparece um aviso do claude.ai ("Faça login para ver os dados deste artefato"), que pode ser ignorado.

- Wireframes (32 pranchas em 4 páginas: Fase 1, Fase 2, Fases 3 e 4, Componentes; revistos em 23/11 com abas na planta, aba Gráficos e compartilhamento por código): https://claude.ai/artifact/5mtQasLaySPNUq3wMcSQLY
- Identidade visual (logotipo, paleta, tipografia e ícones, cor na IHM): https://claude.ai/artifact/FNXXhg5dJGu3YXVAS4ZRsk
- Protótipo (35 pranchas em 6 páginas: T1 e T2, T3, T4 e T5, Estados e outras telas, Fases 3 e 4, Componentes; fluxos das cinco tarefas principais navegáveis): https://claude.ai/artifact/BATxECGZStkjMLSwVSitZk
- Formulário do moderador (privado, só o autor abre; guarda as sessões no próprio Artifact e exporta `resultados.csv`): https://claude.ai/artifact/XFWD466p3YkcWjiqaB4e1z

### Ponto de partida de cada tarefa no protótipo

Abrir a prancha em "Play full screen":

| Tarefa | Começa em | Termina quando |
| --- | --- | --- |
| T1 Abrir uma planta | `Main` (T01) | a planta abre sem mestre (`E01`) |
| T2 Parear um mestre | `E01` ou `T05b` | o mestre aparece conectado (`T06`) |
| T3 Mudar o formato analógico | `T06` | aparece "Formato analógico aplicado" (`T08`) |
| T4 Compartilhar a planta | `T06-Conta` ou `T14` | o convite é enviado ou o código é gerado (`T15`) |
| T5 Injetar falha (professor) | `T16` | o cartão da planta mostra "FALHA · LT-101" (`T18`) |

### Limitações conhecidas do protótipo

Avisar os participantes antes do teste:

- Cada tela é uma prancha separada. O tempo simulado volta ao valor inicial ao trocar de aba, e as abas abertas são as previstas para cada tela, não as que a pessoa abriu.
- O sinóptico foi desenhado para telas de 1280 px ou mais de largura. Em janelas mais estreitas, a ponta direita (rótulo "Vapor") fica cortada.
- Valores e eventos do log são fictícios e fixos. O log mostra a troca 0–27648 → 0–10000, qualquer que seja o formato escolhido.

## Convenções

- Telas identificadas por T01…T25 e estados por E01…E10 (`mapa-de-telas.md`); tarefas por T1…T5 (principais) e S1…S13 (`perfis-e-tarefas.md`).
- Dados do protótipo tirados do repositório: tags, faixas e mapa Modbus de `scenarios/caldeira/scenario.json`; textos de `web/src/locales/pt-BR/translation.json`.
- Nenhum código de produção nesta etapa: `web/src` só muda na D2.
