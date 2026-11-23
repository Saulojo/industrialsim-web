# Perfis e tarefas

Etapa D1, parte A. Quem usa o IndustrialSim Web, em que situação e para fazer o quê. Os perfis vêm do público descrito no descritivo (seção "Visão geral e objetivo" e "Plantas, contas e colaboração") e das formas de uso previstas (visitante, usuário com conta, professor). As tarefas alimentam o mapa de telas (`mapa-de-telas.md`) e o roteiro do teste (`teste/roteiro.md`, parte E).

Os perfis são descrições de uso, não pessoas reais nem personas inventadas com nome e foto: cada um junta o que o descritivo e o plano já dizem sobre aquele tipo de usuário.

## Perfis

### VIS — Visitante experimentando

| | |
| --- | --- |
| Quem | Estudante de outra instituição, técnico ou autodidata que chegou por um link (aula, post, indicação, busca). |
| Conta | Nenhuma. Uma planta temporária de até 60 min, sem salvar nem compartilhar; limite por IP. |
| Onde | Notebook próprio, em casa ou no trabalho, atrás de NAT. Chrome ou Edge. |
| Mestre que usa | O que tiver à mão: CODESYS (gratuito), OpenPLC, um script em Python (`pymodbus`) ou Node-RED. Raramente um CLP real. |
| Conhecimento | Varia muito. Pode nunca ter configurado um cliente Modbus TCP; pode não saber o que é base 0 ou notação clássica. |
| Objetivo | Ver em poucos minutos a planta rodando e o próprio mestre lendo e escrevendo nela. |
| Riscos de abandono | Não entender que a planta não tem controle interno; não achar IP, porta e Unit ID; não perceber o pedido de pareamento; firewall ou rede bloqueando a porta. |
| O que precisa ver | Em uma frase, o que é o site; um caminho único até a planta; os dados de conexão com botão de copiar e um guia para o mestre dele; o pedido de pareamento impossível de perder. |

### ALU — Aluno numa atividade

| | |
| --- | --- |
| Quem | Aluno de graduação ou curso técnico numa aula de laboratório de automação, instrumentação ou controle. |
| Conta | Sim (e-mail ou login social). Entra na turma por código ou link. |
| Onde | PC do laboratório, com TIA Portal (PLCSIM Advanced ou CLP real) ou CODESYS, quase sempre em dupla, com tempo de aula limitado. |
| Mestre que usa | O que a disciplina adota; muitas vezes Siemens `MB_CLIENT` (notação clássica, Unit ID 255 por padrão, formato 0–27648) ou CODESYS (base 0). |
| Conhecimento | Sabe o básico de CLP e de 4–20 mA; está aprendendo escalonamento, malhas PID e intertravamento. |
| Objetivo | Abrir a planta da atividade, ligar o CLP, implementar LIC-101 e PIC-101 (e os intertravamentos) e entregar o ensaio. |
| Tarefas típicas | Achar o endereço e a escala de cada sinal; ver se o CLP está escrevendo o que deveria (monitor); entender uma exceção Modbus (log); provocar distúrbio e olhar a tendência; compartilhar a planta com a dupla; exportar o ensaio. |
| O que precisa ver | A atividade e a planta dela, sem confundir com plantas pessoais; mapa de I/O fácil de achar e de imprimir; diagnóstico claro quando "não comunica". |

### PRO — Professor acompanhando a turma

| | |
| --- | --- |
| Quem | Professor da disciplina (qualquer usuário que cria uma turma vira professor dela). |
| Conta | Sim. |
| Onde | PC do professor no laboratório, às vezes ligado ao projetor; também em casa, preparando atividades. |
| Objetivo | Criar a turma e as atividades (cenário e configuração inicial, por exemplo com uma falha); acompanhar ao vivo as plantas de 10 a 30 alunos ou duplas; injetar falhas e distúrbios para ver se o programa do aluno reage; exportar os ensaios para avaliar. |
| Tarefas típicas | Ver de relance quem está com alarme, sem mestre ou com planta parada; abrir a planta de um aluno como observador; injetar "sensor aberto" no LT-101 de uma planta ou de todas; desligar o bloco de diagnóstico numa avaliação. |
| O que precisa ver | Uma visão geral densa e legível de longe (nível 1 da ISA-101), em que só o anormal chama atenção; ações em massa com confirmação clara de alcance ("1 planta" ou "todas as 24"). |

### PRF — Profissional testando um programa de CLP

| | |
| --- | --- |
| Quem | Integrador, técnico de instrumentação ou engenheiro de automação. |
| Conta | Sim, para salvar a planta e voltar a ela. |
| Onde | Notebook de trabalho, com TIA Portal e PLCSIM Advanced, CODESYS ou um SCADA; PACTware para HART. |
| Conhecimento | Alto em Modbus, 4–20 mA, NAMUR NE43 e HART. |
| Objetivo | Testar a lógica (escalonamento, intertravamentos, watchdog, tratamento de falha de instrumento) antes de um comissionamento ou de uma demonstração. |
| Tarefas típicas | Ajustar formato analógico, ordem das palavras, Unit ID e watchdog para igualar o equipamento real; forçar entradas; ver o valor bruto e de engenharia de cada registrador e quem escreveu; gravar e reproduzir ensaios; mudar faixa e amortecimento pelo HART. |
| O que precisa ver | Tudo o que um integrador ajusta, com termos técnicos sem tradução (coil, holding register, LRV/URV); densidade de informação alta no monitor e no log; nada escondido atrás de assistentes. |

## Tarefas

