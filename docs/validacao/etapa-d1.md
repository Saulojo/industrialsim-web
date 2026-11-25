# Validação — Etapa D1: Prototipagem

> **Concluída em 25/11/2026, sem o teste com usuários.** O teste foi retirado do plano por decisão do autor (D51), com o protótipo aprovado pelo orientador nessa data. O roteiro, o termo e o formulário do moderador continuam em `docs/design/teste/`, prontos para o caso de o teste voltar.
>
> Aprovado pelo orientador em 25/11/2026 (informado pelo autor): o protótipo, as decisões D46 a D50, as ressalvas ISA-101 como estão e a dispensa do Comitê de Ética.

- Branch: `etapa-d1-design`
- Pull request: *(preenchido ao abrir)*
- Data: 23/11/2026 (partes A a E) e 25/11/2026 (parte F e fechamento)

## Ambiente

| Item | Versão |
| --- | --- |
| Sistema (local) | Windows 11 Pro 10.0.26300 |
| Node | 26.13.0 (scripts de `tools/design/`, sem dependência nova) |
| Protótipo, wireframes e identidade | Artifacts do tipo Design no claude.ai (links em `docs/design/README.md`); fontes em `docs/design/fontes/` |
| Formulário do moderador | Artifact com armazenamento próprio (`db`) e exportação de arquivo (`downloads`); fonte em `docs/design/teste/formulario-moderador.html` |
| Navegador da conferência | Painel de navegador do Claude (Chromium), sem login no claude.ai |

Nenhuma mudança em `web/src` nem no servidor (a D1 não tem código de produção).

## O que foi criado

| Parte | Onde | O quê |
| --- | --- | --- |
| A | `docs/design/perfis-e-tarefas.md`, `mapa-de-telas.md` | 4 perfis de uso, tarefas T1–T5 e S1–S13, matriz perfil × tarefa. Mapa com 25 telas (e T05b), 10 estados especiais, modelo de navegação com abas na planta e fluxos em Mermaid. |
| B | Artifact de wireframes; `fontes/wireframes/` | 32 pranchas de baixa fidelidade em 4 páginas, revistas em 23/11 (abas, aba Gráficos, compartilhamento por código). |
| C | `identidade-visual.md`, `tokens.json`, `logo/`, `contraste.md`; Artifact de identidade; `tools/design/contraste.mjs` | Logotipo B (balão de instrumento ISA-5.1 com resposta ao degrau), paleta clara e escura, IBM Plex Sans e Mono, ícones Lucide, regras de cor na IHM. Contraste WCAG 2.2 AA conferido por script. |
| D | Artifact do protótipo; `fontes/prototipo/`; `isa-101.md` | 35 pranchas: todas as telas T01–T25, os estados E01–E10, as fases 3 e 4 em fidelidade média. Revisão ISA-101 com 3 correções e 4 ressalvas. 475 chaves de idioma propostas para os textos novos (`mapa-de-telas.md`, "Textos novos"). |
| E | `teste/roteiro.md`, `teste/termo-de-consentimento.md`, `teste/resultados.csv`; Artifact do formulário | Roteiro de 45 min com T1–T5, S1, S2 e S11, SEQ e SUS (versão brasileira validada); teste piloto; termo; formulário com cronômetro, resumo do critério e exportação do CSV. |
| F | `tools/design/mapa-prototipo.mjs` e testes; `docs/design/fontes/` | Conferência por script do mapa de telas contra os wireframes e o protótipo; fontes dos Artifacts no repositório; decisões D46 a D51 no descritivo. |

## Critérios de validação

