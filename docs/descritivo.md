# IndustrialSim Web — Descritivo do Projeto

Nov 9, 2026 · @Saulo

## Visão geral e objetivo

O IndustrialSim Web é um simulador de plantas industriais de processo contínuo que roda no navegador. Ele serve ao ensino e à pesquisa em automação, instrumentação e controle, e nasce como projeto de iniciação científica.

O usuário abre o site e encontra uma planta em 3D, navegável em primeira pessoa e, opcionalmente, em realidade virtual. A planta não tem controle interno: quem a controla é qualquer mestre Modbus TCP escolhido pelo usuário: um CLP real, o PLCSIM Advanced (TIA Portal), um softPLC como CODESYS ou OpenPLC, um SCADA, um script ou, na Fase 4, o editor Ladder do próprio site. Os instrumentos simulados falam HART e podem ser configurados pelo PACTware ou por um configurador no próprio site.

Várias pessoas podem operar a mesma planta ao mesmo tempo, e professores acompanham ao vivo as plantas das suas turmas. Neste documento, “CLP” e “mestre” se referem a qualquer um desses controladores.

**Princípios que guiam todas as decisões**

1. **Zero instalação:** o usuário só abre o site. Nada do projeto é instalado no PC dele; as ferramentas que ele já usa (TIA Portal, CODESYS, PACTware) ficam à parte.
2. **Aberto a todos:** qualquer pessoa, de dentro ou de fora da universidade, pode usar, com ou sem conta.
3. **Simulação autoritativa no servidor:** a física roda no servidor do laboratório. O navegador só mostra a planta e envia comandos.
4. **Independente de fabricante:** a planta fala Modbus TCP e HART padrão, sem depender de um CLP ou software específico.
5. **Comportamento de planta real:** o controle vem de fora, os sinais têm escala de instrumento (4–20 mA) e os instrumentos têm faixa, amortecimento e falhas.
6. **Determinismo e validação:** mesmas entradas geram as mesmas saídas, e cada modelo é validado contra referências.
7. **Cenários orientados a dados:** uma planta nova é um pacote de arquivos (modelo 3D + descrição), não código novo.
8. **Multilíngue:** a interface segue o idioma do navegador (português, inglês e espanhol no início).

**Público:** qualquer pessoa interessada em automação e controle de processos: estudantes, professores, profissionais e autodidatas, em aulas ou por conta própria. O primeiro uso será nas aulas de laboratório da faculdade.

**Visão de futuro:** vários cenários; editor de Ladder e FBD no estilo do TIA Portal dentro do site, com um soft-PLC rodando no servidor; versões como aplicativo (PWA, Meta Quest Store, desktop).

## Origem e estado atual