As tarefas **T1 a T5** são as tarefas principais do critério de validação da D1 ("pelo menos 4 de 5 participantes concluem sem ajuda"). As tarefas **S** são secundárias: entram no protótipo e algumas podem entrar no teste, se couberem no tempo.

| ID | Tarefa | Começa em | Termina quando | Telas (mapa) |
| --- | --- | --- | --- | --- |
| **T1** | Abrir uma planta da caldeira | Página inicial | A IHM da planta aparece rodando | T01, T03, T04, T06 |
| **T2** | Parear um mestre Modbus | IHM da planta, sem mestre | O mestre aparece como conectado, depois de aceitar o pedido do IP certo | T05, T05b, T06 |
| **T3** | Mudar o formato analógico para 0–10000 | IHM da planta | O formato novo está aplicado (e aparece no log) | T23, T08, T10 |
| **T4** | Compartilhar a planta com um colega | IHM da planta ou Minhas plantas | O convite por e-mail foi enviado ou o código foi gerado e copiado | T15, T14 |
| **T5** | (Professor) Injetar falha "sensor aberto" no LT-101 de uma planta da turma | Painel da turma | A falha está ativa naquela planta (e só nela) | T18, T19 |
| S1 | Achar o endereço Modbus e a escala do PT-101, na notação clássica | IHM | Diz o endereço (30002) e a faixa (0–16 bar) | T11, T09 |
| S2 | Provocar um distúrbio em FV-103 e ver a resposta | IHM | Mudou FV-103 e aponta a vazão de vapor e a pressão na tendência | T06 |
| S3 | Forçar uma entrada (LSHH-101 = 1) e depois liberar | IHM | O valor forçado aparece destacado e depois some | T07, T09 |
| S4 | Descobrir quem escreveu o último comando de FV-101 | IHM | Diz o IP e o horário | T09 |
| S5 | Descobrir por que o mestre não recebe resposta (Unit ID errado) | IHM, com o mestre "conectado" mas recebendo exceção | Encontra a exceção 0x0B no log e muda o Unit ID | T10, T08 |
| S6 | Pausar e retomar a planta; reiniciar | IHM | A planta pausou e voltou; reiniciou com confirmação | T06 |
| S7 | Exportar o ensaio em CSV e o mapa de I/O em PDF | IHM | Os arquivos foram baixados ou a página de impressão abriu | T06, T11 |
| S8 | Entrar numa turma por código e abrir a planta da atividade | Página inicial (com conta) | A planta da atividade está aberta | T13, T16, T17 |
| S9 | Retomar uma planta salva | Página inicial (com conta) | A planta abre no estado em que ficou | T14 |
| S10 | Mudar a faixa (URV) do PT-101 pelo configurador HART | IHM | A faixa nova está gravada e a corrente mudou | T12 |
| S11 | Identificar um alarme ativo e a causa | IHM com alarme | Diz qual alarme e qual sinal o causou | T06, T07 |
| S12 | (Professor) Criar uma atividade com o cenário caldeira | Turma | A atividade aparece para os alunos | T17 |
| S13 | (Professor) Abrir a planta de um aluno como observador | Painel da turma | A IHM do aluno aparece, sem poder comandar | T18, T06 |

## Matriz perfil × tarefa

Frequência esperada: **A** alta (toda sessão), **M** média, **B** baixa, **—** não se aplica.

| Tarefa | VIS | ALU | PRO | PRF |
| --- | --- | --- | --- | --- |
| T1 Abrir uma planta | A | A | M | A |
| T2 Parear um mestre | A | A | B | A |
| T3 Mudar o formato analógico | B | M | M | A |
| T4 Compartilhar uma planta | — | A | M | M |
| T5 Injetar uma falha (professor) | — | — | A | — |
| S1 Endereço e escala de um sinal | A | A | B | M |
| S2 Distúrbio e tendência | A | A | M | M |
| S3 Forçar e liberar | B | M | M | A |
| S4 Quem escreveu | B | M | B | A |
| S5 Diagnóstico de comunicação | M | A | B | M |
| S6 Pausar, retomar, reiniciar | M | M | M | M |
| S7 Exportar ensaio e mapa | B | A | A | M |
| S8 Entrar na turma | — | A | — | — |
| S9 Retomar planta salva | — | M | M | A |
| S10 Faixa pelo HART | B | M | B | A |
| S11 Alarme e causa | M | A | A | M |
| S12 Criar atividade | — | — | A | — |
| S13 Abrir como observador | — | — | A | — |

## O que os perfis pedem do design

1. **Um caminho curto até "meu mestre controla a planta"** (VIS, ALU). Do início até o mestre conectado deve haver um só caminho óbvio; os dados de conexão (IP, porta, Unit ID) ficam visíveis enquanto nenhum mestre estiver conectado, com guias por mestre (os guias de `docs/guias` já existem).
2. **Separar estado do navegador e estado do mestre.** Hoje "Conectado" (o WebSocket do navegador) e "Mestre conectado" aparecem lado a lado e se confundem; para os perfis, o que importa é o mestre.
3. **Densidade ajustada ao perfil.** O profissional quer tudo visível (monitor, log); o visitante quer só o essencial. A IHM segue a hierarquia da ISA-101 (visão geral → unidade → detalhe → diagnóstico), e cada nível tem a densidade dele.
4. **Só o anormal chama atenção** (ISA-101, todos os perfis, sobretudo PRO na visão geral da turma): alarme, valor forçado, planta pausada, mestre desconectado, falha injetada.
5. **Termos técnicos mantidos** (coil, holding register, Unit ID, LRV/URV, HART), com ajuda curta para quem não conhece (VIS).
6. **Ações com alcance explícito** (PRO): "injetar em 1 planta" e "injetar em todas as 24" nunca podem ser confundidas.
