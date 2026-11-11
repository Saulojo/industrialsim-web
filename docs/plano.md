# IndustrialSim Web — Plano de Desenvolvimento

Nov 9, 2026 · @Saulo

O plano tem 24 etapas em 4 fases, mais uma trilha de design com 2 etapas (D1 e D2). Cada etapa cabe em um chat e só termina com todos os seus critérios de validação verificados. O contexto técnico está no documento “IndustrialSim Web — Descritivo do Projeto”.

## Como usar este plano

Cada etapa é feita em um chat do projeto e só é dada como concluída quando todos os seus critérios de validação estiverem marcados.

**Regras**

1. Siga a numeração. Exceções: a Etapa 2 pode correr junto com a 1; a Etapa 15 (caldeira realista) pode correr em paralelo a partir da Etapa 4; as Fases 2 e 3 podem trocar de ordem, se for melhor para a pesquisa; a trilha de design (D1 e D2) começa depois da Etapa 7 e termina antes da Etapa 10.
2. Comece cada chat dizendo a etapa, por exemplo: “Vamos fazer a Etapa 3”.
3. Se a etapa não couber numa resposta, o Claude a divide em partes (A, B…), cada uma compilando e com testes passando.
4. Marque os critérios aqui conforme forem verificados. Um critério impossível de verificar agora (ex.: sem CLP real) fica desmarcado, com uma nota dizendo em qual etapa será verificado.
5. Portões de decisão (◆) definem o caminho das etapas seguintes. O resultado é registrado no descritivo antes de seguir.
6. Mudanças no plano (etapa nova, critério alterado) são registradas aqui antes de serem executadas.

**Definição de pronto (vale para todas as etapas)**

- Todos os critérios de validação da etapa marcados.
- CI verde na branch da etapa e pull request integrado à `main`.
- Evidências em `docs/validacao/etapa-NN.md`: comandos, saídas, números medidos e capturas de tela.
- Descritivo atualizado: linha “Estado atual” e decisões novas.

**Formato de cada etapa:** objetivo, dependências, escopo, fora do escopo (quando importa), entregáveis e critérios de validação marcáveis.

## Visão geral

Cada fase termina num marco que já pode ser usado em aula: primeiro o controle por qualquer mestre Modbus, depois a plataforma aberta com contas e turmas, a imersão e, por fim, a programação de CLP no site.

```text
Fase 1 · MVP              Fase 2 · Plataforma        Fase 3 · Imersão           Fase 4 · CLP web
E0  Fundação e CI         E10 Contas e plantas       E14 3D e 1ª pessoa         E21 Runtime soft-PLC
E1  Núcleo numérico       E11 Compartilhamento       E15 Modelo realista        E22 Editor Ladder
E2  PoC HART-IP ◆         E12 Turmas/professor       E16 Núcleo HART            E23 Editor FBD
E3  Motor de cenários     E13 Abertura pública       E17 HART-IP/PACTware
E4  I/O e formatos                                   E18 Config. HART web
E5  Tempo real                                       E19 VR (WebXR)
E6  Modbus por planta                                E20 Efeitos visuais
E7  WebSocket e IHM
E8  Config. e monitor
E9  Implantação
◆ Marco 1: MVP com        ◆ Marco 2: aberto ao       ◆ Marco 3: 3D, HART e      ◆ Marco 4: Ladder e FBD
  Modbus no laboratório     público, contas e turmas   VR funcionando             no soft-PLC
Trilha de design (entre E7 e E10): D1 Protótipo e teste com usuários → D2 Sistema de design
(◆ em E2 = portão de decisão do HART)
```

Em destaque, a Etapa 2: a prova de conceito HART-IP vem cedo por ser o maior risco técnico. Ela termina num portão de decisão (◆) e pode correr junto com a Etapa 1; a Etapa 15 pode correr em paralelo a partir da 4; a trilha de design (D1 e D2) fica entre as Etapas 7 e 10.

## Fase 1 — Fundação e MVP

Ao fim da Fase 1, o usuário abre o site, pareia o mestre Modbus dele (CLP, CODESYS, SCADA ou script) e controla a caldeira v1, ainda sem 3D. É o Marco 1.

### Etapa 0 — Fundação do repositório e CI

**Objetivo:** repositório novo compilando e testando de ponta a ponta no CI, antes de qualquer funcionalidade.

**Depende de:** nada.

**Escopo**

- Monorepo com a estrutura do descritivo (`server`, `web`, `scenarios`, `tools`, `deploy`, `docs`).
- Solução .NET 10 com os projetos `Core`, `IO`, `Contracts` e `Server`, mais os de teste; `Directory.Build.props` com `Nullable` e `TreatWarningsAsErrors`.
- Projeto web com Vite, TypeScript `strict`, React, ESLint, Prettier e Vitest; página inicial que chama `GET /api/health`.
- Idiomas desde o início: i18next configurado com pt-BR e uma regra que impede texto fixo nos componentes. Cores, fontes e espaçamentos também vêm de tokens (variáveis CSS), para o design final ser só troca de valores.
- Servidor ASP.NET Core mínimo com `/api/health` (versão e hora do servidor), servindo o build do web.
- GitHub Actions: build e testes do .NET e do web a cada push e pull request.
- `docs/descritivo.md` e `docs/plano.md` com estes documentos; README explicando como rodar localmente.

**Entregáveis:** repositório, pipeline de CI, README.

**Validação**

- [x] `dotnet build` e `dotnet test` passam sem avisos numa máquina limpa.
- [x] `npm ci`, `npm run lint`, `npm test` e `npm run build` passam.
- [x] Com o servidor rodando, a página mostra a versão vinda de `/api/health`.
- [x] Um texto fixo colocado de propósito num componente faz o lint falhar.
- [x] O CI fica verde num pull request e vermelho quando um teste é quebrado de propósito.
- [x] Outra pessoa segue o README e roda o projeto sem ajuda.

Evidências: `docs/validacao/etapa-00.md`.

### Etapa 1 — Núcleo de simulação

**Objetivo:** criar do zero o núcleo numérico, com testes que provem precisão, conservação e determinismo.

**Depende de:** Etapa 0.

**Escopo**

- Criar em `IndustrialSim.Core`, do zero: integrador RK4 (escalar e vetorial), atuador de 1ª ordem, modelo de tanque e conversão de função de transferência para espaço de estados.
- Bomba comandável desde o início (o protótipo forçava a bomba ligada a cada passo; não repetir). Nenhum controlador dentro da planta; um PID existe só nos testes, como referência.
- Relógio de simulação de passo fixo e gerador pseudoaleatório com semente, para ruído.
- Abstração de componente: estado, entradas, saídas e `Step(dt)`, com sinais nomeados.
- Função de hash do estado, para testes de determinismo.