O projeto é feito do zero, com nova arquitetura, a partir da experiência com o protótipo em Unity do repositório [Saulojo/IC\_Proccess\_Simulation](https://github.com/Saulojo/IC_Proccess_Simulation). Do protótipo, só os modelos 3D são aproveitados; o código serve apenas como referência de lições aprendidas. O protótipo também não rodaria na web: usa HDRP, sem suporte a navegador, e um servidor Modbus com sockets e threads, que o navegador não permite.

**O que vem do protótipo**

| Item do protótipo | Situação no novo projeto |
| --- | --- |
| Modelos 3D no Blender: tubulão, válvula borboleta, sensor de vazão, bomba, planta final texturizada (`Assets/Models`) | Aproveitados: exportados para glTF (.glb) |
| Código C#: RK4, atuadores, tanque, imagem de I/O, servidor Modbus (branch Modbus-Protocol) e trabalho em HART (branch HART-Protocol) | Não aproveitado. Serve só como referência; tudo é escrito do zero, com testes desde o início |
| Cenas, shaders, VFX e scripts da Unity | Não aproveitados |

**Problemas conhecidos do protótipo, que não devem ser repetidos**

- `WaterTankPlant.Step()` força `Pump.Target = 1.0` a cada passo, o que torna a bomba incomandável e faz falhar o teste de nível decrescente.
- O servidor Modbus nunca foi ligado à planta: os comandos vêm só do Inspector e as medições nunca são publicadas.
- A simulação depende do `FixedUpdate` da Unity, em vez de um passo fixo próprio.
- A imagem de I/O reserva os 65.536 endereços de cada área (cerca de 400 KB por planta), em vez de se dimensionar pelo mapa de I/O.

**Estado atual do novo projeto:** Etapas 1 a 8 e D1 concluídas (Etapa 2 em 15/11/2026, com o portão do HART em A provisório: falta medir o tempo de resposta com o servidor em outro PC, na Etapa 9B; Etapas 3 e 4 em 15/11/2026; Etapa 5 em 17/11/2026, com o servidor do laboratório passando a Windows pela D10; Etapa 6 em 17/11/2026, validada com `pymodbus` e CODESYS, e com o teste com CLP real retirado do plano; Etapa 7 em 19/11/2026, com o reinício da planta trazido da Etapa 8; Etapa 8 em 20/11/2026; Etapa D1 em 25/11/2026, com o protótipo aprovado pelo orientador e o teste com usuários retirado do plano pela D51). Etapa 9 replanejada em 21/11/2026, em 9A (implantação reproduzível) e 9B (PC do laboratório, Marco 1), com o servidor numa VM Linux pela D10; as duas aguardam o PC do laboratório. Próxima: Etapa D2 (sistema de design aplicado), que termina antes da Etapa 10. Atualize esta linha ao concluir cada etapa do Plano de Desenvolvimento (ex.: “Etapa 3 concluída em dd/mm/aaaa; Etapa 4 em andamento”).

## Requisitos

A Fase 1 entrega o essencial: uma planta controlada por qualquer mestre Modbus, via site, sem instalar nada. A Fase 2 abre a plataforma ao público, com contas, compartilhamento e turmas; a Fase 3 traz fidelidade e imersão; a Fase 4, a programação de CLP no navegador.

**Funcionais**

| ID | Requisito | Fase |
| --- | --- | --- |
| RF01 | Carregar uma planta a partir de um pacote de cenário (modelo 3D, descrição JSON, mapa de I/O, instrumentos). Primeiro cenário: caldeira. | Fase 1 (MVP) |
| RF02 | Simular em tempo real, com passo fixo de 10 ms, no servidor. | Fase 1 (MVP) |
| RF03 | Dar a cada planta em execução um servidor Modbus TCP próprio, compatível com qualquer mestre (CLP real, PLCSIM Advanced, CODESYS, OpenPLC, SCADA, script). | Fase 1 (MVP) |
| RF04 | IHM web: valores ao vivo, tendências, alarmes, comando de distúrbios e estado da conexão do mestre. | Fase 1 (MVP) |
| RF05 | Configurações da planta (Unit ID, formato analógico, watchdog etc.) e monitor de comunicação: registradores ao vivo, log e valores forçados. | Fase 1 (MVP) |
| RF06 | Atender pelo menos 30 plantas simultâneas e isoladas. | Fase 1 (MVP) |
| RF07 | Exportar o ensaio em CSV (comandos e medições) e reproduzir um ensaio gravado. | Fase 1 (MVP) |
| RF08 | Contas (e-mail e login social), plantas salvas que retomam de onde pararam e modo visitante sem conta. | Fase 2 (Plataforma) |
| RF09 | Compartilhar uma planta, por e-mail ou por código, sem papéis (D46); várias pessoas e vários mestres operando a mesma planta. | Fase 2 (Plataforma) |
| RF10 | Turmas e painel do professor: ver as plantas dos alunos ao vivo e injetar falhas e distúrbios. | Fase 2 (Plataforma) |
| RF11 | Interface em português, inglês e espanhol, com o idioma detectado pelo navegador. | Fase 2 (Plataforma) |
| RF12 | Uso aberto a qualquer pessoa, com limites que protegem o servidor. | Fase 2 (Plataforma) |
| RF13 | Visualização 3D com navegação em primeira pessoa (teclado e mouse) e feedback visual do processo: água, fervura, vapor, fluxo e vista em corte. | Fase 3 (Imersão) |
| RF14 | Modelo realista de caldeira: pressão, nível com swell/shrink, geração de vapor. | Fase 3 (Imersão) |
| RF15 | Instrumentos HART com comandos universais e de prática comum, com efeito real na planta. | Fase 3 (Imersão) |
| RF16 | Acesso HART pelo configurador do site e pelo PACTware, conforme o portão de decisão da Etapa 2. | Fase 3 (Imersão) |
| RF17 | Realidade virtual opcional via WebXR (ex.: Meta Quest). | Fase 3 (Imersão) |
| RF18 | Soft-PLC no servidor executando programas IEC 61131-3 em Ladder e FBD. | Fase 4 (CLP web) |
| RF19 | Editor de Ladder e FBD no navegador, salvando em PLCopen XML. | Fase 4 (CLP web) |
| RF20 | Aplicativos: PWA, Meta Quest Store e desktop com servidor embutido. | Extensão |

**Não funcionais (valem em todas as fases)**

| ID | Requisito | Critério verificável |
| --- | --- | --- |
| RNF01 | Zero instalação | Um usuário novo, de qualquer rede, controla a planta usando só Chrome ou Edge atuais e um mestre Modbus próprio (e o navegador do Quest, para VR). |
| RNF02 | Determinismo | Mesmas entradas no mesmo tempo simulado geram estado idêntico bit a bit (teste com hash do estado). |
| RNF03 | Realismo validado | Propriedades da água conferidas com as tabelas de verificação da IAPWS-IF97; caldeira conferida com Åström & Bell (2000) e com uma implementação de referência independente. |
| RNF04 | Desempenho | Física a 100 Hz por planta; estado enviado ao cliente a 20–30 Hz; 30 plantas usando menos de 50% de um núcleo; cliente ≥ 60 fps em notebook de referência e ≥ 72 fps no Quest. |
| RNF05 | Tempo real | Deriva entre tempo simulado e tempo de relógio < 0,1%; um comando Modbus que chega ao servidor vale no passo seguinte (≤ 10 ms). |
| RNF06 | Segurança | Da internet, só a porta 443 e as faixas de portas de planta ficam acessíveis; conexões Modbus e HART-IP só valem após pareamento; limites de uso por conta e por IP. |
| RNF07 | Hardware modesto | Roda numa VM Linux no PC do laboratório, que tem Windows 10 ou 11 como hospedeiro; meta da VM: 4 GB de RAM e 4 núcleos virtuais (CPU de 2015 em diante); recomendado PC com 16 GB, para sobrar memória ao uso do autor no Windows. Se a demanda crescer, a mesma VM migra para um servidor da universidade ou para a nuvem. |
| RNF08 | Testabilidade | Núcleo sem dependência de rede ou interface; testes automatizados em Core, IO, Modbus e HART; CI verde para integrar. |
| RNF09 | Evolução segura | Protocolo cliente-servidor versionado; cenários validados por um esquema JSON; migrações de banco versionadas. |
| RNF10 | Privacidade (LGPD) | Dados pessoais mínimos; termos de uso e política de privacidade publicados; exportação e exclusão de conta funcionando. |
| RNF11 | Multilíngue | Nenhum texto de interface fora dos arquivos de idioma, e nenhuma chave de tradução faltando (verificado no CI). |
| RNF12 | Usabilidade e identidade visual | Interface projetada a partir de protótipo navegável aprovado pelo orientador (trilha de design; teste com usuários retirado em 25/11/2026, D51); contraste WCAG 2.2 AA nos temas claro e escuro; IHM segue a ISA-101 (cor reservada para estados anormais). |

## Decisões de arquitetura

A decisão central é a D01: a planta existe só no servidor, e todo o resto decorre dela. Para mudar qualquer decisão, registre a nova aqui, com data e motivo, antes de alterar o código.

| ID | Decisão | Motivo | Descartado |
| --- | --- | --- | --- |
| D01 | Simulação autoritativa no servidor; o cliente só desenha e faz efeitos cosméticos. | Modbus e HART exigem um servidor; abas em segundo plano congelam timers; todos veem o mesmo valor; a física é barata (\~0,7% de um núcleo por planta a 100 Hz, medido num modelo pesado). | Física no navegador (só num futuro modo demonstração, sem Modbus). |
| D02 | Backend em C# / .NET 10 LTS com ASP.NET Core; um único processo serve site, WebSocket, Modbus e HART-IP. | A equipe já conhece C#; tipagem forte; desempenho; bibliotecas Modbus maduras. | Python (tipagem fraca para um sistema grande), Node (física e rede numa thread), Rust/Go (curva de aprendizado). |
| D03 | Frontend em TypeScript com Vite; 3D com Babylon.js; interface 2D com React. | WebXR oficial no Babylon, WebGPU e WebGL2; IHM, painéis e editor Ladder são aplicações web 2D. | Unity Web (sem HDRP, VR por pacote comunitário, build pesado), three.js, Godot (C# sem exportação web), Unreal (sem web). |
| D04 | Cenários orientados a dados: glTF + `scenario.json` (componentes, ligações, parâmetros, mapa de I/O, instrumentos). | Cenário novo sem código novo; cenários testáveis sem 3D. | Uma classe C# por planta, como no protótipo. |
| D05 | Cliente e servidor conversam por WebSocket seguro (wss), com mensagens JSON versionadas; tipos TS gerados a partir do contrato em C#. | Simples de depurar; contrato único evita divergência. | Binário (MessagePack) por ora; pode vir se a banda exigir. |
| D06 | RK4 de passo fixo, dt = 10 ms; comandos aplicados no início do passo; ruído com semente fixa; unidades SI internas. | Determinismo e reprodutibilidade; comportamento igual ao ciclo de varredura de um CLP. | Passo variável ou ligado ao framerate. |
| D07 | Modbus TCP padrão, sem extensões de fabricante: uma porta por planta em execução (faixa configurável, padrão 15000–15999), Unit ID configurável, pareamento por IP confirmado no navegador, um ou vários mestres por planta. | Funciona com qualquer mestre (CLP, CODESYS, OpenPLC, SCADA, script) e isola as plantas mesmo atrás do mesmo IP público. | Unit ID como isolamento (editável, não isola); só IP de origem (falha com NAT). |
| D08 | Formato analógico escolhido por planta: 0–27648 (padrão, igual ao cartão Siemens), 0–10000, 0–32767 ou `float32` em unidades de engenharia. | O usuário escala no mestre como faria numa planta real, no formato do controlador que usa; a faixa HART altera o sinal. | Um único formato fixo. |
| D09 | (alterada em 14/11/2026) HART: núcleo de protocolo próprio, independente do transporte; configurador HART no site sempre disponível; PACTware primeiro pela serial: DTM de modem HART numa COM virtual (com0com) ligada ao navegador pela Web Serial, que só repassa bytes ao servidor pelo WebSocket. O escopo final segue o portão de decisão da Etapa 2 (resultados A, B ou C); HART-IP fica como extensão. **Resultado do portão (15/11/2026): A provisório.** S1 a S6 e S8 atendidos; S7 (tempo de resposta com o servidor em outro PC) será medido na Etapa 9B e pode mudar o resultado para B. Definido na prova: instrumento em HART 7; serial em 1200 baud, 8 bits, paridade ímpar, 1 stop bit, sem controle de fluxo; com0com 2.2.2.0 assinado, com a emulação de baud desligada. Evidências em `docs/validacao/etapa-02.md`. | Usa só ferramentas gratuitas (PACTware, DTM de modem HART, com0com) e o caminho que todo DTM de modem suporta; a Web Serial já funciona em ferramentas como o Teleplot; não abre portas extras no servidor nem exige pareamento. O custo é um driver de terceiros no PC de quem usa o PACTware. | HART-IP como caminho principal (DTM HART-IP em geral pago e uma porta por planta); conector local instalável (software do projeto no PC do usuário). |
| D10 | (alterada em 21/11/2026) Hospedagem numa VM Linux (Ubuntu Server LTS, versão fixada na Etapa 9A) no Hyper-V do PC do laboratório, que continua com Windows para o uso do autor (estudo e desenvolvimento). Na VM: servidor .NET como serviço systemd (início automático e reinício em falha), atrás do Caddy como proxy reverso com HTTPS automático, também como serviço; PostgreSQL na mesma VM (a partir da Etapa 10); firewall da VM só com as portas previstas. A VM tem núcleos e RAM reservados, IP próprio na rede do laboratório e início automático junto com o Windows. No Linux, o laço espera com `Thread.Sleep` (D33); a precisão é medida no teste de carga da Etapa 9A. Arquivos estáticos em CDN gratuita se a banda limitar. O código continua multiplataforma, com testes no Windows e no Linux; a mesma VM (ou o mesmo roteiro) migra para um servidor da universidade ou para a nuvem se a demanda crescer. | O PC do laboratório também é a estação de trabalho do autor: a VM isola o site público e as portas de planta dos arquivos e contas dele, e os núcleos reservados protegem o laço de tempo real das cargas de desenvolvimento; o Linux raramente precisa reiniciar (um reinício do Windows ainda derruba a VM, que volta sozinha); quase todo servidor de universidade ou de nuvem é Linux, então a migração prevista fica direta; systemd, Caddy e firewall são de primeira classe no Linux; WebXR e WebGPU exigem HTTPS. | Tudo no Windows, com serviço nativo (D10 de 16/11/2026: o site público dividiria a estação de trabalho do autor sem isolamento, e as cargas de desenvolvimento e os reinícios do Windows atingiriam a planta); dual boot (o servidor sairia do ar sempre que o Windows estivesse em uso); Linux como sistema principal com o Windows em VM (TIA Portal, PLCSIM e dongles USB funcionam mal em VM); Docker por ora (as 1000 portas Modbus exigiriam rede em modo host, e o contêiner seria mais uma camada dentro da VM; pode voltar se a migração pedir); WSL2 (rede por NAT e sem início automático confiável como servidor); IIS e hospedagem serverless (o laço de tempo real precisa de um processo sempre ativo e de portas TCP próprias). |
| D11 | Soft-PLC (Fase 4) roda em .NET no mesmo laço da planta, lendo a imagem de I/O direto; projetos em PLCopen XML. | Determinismo total na malha fechada; intercâmbio com CODESYS, OpenPLC e Beremiz. | CLP rodando no navegador. |
| D12 | Modelos 3D: Blender → glTF binário (.glb) com compressão (Draco ou Meshopt, texturas KTX2); ligação objeto ↔ variável por convenção de nomes. | Carregamento rápido no navegador; serve a qualquer motor. | FBX e formatos proprietários. |
| D13 | Contas com ASP.NET Core Identity e PostgreSQL (EF Core): e-mail com verificação e login social (Google, Microsoft, GitHub); modo visitante sem conta, com limites. | Componente maduro, gratuito e no mesmo processo; visitante mantém o “só abrir o site”. | Provedor de identidade separado (ex.: Keycloak), pesado para o PC do laboratório. |
| D14 | Planta salva (dono, cenário, configurações, estado) separada da instância em execução; instância hiberna sem ninguém conectado e retoma do mesmo estado. | Muitos usuários cadastrados com poucas plantas rodando ao mesmo tempo; o servidor modesto aguenta. | Plantas sempre rodando. |
| D15 | (alterada em 25/11/2026, pela D46) Colaboração sem papéis: quem tem acesso a uma planta tem os mesmos direitos (D46); vale a última escrita; toda ação registrada com autor e horário. | Simples, previsível e auditável, como numa sala de controle com vários operadores. | Bloqueio de edição por usuário; papéis dono, operador e observador (até 25/11/2026). |
| D16 | Idiomas: textos em arquivos de recurso (i18next no frontend, recursos .NET no backend); idioma do navegador; pt-BR, en e es; traduções iniciais geradas com IA e revisadas; termos técnicos (coil, holding register, HART) mantidos. | Tradução de qualidade e sem custo em tempo de uso; adicionar idioma = adicionar um arquivo. | Tradução automática em tempo real (custo, latência, erros em termos técnicos). |
| D17 | Plataforma aberta ao público, com limites por conta e por IP, fila quando o servidor lotar, termos de uso e política de privacidade (LGPD). | Cumpre o objetivo de abrir o software a qualquer pessoa sem derrubar o servidor do laboratório. | Acesso restrito à universidade. |
| D18 | Código escrito do zero; do protótipo, só os modelos 3D são aproveitados. | A arquitetura é outra (servidor autoritativo, web); código novo nasce testado e coerente com as decisões deste documento. | Portar código do protótipo Unity. |
| D19 | (alterada em 25/11/2026, pela D51) Design numa trilha própria (D1: prototipagem; D2: sistema de design), entre a Etapa 7 e a Etapa 10. Até lá, telas funcionais simples, já usando tokens (variáveis CSS). IHM segue a ISA-101. | Decisões de interface tomadas num protótipo navegável aprovado pelo orientador; o redesenho vira troca de tokens e componentes, sem refazer telas; as telas da Fase 2 já nascem no design final. | Desenhar cada tela na etapa que a cria; design só no fim do projeto. |
| D20 | (11/11/2026, Etapa 0) Testes .NET com xUnit v3 (4.x) sobre a Microsoft Testing Platform, ativada no `global.json` (`"test": {"runner": "Microsoft.Testing.Platform"}`), que também fixa o SDK 10 (`rollForward: latestFeature`). | É o caminho padrão do xUnit 4 com o SDK 10; dispensa `Microsoft.NET.Test.Sdk` e `xunit.runner.visualstudio`; local e CI usam o mesmo SDK, então os avisos tratados como erro são os mesmos nos dois. | VSTest (o xUnit 4 o trata como legado no SDK 10); deixar o SDK livre (o SDK 11 local daria avisos diferentes dos do CI). |
| D21 | (11/11/2026, Etapa 0) TypeScript fixado em 6.0.x até o typescript-eslint aceitar o TypeScript 7; stylelint com `stylelint-declaration-strict-value` obriga tokens (`var(--…)`) em cor, fonte, espaçamento e raio. | O typescript-eslint 8.72 exige TypeScript abaixo de 6.1; a regra de tokens passa a ser verificada no lint, e não só combinada. | TypeScript 7 sem lint com tipos; tokens só por convenção. |
| D22 | (14/11/2026, Etapa 1) Gerador pseudoaleatório próprio para o ruído: xoshiro256\*\* com a semente expandida por SplitMix64 (`IndustrialSim.Core.Numerics.SeededRandom`), com teste contra vetores de referência. | A sequência do `System.Random` com semente não é garantida entre versões do .NET, e a sequência do ruído faz parte do determinismo (RNF02). | `System.Random` com semente. |
| D23 | (15/11/2026, Etapa 3) O `scenario.json` é validado por código próprio (`ScenarioLoader` e `ScenarioPlant`), que acumula os erros com o caminho do campo. O esquema JSON (`scenarios/scenario.schema.json`) é gerado do mesmo registro de tipos de componente e conferido por teste. | Mensagens em português com o caminho do campo e sem dependência nova; o esquema serve ao editor e não diverge do validador. | Biblioteca de JSON Schema como validador (mensagens genéricas e mais uma dependência). |
| D24 | (15/11/2026, Etapa 3) A ordem em que os componentes avançam no passo é calculada pelas ligações: quem fornece um sinal avança antes de quem o consome. Num laço de realimentação, a ligação que fecha o laço entrega o valor do passo anterior e entra num componente com estado; um laço só de componentes sem estado (laço algébrico) é erro de validação. | A ordem dos componentes no arquivo deixa de importar, e o resultado continua determinístico (RNF02). | Executar na ordem do arquivo; resolver laços algébricos por iteração. |
| D25 | (15/11/2026, Etapa 3) Cada transmissor tem gerador de ruído próprio, com semente `noiseSeed + k` (k = ordem do transmissor no arquivo, a partir de 0), e sorteia uma vez por passo. O ruído não entra no hash de estado. | Acrescentar um instrumento no fim não muda o ruído dos anteriores; o primeiro transmissor reproduz o gerador único do modelo da Etapa 1. | Um gerador único para a planta inteira. |
| D26 | (15/11/2026, Etapa 3) Unidades de vazão nos componentes: `tank` em m³/s; `drum`, `burner` e `steamPressure` em kg/s; `pump` e `valve` na unidade de vazão do cenário. | O bloco de diagnóstico da caldeira é em kg/s; o tanque da Etapa 1 fica como estava. | Converter tudo para uma única unidade de vazão. |
| D27 | (15/11/2026, Etapa 4) No formato `float32`, o sinal analógico de endereço `a` ocupa os registradores `2a` e `2a+1`. Heartbeat e bloco de diagnóstico não mudam de endereço, e o cenário é recusado se eles caírem sobre os sinais nesse arranjo. | Um `float32` precisa de 2 registradores e os endereços dos sinais no mapa são consecutivos; a regra é simples de explicar e de configurar no mestre. | Mapa separado para o `float32`; manter os endereços e sobrepor sinais. |
| D28 | (15/11/2026, Etapa 4) A medição satura em 3,8 e 20,5 mA; 3,6 e 21 mA só aparecem com o transmissor em falha (NAMUR NE43). No formato 0–27648 as contagens vão de −4864 a 32511; nos formatos 0–10000 e 0–32767, o valor é limitado ao que cabe em `INT` (em 0–32767, 21 mA satura em 32767). | Comportamento de transmissor real; o mestre distingue medição fora da faixa de instrumento em falha. | Corrente sem limite; registrador sem sinal nos outros formatos. |
| D29 | (15/11/2026, Etapa 4) A faixa (LRV/URV) fica na entrada `io` de cada registrador, na unidade do sinal da planta; `unit` e `unitFactor` dão a unidade de engenharia (valor de engenharia = valor do sinal × fator), usada no `float32`, no bloco de diagnóstico e nos valores forçados. | A planta continua em SI (D06) e o usuário vê bar e %. | Faixa em unidade de engenharia; planta fora do SI. |
| D30 | (15/11/2026, Etapa 4) O valor forçado é guardado em unidade de engenharia (ou booleano), passa pela faixa e pelo formato atuais e tem prioridade sobre a planta e sobre a falha do transmissor. | Continua válido quando o formato muda. | Forçar o valor bruto do registrador. |
| D31 | (15/11/2026, Etapa 4) Ao trocar o formato analógico ou a ordem das palavras, os comandos que estão nos holding registers são regravados no novo formato. | Sem isso, as contagens antigas seriam lidas no formato novo e as válvulas dariam um salto até o mestre reescrever. | Deixar os registradores como estão; zerar os comandos. |
| D32 | (15/11/2026, Etapa 4) O watchdog conta tempo simulado (passos da planta), e não tempo de relógio. | Determinismo (RNF02): uma gravação reproduzida dispara o watchdog no mesmo passo. | Temporizador de relógio. |
| D33 | (17/11/2026, Etapa 5) Laço de tempo real em threads dedicadas (padrão: 2), compartilhadas pelas plantas; cada planta fica numa thread só, a menos ocupada quando entra. Os ciclos seguem uma grade fixa no relógio monotônico (`Stopwatch`); depois de um atraso, até 10 passos são recuperados em seguida, e o excesso é descartado e aparece na deriva. No Windows, a espera usa o temporizador de alta resolução (`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`); nos outros sistemas, `Thread.Sleep`. | Passo de 10 ms preciso no Windows (D10) sem girar a CPU nem mudar o relógio do sistema; a grade fixa não acumula erro (RNF05); uma planta que falha sai do laço sem parar as outras. Medido: 30 plantas, p99 de 0,76 ms e 1,1% de um núcleo. | Uma thread por planta (mais threads e trocas de contexto); `Thread.Sleep` ou `PeriodicTimer` no Windows (resolução de cerca de 15,6 ms); `timeBeginPeriod` (muda o relógio do sistema inteiro); recuperar todo o atraso (rajada de passos depois de uma pausa longa). |
| D34 | (17/11/2026, Etapa 5) Gravação de ensaio em CSV (`industrialsim-recording/1`): cabeçalho com cenário, semente, passo e amostragem; por linha, os coils e holding registers brutos copiados no início do passo, as configurações de I/O, as entradas e as saídas da planta em SI e o watchdog; uma linha por passo com mudança e uma a cada 100 ms. A reprodução aplica esses valores passo a passo e gera o mesmo arquivo, byte a byte. A planta em execução escreve o arquivo por uma fila, fora da thread do laço. | Registra os comandos como o mestre os mandou (formato, watchdog e distúrbios incluídos) e transforma a reprodução num teste de determinismo (RNF02); a escrita no disco não atrasa o passo. | Gravar só as entradas da planta em SI (não reproduz formato nem watchdog); uma linha por passo (arquivo 10 vezes maior); escrever no disco pela thread do laço. |
| D35 | (17/11/2026, Etapa 5) Uma planta sem cliente expira depois de `IdleTimeout` (padrão: 30 min) sem atividade (criação, consulta pela API ou saída de um cliente); até a Etapa 10, expirar é encerrar. Os erros da API REST trazem só um código (`{ "code": "..." }`), que a interface traduz pelos arquivos de idioma. | Libera memória sem depender do banco de dados (Etapa 10); erros sem texto fixo no servidor (RNF11). | Plantas sem expiração até a Etapa 10; mensagens de erro em texto na API. |
| D36 | (17/11/2026, Etapa 6) Servidor Modbus TCP escrito no projeto (`IndustrialSim.Modbus`): cabeçalho MBAP e as funções 1, 2, 3, 4, 5, 6, 15 e 16, lendo e escrevendo direto na imagem de I/O. A FluentModbus (MIT) entra só nos testes, como mestre independente. | Nenhuma das bibliotecas avaliadas (NModbus 3.0, FluentModbus 5.3) expõe o IP de quem escreveu nem deixa segurar a conexão durante o pareamento; a FluentModbus reserva 65.536 endereços por área (o problema do protótipo) e a NModbus aceita todo cliente sem filtro. O protocolo de servidor é pequeno e fica testado contra três mestres independentes. | NModbus ou FluentModbus com uma cópia da imagem sincronizada e um proxy na frente para o pareamento. |
| D37 | (17/11/2026, Etapa 6) Unit ID configurável de 1 a 255 (padrão 1), alterável com a planta rodando. Requisição com outro Unit ID recebe a exceção 0x0B (*gateway target device failed to respond*), sem ler nem escrever. | O erro fica visível no mestre e no log, em vez de um time-out; o `MB_CLIENT` usa 255 por padrão, e o guia mostra as duas saídas. | Ignorar a requisição (time-out sem explicação); aceitar qualquer Unit ID. |
| D38 | (17/11/2026, Etapa 6) Pareamento por IP: a conexão de IP não autorizado fica aberta e sem resposta, e as requisições recebidas são descartadas (salvo as que chegam até 50 ms antes de a autorização alcançar a conexão). Um pedido por IP, com prazo de 60 s: aceitar autoriza o IP; recusar fecha e bloqueia o IP por 60 s, sem novo pedido; sem resposta, fecha, e a próxima conexão abre outro pedido; retirar a autorização fecha as conexões do IP. Por planta, até 8 pedidos pendentes e 8 conexões. Mestres autorizados contam como clientes (a planta não expira). As portas da faixa são entregues em rodízio. Até as contas (Etapa 10), quem tem o identificador da planta aceita e recusa. | Comandos antigos não são executados depois da autorização; um mestre que reconecta sozinho (ex.: CODESYS com reconexão automática) não enche a IHM de pedidos; a porta de uma planta encerrada não vai logo para outra. | Responder com exceção enquanto pendente; executar depois do aceite os comandos guardados; reaproveitar logo a porta liberada. |
| D39 | (19/11/2026, Etapa 7) Seção opcional `hmi` no `scenario.json`: `disturbances` (entradas externas que a IHM muda, com faixa e unidade de engenharia) e `alarms` (saídas externas que, ativas, viram alarme). O watchdog disparado é sempre alarme. Um distúrbio não pode estar numa entrada comandada pelo mapa Modbus; o valor pedido é limitado à faixa e vale no passo seguinte. | Cenário novo sem código novo (D04); o servidor decide o que a IHM pode comandar e quando há alarme, e todos os clientes veem o mesmo. | Listas fixas no código do web; deduzir alarmes das entradas discretas (P-101 em operação não é alarme). |
| D40 | (19/11/2026, Etapa 7) Tipos TypeScript gerados por código próprio (`TypeScriptWriter`, por reflexão sobre `IndustrialSim.Contracts`; `npm run contracts`) e conferidos por teste, como o esquema do cenário (D23). WebSocket em `/api/plants/{id}/ws`, na porta do site, com mensagens JSON identificadas pelo campo `type` e versão do protocolo no `hello`. O estado vai a 20 Hz (a cada 5 passos, publicado pelo laço numa fila de 1 posição por cliente) como listas de números `float32`, na ordem combinada no `welcome`; Modbus e alarmes só quando mudam. A IHM reconecta sozinha (0,25 s dobrando até 2 s). | Sem dependência nova; contrato C# como fonte única (D05); cerca de 33 kbit/s por cliente (medido); um cliente lento pula estados sem atrasar o laço. | NSwag ou TypeGen (dependência para poucos tipos); nomes dos sinais em cada estado (cerca de 3 vezes mais banda); temporizador por conexão (resolução de cerca de 15,6 ms no Windows). |
| D41 | (19/11/2026, Etapa 7) Reiniciar a planta é remontá-la do cenário, com a mesma semente, mantendo a imagem de I/O, o Modbus, o pareamento e as configurações: os comandos do mestre valem já no primeiro passo, e os distúrbios voltam ao valor inicial. A gravação fecha o arquivo e começa outro (`{id}-{reinício}.csv`). O estado leva o número do reinício (`run`), e a IHM recomeça a tendência. | O mestre não precisa reconectar; o reinício é determinístico (mesmo hash de uma planta nova); cada gravação continua reproduzível do passo 0 (D34). | Criar outra planta (mudaria a porta e o pareamento); continuar o mesmo arquivo de gravação (a reprodução teria um salto). |
| D42 | (20/11/2026, Etapa 8) Mestres permitidos por planta: “vários ao mesmo tempo” (padrão) ou “um por vez”. No modo um por vez, aceitar ou autorizar outro IP retira a autorização do anterior e fecha as conexões dele (o diálogo de pareamento avisa antes); pedir esse modo com mais de um IP autorizado é recusado, e quem configura escolhe quem retirar. | Evita dois mestres escrevendo na mesma planta sem mudar o fluxo de pareamento da D38; o padrão mantém o comportamento das Etapas 6 e 7. | Manter as autorizações e recusar a conexão de um segundo IP enquanto o primeiro estiver conectado (um mestre que reconecta sozinho ficaria preso sem explicação). |
| D43 | (20/11/2026, Etapa 8) Pausar deixa a planta no laço com a situação `Paused`: o passo não avança (tempo simulado, ruído e watchdog param), o estado continua publicado a 20 Hz e o mestre continua lendo e escrevendo; os comandos valem no primeiro passo depois de retomar. Forçar ou liberar com a planta pausada publica as medições na hora; reiniciar uma planta pausada a mantém pausada. | Retomar continua do mesmo estado, bit a bit (mesmo hash de uma planta que nunca pausou); IHM e monitor continuam vivos durante a pausa. | Tirar a planta do laço (perderia a thread e a publicação do estado). |
| D44 | (20/11/2026, Etapa 8) Monitor de registradores e log de comunicação pelo WebSocket da planta (protocolo versão 2). O monitor vai só para as conexões que o ligaram, a 10 Hz, com as linhas do mapa (reenviadas quando o formato muda), valores brutos e de engenharia calculados no servidor, marcas de forçado e a última escrita de cada linha (reenviada quando muda o autor ou a cada 1 s). O log guarda os últimos 500 eventos por planta, em memória, com tipo e argumentos estáveis traduzidos pela IHM; exceções e recusas repetidas viram uma linha com contagem; os novos vão a cada 250 ms. Até as contas, o autor das ações é o IP do navegador. | Escrita no monitor em cerca de 90 ms; 61 kbit/s com o monitor ligado e um mestre escrevendo (medido); a IHM não converte contagens (D01); textos só nos arquivos de idioma (RNF11). | Monitor a 20 Hz (cerca de 100 kbit/s só ele); API REST consultada pela IHM; texto pronto no log do servidor. |
| D45 | (20/11/2026, Etapa 8) Mapa de I/O exportado pelo servidor, no formato e com as faixas atuais da planta: em CSV (`GET /api/plants/{id}/io-map.csv`, colunas em inglês e números na cultura invariante) e em JSON para a página `/plants/{id}/io-map`, que o navegador imprime ou salva em PDF. | Sem dependência nova; o PDF já sai traduzido pelos arquivos de idioma do web. | jsPDF (mais uma dependência no cliente); gerar o PDF no servidor (exigiria os textos traduzidos também no .NET). |
| D46 | (25/11/2026, Etapa D1) Compartilhamento sem papéis. Quem tem acesso a uma planta pode tudo o que quem a compartilhou pode: operar, parear e retirar mestres, configurar, forçar e compartilhar; só quem criou a planta a exclui e remove pessoas. Convite pelo e-mail de quem já tem conta ou por código de 1 uso ou de vários usos, com validade (24 h, 7 dias ou até revogar) e revogável; o código entra em Minhas plantas ou em Turmas, no mesmo campo do código de turma, ou abre como link. Ver sem comandar só existe para o professor, que abre a planta de um aluno pelo painel da turma. | Numa aula, todos da dupla precisam comandar; escolher papel a cada convite é um passo que quase sempre dá "operador"; o código serve para quem está ao lado, sem trocar e-mail. Pedido do autor, aprovado pelo orientador. | Papéis dono, operador e observador (D15 e RF09 até 25/11/2026). |
| D47 | (25/11/2026, Etapa D1) Histórico dos gráficos só da sessão atual, guardado pelo servidor em memória, por planta. A sessão vai de quando a planta começa a rodar (criada ou retomada da hibernação) até ela encerrar ou hibernar; reiniciar zera o histórico (D41). Quem entra depois recebe o histórico da sessão ao conectar. Os últimos minutos ficam a 20 Hz e o resto é reduzido (por exemplo, a 1 Hz: 16 sinais `float32` dão 230 kB por hora por planta). Os ensaios gravados (CSV) continuam como estão. | A aba Gráficos mostra a sessão inteira a quem chega atrasado (o professor, a dupla), sem guardar dados entre sessões, e cabe na memória do PC do laboratório (RNF07). | Histórico só no navegador (quem entra depois veria só dali em diante); histórico persistente entre sessões. |
| D48 | (25/11/2026, Etapa D1) Identidade visual: nome "IndustrialSim Web"; logotipo conceito B (balão de instrumento ISA-5.1 com uma resposta ao degrau); fontes IBM Plex Sans e IBM Plex Mono (SIL OFL 1.1), servidas pelo próprio site, com números sempre em mono; ícones de interface Lucide (ISC) e símbolos de processo próprios no estilo da ISA-5.1. `docs/design/tokens.json` é a fonte dos tokens da D2 (cada chave de `color` vira `--color-<chave>`, nos temas claro e escuro), e o contraste WCAG 2.2 AA de cada par usado é conferido por `tools/design/contraste.mjs`. | Licenças livres e compatíveis; sem pedido a terceiros durante o uso (LGPD, rede do laboratório); paleta com o normal em cinza e cor só para o anormal (ISA-101); uma fonte única para o design e o código. | Conceitos de logotipo A (tubulação) e C (malha); fontes do Google Fonts carregadas em uso. |
| D49 | (25/11/2026, Etapa D1) Navegação dentro da planta por abas, sem a barra global: ⌂ (sai da planta), nome da planta com o menu da planta, Processo fixa e as outras ferramentas em abas que fecham; o + abre o menu de nova aba com um clique. A barra de estado (mestre, situação, tempo simulado, alarmes, Pausar, Reiniciar, Exportar, Compartilhar) fica sempre visível. A aba Comunicação da Etapa 8 se divide em Conexão e Configurações, e entra a aba Gráficos. | Mais espaço para a planta; uma ferramenta a um clique de qualquer aba. Um clique, e não duplo clique: o duplo clique é difícil de descobrir, não existe em tela de toque e exige alternativa de teclado (WCAG 2.1.1). | Barra global dentro da planta; menus separados por ferramenta. |
| D50 | (25/11/2026, Etapa D1) Prioridade de alarme vinda do cenário: cada alarme em `hmi.alarms` do `scenario.json` ganha `priority` (1, 2 ou 3), e a IHM mostra a prioridade por cor, forma e número (círculo com 1, triângulo com 2, quadrado com 3) na faixa de alarmes, no sinóptico e no painel do professor. Na caldeira: watchdog = 1; LSHH-101, LSLL-101 e PSH-101 = 2. | ISA-101: prioridade sempre visível e nunca só pela cor; a prioridade é do processo, e cada cenário define a sua. | Prioridade fixa no código da IHM. |
| D51 | (25/11/2026, Etapa D1) O teste de usabilidade com usuários sai do plano: a Etapa D1 termina com o protótipo navegável aprovado pelo orientador, conferido contra o mapa de telas por script e revisado contra a ISA-101. O roteiro, o termo de consentimento e o formulário do moderador ficam em `docs/design/teste/`, prontos para o caso de o teste voltar. | Decisão do autor, com o protótipo aprovado pelo orientador em 25/11/2026. | Teste com 5 pessoas antes de fechar a D1; adiar o teste para a D2; uma etapa de teste própria. |

## Arquitetura e estrutura do repositório

Um único processo .NET no servidor do laboratório mantém todas as plantas; navegador, CLP e PACTware do usuário apenas se conectam a ele.

```text
PC do usuário                                Servidor do laboratório (.NET 10, VM Linux)
┌─────────────────────────────┐              ┌──────────────────────────────────────────┐
│ Navegador                   │ HTTPS :443   │ Proxy HTTPS (Caddy)                      │
│  3D e VR (Babylon.js)       │─────────────►│        ▼                                 │
│  IHM, turmas e editor       │              │ ASP.NET Core e WebSocket ──► PostgreSQL  │
│  Chrome, Edge ou Quest      │              │        ▼                                 │
│                             │              │ Planta em execução (uma por planta aberta)│
│ Mestre Modbus               │ Modbus TCP   │  ├ Imagem de I/O    ├ Planta (10 ms)     │
│  CLP, CODESYS, SCADA, script│─────────────►│  ├ Modbus :15xxx    ├ Soft-PLC (Fase 4)  │
│ PACTware ► com0com ► página │ WebSocket    │  └ Núcleo HART      └ Gravação CSV       │
│  (ponte Web Serial)         │─────────────►│                                          │
└─────────────────────────────┘              └──────────────────────────────────────────┘
```

Dentro de cada sessão, planta, Modbus, HART e soft-PLC leem e escrevem a mesma imagem de I/O; o navegador recebe o estado da planta pelo WebSocket.

**Módulos do backend (`server/src`)**

| Projeto | Responsabilidade |
| --- | --- |
| `IndustrialSim.Core` | Integrador RK4, biblioteca de componentes (tubulão, válvula, bomba, transmissor), motor de cenário, propriedades da água (IF97). Sem rede e sem relógio. |
| `IndustrialSim.IO` | Imagem de I/O dimensionada pelo mapa do cenário, formatos analógicos, valores forçados, watchdog. |
| `IndustrialSim.Modbus` | Servidor Modbus TCP de uma planta, ligado à imagem de I/O; registro de quem escreveu cada registrador. |
| `IndustrialSim.Hart` | Quadros e comandos HART, modelo de instrumento, transportes (HART-IP, serial). |
| `IndustrialSim.Sessions` | Instâncias em execução, laço de tempo real, alocação de portas, pareamento, hibernação, gravação e reprodução. |
| `IndustrialSim.Accounts` | Contas (ASP.NET Core Identity), plantas salvas, compartilhamento e papéis, turmas, limites de uso; acesso ao PostgreSQL via EF Core. |
| `IndustrialSim.Contracts` | Mensagens cliente–servidor; fonte dos tipos TypeScript gerados. |
| `IndustrialSim.Server` | Host ASP.NET Core: arquivos estáticos, WebSocket, API REST, autenticação. |
| `IndustrialSim.SimRun` | Linha de comando que roda um cenário sem servidor, para testes e validação. |
| `IndustrialSim.Plc` | Runtime do soft-PLC e leitura de PLCopen XML (Fase 4). |

**Módulos do frontend (`web/src`)**

- `net`: cliente WebSocket e tipos gerados do contrato.
- `design`: tokens (variáveis CSS, temas claro e escuro) e componentes do sistema de design.
- `scene`: cena Babylon.js, câmera em primeira pessoa, ligação de objetos 3D a sinais, interpolação, efeitos visuais (água, bolhas, vapor, fluxo) e vista em corte.
- `xr`: sessão WebXR, locomoção e painéis em VR.
- `ui`: React; menu, seleção de cenários, IHM, tendências, alarmes, pareamento, configurações, monitor de registradores, configurador HART.
- `account`: login, minhas plantas, compartilhamento, turmas e painel do professor.
- `editor`: editores Ladder e FBD (Fase 4).
- `locales`: arquivos de idioma (pt-BR, en, es).

**Estrutura do repositório**

```text
industrialsim-web/
├─ server/
│  ├─ IndustrialSim.sln
│  ├─ src/        (projetos da tabela acima)
│  └─ tests/      (um projeto de testes por módulo)
├─ web/
│  ├─ package.json, vite.config.ts
│  └─ src/        (net, scene, xr, ui, account, editor, locales)
├─ scenarios/
│  └─ caldeira/
│     ├─ scenario.json
│     └─ model.glb
├─ tools/         (implementações de referência em Python para validação cruzada)
├─ deploy/        (publicação, serviços systemd, Caddyfile e firewall da VM Linux)
└─ docs/
   ├─ descritivo.md, plano.md
   ├─ guias/      (conexão com TIA Portal, CODESYS, OpenPLC, Python, PACTware)
   └─ validacao/  (evidências de cada etapa)
```

**Pacote de cenário:** o `scenario.json` tem `schemaVersion`, identificação, componentes com parâmetros, ligações entre sinais, mapa de I/O com escalas, instrumentos HART (tag, faixa, amortecimento, unidade) e o bloco `visual`. O bloco `visual` liga objetos do glTF, nomeados pela tag e pela parte (ex.: `FV-101_disco`), aos sinais que os animam.

## Planta inicial: caldeira

O primeiro cenário é uma caldeira com tubulão, controlada em nível e pressão pelo CLP do usuário. A lista abaixo é uma proposta inicial, a ajustar com o orientador; as tags seguem o padrão ISA-5.1.

| Tag | Equipamento | Papel | Sinal visto pelo CLP |
| --- | --- | --- | --- |
| P-101 | Bomba de alimentação (motor) | Fornece água ao tubulão | Saída digital (liga) + entrada digital (em operação) |
| FV-101 | Válvula borboleta de água, com posicionador | Elemento final da malha de nível | Saída analógica 4–20 mA |
| FV-102 | Válvula de combustível do queimador | Elemento final da malha de pressão | Saída analógica 4–20 mA |
| FV-103 | Válvula de saída de vapor | Carga e distúrbio, comandada pela IHM ou pelo professor | Não é do CLP (posição lida em diagnóstico) |
| LT-101 | Transmissor de nível do tubulão | Medição da malha de nível; HART | Entrada analógica 4–20 mA |
| PT-101 | Transmissor de pressão do vapor | Medição da malha de pressão; HART | Entrada analógica 4–20 mA |
| FT-101 | Transmissor de vazão de água | Terceiro elemento (opcional); HART | Entrada analógica 4–20 mA |
| FT-102 | Transmissor de vazão de vapor | Terceiro elemento (opcional); HART | Entrada analógica 4–20 mA |
| LSLL-101 / LSHH-101 | Chaves de nível muito baixo e muito alto | Intertravamento, feito pelo aluno no CLP | Entradas digitais |
| PSH-101 | Pressostato de pressão alta | Intertravamento | Entrada digital |
| PSV-101 | Válvula de alívio | Proteção mecânica, faz parte do modelo físico | Entrada digital (aberta) |

**Malhas que o usuário implementa no CLP:** LIC-101 (LT-101 → FV-101) e PIC-101 (PT-101 → FV-102). Como avanço, controle de nível a três elementos (LT-101, FT-101, FT-102). A planta não tem nenhum controle interno.

**Versões do modelo, com a mesma interface de I/O**

- **v1 (Fase 1):** balanço de massa no tubulão (modelo simples) e pressão com dinâmica de 1ª ordem em função do combustível e da saída de vapor. Serve para testar Modbus, IHM e implantação.
- **v2 (Fase 2):** modelo de tubulão de Åström & Bell (2000), com propriedades da água e do vapor pela IAPWS-IF97. Reproduz o swell/shrink (resposta inversa do nível) e o acoplamento entre pressão e nível.

Como o mapa de I/O é o mesmo nas duas versões, um programa de CLP feito na Fase 1 continua funcionando na Fase 2.

## Mapa Modbus inicial (caldeira)

Qualquer mestre Modbus TCP enxerga a planta como enxergaria cartões de I/O: sinais analógicos em contagens, que ele precisa escalar, no formato escolhido para a planta. Cada cenário define o próprio mapa no `scenario.json`; o mapa abaixo é o da caldeira.

**Convenções**

- **Compatibilidade:** Modbus TCP padrão, sem extensão de fabricante. Funciona com CLPs Siemens (`MB_CLIENT`), CODESYS (Modbus TCP Client; "Master" antes da V3.5 SP22), OpenPLC, SCADAs, Node-RED, Python (`pymodbus`) e, na Fase 4, o soft-PLC do site. Nas tabelas, “CLP” = qualquer mestre.
- **Endereços:** base 0 no protocolo. Ferramentas com base 0 (CODESYS, `pymodbus`) usam o endereço direto. Ferramentas com a notação clássica (Siemens `MB_CLIENT` e muitos SCADAs) usam a coluna “Notação clássica”: coil 0 = `1`, entrada discreta 0 = `10001`, input register 0 = `30001`, holding register 0 = `40001`. A IHM mostra as duas.
- **Formato analógico (escolhido por planta):** 0–27648 (padrão, igual ao cartão Siemens), 0–10000, 0–32767 ou `float32` em unidades de engenharia. Nos três primeiros, o início da faixa (4 mA) é 0 e o fim (20 mA) é o máximo. A faixa (LRV/URV) de cada transmissor vem do cenário e pode mudar via HART.
- **Fora da faixa (formato 0–27648):** acima de 20 mA chega a 32511; abaixo de 4 mA vira inteiro negativo em complemento de dois (ler como `INT`). Correntes de falha NAMUR NE43: 3,6 mA → −691; 21 mA → 29376.
- **Unit ID:** 1 por padrão, configurável por planta.
- Um comando escrito vale no início do passo seguinte (≤ 10 ms).
- **Watchdog (por planta, padrão desligado):** se o heartbeat ficar 2 s sem mudar, a planta vai ao estado seguro: FV-101 abre (falha aberta), FV-102 fecha (falha fechada) e P-101 continua como estava.

**Coils (o CLP escreve)**

| Endereço | Notação clássica | Sinal |
| --- | --- | --- |
| 0 | 1 | P-101: 1 = ligar a bomba |

**Entradas discretas (o CLP lê)**

| Endereço | Notação clássica | Sinal (1 = condição presente) |
| --- | --- | --- |
| 0 | 10001 | P-101 em operação |
| 1 | 10002 | LSLL-101: nível muito baixo |
| 2 | 10003 | LSHH-101: nível muito alto |
| 3 | 10004 | PSH-101: pressão alta |
| 4 | 10005 | PSV-101 aberta |
| 5 | 10006 | Watchdog disparado |

**Holding registers (o CLP escreve)**

| Endereço | Notação clássica | Sinal | Escala |
| --- | --- | --- | --- |
| 0 | 40001 | FV-101: comando da válvula de água | Formato analógico da planta |
| 1 | 40002 | FV-102: comando da válvula de combustível | Formato analógico da planta |
| 10 | 40011 | Heartbeat do CLP | Contador livre; basta mudar |

**Input registers (o CLP lê)**

| Endereço | Notação clássica | Sinal | Escala |
| --- | --- | --- | --- |
| 0 | 30001 | LT-101: nível do tubulão | Formato analógico da planta |
| 1 | 30002 | PT-101: pressão do vapor | Formato analógico da planta |
| 2 | 30003 | FT-101: vazão de água | Formato analógico da planta |
| 3 | 30004 | FT-102: vazão de vapor | Formato analógico da planta |
| 4 | 30005 | FV-101: posição real | Formato analógico da planta |
| 5 | 30006 | FV-102: posição real | Formato analógico da planta |
| 100–109 | 30101–30110 | Diagnóstico em unidades de engenharia: nível (m), pressão (bar), vazão de água (kg/s), vazão de vapor (kg/s), abertura de FV-103 (%) | `float32` em 2 registradores cada; ordem das palavras configurável; o professor pode desligar |

## Instrumentos HART

Os transmissores LT-101, PT-101, FT-101 e FT-102 são dispositivos HART simulados, e cada mudança de configuração tem efeito real no sinal que o CLP lê. Os posicionadores das válvulas podem virar HART depois, como extensão.

**Pontos definidos na Etapa 2 (prova de conceito com o PACTware)**

- Revisão do protocolo: **HART 7** (definido na Etapa 2). O DTM genérico "Generic H7" da FieldComm Group, que acompanha o PACTware 6.3, só abre o instrumento quando ele se declara HART 7; em HART 5 a tela fica vazia.
- Serial (definido na Etapa 2): 1200 baud, 8 bits, paridade ímpar, 1 stop bit, sem controle de fluxo e sem eco. O "Serial HART CommDTM" usa 5 preâmbulos, 3 retentativas e espera cerca de 0,44 s por resposta (estimado pelo log; a tela dele não tem campo de time-out). A resposta local chega em 15 a 50 ms com a emulação de baud do com0com desligada (recomendado) e em 200 a 330 ms com ela ligada. O uso de RTS e DTR pelo DTM não foi observado; a ponte manteve os dois ligados.
- Comandos que o Generic H7 pede além dos previstos: 7, 8, 9, 20, 48 e 50 (sem o 50, a tela não abre); ele também sonda 31, 40, 44, 45, 47, 52, 53, 54, 72, 76 e 105 e aceita "não implementado". O assistente "Aplicar valores" usa os comandos 36 e 37.
- Endereçamento: os quatro instrumentos em multidrop na mesma porta, cada um com seu endereço de polling.
- Identificação (fabricante e tipo de dispositivo): valores que façam o PACTware usar o DTM HART genérico, sem imitar marca real. Exceção (14/11/2026, a pedido do autor): na prova de conceito da Etapa 2 (`tools/hart-poc`, fora do produto), o instrumento se identifica como o transmissor de pressão Smar LD301 (fabricante `0x3E`, tipo `0x01`; HCF_SPEC-183), só para teste. A identificação dos instrumentos do produto é definida no resultado do portão.

**Comandos e efeito na simulação**

| Comando | Função | Efeito no simulador |
| --- | --- | --- |
| 0, 11, 21 | Identificação (por endereço, tag ou tag longa) | Usado pelo PACTware na busca de dispositivos |
| 1, 2, 3, 9, 33 | Ler PV, corrente, % da faixa e variáveis | Valores vindos da planta, depois do amortecimento |
| 6, 7, 59 | Endereço de polling, configuração de malha, preâmbulos | Parâmetros de comunicação do instrumento |
| 12, 13, 17, 18, 20, 22 | Mensagem, tag, descritor, data, tag longa | Aparecem na IHM e na plaqueta do instrumento no 3D |
| 14, 15 | Informações do sensor e do dispositivo | Faixa, unidade, amortecimento, função de transferência |
| 34 | Escrever amortecimento | Filtro de 1ª ordem na PV, com a constante escrita |
| 35, 36, 37 | Escrever faixa (LRV/URV) | Muda a corrente e o valor 0–27648 que o CLP lê |
| 44 | Escrever unidade da PV | Muda a unidade reportada no HART; a corrente não muda |
| 40 | Modo de corrente fixa | Força a corrente e o valor no Modbus: teste de malha |
| 45, 46 | Trim de zero e de ganho da corrente | Desvio e ganho aplicados à saída de corrente |
| 38, 48 | Limpar “configuração alterada”, status adicional | Bits de status coerentes com o que mudou |
| 41, 42 | Autoteste e reset | Respostas de status; o reset reinicia o instrumento simulado |

**Falhas simuladas (pela IHM ou pelo professor):** sensor aberto, deriva, travamento do valor e falha de eletrônica. Elas levam à corrente de falha NAMUR NE43 e aos bits de status correspondentes.

**Transportes**

- **Serial pela ponte no navegador (caminho do PACTware, conforme o portão da Etapa 2):** o PACTware usa o DTM de modem HART numa COM virtual do com0com; o par dessa porta é aberto pela página da planta com a Web Serial (Chrome e Edge), em 1200 baud, 8 bits, paridade ímpar e 1 stop bit, e os bytes vão e voltam pelo WebSocket da planta. O isolamento entre plantas vem do próprio WebSocket, sem porta extra nem pareamento.
- **HART-IP (extensão):** TCP e UDP, uma porta por sessão (faixa 16000–16999) com o mesmo pareamento do Modbus, ou a 5094 com pareamento por IP. Só entra se o portão da Etapa 2 pedir ou como extensão escolhida.
- **Configurador HART web:** uma aba no site que lista os instrumentos, lê e escreve os parâmetros e, no modo didático, mostra os quadros trocados em hexadecimal. É o caminho HART garantido, sem instalar nada, qualquer que seja o resultado da Etapa 2.

## Plantas, contas e colaboração

Qualquer pessoa pode usar o simulador: sem conta, como visitante, ou com conta, para salvar e compartilhar plantas e participar de turmas. Cada planta em execução é isolada das demais, dentro de um único processo .NET.

**Formas de uso**

| Quem | O que pode fazer |
| --- | --- |
| Visitante (sem conta) | Uma planta temporária (até 60 min), sem salvar nem compartilhar; limite por IP |
| Usuário com conta | Plantas salvas, compartilhamento, participação em turmas |
| Professor (qualquer usuário que cria uma turma) | Painel da turma, atividades, injeção de falhas e distúrbios |

**Planta salva e instância**

- A planta salva guarda dono, cenário, configurações, estado e histórico de ensaios no banco de dados.
- A instância é a planta rodando no servidor. Ela nasce quando alguém abre a planta, hiberna (gravando o estado) após 30 minutos sem ninguém conectado e, ao ser reaberta, continua de onde parou.
- Cada instância recebe uma porta Modbus (padrão 15000–15999) e, se o HART-IP for adotado, uma porta HART-IP (padrão 16000–16999). Ao reabrir, o servidor tenta devolver a mesma porta, para o mestre não precisar ser reconfigurado.

**Compartilhamento** (sem papéis, D46)

| Quem | Pode |
| --- | --- |
| Quem criou a planta | Tudo, inclusive excluir a planta e remover pessoas |
| Quem recebeu acesso | Tudo o mais: operar pela IHM, parear e retirar mestres Modbus e HART, configurar, forçar, provocar distúrbios e compartilhar |
| Professor, pelo painel da turma | Apenas ver a planta do aluno, a IHM e o monitor (observador) |

- Convite pelo e-mail de um usuário cadastrado, ou por código de 1 uso ou de vários usos, com validade (24 h, 7 dias ou até revogar), digitado em Minhas plantas ou em Turmas ou aberto como link; o acesso pode ser revogado.
- Várias pessoas operam ao mesmo tempo: vale a última escrita, e cada ação fica registrada com autor e horário. A IHM mostra quem está na planta.
- Vários mestres Modbus podem ser pareados na mesma planta; o monitor mostra quem escreveu cada registrador e avisa conflitos. Quem tem acesso à planta pode exigir um mestre por vez (D42).

**Turmas e professor**

- Qualquer usuário cria uma turma e vira professor dela; alunos entram por código ou link.
- O professor cria atividades (cenário e configuração inicial, por exemplo com uma falha), e cada aluno ou equipe recebe sua planta.
- Painel do professor: todas as plantas da turma ao vivo, com abertura como observador, injeção de falhas e distúrbios em uma ou em todas, e exportação dos ensaios. Plantas pessoais dos alunos, fora da turma, não aparecem.

**Pareamento de um mestre externo (Modbus ou HART-IP)**

1. O site mostra o IP do servidor e a porta da planta, para configurar no mestre (ou no PACTware).
2. Na primeira conexão de um IP novo, o servidor a segura e pergunta a quem está na planta: “Aceitar conexão de 200.18.4.42?”.
3. Aceita, aquele IP fica autorizado na planta; conexões de IPs não autorizados são recusadas e viram aviso na IHM.
4. Recusada ou sem resposta em 60 s, a conexão é fechada.
5. Quem tem acesso à planta pode retirar a autorização a qualquer momento.

## Configurações e diagnóstico da planta

O usuário vê e ajusta tudo o que um integrador ajustaria ao ligar um controlador a um equipamento, sem mexer no modelo físico.

| Configuração | Quem altera | Observação |
| --- | --- | --- |
| IP do servidor | Ninguém (só leitura) | É o endereço do servidor do laboratório. No futuro aplicativo desktop, o usuário escolherá o IP e a porta locais. |
| Porta Modbus e porta HART-IP | Atribuídas pelo servidor | Mostradas na tela; reaproveitadas ao reabrir a planta. |
| Unit ID | Quem tem acesso à planta | Padrão 1. |
| Formato analógico | Quem tem acesso à planta | 0–27648 (padrão), 0–10000, 0–32767 ou `float32` em unidades de engenharia. |
| Ordem das palavras do `float32` | Quem tem acesso à planta | Normal (big-endian) ou invertida (word swap). |
| Notação de endereços exibida | Cada usuário | Base 0 ou clássica (1, 10001, 30001, 40001). |
| Watchdog | Quem tem acesso à planta | Ligado ou desligado, e tempo limite. |
| Mestres permitidos | Quem tem acesso à planta | Um por vez ou vários ao mesmo tempo. |
| Bloco de diagnóstico em unidades de engenharia | Quem tem acesso à planta, ou o professor | Pode ser desligado em avaliações. |
| Faixa, amortecimento e unidade dos instrumentos | Via HART | Pelo configurador do site ou pelo PACTware. |
| Distúrbios e falhas | Quem tem acesso à planta, e o professor | Pela IHM ou pelo painel do professor. |

**Diagnóstico**

- Monitor de registradores ao vivo: valor bruto, valor de engenharia, unidade, autor da última escrita (IP ou usuário) e horário.
- Log de comunicação: conexões, pareamentos, requisições por segundo e exceções Modbus.
- Forçar valores de entradas, por exemplo para simular falha de sensor, com destaque visível de “forçado”.
- Mapa de I/O exportável (CSV e PDF), pronto para configurar o mestre.
- Pausar, retomar e reiniciar a planta, com aviso de que o mestre externo continua rodando.

## Abertura pública e segurança

O site é aberto a qualquer pessoa, dentro e fora da universidade; os limites existem para proteger o servidor modesto do laboratório e os dados de quem usa.

- **Contas:** ASP.NET Core Identity; senhas guardadas só como hash; verificação de e-mail; login social (Google, Microsoft, GitHub); proteção contra robôs no cadastro.
- **Limites configuráveis:** plantas em execução por conta e por IP, total de instâncias no servidor (com fila e aviso quando lotar), requisições por IP e bloqueio temporário após abuso.
- **Rede:** da internet, só ficam acessíveis a porta 443 (site e WebSocket) e as faixas de portas de planta. Todo o resto fica fechado no firewall.
- **Protocolos industriais:** Modbus TCP e HART-IP não têm senha nem criptografia; a proteção é o pareamento por IP. Isso aparece nos termos de uso e na própria interface.
- **LGPD:** dados pessoais mínimos (e-mail e nome de exibição); termos de uso e política de privacidade; exportação e exclusão da conta; logs com retenção curta.
- **Capacidade:** o limite de plantas e clientes do PC do laboratório é medido na Etapa 9A (teste de carga crescente); se a demanda passar dele, a mesma VM Linux migra para um servidor da universidade ou para a nuvem. A banda de upload do laboratório (cerca de 33 kbit/s por IHM aberta, D40) é combinada com a TI.
- **TI da universidade:** domínio, certificado HTTPS, IP público e liberação das portas. Sem isso, o uso fica restrito à rede da universidade até a migração para uma VM.

## Convenções de desenvolvimento e testes

Nenhuma etapa está concluída sem testes automáticos passando no CI e sem a evidência de validação registrada no repositório.

**Idioma**

- Nomes no código (classes, métodos, variáveis) em inglês, como no protótipo.
- Comentários, documentação e mensagens de commit em português.
- Interface: nenhum texto fixo no código. Todo texto vem dos arquivos de idioma (`web/src/locales`, i18next; recursos .NET para mensagens do servidor). Português é o idioma de referência; inglês e espanhol são gerados com IA e revisados.
- O idioma segue o navegador e pode ser trocado pelo usuário. Números e datas são formatados pelo idioma (vírgula ou ponto decimal); unidades de engenharia não mudam.
- Termos técnicos consagrados (coil, holding register, HART, PV, LRV/URV) não são traduzidos.

**Repositório e fluxo**

- Um repositório novo, em formato monorepo (estrutura na seção de arquitetura). O protótipo Unity fica arquivado como referência.
- Branch `main` protegida; uma branch por etapa (`etapa-03-cenarios`); integração por pull request com o checklist de validação da etapa.
- Merge só com CI verde (GitHub Actions: build, testes .NET, testes web).
- Evidências de cada etapa em `docs/validacao/etapa-NN.md`: comandos executados, saídas, números medidos e capturas de tela.

**Backend (.NET)**

- .NET 10, `Nullable` habilitado, `TreatWarningsAsErrors`, analisadores padrão.
- Testes com xUnit. Categorias: unitário, validação numérica (contra solução analítica ou referência), integração (Modbus em loopback, quadros HART) e desempenho.
- O núcleo de simulação não faz I/O, não lê relógio (`DateTime.Now`) e não usa aleatório sem semente.
- Física sempre em `double` e em unidades SI. Quando a unidade não for óbvia, ela vai no nome (`levelM`, `pressurePa`, `flowKgPerS`).

**Frontend (TypeScript)**

- TypeScript em modo `strict`, ESLint e Prettier.
- Testes unitários com Vitest; testes de ponta a ponta com Playwright.
- O cliente nunca calcula a planta: só interpola estados recebidos e faz efeitos visuais.
- Cores, fontes, espaçamentos e raios só por tokens (variáveis CSS), desde a Etapa 0. Até a trilha de design, as telas são funcionais e simples; depois, usam os componentes da Etapa D2.
- IHM no estilo ISA-101: fundo neutro, cor reservada para alarmes e estados anormais.

**Versões e dependências**

- Protocolo WebSocket com número de versão na mensagem inicial; esquema do `scenario.json` também versionado.
- Dependências mínimas e com licença permissiva (MIT, Apache-2.0, BSD). Conferir a licença antes de adicionar qualquer pacote; GPL só com decisão registrada.
- Versões exatas fixadas (`packages.lock.json`, `package-lock.json`).

## Instruções para o Claude em cada chat do projeto

Cada chat trabalha em uma etapa do Plano de Desenvolvimento por vez, respeita as decisões deste descritivo e termina mostrando como cada critério de validação foi verificado.

**Ao começar**

1. Ler este descritivo e o Plano de Desenvolvimento.
2. Identificar a etapa atual pela linha “Estado atual” ou perguntar ao usuário. Não adiantar trabalho de etapas futuras.
3. Sem acesso ao repositório, pedir os arquivos que a etapa precisa em vez de supor o conteúdo deles.

**Durante a etapa**

- Seguir as decisões D01 a D51. Se algo pedir para contrariá-las, apontar o conflito e propor a mudança explicitamente; nunca mudar em silêncio.
- Entregar arquivos completos, com o caminho no repositório. Nada de trechos com “...” ou “resto igual”.
- Se a etapa não couber numa resposta, anunciar no início a divisão em partes (A, B, C…). Cada parte termina compilando e com seus testes passando.
- Entregar os testes junto com o código, nunca depois.
- Não inventar APIs de bibliotecas. Na dúvida, dizer a versão assumida e pedir para conferir na documentação.
- Para equações e parâmetros físicos, citar a fonte.
- Responder em português; código com nomes em inglês.

**Nunca**

- Colocar cálculo da planta no cliente web, texto de interface fora dos arquivos de idioma, ou cores e fontes fora dos tokens de design. Copiar código do protótipo Unity (ele só serve de referência).
- Abrir portas de rede além das definidas em Abertura pública e segurança.
- Adicionar dependência com licença GPL ou comercial sem decisão registrada.
- Marcar uma etapa como concluída com critério de validação não verificado.

**Ao terminar**

- Listar cada critério de validação da etapa, como foi verificado e o resultado.
- Gerar o conteúdo de `docs/validacao/etapa-NN.md`.
- Sugerir a nova linha de “Estado atual” e qualquer decisão nova para este descritivo.

## Riscos conhecidos

Os dois maiores riscos são externos ao código: o PACTware funcionar pela ponte serial e a TI liberar o acesso de fora. Por isso eles são testados cedo (Etapas 2 e 9).

| Risco | Impacto | Mitigação | Etapa |
| --- | --- | --- | --- |
| PACTware não funcionar pela ponte serial (driver com0com, Web Serial ou tempo de resposta) | Alto | Portão de decisão na Etapa 2 com três resultados já planejados; HART-IP como alternativa; configurador HART no site garante o uso em qualquer caso | 2, 17, 18 |
| TI não liberar IP público, portas ou certificado | Alto | Conversar com a TI antes da Etapa 9B (já, porque costuma demorar); plano B: a mesma VM num servidor da universidade ou na nuvem | 9B, 13 |
| Abuso ou ataque com o site aberto ao público | Alto | Limites por conta e por IP, pareamento, varredura de segurança, monitoramento e alertas | 13 |
| Escopo grande para uma iniciação científica | Alto | Marcos usáveis ao fim de cada fase; Fases 2 e 3 podem trocar de ordem; Fase 4 só no fim | Todas |
| Demanda maior que o PC do laboratório | Médio | Hibernação de plantas, fila de espera, migração para VM | 10, 13 |
| Usuário sem mestre Modbus disponível (o PLCSIM comum não faz Modbus TCP) | Médio | Guias para opções gratuitas (CODESYS, OpenPLC, Python); soft-PLC do site na Fase 4 | 6, 21 |
| Dados pessoais e LGPD | Médio | Dados mínimos, termos e política de privacidade, exportação e exclusão de conta | 10, 13 |
| Redes que bloqueiam portas diferentes da 443 | Médio | Testar de redes externas; documentar; portas configuráveis | 9, 13 |
| Desempenho de VR no Quest, que tem processador de celular | Médio | Orçamento de polígonos, níveis de detalhe e texturas KTX2; medir no aparelho | 14, 19, 20 |
| Modelo da caldeira instável com dt = 10 ms | Médio | Verificação cruzada com integrador de passo variável; subpassos se necessário | 15 |
| PC do laboratório desligado ou indisponível | Médio | VM com início automático junto com o Windows; serviços systemd com reinício em falha; backup diário do banco; configuração versionada | 9A, 9B |
| Traduções imprecisas | Baixo | Revisão humana; termos técnicos mantidos; verificação de chaves no CI | 13 |

## Glossário

| Termo | Significado neste projeto |
| --- | --- |
| Mestre Modbus | Qualquer controlador ou programa que lê e escreve os registradores da planta: CLP real, PLCSIM Advanced, CODESYS, OpenPLC, SCADA, script ou o soft-PLC do site. |
| CLP | Controlador lógico programável. Neste documento, usado também como sinônimo de mestre Modbus. |
| SCADA / IHM | Software de supervisão (telas, alarmes, histórico) / interface homem-máquina. O site tem uma IHM própria. |
| TIA Portal | Software de engenharia da Siemens usado para programar CLPs S7-1200/1500. |
| PLCSIM / PLCSIM Advanced | Simuladores de CLP da Siemens. Só o Advanced (S7-1500, licença paga) faz Modbus TCP com a rede. |
| CODESYS | Ambiente de programação IEC 61131-3 de vários fabricantes, com softPLC e Modbus TCP Master. |
| OpenPLC | CLP de código aberto, com editor e runtime, que pode ser mestre Modbus. |
| Modbus TCP | Protocolo industrial cliente-servidor. A planta é o servidor; o mestre é o cliente. |
| Coil / entrada discreta / holding register / input register | Áreas do Modbus: saída digital, entrada digital, saída analógica e entrada analógica, do ponto de vista do mestre. |
| Notação clássica | Endereços Modbus com prefixo de área (1, 10001, 30001, 40001), usados pela Siemens e por muitos SCADAs. |
| `MB_CLIENT` | Bloco da Siemens que faz o CLP agir como cliente Modbus TCP. |
| HART | Protocolo digital sobreposto ao sinal de 4–20 mA, usado para configurar e diagnosticar instrumentos. |
| HART-IP | HART transportado por TCP/UDP, na porta padrão 5094. |
| com0com | Driver gratuito para Windows que cria pares de portas COM virtuais ligadas entre si: o que entra numa sai na outra. |
| Web Serial | API do Chrome e do Edge que deixa uma página abrir uma porta serial do PC, com permissão do usuário. |
| PACTware / FDT / DTM | PACTware é um aplicativo FDT gratuito; DTMs são os “drivers” que ele usa, de comunicação (modem, HART-IP) e de dispositivo. |
| PV, LRV, URV | Variável primária do instrumento; início e fim da faixa que corresponde a 4 e 20 mA. |
| Amortecimento | Constante de tempo do filtro que o instrumento aplica à medição. |
| NAMUR NE43 | Recomendação que define correntes de falha (≤ 3,6 mA ou ≥ 21 mA). |
| Tubulão | Vaso da caldeira onde água e vapor se separam; o nível é medido nele. |
| Swell / shrink | Resposta inversa do nível do tubulão: ao aumentar a demanda de vapor, o nível sobe antes de cair. |
| Controle a três elementos | Controle de nível que usa nível, vazão de água e vazão de vapor. |
| IAPWS-IF97 | Formulação industrial padrão das propriedades da água e do vapor. |
| RK4 | Método de Runge-Kutta de 4ª ordem, usado para integrar as equações da planta. |
| glTF / .glb | Formato aberto de modelos 3D para a web. |
| WebXR / WebGPU | APIs do navegador para realidade virtual e para gráficos modernos; ambas exigem HTTPS. |
| Soft-PLC | CLP em software; aqui, um runtime no servidor que executa Ladder e FBD. |
| PLCopen XML | Formato padrão para trocar programas IEC 61131-3 entre ferramentas. |
| Planta salva / instância | A planta guardada no banco (dono, cenário, configurações, estado) / a mesma planta rodando no servidor. |
| Visitante | Quem usa sem conta: uma planta temporária, sem salvar nem compartilhar. |
| Turma | Grupo criado por um professor; ele acompanha as plantas das atividades da turma. |
| Pareamento | Autorização, no navegador, dos IPs que podem conectar às portas Modbus e HART-IP da planta. |
| Portão de decisão | Ponto do plano em que um resultado medido decide qual caminho seguir (ex.: caminho do PACTware na Etapa 2). |
| i18n | Internacionalização: textos da interface separados do código, em arquivos por idioma. |

## Referências

**Bibliografia técnica**

- Åström, K. J.; Bell, R. D. *Drum-boiler dynamics*. Automatica, v. 36, n. 3, p. 363–378, 2000.
- IAPWS. *Revised Release on the IAPWS Industrial Formulation 1997 for the Thermodynamic Properties of Water and Steam* (IAPWS-IF97), inclui as tabelas de verificação.
- Especificações HART e HART-IP, FieldComm Group.
- IEC 61131-3 (linguagens de CLP) e especificação PLCopen XML (TC6).

**Fontes consultadas nas decisões (novembro de 2026)**

- [Protótipo: Saulojo/IC\_Proccess\_Simulation](https://github.com/Saulojo/IC_Proccess_Simulation)
- [Unity 6.5 — suporte a APIs .NET na plataforma Web](https://docs.unity.com/en-us/engine/6000.5/manual/platform-specific/webgl/develop/web-dotnet-api-support)
- [Unity Discussions — HDRP não disponível em WebGPU](https://discussions.unity.com/t/unity-6-hdrp-on-webgpu/1540460)
- [Unity 6.6 — WebGPU sai do experimental](https://app.cinevva.com/news/2026-09-01-unity-6-6-webgpu-production)
- [De-Panther/unity-webxr-export](https://github.com/De-Panther/unity-webxr-export)
- [Babylon.js 8.0: glTF, USDz e WebXR](https://blogs.windows.com/windowsdeveloper/2025/04/03/part-3-babylon-js-8-0-gltf-usdz-and-webxr-advancements/)
- [Godot — status da exportação C# para web](https://forum.godotengine.org/t/is-there-an-update-on-exporting-c-projects-to-web/128821)
- [Chrome — limitação de timers em abas ocultas](https://developer.chrome.com/blog/timer-throttling-in-chrome-88/)
- [Meta Horizon — PWAs na Quest Store](https://developers.meta.com/horizon/documentation/web/pwa-overview/)
- [Babylon.js Forum — status do React Native](https://forum.babylonjs.com/t/react-native-babylonjs-what-is-the-current-status/58315)
- [PLCSIM: comunicação S7 vs. comunicação aberta](https://industrialmonitordirect.com/blogs/knowledgebase/plcsim-communication-blocks-s7-connection-vs-open-communication-support)
- [Factory I/O — driver S7-PLCSIM](https://docs.factoryio.com/manual/drivers/s7-plcsim/)
- [FieldComm Group no GitHub (kit HART-IP, cliente C#)](https://github.com/FieldCommGroup)
- [Pepperl+Fuchs — DTM HART Comm](https://files.pepperl-fuchs.com/webcat/navi/productInfo/pds/t34035_eng.pdf)
- [Softing smartLink SW-HT (HART-IP com DTM)](https://industrial.softing.com/us/products/docker/smartlink-sw-ht.html)
