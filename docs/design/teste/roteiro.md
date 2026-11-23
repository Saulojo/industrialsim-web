# Roteiro do teste de usabilidade

Etapa D1, parte E. Este é o roteiro que o moderador (o autor) segue em cada sessão. As falas em citação são para ler em voz alta, do mesmo jeito para todos.

**Critério da D1:** nas tarefas principais (T1 a T5), pelo menos 4 de 5 participantes concluem sem ajuda. Os problemas encontrados ficam registrados e são corrigidos no protótipo (parte F).

## 1. Materiais

| Item | Onde |
| --- | --- |
| Protótipo (link público, abre sem login) | https://claude.ai/artifact/BATxECGZStkjMLSwVSitZk |
| Formulário do moderador (cronômetro, resultados, SEQ, SUS, exportação) | https://claude.ai/artifact/XFWD466p3YkcWjiqaB4e1z (privado: só o autor abre) |
| Termo de consentimento, 2 vias por participante | [`termo-de-consentimento.md`](termo-de-consentimento.md) |
| Folha do SUS em português do Brasil | Versão de Lourenço, Carmona e Lopes (2022), copiada do artigo (referência no fim) |
| Planilha de resultados | [`resultados.csv`](resultados.csv), exportada do formulário |

**Ambiente**

- **Participante:** computador do autor, com Chrome ou Edge, tela de pelo menos 1280 px de largura e o protótipo em "Play full screen".
- **Formulário:** fica em outro aparelho (celular ou notebook), à vista só do moderador.
- **Gravação de tela:** só se o participante autorizar no termo.
- **Aviso do claude.ai:** o link público mostra "Faça login para ver os dados deste artefato" e o botão Entrar. Os dois são do claude.ai, não do protótipo. O participante não deve clicar neles; avise no começo.

## 2. Participantes

- **Quantos:** 5 pessoas no teste e mais 1 no teste piloto (seção 8). O piloto não entra no resultado.
- **Quem:** pelo menos 1 professor e alunos de cursos com automação ou controle (graduação ou técnico). Varie a experiência com CLP: pelo menos uma pessoa com pouca experiência e uma com bastante.
- **O que não precisam:** conhecer o IndustrialSim ou ter visto os wireframes. Quem acompanhou o projeto de perto não participa.
- **Todos fazem T1 a T5.** Na T5, quem não é professor faz o papel de professor; o enunciado já dá esse contexto.
- **Identificação:** cada pessoa é um código (P1 a P5; o piloto é P0). Nome e contato ficam só no termo de papel, guardado à parte.

## 3. Antes de cada sessão

1. No formulário, crie a sessão com o código do participante (marque "Teste piloto" para o P0).
2. Abra o protótipo numa janela nova e deixe a prancha **T01 · Página inicial** em "Play full screen". Recarregar a página volta o protótipo ao estado inicial.
3. Feche outras abas e notificações. Deixe o som do computador desligado.
4. Tenha à mão as duas vias do termo e a folha do SUS impressa.

## 4. Andamento da sessão (cerca de 45 minutos)

| Parte | Tempo |
| --- | --- |
| Boas-vindas e termo | 5 min |
| Perguntas iniciais | 3 min |
| Tarefas T1 a T5, com SEQ depois de cada uma | 25 min |
| Tarefas secundárias, se der tempo | até 8 min |
| SUS | 3 min |
| Conversa final | 5 min |

### 4.1 Boas-vindas e termo

> Obrigado por vir. Estou testando o protótipo de um simulador de plantas industriais que roda no navegador. Quem está sendo testado é o protótipo, não você: se algo ficar difícil, a culpa é do desenho, e é exatamente isso que eu preciso descobrir. Antes de começar, vamos ler juntos o termo de consentimento.

Leia o termo com a pessoa, tire as dúvidas e colha as assinaturas nas duas vias. Marque "Termo de consentimento assinado" no formulário.