**Fora do escopo:** JSON de cenário, rede.

**Validação**

- [ ] RK4: dy/dt = −y em t = 1 s, com dt = 0,01 s, coincide com e⁻¹ com erro < 1e-8.
- [ ] O atuador de 1ª ordem chega a 63,2% do degrau em t = τ (erro < 0,5%).
- [ ] Tanque: a variação de volume iguala ∫(Qin − Qout)dt, com erro relativo < 1e-6 em 600 s.
- [ ] Bomba desligada e saída aberta: o nível cai.
- [ ] O nível nunca fica negativo nem passa da altura do tanque.
- [ ] Duas execuções com as mesmas entradas e semente geram o mesmo hash de estado; outra semente muda só o ruído.
- [ ] Funções de transferência de 1ª e 2ª ordem convertidas para espaço de estados: a resposta ao degrau coincide com a solução analítica (erro < 1e-6).

### Etapa 2 — Prova de conceito HART-IP com PACTware

**Objetivo:** descobrir cedo se o PACTware configura um instrumento simulado via HART-IP, sem instalar nada do projeto. É o maior risco técnico, por isso vem antes do resto do HART.

**Depende de:** Etapa 0 (pode correr junto com a Etapa 1).

**Escopo**

- Consultar o trabalho HART do protótipo só como referência; todo o código desta etapa é novo.
- Servidor HART-IP mínimo em .NET, fora do produto (`tools/hart-poc`): comandos 0, 1, 3, 13, 15, 34 e 35 para um transmissor com valor senoidal.
- Testar com o cliente HART-IP da FieldComm (WindowsHartIpClient) como referência do protocolo.
- Testar com PACTware, DTMs de comunicação HART-IP disponíveis (ex.: versão de teste do DTM Softing smartLink) e o DTM HART genérico de dispositivo.
- Medir o plano B: par de COM virtuais (com0com) com o DTM de modem HART.
- Registrar revisão HART necessária, topologia (gateway ou dispositivo direto), identificação aceita e se a porta é configurável.

**Entregáveis:** `tools/hart-poc/` e `docs/validacao/etapa-02.md` com a decisão.

**Validação**

- [ ] O cliente da FieldComm conecta e lê a identificação (comando 0) e a PV do instrumento simulado.
- [ ] Resultado registrado para o PACTware via HART-IP: conecta, encontra o dispositivo, lê a PV e escreve faixa e amortecimento, ou o ponto exato onde falha.
- [ ] Resultado registrado para o caminho serial com COM virtual.
- [ ] Resultado do portão de decisão (A, B ou C) registrado na D09 do descritivo, com topologia e revisão HART definidas.

**Portão de decisão: caminho do HART (◆)**

Ao fim da etapa, os critérios medidos abaixo decidem o escopo das Etapas 17 e 18. O resultado, com data e evidências, vai para a decisão D09 do descritivo antes de seguir.

| Critério | Como medir |
| --- | --- |
| C1 Conexão | O PACTware conecta ao servidor HART-IP do simulador |
| C2 Descoberta | Encontra o instrumento simulado na busca de dispositivos |
| C3 Leitura | Lê PV, corrente e faixa |
| C4 Escrita | Escreve faixa e amortecimento, e o efeito aparece no simulador |
| C5 Custo | O DTM de comunicação HART-IP usado é gratuito |
| C6 Instalação | Nada além do PACTware e de DTMs precisa ser instalado |
| C7 Estabilidade | 30 minutos conectado sem queda |
| C8 Isolamento | A porta por planta (ou o pareamento por IP) funciona com o DTM |

| Resultado | Quando | O que fazemos |
| --- | --- | --- |
| A — HART-IP aprovado | C1 a C8 atendidos | Etapa 17 implementa o HART-IP completo como caminho do PACTware. Etapa 18 (configurador web) segue como caminho sem instalação, com foco didático. |
| B — Aprovado com DTM pago | C1–C4, C6–C8 atendidos, mas C5 falha | Etapa 17 mantém o HART-IP para quem tiver o DTM e para outras ferramentas. Etapa 18 vira o caminho principal e completo. O guia explica o custo do DTM. |
| C — Reprovado | Algum entre C1 e C4 falha | Etapa 17 se reduz a HART-IP para ferramentas abertas (cliente FieldComm, scripts) ou sai do plano. Etapa 18 é o caminho principal. O “conector local HART serial” passa de extensão a etapa opcional depois da Fase 3, para quem precisa do PACTware. |

### Etapa 3 — Motor de cenários

**Objetivo:** montar plantas a partir do `scenario.json`, sem código específico por planta.

**Depende de:** Etapa 1.

**Escopo**

- Esquema JSON versionado: componentes, parâmetros, ligações e condições iniciais; validação com mensagens que apontam o campo com erro.
- Biblioteca de componentes: bomba, válvula (característica linear e igual-percentual), tanque e tubulão v1, transmissor (atraso, ruído, quantização), chaves de nível e pressão, válvula de alívio.
- Modelo de pressão v1, de 1ª ordem.
- Cenário `caldeira` v1 em JSON.
- Linha de comando `simrun cenario.json --entradas roteiro.csv --saida resultado.csv`.

**Validação**

- [ ] O tanque descrito em JSON gera resultado idêntico, bit a bit, ao modelo codificado da Etapa 1 para o mesmo roteiro.
- [ ] Um cenário de dois tanques em série é criado só com JSON, sem alterar código, e conserva massa (erro < 1e-6).
- [ ] Dez casos de JSON inválido (tipo desconhecido, ligação para sinal inexistente, parâmetro fora da faixa…) geram erro claro com o caminho do campo.
- [ ] Caldeira v1 com degraus coerentes: mais água sobe o nível; mais combustível sobe a pressão; mais vapor baixa a pressão (testes de sinal).
- [ ] A válvula igual-percentual segue a curva teórica com erro < 1%.

### Etapa 4 — Imagem de I/O, escalas e watchdog

**Objetivo:** a fronteira entre planta e CLP se comporta como cartões de I/O reais.

**Depende de:** Etapa 3.

**Escopo**