| # | Critério (plano, Etapa D1) | Situação | Como foi verificado |
| --- | --- | --- | --- |
| 1 | Todas as telas do mapa existem no protótipo e cobrem os fluxos do visitante, do aluno e do professor | **verificado** | Script `tools/design/mapa-prototipo.mjs` (saída abaixo): 26 de 26 telas com prancha de wireframe e de protótipo, 10 de 10 estados, nenhum link quebrado, e a partir da página inicial se chega ao fim das 5 tarefas principais. Os fluxos das 5 tarefas foram percorridos à mão no navegador, sem login, em 23/11. Na T4, "Convidar" mostra "Convite enviado" e "Gerar código" mostra o código com a validade. |
| 2 | Nas tarefas principais, pelo menos 4 de 5 participantes concluem sem ajuda | **retirado (D51)** | Teste com usuários retirado do plano em 25/11/2026, por decisão do autor. Não foi verificado. |
| 3 | Os problemas encontrados no teste estão registrados e corrigidos no protótipo | **retirado (D51)** | Sem teste, não há problemas do teste. Os problemas achados na conferência no navegador e na revisão ISA-101 foram corrigidos (ver "Correções no protótipo"). |
| 4 | Paleta aprovada no verificador de contraste WCAG AA, nos temas claro e escuro | **verificado** | `node tools/design/contraste.mjs`: 98 pares, 98 passam, saída 0 (`docs/design/contraste.md`). |
| 5 | Telas de IHM revisadas contra os princípios da ISA-101 | **verificado** | `docs/design/isa-101.md`: 20 princípios tela a tela e 3 problemas corrigidos no protótipo. As 4 ressalvas foram decididas pelo orientador em 25/11/2026: ficam como estão, e a prioridade de alarme virou a D50. A conferência contra o texto da norma continua pendente (fonte usada: Hollifield et al., 2008). |
| 6 | Protótipo aprovado pelo orientador | **verificado** | Aprovado em 25/11/2026, segundo o autor, na versão publicada nessa data (fontes em `docs/design/fontes/prototipo/`). |

### Saída da conferência do mapa (critério 1)

Comando: `node tools/design/mapa-prototipo.mjs` (saída 0). Testes: `node --test tools/design/` → 13 de 13 passam (7 do contraste, 6 da conferência do mapa).

| Conferência | Resultado | Detalhe |
| --- | --- | --- |
| Tabela tem T01 a T25 | passa | 26 linhas (T01 a T25 e T05b) |
| Cada tela tem prancha de wireframe que existe | passa | 26 de 26 |
| Cada tela tem prancha no protótipo que existe | passa | 26 de 26 |
| Cada estado E01 a E10 tem prancha no protótipo | passa | 10 de 10 |
| Toda prancha do canvas do protótipo tem arquivo | passa | 35 pranchas |
| Nenhum link do protótipo aponta para prancha inexistente | passa | nenhum link quebrado |
| Da página inicial se chega ao fim de cada tarefa principal | passa | T1 → E01-SemMestre, T2 → T06-IHM, T3 → T08-Configuracoes, T4 → T15-Compartilhar, T5 → T18-Painel |

Pranchas que só abrem pelo canvas, porque são variantes de estado ou telas abertas por um menu: `EA-Alarme`, `EC-Avisos`, `T02-Menu`, `T06-Escuro`, `T19-InjetarFalha`, `T23-NovaAba`, `T24-MenuPlanta`. O roteiro do teste diz quando abrir cada uma.

## Correções no protótipo

Problemas achados na conferência no navegador, na revisão ISA-101 e na preparação do roteiro do teste. Todos foram corrigidos e republicados em 23/11.

| Onde | Problema | Correção |
| --- | --- | --- |
| T06 sinóptico | Etiquetas das válvulas cobriam as tubulações (água e combustível pareciam não chegar); valor da FV-102 cortado; rótulos sobre a bomba e sobre a LSHH-101 | Etiquetas reposicionadas; "cmd do mestre" saiu do sinóptico |
| Menu de nova aba (T23) | Saía da tela pela direita em janelas estreitas | Preso à largura da faixa de abas |
| T09 e T10 | A aba Conexão sumia ao vir de Configurações | Abas abertas alinhadas |
| Mensagens de confirmação | Ficavam atrás do aviso de login do claude.ai, que os participantes também veem | Passaram para o topo |
| Diálogos | Abriam no topo da página, mesmo com a página rolada | Presos à janela |
| T06 (alarme) | LSHH-101 em alarme só pela cor (ISA-101, C3) | Triângulo com o número 2 |
| T10 | Exceção Modbus com a cor e a forma do alarme de prioridade 2 (ISA-101, C2) | Marca neutra "EXCEÇÃO" |
| T18 | Tracejado marcava "não aberta" e "falha" | Tracejado só para a falha |
| Barra da planta | Sem mestre, a aba Conexão abria a tela com o mestre já conectado (a T2 podia ser concluída sem parear) | Sem mestre, a aba abre a tela do pareamento |
| T05b | Pedido recusado ou expirado nunca voltava (a T2 travava) | Volta depois do bloqueio de 60 s ou 6 s depois de expirar, como um CLP que tenta de novo |
| T11 | Notas de fora da faixa no formato 0–10000 contrariavam a D28 | Corrigidas: a medição satura em 3,8 e 20,5 mA |