### 4.2 Perguntas iniciais

Registre no formulário:

- perfil (aluno, professor, profissional);
- experiência com CLP (nenhuma, básica, intermediária, avançada);
- mestre que costuma usar (CODESYS, TIA Portal, OpenPLC, Python…).

### 4.3 Como conduzir as tarefas

> Vou pedir algumas tarefas, uma de cada vez. Enquanto faz, tente falar em voz alta o que está pensando: o que procura, o que espera que aconteça, o que te surpreende. É um protótipo: algumas partes são de enfeite e não fazem nada, e os números são inventados. Se ficar preso, pode me dizer. Eu não posso ajudar durante a tarefa, mas depois explico tudo. E o aviso de login no canto da tela é do site que hospeda o protótipo: pode ignorar.

**Para cada tarefa**

1. **Prepare o ponto de partida** (coluna "Começa em" da tabela).
2. **Leia o enunciado em voz alta** e entregue o cartão impresso com o mesmo texto.
3. **Inicie o cronômetro** quando a pessoa tocar no mouse.
4. **Pare o cronômetro** quando a tela de fim aparecer, quando a pessoa disser que terminou ou quando o limite passar sem progresso.
5. **Marque o resultado:**
   - **sem ajuda:** chegou à tela de fim sozinha;
   - **com ajuda:** chegou, mas depois de uma dica sua;
   - **não concluiu:** desistiu, passou do limite ou terminou em outro lugar achando que tinha acabado.
6. **Conte os erros e desvios:** cada caminho errado de que a pessoa precisou voltar, ou cada clique num controle sem relação com a tarefa.
7. **Pergunte o SEQ** e anote a nota:
   > No geral, esta tarefa foi difícil ou fácil? Dê uma nota de 1 a 7, em que 1 é muito difícil e 7 é muito fácil.
8. **Anote o que observou:** onde hesitou e o que disse.

**O que conta como ajuda**