- Seção `io` do cenário: coils, entradas discretas, holding e input registers ligados a sinais, com escala.
- Imagem de I/O dimensionada pelo mapa, thread-safe, com cópia instantânea (snapshot).
- Formatos analógicos escolhidos por planta: 0–27648 (padrão, com sobrefaixa e valores negativos), 0–10000, 0–32767 e `float32` em unidades de engenharia, com ordem de palavras configurável.
- Transmissor: PV → mA (pela faixa LRV/URV) → contagens no formato da planta; correntes de falha NAMUR NE43.
- Valores forçados: uma entrada pode ser fixada manualmente, com indicação de “forçado”.
- Comandos copiados para a planta só no início do passo.
- Watchdog com heartbeat, tempo configurável e estado seguro definido no cenário.
- Mapa da caldeira conforme o descritivo, com endereços em base 0 e na notação clássica.

**Validação**

- [ ] Conversões de ida e volta erram no máximo 1 contagem em toda a faixa, nos três formatos inteiros (teste por varredura).
- [ ] No formato 0–27648, 3,6 mA vira −691 e 21 mA vira 29376; lido como `INT`, o valor se mantém.
- [ ] `float32` correto nas duas ordens de palavra, com valores de referência fixos.
- [ ] Trocar o formato com a planta rodando muda os valores no passo seguinte, sem reiniciar.
- [ ] Um valor forçado ignora a planta até ser liberado.
- [ ] Um comando escrito no meio de um passo só muda a planta no passo seguinte.
- [ ] Com o watchdog ligado e sem mudança do heartbeat por 2 s: FV-101 vai a 100%, FV-102 a 0% e a entrada discreta 5 sobe; com o heartbeat de volta, os comandos voltam a valer.
- [ ] A imagem de I/O da caldeira ocupa menos de 4 KB (contra cerca de 400 KB no protótipo).

### Etapa 5 — Servidor de tempo real e instâncias

**Objetivo:** muitas plantas rodando em tempo real no servidor, cada uma isolada, com gravação e reprodução de ensaios.

**Depende de:** Etapa 4.

**Escopo**

- Laço de tempo real com `Stopwatch` e passo fixo de 10 ms; recuperação limitada após atraso; métricas de jitter, sobrecarga e deriva.
- Gerenciador de instâncias: criar, listar, encerrar e expirar por inatividade, com limite configurável. O modelo já prevê dono e participantes, usados a partir da Etapa 10.
- API REST: listar cenários, criar planta, consultar estado, encerrar.
- Gravação: comandos recebidos com o tempo simulado, mais medições amostradas, em CSV; reprodução a partir da gravação.
- Logs estruturados.

**Validação**

- [ ] 30 plantas da caldeira por 10 minutos: deriva entre tempo simulado e relógio < 0,1%; atraso do passo no percentil 99 < 5 ms; CPU total < 50% de um núcleo (números no relatório).
- [ ] Reproduzir uma gravação gera CSV idêntico ao original (mesmo hash).
- [ ] Uma planta sem cliente expira no tempo configurado e libera recursos (teste com tempo reduzido).
- [ ] Testes de API com `WebApplicationFactory` cobrem criar, consultar e encerrar.
- [ ] Encerrar o processo com plantas ativas não deixa threads penduradas.

### Etapa 6 — Modbus TCP por planta com pareamento

**Objetivo:** qualquer mestre Modbus controla a planta do usuário, e só a dele.

**Depende de:** Etapa 5.

**Escopo**

- Criar o servidor Modbus, sobre uma biblioteca escolhida na etapa (NModbus ou FluentModbus), em `IndustrialSim.Modbus`, ligado à imagem de I/O da planta.
- Portas na faixa configurada (padrão 15000–15999), liberadas ao fim da planta; Unit ID configurável.
- Pareamento por IP (por API nesta etapa; a tela vem na Etapa 7): conexão pendente, aceitar ou recusar, lista de IPs autorizados, recusa dos demais, retirada de autorização.
- Registro de autor (IP) e horário da última escrita em cada registrador, para o monitor da Etapa 8.
- Exceção Modbus 02 para endereço fora do mapa, sem derrubar a conexão.
- Guias em `docs/guias/`: TIA Portal (`MB_CLIENT`, S7-1200/1500 ou PLCSIM Advanced), CODESYS (Modbus TCP Master), OpenPLC e Python (`pymodbus`), cada um com um exemplo de malha de nível.

**Validação**

- [ ] Teste de integração: um cliente Modbus escreve em FV-101 e a posição real começa a mudar em até 2 passos; a leitura de LT-101 é coerente com a planta.
- [ ] 30 plantas com 30 clientes simultâneos escrevendo valores distintos: nenhuma planta recebe valor de outra.
- [ ] Numa planta autorizada só para 127.0.0.1, uma conexão de 127.0.0.2 (Linux) é recusada.
- [ ] Leitura fora do mapa retorna a exceção 02 e a conexão continua válida.
- [ ] Testado com pelo menos dois mestres reais diferentes (ex.: CODESYS e `pymodbus`), com capturas de tela registradas.
- [ ] Com CLP real ou PLCSIM Advanced disponível: o `MB_CLIENT` lê o nível e comanda a válvula. Se não houver, fica pendente para a Etapa 9.

### Etapa 7 — WebSocket e IHM web

**Objetivo:** pelo navegador, o usuário abre uma planta, pareia o mestre, acompanha o processo e provoca distúrbios.

**Depende de:** Etapa 6.

**Escopo**

- Contrato em `IndustrialSim.Contracts` e geração automática dos tipos TypeScript; mensagem inicial com a versão do protocolo.
- WebSocket: estado da planta a 20–30 Hz; eventos (pedido de pareamento, alarmes, watchdog); comandos (distúrbio em FV-103, reset, aceitar ou recusar pareamento).
- IHM em React: escolha de cenário, dados de conexão (IP, porta, Unit ID), diálogo de pareamento, sinóptico 2D simples da caldeira com valores, tendências, alarmes, controle do distúrbio e exportação de CSV.
- Todos os textos vindos dos arquivos de idioma.
- Reconexão automática do WebSocket.

**Validação**

- [ ] Playwright: abrir uma planta, ver os valores mudarem, mover o distúrbio e ver vazão de vapor e pressão responderem na tendência.
- [ ] Playwright: um cliente Modbus de teste conecta, o diálogo de pareamento aparece, aceitar mostra “Mestre conectado” e recusar fecha a conexão.
- [ ] Após uma queda do WebSocket, a IHM reconecta sozinha em menos de 5 s, sem perder a planta.
- [ ] Um cliente com versão de protocolo diferente recebe mensagem clara de incompatibilidade.
- [ ] O CSV baixado tem os mesmos dados da gravação do servidor.
- [ ] Banda medida por cliente ≤ 100 kbit/s.