## Decisões novas (D46 a D51, registradas no descritivo em 25/11/2026)

D46 a D50 foram aprovadas pelo orientador em 25/11/2026; a D51 é decisão do autor na mesma data. Junto com elas, mudaram no descritivo:

- a D15 (marcada como alterada pela D46);
- o RF09;
- a seção "Compartilhamento";
- os passos 2 e 5 do pareamento;
- a coluna "Quem altera" da tabela de configurações;
- o RNF12 e a D19 (sem o teste com usuários, D51);
- a linha "Estado atual";
- a referência "D01 a D51" (também no `CLAUDE.md`).

No plano, a Etapa D1 passou a se chamar "Prototipagem", e o teste e os critérios 2 e 3 ficaram riscados, com a nota da D51.

O texto abaixo é o resumo com o raciocínio; a versão de referência é a tabela de decisões do descritivo.

- **D46 (D1) · Compartilhamento sem papéis.**
  - Quem tem acesso a uma planta pode tudo o que quem a compartilhou pode: operar, parear e retirar mestres, configurar, forçar e compartilhar. Só quem criou a planta a exclui e remove pessoas.
  - Convite pelo e-mail de quem já tem conta, ou por código de 1 uso ou de vários usos, com validade (24 h, 7 dias ou até revogar) e revogável. O código entra em Minhas plantas ou em Turmas, no mesmo campo do código de turma, ou abre como link.
  - Ver sem comandar só existe para o professor, que abre a planta de um aluno pelo painel da turma (estado E09).
  - Continua da D15: vale a última escrita, e cada ação fica registrada com autor e horário.
  - *Motivo:* pedido do autor (23/11/2026). Numa aula, todos da dupla precisam comandar; escolher papel a cada convite é um passo que quase sempre dá "operador"; o código serve para quem está ao lado, sem trocar e-mail.
  - *Descartado:* papéis dono, operador e observador (D15 e RF09).
  - *Muda no descritivo:* a D15 (a parte dos papéis), o RF09, a tabela "Compartilhamento" e o passo 2 do pareamento, onde "ao dono e aos operadores" passa a "a quem está na planta".
- **D47 (D1) · Histórico dos gráficos só da sessão atual.**
  - O servidor guarda em memória, por planta, o histórico desde o início da sessão. A sessão vai de quando a planta começa a rodar (criada ou retomada da hibernação) até ela encerrar ou hibernar; reiniciar zera o histórico (D41).
  - Quem entra depois recebe esse histórico ao conectar. Não há histórico de sessões anteriores; os ensaios gravados (CSV) continuam como hoje.
  - Os últimos minutos ficam a 20 Hz e o resto é reduzido (por exemplo, a 1 Hz). A 1 Hz, 16 sinais em `float32` dão 230 kB por hora por planta (16 × 4 B × 3600).
  - A definição de sessão acima foi aprovada junto com a decisão.
  - *Motivo:* decisão do autor (23/11/2026). A aba Gráficos (T25) precisa mostrar a sessão inteira a quem chega atrasado, sem guardar dados entre sessões, e cabe na memória do PC do laboratório (RNF07).
  - *Descartado:* histórico só no navegador (quem entra depois veria só dali em diante); histórico persistente entre sessões.