- **É ajuda:** qualquer fala que aponte onde clicar, que nomeie um controle ou uma aba, ou que confirme que a pessoa está no caminho certo.
- **Não é ajuda** (o resultado continua "sem ajuda"):
  - repetir o enunciado;
  - responder "o que você acha?";
  - avisar de uma limitação conhecida do protótipo (lista no [`README.md`](../README.md#limitações-conhecidas-do-protótipo)), por exemplo "esse botão não faz nada no protótipo".

**Falas neutras permitidas:** "O que você está procurando?", "O que esperava que acontecesse?", "Pode continuar.", "Quando achar que terminou, me avise."

## 5. Tarefas

### Principais (todas as sessões)

| ID | Enunciado (ler em voz alta) | Começa em | Termina quando | Limite |
| --- | --- | --- | --- | --- |
| T1 | "Você quer experimentar o simulador sem criar conta. Abra uma planta da caldeira." | T01 · Página inicial | A planta aberta aparece (prancha E01, "Nenhum mestre conectado") | 3 min |
| T2 | "O seu CLP já está configurado com os dados desta planta, e o endereço IP dele é 200.18.4.42. Faça a planta aceitar a conexão desse CLP." | Continua da T1 (E01) | A barra mostra "Mestre conectado · 200.18.4.42" (T06) | 4 min |
| T3 | "O cartão analógico do seu CLP trabalha com valores de 0 a 10000. Ajuste a planta para usar essa faixa." | Continua da T2 (T06) | Aparece "Formato analógico aplicado: 0–10000." (T08) | 3 min |
| T4 | "Agora imagine que você entrou com a sua conta e está na planta 'Caldeira · bancada 3'. Seu colega Bruno, que também tem conta, precisa entrar nesta planta. Dê acesso a ele." | Prancha T06-Conta (abra em "Play full screen") | O convite é enviado ("Convite enviado") ou um código é gerado (T15) | 3 min |
| T5 | "Agora você é a professora ou o professor da turma A de Controle de Processos. Na atividade de malha de nível, você quer ver se a Dupla 03 percebe um sensor com defeito. Simule um sensor aberto no LT-101 só na planta dessa dupla." | Prancha T16 · Turmas | O cartão da Dupla 03 mostra "FALHA · LT-101", e nenhum outro cartão mostra falha (T18) | 4 min |

**Observações por tarefa**

- **T2:** no protótipo, o pedido de conexão chega sozinho uns 6 segundos depois de a pessoa abrir a aba Conexão.
  - Se ela recusar, o IP fica bloqueado por 60 s e depois o pedido volta, como faria um CLP de verdade. A tarefa continua, e a recusa conta 1 erro.
  - Se ninguém responder em 60 s, o pedido some e volta 6 s depois.
- **T3:** chegar a Configurações e escolher 0–10000 sem clicar em Aplicar não conclui a tarefa.
- **T4:** convite por e-mail e código valem os dois.
- **T5:** injetar em todas as plantas é "não concluiu", mesmo que a pessoa depois remova as falhas das outras. A tarefa pede "só na planta dessa dupla".

### Secundárias (se sobrar tempo, nesta ordem)

| ID | Enunciado | Começa em | Termina quando | Limite |
| --- | --- | --- | --- | --- |
| S1 | "Você vai programar o CLP com endereços na notação clássica (30001, 40001…). Qual é o endereço da pressão do vapor, o PT-101, e qual é a faixa dele?" | T06 | Diz 30002 e 0 a 16 bar | 3 min |
| S2 | "Abra a válvula de carga FV-103 para 70 % e me diga o que aconteceu com a vazão de vapor e com a pressão." | T06 | Mudou FV-103 e diz que a vazão de vapor subiu e a pressão caiu | 3 min |
| S11 | "Algo está errado nesta planta. Diga qual é o problema e o que o causou." | Prancha EA | Aponta o alarme de nível muito alto (LSHH-101) e diz que o LT-101 está forçado | 2 min |

## 6. SUS e conversa final

**SUS.** Entregue a folha da versão brasileira (Lourenço, Carmona e Lopes, 2022):

> Pensando no protótipo como um todo, marque o quanto você concorda com cada frase. Não pense muito: vale a primeira impressão.

Depois, passe as 10 notas para o formulário, que calcula a pontuação de 0 a 100 (Brooke, 1996). Como referência, a média de muitos estudos fica perto de 68 (Sauro, 2011). Pontuação abaixo disso indica usabilidade abaixo da média, mas o critério da D1 é o das tarefas, não o SUS.

**Conversa final.** Anote em "Conversa final e observações":

1. O que foi mais difícil? Por quê?
2. Teve algum momento em que você não sabia onde estava ou como voltar?
3. As abas da planta (Processo, Gráficos, Conexão…) ajudaram ou atrapalharam?
4. Os alarmes e os valores destacados ficaram claros? (Mostre a prancha EA se a pessoa não a viu.)
5. Você usaria isto numa aula de laboratório? O que faltaria?

Encerramento:

> Muito obrigado. Agora posso responder qualquer pergunta sobre o que você viu.

## 7. Depois de cada sessão

1. No formulário, confira que as 5 tarefas principais têm resultado e que o SUS está completo.
2. Clique em **Exportar resultados.csv** e salve sobre `docs/design/teste/resultados.csv`. O arquivo sempre traz todas as sessões, então substituir é seguro.
3. Guarde o termo assinado (via do pesquisador) à parte, fora do repositório.
4. Em até um dia, escreva os problemas observados enquanto estão frescos: "Conversa final e observações" no formulário, ou uma nota solta.

**Colunas de `resultados.csv`** (uma linha por sessão, separador vírgula, UTF-8)

| Coluna | Valores |
| --- | --- |
| `participante` | Código (P0 a P5) |
| `piloto` | `sim` ou `nao`; o piloto não entra no critério |
| `perfil` | `aluno`, `professor`, `profissional`, `outro` ou vazio |
| `experiencia` | `nenhuma`, `basica`, `intermediaria`, `avancada` ou vazio |
| `mestre`, `data` | Texto livre; data no formato AAAA-MM-DD |
| `consentimento` | `sim` ou `nao` |
| `<tarefa>_resultado` | `sem_ajuda`, `com_ajuda`, `nao_concluiu`, `nao_aplicada` ou vazio |
| `<tarefa>_tempo_s` | Segundos cronometrados (inteiro) |
| `<tarefa>_erros` | Erros e desvios contados (inteiro) |
| `<tarefa>_seq` | 1 (muito difícil) a 7 (muito fácil) ou vazio |
| `<tarefa>_notas` | Observações do moderador |
| `sus_1` a `sus_10` | 1 (discordo totalmente) a 5 (concordo totalmente) |
| `sus_pontos` | 0 a 100 (Brooke, 1996); vazio se faltar alguma resposta |
| `observacoes` | Conversa final e observações gerais |

As tarefas, nesta ordem: T1, T2, T3, T4, T5, S1, S2, S11.

## 8. Teste piloto

Uma sessão completa com uma pessoa do perfil (sessão P0, marcada como piloto), antes das 5 sessões. Serve para conferir:

- se cada enunciado é entendido sem explicação extra;
- se os limites de tempo fazem sentido (alguém que sabe o caminho termina bem antes);
- se a sessão cabe em 45 minutos;
- se o formulário e a exportação funcionam no aparelho escolhido.

Mudanças depois do piloto (enunciados, limites, ordem) ficam anotadas aqui, com data, antes do P1. Se o protótipo mudar depois do piloto, as 5 sessões usam a versão nova, e nenhuma das 5 é feita em versão diferente das outras.

| Data | Mudança | Motivo |
| --- | --- | --- |
| — | — | — |

## 9. Ética e dados pessoais

- **Participação:** voluntária, com termo de consentimento assinado antes de qualquer tarefa. A pessoa pode parar a qualquer momento, sem dar motivo.
- **Dados guardados:** só o código do participante, perfil, experiência, tempos, resultados, notas e observações. Nada que identifique a pessoa vai para o repositório nem para o formulário. Isso segue o princípio de necessidade da LGPD (Lei 13.709/2018, art. 6º, III).
- **Gravação de tela:** só com autorização marcada no termo. Fica fora do repositório e é apagada depois do relatório.
- **Comitê de Ética:** **pendente, decidir com o orientador.** Confirmar se a instituição exige submissão ao Comitê de Ética em Pesquisa (Sistema CEP/Conep) para este teste. Se exigir, o teste só começa depois da aprovação, e o termo é ajustado ao modelo do comitê.

## Referências

- Brooke, J. *SUS: a "quick and dirty" usability scale*. Em: Jordan, P. W. et al. (org.), *Usability Evaluation in Industry*. Londres: Taylor & Francis, 1996, p. 189–194.
- Lourenço, D. F.; Carmona, E. V.; Lopes, M. H. B. M. Tradução e adaptação transcultural da System Usability Scale para o português do Brasil. *Aquichan*, v. 22, n. 2, e2228, 2022. DOI: 10.5294/aqui.2022.22.2.8.
- Sauro, J. *A Practical Guide to the System Usability Scale*. Denver: Measuring Usability LLC, 2011.
- Sauro, J.; Dumas, J. S. Comparison of three one-question, post-task usability questionnaires. Em: *Proceedings of CHI 2009*. ACM, 2009, p. 1599–1608. (Origem do SEQ; a pergunta em português é tradução livre.)
- Nielsen, J. *Why You Only Need to Test with 5 Users*. Nielsen Norman Group, 2000. (Justificativa do tamanho da amostra pedido no plano.)