### Etapa 8 — Configurações da planta e monitor de comunicação

**Objetivo:** o usuário ajusta como a planta conversa com o mestre e enxerga cada registrador ao vivo.

**Depende de:** Etapa 7.

**Escopo**

- Painel de conexão: IP do servidor e portas (só leitura), Unit ID, formato analógico, ordem de palavras do `float32`, watchdog, mestres permitidos (um ou vários), bloco de diagnóstico ligado ou desligado.
- Monitor de registradores: tabela ao vivo de coils, entradas discretas, holding e input registers, com valor bruto, valor de engenharia, unidade, autor e horário da última escrita; endereços em base 0 ou na notação clássica.
- Log de comunicação: conexões, pareamentos, requisições por segundo, exceções Modbus, mudanças de configuração.
- Forçar e liberar entradas pela IHM.
- Exportar o mapa de I/O do cenário em CSV e PDF.
- Pausar, retomar e reiniciar a planta.
- As configurações valem enquanto a planta existir; a persistência vem com as contas, na Etapa 10.

**Validação**

- [ ] Mudar o formato analógico ou o Unit ID na tela altera o que o mestre lê no passo seguinte, sem reiniciar.
- [ ] Uma escrita de um cliente de teste aparece no monitor em até 200 ms, com o IP de origem.
- [ ] Um valor forçado aparece destacado na IHM e no monitor, e deixa de valer ao ser liberado.
- [ ] O mapa de I/O exportado bate com o `scenario.json` (teste automático).
- [ ] Pausar congela a planta e o monitor mostra “pausada”; retomar continua do mesmo estado (mesmo hash).
- [ ] Toda mudança de configuração aparece no log.

### Etapa 9 — Implantação no laboratório (Marco 1: MVP)

**Objetivo:** o MVP no ar no PC do laboratório, acessível pela rede da universidade e, se a TI liberar, pela internet.

**Depende de:** Etapa 8. Conversar com a TI antes de começar.

**Escopo**

- Dockerfile multiestágio (build do web e do servidor), `docker-compose.yml` e Caddy com HTTPS.
- Serviço com reinício automático; logs persistidos com rotação.
- Firewall: 443, 15000–15999 e 16000–16999 (reservada para HART-IP); todo o resto fechado.
- Com a TI: DNS, certificado e IP público, ou acesso por VPN.
- Opcional: arquivos estáticos em CDN se a banda do laboratório limitar.
- Roteiro de implantação e atualização em `docs/implantacao.md`.

**Validação**

- [ ] O site abre por HTTPS, com certificado válido, a partir de outro PC da rede (e de fora, se liberado).
- [ ] Ensaio completo com um mestre real (CLP, PLCSIM Advanced ou CODESYS) num PC do laboratório: pareamento, malha de nível fechada no mestre, distúrbio pela IHM e CSV exportado.
- [ ] Após reiniciar o PC do laboratório, o serviço volta sozinho em menos de 2 minutos.
- [ ] Teste de carga com 30 plantas e 30 clientes Modbus simulados por 30 minutos, sem erros e dentro das metas de tempo real.
- [ ] Uma varredura de portas feita de fora mostra só as portas previstas.
- [ ] Marco 1 registrado na linha “Estado atual” do descritivo.

## Trilha de design

A trilha de design define como o simulador se apresenta: menu, seleção de cenários, configurações, IHM, contas, turmas e painéis em 3D e VR. Ela começa depois da Etapa 7, quando já existe uma IHM funcional para observar, e termina antes da Etapa 10, para que as telas da Fase 2 já nasçam no design final.

### Etapa D1 — Prototipagem e teste com usuários

**Objetivo:** definir, antes das telas finais, como o usuário navega e o que vê, com um protótipo navegável validado com usuários reais.

**Depende de:** Etapa 7 (pode correr junto com as Etapas 8 e 9).

**Escopo**

- Perfis e tarefas principais: visitante experimentando, aluno numa atividade, professor acompanhando a turma, profissional testando um programa de CLP.
- Mapa de telas e fluxos: página inicial, menu principal, seleção de cenários, criar e abrir planta, conexão e pareamento do mestre, IHM, configurações, monitor de registradores, configurador HART, login e minhas plantas, compartilhamento, turmas e painel do professor, painéis sobre a cena 3D e em VR, esboço do editor Ladder.
- Wireframes de baixa fidelidade e, depois, protótipo navegável de alta fidelidade das telas principais (ferramenta de design do Claude ou Figma).
- Identidade visual: nome e logotipo, paleta com temas claro e escuro, tipografia e ícones.
- Princípios de IHM industrial da ISA-101 (fundo neutro, cor só para alarmes e estados anormais) e acessibilidade WCAG 2.2 AA.
- Teste de usabilidade com pelo menos 5 pessoas (alunos e um professor), com tarefas definidas e cronometradas.

**Entregáveis:** protótipo navegável, mapa de telas, guia de identidade visual e relatório do teste em `docs/design/`.

**Validação**

- [ ] Todas as telas do mapa existem no protótipo e cobrem os fluxos do visitante, do aluno e do professor.
- [ ] Nas tarefas principais (abrir uma planta, parear um mestre, mudar o formato analógico, compartilhar uma planta, professor injetar uma falha), pelo menos 4 de 5 participantes concluem sem ajuda.
- [ ] Os problemas encontrados no teste estão registrados e corrigidos no protótipo.
- [ ] Paleta aprovada no verificador de contraste WCAG AA, nos temas claro e escuro.
- [ ] Telas de IHM revisadas contra os princípios da ISA-101.
- [ ] Protótipo aprovado pelo orientador.

### Etapa D2 — Sistema de design aplicado

**Objetivo:** transformar o protótipo em componentes reutilizáveis no código e aplicá-los às telas existentes.

**Depende de:** Etapas D1 e 8. Termina antes da Etapa 10.

**Escopo**

- Tokens finais (cores, tipografia, espaçamentos, raios, sombras) em variáveis CSS, com temas claro e escuro.
- Biblioteca de componentes React (botões, campos, tabelas, diálogos, abas, indicadores de processo, faceplates de instrumentos, tendências e alarmes), documentada num catálogo (ex.: Storybook).
- Menu principal e seleção de cenários criados no design final.
- Telas das Etapas 7 e 8 (IHM, configurações, monitor) refeitas com os componentes.
- Estilos dos painéis 3D e VR (Babylon GUI) definidos a partir dos mesmos tokens; a aplicação vem nas Etapas 14 e 19.
- Testes de regressão visual (capturas comparadas) no CI.