- **D48 (D1) · Identidade visual e tokens.**
  - O nome continua "IndustrialSim Web", e o logotipo é o conceito B: balão de instrumento ISA-5.1 com uma resposta ao degrau.
  - Fontes IBM Plex Sans e IBM Plex Mono (SIL OFL 1.1), servidas pelo próprio site, sem Google Fonts em uso (LGPD e rede do laboratório). Números sempre em mono.
  - Ícones de interface Lucide (ISC); os símbolos de processo são próprios, no estilo da ISA-5.1.
  - `docs/design/tokens.json` é a fonte dos tokens da D2: cada chave de `color` vira `--color-<chave>`, nos temas claro e escuro.
  - O contraste é conferido por `tools/design/contraste.mjs`, e todo par novo entra em `contrastPairs`.
  - *Motivo:* licenças livres e compatíveis (nenhuma GPL ou comercial); paleta com o normal em cinza e cor só para o anormal (ISA-101); uma fonte para o design e o código.
  - *Descartado:* conceitos de logotipo A (tubulação) e C (malha).
- **D49 (D1) · Navegação dentro da planta por abas.**
  - Dentro da planta não há barra global. No lugar dela, uma faixa de abas: ⌂ (sai da planta), o nome da planta com o menu da planta (T24), Processo fixa e as outras ferramentas em abas que fecham.
  - O + abre o menu de nova aba (T23) com um clique. A barra de estado (mestre, situação, tempo simulado, alarmes, Pausar, Reiniciar, Exportar, Compartilhar) fica sempre visível.
  - A aba Comunicação da Etapa 8 se divide em Conexão e Configurações, e entra a aba Gráficos (T25).
  - *Motivo:* pedido do autor (23/11/2026), para sobrar espaço para a planta. Um clique, e não duplo clique: o duplo clique é difícil de descobrir, não existe em tela de toque e exige alternativa de teclado (WCAG 2.1.1).
  - *Descartado:* manter a barra global dentro da planta; menus separados por ferramenta.
- **D50 (D1) · Prioridade de alarme vinda do cenário** (ressalva 4 de `isa-101.md`).
  - Cada alarme em `hmi.alarms` do `scenario.json` ganha `priority` (1, 2 ou 3).
  - A IHM mostra a prioridade por cor, forma e número: círculo com 1, triângulo com 2, quadrado com 3. É assim na faixa de alarmes, no sinóptico e no painel do professor.
  - Na caldeira: watchdog = 1; LSHH-101, LSLL-101 e PSH-101 = 2.
  - *Motivo:* ISA-101 (prioridade sempre visível e nunca só pela cor); a prioridade é do processo, e cada cenário define a sua.
  - *Descartado:* prioridade fixa no código da IHM.
- **D51 (D1) · Teste com usuários retirado do plano.**
  - A Etapa D1 termina com o protótipo aprovado pelo orientador, conferido contra o mapa de telas por script e revisado contra a ISA-101.
  - O material do teste (roteiro, termo, formulário do moderador e `resultados.csv` vazio) fica no repositório para o caso de o teste voltar.
  - *Motivo:* decisão do autor, com o protótipo aprovado pelo orientador em 25/11/2026.
  - *Descartado:* fazer o teste antes de fechar a D1; adiar o teste para a D2; criar uma etapa de teste própria.

## Pendências (não impedem o fechamento)

1. **Logotipo:** converter o nome em curvas (Inkscape ou Figma).
2. **Conferência contra o texto da ANSI/ISA-101.01-2015,** se houver acesso à norma.
3. **Teste com usuários,** se voltar ao plano: material pronto em `docs/design/teste/`; o Comitê de Ética foi dispensado pelo orientador.

## Observações

- **Zoom do canvas:** desde a atualização do tipo Design em 23/11, o canvas do protótipo às vezes abre com zoom de 1% e parece vazio. Para ver as pranchas, use o menu de zoom › "Fit artboards" (Shift+1) ou troque de página. É do visualizador do claude.ai, não das pranchas; o roteiro tem a mesma dica.
- **Links do protótipo:** sem login, o claude.ai mostra o aviso "Faça login para ver os dados deste artefato", mas ver e navegar pelo protótipo funciona.

## Linha "Estado atual"

Atualizada no descritivo em 25/11/2026: "Etapas 1 a 8 e D1 concluídas (…; Etapa D1 em 25/11/2026, com o protótipo aprovado pelo orientador e o teste com usuários retirado do plano pela D51). … Próxima: Etapa D2 (sistema de design aplicado), que termina antes da Etapa 10."