**Validação**

- [ ] Nenhuma cor, fonte ou espaçamento fora dos tokens (verificado no lint).
- [ ] O catálogo mostra todos os componentes nos dois temas.
- [ ] Menu, seleção de cenários e telas das Etapas 7 e 8 conferem com o protótipo (revisão lado a lado registrada).
- [ ] Teste automático de acessibilidade (ex.: axe) sem violações graves.
- [ ] Testes de captura de tela estáveis no CI.
- [ ] Todos os testes de ponta a ponta das Etapas 7 e 8 continuam passando.

## Fase 2 — Plataforma aberta e colaboração

Ao fim da Fase 2, qualquer pessoa, de qualquer lugar, cria conta (ou entra como visitante), salva e compartilha plantas, e professores acompanham suas turmas, em português, inglês ou espanhol. É o Marco 2.

### Etapa 10 — Contas e plantas salvas

**Objetivo:** pessoas criam conta, salvam plantas e voltam a elas de onde pararam; quem não quer conta usa como visitante.

**Depende de:** Etapa 9 e trilha de design (Etapa D2).

**Escopo**

- ASP.NET Core Identity com PostgreSQL (EF Core): cadastro por e-mail com verificação, política de senha, recuperação de senha; login social (Google, Microsoft, GitHub).
- Modo visitante: planta temporária, sem salvar nem compartilhar, até 60 minutos, com limite por IP.
- Planta salva: dono, cenário, configurações da Etapa 8, estado e histórico de ensaios. Ao abrir, vira instância; sem ninguém conectado por 30 minutos, hiberna gravando o estado; ao reabrir, continua de onde parou.
- Ao reabrir, o servidor tenta devolver as mesmas portas Modbus e HART-IP.
- Página “Minhas plantas”: criar, renomear, duplicar e excluir.
- Conta: editar perfil, exportar os próprios dados e excluir a conta.
- Migrações de banco versionadas e backup diário.

**Validação**

- [ ] Cadastro, verificação de e-mail, login, logout e recuperação de senha passam em testes automatizados.
- [ ] Login com pelo menos um provedor social funciona no servidor do laboratório.
- [ ] Uma planta salva hiberna e, ao reabrir, retoma o mesmo estado (hash igual ao gravado).
- [ ] Reabrir uma planta devolve a mesma porta quando ela está livre.
- [ ] Visitante não consegue salvar nem compartilhar, e a planta temporária expira no tempo definido.
- [ ] Excluir a conta remove dados pessoais e plantas do usuário (teste).
- [ ] Restaurar o backup num banco vazio recupera contas e plantas.

### Etapa 11 — Compartilhamento e operação colaborativa

**Objetivo:** várias pessoas e vários mestres Modbus operam a mesma planta, com cada ação registrada com seu autor.

**Depende de:** Etapa 10.

**Escopo**

- Papéis por planta: dono, operador e observador, aplicados na API e na IHM.
- Convite por link (com papel e validade) e pelo e-mail de usuário cadastrado; revogação de acesso.
- Presença: quem está na planta agora.
- Comandos simultâneos: vale a última escrita; log da planta com autor, ação e horário, exportável em CSV.
- Vários mestres Modbus autorizados na mesma planta; aviso quando dois mestres escrevem o mesmo registrador em menos de 1 s; opção “um mestre por vez”.

**Validação**

- [ ] Teste com três navegadores (dono, operador, observador): permissões respeitadas na API e na IHM; o observador recebe erro ao tentar comandar.
- [ ] Convite expirado ou revogado não dá acesso.
- [ ] Dois clientes Modbus autorizados escrevem: o monitor mostra o autor de cada escrita e o aviso de conflito aparece.
- [ ] No modo “um mestre por vez”, o segundo mestre é recusado.
- [ ] O log lista todas as ações com autor e exporta em CSV.
- [ ] A presença atualiza em menos de 2 s quando alguém entra ou sai.

### Etapa 12 — Turmas e painel do professor

**Objetivo:** um professor cria uma turma e acompanha ao vivo as plantas dos alunos.

**Depende de:** Etapa 11.

**Escopo**

- Qualquer usuário cria uma turma e vira professor dela; alunos entram por código ou link.
- Atividades: o professor define cenário e configuração inicial (por exemplo, com uma falha), e cada aluno ou equipe recebe sua planta.
- Painel: grade com as plantas da turma (estado, mestre conectado, alarmes, principais variáveis), com filtro por aluno ou equipe.
- Ações do professor: abrir qualquer planta da turma como observador, injetar falhas e distúrbios em uma ou em todas, encerrar ou reiniciar a atividade, exportar os ensaios da turma.
- Plantas pessoais dos alunos, fora da turma, não aparecem para o professor.

**Validação**

- [ ] Com 30 plantas numa turma (alunos simulados), o painel atualiza em até 2 s sem passar a meta de CPU.
- [ ] Uma falha injetada em todas as plantas chega a todas em até 1 s.
- [ ] Alunos de outra turma e plantas pessoais não aparecem no painel.
- [ ] A exportação gera um arquivo por planta, com o nome do aluno ou da equipe.
- [ ] Playwright: criar turma, aluno entrar, criar atividade, professor injetar falha e aluno ver o alarme.

### Etapa 13 — Abertura pública (Marco 2)

**Objetivo:** o site pronto para qualquer pessoa, em três idiomas, com limites que protegem o servidor.

**Depende de:** Etapa 12.

**Escopo**

- Idiomas pt-BR, en e es: detecção pelo navegador e seletor manual; traduções geradas com IA e revisadas; números e datas formatados pelo idioma; termos técnicos mantidos.
- Verificação no CI de que nenhuma chave de tradução está faltando.
- Limites: plantas em execução por conta e por IP, total de instâncias no servidor, fila com aviso quando lotar, requisições por IP, bloqueio temporário após abuso.
- Proteção contra robôs no cadastro.
- Termos de uso, política de privacidade (LGPD), página “sobre” e contato; aviso de que Modbus e HART-IP não têm criptografia.
- Painel mínimo de administração: uso, plantas ativas e bloqueios.
- Monitoramento com alerta (ex.: e-mail quando a CPU passar de 80%).

**Validação**

- [ ] A interface inteira aparece em inglês e espanhol sem chaves faltando (CI) e com números formatados pelo idioma.
- [ ] Teste de carga com 100 usuários anônimos abrindo plantas: o servidor respeita os limites, mostra a fila e não cai.
- [ ] Varredura de segurança básica (ex.: OWASP ZAP) sem achados de severidade alta.
- [ ] Termos e política publicados e aceitos no cadastro.
- [ ] Uma pessoa de fora da universidade, em outra rede, cria conta e controla uma planta com o próprio mestre Modbus.
- [ ] Marco 2 registrado na linha “Estado atual” do descritivo.

## Fase 3 — Fidelidade e imersão

Ao fim da Fase 3, a caldeira tem modelo realista, aparece em 3D e em VR, mostra o processo por dentro com efeitos visuais (água, fervura, vapor, fluxo, vista em corte) e tem instrumentos configuráveis por HART. É o Marco 3.

### Etapa 14 — Visualização 3D e primeira pessoa

**Objetivo:** a caldeira em 3D no navegador, navegável em primeira pessoa e animada pelo estado real da planta.

**Depende de:** Etapa 7 (Etapa 9 desejável).

**Escopo**

- Exportar os modelos do Blender para `.glb` com compressão, nomeando objetos como `TAG_parte`; orçamento inicial de cena: até 20 MB e 300 mil triângulos (revisto após medir no Quest, na Etapa 19).
- Cena Babylon.js com iluminação PBR simples, piso e colisões.
- Câmera em primeira pessoa: WASD, mouse com pointer lock, gravidade e colisão com os equipamentos.
- Bloco `visual` do cenário: nível → altura da água, posição → rotação do disco da válvula, bomba → rotação, vazão → indicador.
- Interpolação entre os estados recebidos.
- Etiquetas dos instrumentos: clicar mostra tag, valor e unidade.
- IHM, configurações e monitor em painéis sobre a cena, com os componentes da Etapa D2; outros participantes da planta visíveis como avatares simples.

**Fora do escopo:** efeitos elaborados de água e vapor (Etapa 20), VR (Etapa 19).

**Validação**

- [ ] Primeiro carregamento em até 10 s numa conexão de 50 Mbit/s; recarregamento em até 3 s (cache).
- [ ] Pelo menos 60 fps no notebook de referência (definido e registrado na etapa), com a IHM aberta.
- [ ] Nível visual e nível simulado diferem menos de 1% da altura (teste automático por um gancho de depuração).
- [ ] O ângulo do disco de FV-101 corresponde à posição real, com erro < 1°.
- [ ] Não é possível atravessar equipamentos nem cair do piso (roteiro manual registrado).
- [ ] Com duas pessoas na mesma planta, cada uma vê o avatar da outra se mover.
- [ ] Teste de captura de tela no Playwright estável.

### Etapa 15 — Modelo realista da caldeira

**Objetivo:** trocar o modelo v1 pelo de Åström & Bell com IAPWS-IF97, validado, sem mudar o mapa de I/O.

**Depende de:** Etapa 4 (pode correr em paralelo com as Etapas 5 a 14).

**Escopo**

- Propriedades da água e do vapor pela IF97 (no mínimo as regiões 1, 2 e 4): comparar uma biblioteca existente (ex.: CoolProp, licença MIT) com implementação própria em C#; decidir e registrar.
- Componente tubulão de Åström & Bell, com quatro estados: volume total de água, pressão, título de vapor nos risers e volume de vapor sob o nível.
- Validar primeiro com os parâmetros do artigo; depois criar e documentar a parametrização de uma caldeira didática.
- Implementação de referência independente em Python, com integrador de passo variável (`tools/`).
- Faixas dos instrumentos e estado inicial em regime permanente.

**Validação**

- [ ] IF97 reproduz os valores das tabelas de verificação da IAPWS em todos os dígitos publicados (erro relativo < 1e-8).
- [ ] Resíduos de massa e energia < 1e-6 (relativo) em 1 hora simulada.
- [ ] Partindo do regime permanente, sem perturbação, o estado varia menos de 0,01% em 10 minutos.
- [ ] Degrau de vapor: o nível sobe antes de cair (swell); degrau de água: shrink. Formas comparadas às figuras de Åström & Bell (2000).
- [ ] C# (RK4, 10 ms) contra Python (passo variável, tolerância 1e-10): erro máximo < 0,5% em nível e pressão nos ensaios de degrau.
- [ ] 30 plantas com o modelo v2 continuam abaixo de 50% de um núcleo.
- [ ] Um programa de mestre feito na Fase 1 continua funcionando sem mudar endereços.

### Etapa 16 — Núcleo HART e modelo de instrumento

**Objetivo:** instrumentos HART simulados completos, testados sem rede.

**Depende de:** Etapas 2 e 4.

**Escopo**

- Codificação e decodificação de quadros independente do transporte: preâmbulos, delimitadores curto e longo, endereço, contagem de bytes, status e checksum.
- Comandos da tabela do descritivo para transmissores, com as respostas de erro previstas (comando não implementado, parâmetro inválido etc.).
- Modelo de instrumento: PV, faixa, unidade, amortecimento, corrente, corrente fixa, trim, tag, mensagem, bit de configuração alterada e falhas.
- Efeitos na planta pela imagem de I/O: faixa → contagens; amortecimento → filtro; corrente fixa → valor forçado.
- Instrumentos declarados no `scenario.json`.

**Validação**

- [ ] Vetores de teste de quadros (da prova de conceito da Etapa 2 e do cliente FieldComm) codificados e decodificados byte a byte.
- [ ] Checksum errado ou quadro truncado: nenhuma resposta e nenhuma exceção.
- [ ] Faixa de LT-101 alterada de 0–2 m para 0–1 m com nível em 0,5 m: no formato 0–27648, o valor no Modbus passa de 6912 para 13824.
- [ ] Amortecimento de 2 s: a resposta a degrau atinge 63,2% em 2 s (erro < 2%).
- [ ] Corrente fixa em 12 mA: o Modbus mostra 13824 até sair do modo.
- [ ] O bit de configuração alterada sobe após uma escrita e desce com o comando 38.

### Etapa 17 — HART-IP e PACTware (conforme o portão da Etapa 2)

**Objetivo:** cada planta expõe os próprios instrumentos via HART-IP, no escopo definido pelo resultado do portão de decisão da Etapa 2.

**Depende de:** Etapas 2 (resultado do portão), 6 (pareamento) e 16.

**Escopo comum**

- Servidor HART-IP por planta: portas 16000–16999, ou a 5094 com pareamento por IP, conforme a Etapa 2; TCP e UDP; sessões HART-IP, keep-alive e timeouts.
- Topologia definida na Etapa 2 (gateway com subdispositivos ou dispositivo direto).
- Pareamento reaproveitado da Etapa 6.
- Eventos HART visíveis na IHM e no log (ex.: “faixa de LT-101 alterada pelo PACTware”).

**Por resultado do portão**

- **A:** guia `docs/guias/pactware.md` completo; o PACTware é o caminho principal para quem usa a ferramenta.
- **B:** mesmo servidor; o guia explica a licença do DTM e aponta o configurador web como alternativa gratuita.
- **C:** apenas o servidor HART-IP para ferramentas abertas (cliente FieldComm, scripts), ou a etapa sai do plano, conforme registrado na D09.

**Validação**

- [ ] O cliente HART-IP da FieldComm lista os quatro instrumentos e lê e escreve seus parâmetros.
- [ ] (Resultados A ou B) O PACTware, com o DTM escolhido, encontra os instrumentos, lê a PV e muda faixa e amortecimento; o efeito aparece no Modbus e na IHM.
- [ ] Com duas plantas simultâneas, o cliente de uma não enxerga os instrumentos da outra.
- [ ] Uma queda da conexão HART-IP não afeta a planta nem o Modbus.
- [ ] O escopo executado bate com o resultado registrado na D09.

### Etapa 18 — Configurador HART web

**Objetivo:** configurar os instrumentos pelo próprio site, sem instalar nada. É o caminho HART garantido, qualquer que seja o resultado da Etapa 2.

**Depende de:** Etapas 7 e 16.

**Escopo**

- Aba “Instrumentos”: lista, leitura de variáveis, edição de tag, faixa, unidade e amortecimento, corrente fixa (teste de malha), trim e falhas (professor).
- O configurador fala HART de verdade: o navegador envia comandos HART pelo WebSocket ao núcleo da Etapa 16, nunca altera a planta direto.
- Permissões pelos papéis da Etapa 11: dono e operador alteram; observador só lê.
- Modo didático: quadros enviados e recebidos em hexadecimal, com os campos decodificados.
- No resultado C do portão, cobre todos os comandos da tabela do descritivo.

**Validação**

- [ ] Todo parâmetro editável pelo PACTware na Etapa 17 (ou, no resultado C, toda a tabela de comandos) é editável no configurador, com o mesmo efeito.
- [ ] O modo didático mostra quadros idênticos aos vetores de teste da Etapa 16.
- [ ] Playwright: mudar a faixa de LT-101 altera o valor no Modbus como esperado.
- [ ] Teste de malha no formato 0–27648: corrente fixa em 4, 12 e 20 mA aparece no mestre como 0, 13824 e 27648.
- [ ] Um observador não consegue alterar nenhum parâmetro.

### Etapa 19 — Realidade virtual

**Objetivo:** a mesma planta em VR no navegador do Meta Quest, como opção do usuário.

**Depende de:** Etapa 14.

**Escopo**

- Botão “Entrar em VR” (WebXR pelo Babylon.js), exibido só quando há suporte.
- Locomoção por teleporte (padrão) e contínua (opcional), com vinheta de conforto.
- Interação: apontar para instrumentos e válvulas; painel flutuante da IHM (Babylon GUI, com os tokens da Etapa D2); controle do distúrbio.
- Ajustes de desempenho para o Quest: níveis de detalhe, texturas e sombras.

**Validação**

- [ ] No Meta Quest disponível no laboratório: entra em VR e mantém pelo menos 72 fps por 10 minutos (medido).
- [ ] O teleporte funciona e não atravessa equipamentos.
- [ ] O painel em VR mostra os mesmos valores da IHM web, e mudar o distúrbio em VR afeta a planta.
- [ ] Sem headset, o site funciona como na Etapa 14, sem erros no console.
- [ ] Teste em desenvolvimento com emulador WebXR registrado.
- [ ] Orçamento de desempenho no Quest (fps, tempo de quadro, memória) medido e registrado, como base para a Etapa 20.

### Etapa 20 — Efeitos visuais e vista em corte (Marco 3)

**Objetivo:** o usuário vê o processo acontecer (água subindo e se agitando, fervura, vapor, fluxo nas tubulações), com tudo guiado pelas variáveis reais da planta.

**Depende de:** Etapas 15 (fervura e swell vêm da física) e 19 (orçamento de desempenho no Quest).

**Escopo**

- **Vista em corte (“raio-X”):** um botão na IHM, um atalho de teclado e um botão do controle de VR alternam entre normal, paredes translúcidas e corte por plano. Vale para a planta inteira ou só para o equipamento selecionado, e mostra a água dentro do tubulão e das tubulações.
- **Água:** superfície com ondas (shader) na altura do nível interpolado; agitação proporcional à vazão de entrada e à fervura; respingo e espuma no ponto de entrada; o swell aparece como a superfície subindo cheia de bolhas quando a pressão cai.
- **Fervura:** bolhas em partículas (na GPU quando houver WebGPU), com densidade proporcional ao calor transferido.
- **Vapor:** pluma na saída de FV-103 proporcional à vazão de vapor; jato forte e visível quando a PSV-101 abre.
- **Fluxo:** partículas ou textura em movimento dentro das tubulações, com velocidade proporcional à vazão; tubulação sem fluxo fica parada.
- **Equipamentos e instrumentos locais:** chama do queimador proporcional ao combustível; rotor da bomba girando conforme a operação; manômetro com ponteiro e visor de nível de vidro no tubulão, como numa planta real.
- **Alarmes na cena:** sinaleiro girando e piscando em alarme; instrumento em falha destacado.
- **Rótulos flutuantes:** valores sobre os equipamentos, ligados e desligados por um botão.
- **Níveis de qualidade:** alto, médio e baixo, escolhidos automaticamente pelo desempenho, com um perfil próprio para o Quest.
- Todos os efeitos são locais e cosméticos: só leem o estado recebido do servidor e nunca alteram a planta.

**Fora do escopo:** simulação de fluido real (CFD ou partículas físicas da água) e som.

**Validação**

- [ ] Na vista em corte, a superfície da água difere menos de 1% da altura do nível simulado (teste automático por um gancho de depuração).
- [ ] Num roteiro de ensaio (degrau de combustível, abertura de FV-103, abertura da PSV-101), bolhas, vapor e fluxo mudam visivelmente em até 0,5 s após a variável mudar.
- [ ] Com vazão zero, não há animação de fluxo; com a bomba desligada, o rotor fica parado.
- [ ] Num degrau de vapor, a vista em corte mostra o nível subindo antes de cair, coerente com a tendência na IHM.
- [ ] A vista em corte alterna pelo botão da IHM, pelo atalho de teclado e pelo controle de VR.
- [ ] Com os efeitos ligados: pelo menos 60 fps no notebook de referência na qualidade alta e pelo menos 72 fps no Quest no perfil Quest.
- [ ] Teste de captura de tela estável com a vista em corte ligada.
- [ ] Marco 3 registrado na linha “Estado atual” do descritivo.

## Fase 4 — CLP no navegador

Ao fim da Fase 4, o usuário programa a planta em Ladder ou FBD no próprio site, sem precisar de nenhum mestre externo. É o Marco 4. Esta fase só começa com as Fases 1 a 3 concluídas.

### Etapa 21 — Runtime de soft-PLC

**Objetivo:** executar programas Ladder e FBD no servidor, no mesmo laço da planta.

**Depende de:** Etapa 5 (e caldeira v2 da Etapa 15 para o teste de malha).

**Escopo**

- Representação intermediária de programas e leitura de um subconjunto de PLCopen XML (LD e FBD).
- Instruções: contatos NA e NF; bobinas normal, set e reset; TON, TOF, TP, CTU, CTD; comparações; MOVE; aritmética; `NORM_X`/`SCALE_X`; bloco PID didático.
- Ciclo de varredura (ler entradas, executar, escrever saídas) sincronizado com o passo da planta, a cada 10 ms ou múltiplo configurável.
- Tags mapeadas na imagem de I/O (`%I`, `%Q`, `%IW`, `%QW`). O soft-PLC aparece no monitor como um mestre (autor “soft-PLC”) e segue a regra de mestres da planta (um por vez ou vários).
- Carregar, iniciar, parar e consultar variáveis pela API.
- Decisão registrada: runtime próprio ou integração com o OpenPLC (licença GPL).

**Validação**

- [ ] Programas de teste em PLCopen XML (selo de partida, temporizador, contador, malha PI de nível) carregam e executam com os resultados esperados.
- [ ] Temporizadores IEC com erro de no máximo um ciclo de varredura.
- [ ] A malha PI de nível no soft-PLC estabiliza a caldeira v2 no setpoint.
- [ ] Mesmas entradas geram o mesmo resultado bit a bit (soft-PLC e planta determinísticos juntos).
- [ ] No modo “um mestre por vez”, o soft-PLC ativo bloqueia mestres externos, e vice-versa.
- [ ] Um arquivo exportado do CODESYS ou do OpenPLC Editor, dentro do subconjunto suportado, é importado.

### Etapa 22 — Editor Ladder

**Objetivo:** escrever, salvar e executar programas Ladder pelo navegador.

**Depende de:** Etapas 11 e 21.

**Escopo**

- Editor em grade (React e SVG): redes, contatos, bobinas, blocos (temporizadores, contadores, comparações, MOVE) e ramos paralelos.
- Tabela de tags com tipos e endereços; validação durante a edição.
- Salvar na conta e abrir em PLCopen XML; baixar o arquivo; compartilhar com os mesmos papéis das plantas; o professor pode distribuir um programa-base numa atividade.
- Modo online: enviar ao soft-PLC, rodar e parar, e ver os estados ao vivo (contatos energizados em destaque).
- Desfazer e refazer; copiar e colar.

**Validação**

- [ ] Os programas de teste da Etapa 21, recriados no editor, geram a mesma representação intermediária.
- [ ] Do envio ao estado ao vivo na tela: atraso menor que 2 s.
- [ ] Erros (bobina sem condição, tag inexistente) aparecem antes do envio.
- [ ] Playwright: montar um selo de partida da bomba e ver P-101 ligar na planta.
- [ ] Um arquivo salvo abre de novo sem perda.

### Etapa 23 — Editor FBD (Marco 4)

**Objetivo:** o mesmo da Etapa 22, em diagrama de blocos de função.

**Depende de:** Etapa 22 (reaproveita tags, modo online e persistência).

**Escopo**

- Editor de blocos e ligações: lógicos, aritméticos, comparação, temporizadores, `NORM_X`/`SCALE_X` e PID.
- Ordem de execução explícita e detecção de laços algébricos.
- PLCopen XML (FBD) e modo online com valores nas ligações.

**Validação**

- [ ] A malha PI de nível feita em FBD tem a mesma resposta da feita em Ladder.
- [ ] Um laço algébrico é detectado e explicado antes do envio.
- [ ] Os valores nas ligações atualizam a pelo menos 5 Hz.
- [ ] Teste Playwright de ponta a ponta com um programa FBD.
- [ ] Marco 4 registrado na linha “Estado atual” do descritivo.

## Extensões sob demanda

Estas entregas ficam fora da sequência principal. Quando uma for escolhida, ela vira uma etapa numerada aqui, com escopo e critérios de validação no mesmo formato.

| Extensão | Para que serve | Pré-requisito |
| --- | --- | --- |
| Segundo cenário 3D (ex.: tanques acoplados ou trocador de calor) | Provar, com 3D, que um cenário novo não exige código novo | Marco 3 |
| PWA e publicação na Meta Quest Store (Bubblewrap) | Instalar como aplicativo no PC e no Quest, a partir do próprio site | Marco 3 |
| Aplicativo desktop (Tauri) com servidor embutido | Uso sem internet e sem o PC do laboratório, com IP e porta locais escolhidos pelo usuário | Marco 3 |
| Conector local para HART serial (COM virtual) | PACTware via modem, como instalação opcional; vira etapa opcional se o portão da Etapa 2 der resultado C | Etapa 17 |
| Driver S7 para o PLCSIM comum | Usuários de TIA Portal sem PLCSIM Advanced nem CLP real | Marco 1 |
| Som ambiente (bomba, vapor, queimador, buzina de alarme) | Imersão e ambientação | Etapa 20 |
| Posicionadores HART nas válvulas | Diagnóstico de válvula (curso, atrito) por HART | Etapa 17 |
| Novos idiomas | Adicionar um arquivo de idioma e revisar | Etapa 13 |
| Login pela conta da universidade (SSO) | Entrar com o usuário institucional, além do e-mail e login social | Etapa 10 |
