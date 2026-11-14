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

**Estado atual do novo projeto:** Etapa 1 concluída em 14/11/2026; próxima: Etapa 2 (prova de conceito HART-IP) ou Etapa 3 (motor de cenários). Atualize esta linha ao concluir cada etapa do Plano de Desenvolvimento (ex.: “Etapa 3 concluída em dd/mm/aaaa; Etapa 4 em andamento”).

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
| RF09 | Compartilhar uma planta com papéis (dono, operador, observador); várias pessoas e vários mestres operando a mesma planta. | Fase 2 (Plataforma) |
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
| RNF07 | Hardware modesto | Roda no PC do laboratório em Linux; meta: 4 GB de RAM e CPU de 4 núcleos (2015 em diante). A mesma imagem Docker roda numa VM se a demanda crescer. |
| RNF08 | Testabilidade | Núcleo sem dependência de rede ou interface; testes automatizados em Core, IO, Modbus e HART; CI verde para integrar. |
| RNF09 | Evolução segura | Protocolo cliente-servidor versionado; cenários validados por um esquema JSON; migrações de banco versionadas. |
| RNF10 | Privacidade (LGPD) | Dados pessoais mínimos; termos de uso e política de privacidade publicados; exportação e exclusão de conta funcionando. |
| RNF11 | Multilíngue | Nenhum texto de interface fora dos arquivos de idioma, e nenhuma chave de tradução faltando (verificado no CI). |
| RNF12 | Usabilidade e identidade visual | Interface projetada a partir de protótipo testado com usuários (trilha de design); contraste WCAG 2.2 AA nos temas claro e escuro; IHM segue a ISA-101 (cor reservada para estados anormais). |

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
| D09 | HART: núcleo de protocolo próprio, independente do transporte; configurador HART no site sempre disponível; HART-IP para o PACTware conforme o portão de decisão da Etapa 2 (resultados A, B ou C). | Garante HART sem instalação em qualquer caso e decide o caminho do PACTware com dados, não com suposição. | Serial como caminho principal (exige driver de COM virtual no PC do usuário). |
| D10 | Hospedagem no PC do laboratório: Linux, Docker, proxy reverso com HTTPS automático (Caddy); arquivos estáticos em CDN gratuita se a banda limitar; mesma imagem migrável para uma VM. | WebXR e WebGPU exigem HTTPS; temporização estável; implantação reproduzível. | Hospedagem serverless (não mantém processo nem abre porta TCP). |
| D11 | Soft-PLC (Fase 4) roda em .NET no mesmo laço da planta, lendo a imagem de I/O direto; projetos em PLCopen XML. | Determinismo total na malha fechada; intercâmbio com CODESYS, OpenPLC e Beremiz. | CLP rodando no navegador. |
| D12 | Modelos 3D: Blender → glTF binário (.glb) com compressão (Draco ou Meshopt, texturas KTX2); ligação objeto ↔ variável por convenção de nomes. | Carregamento rápido no navegador; serve a qualquer motor. | FBX e formatos proprietários. |
| D13 | Contas com ASP.NET Core Identity e PostgreSQL (EF Core): e-mail com verificação e login social (Google, Microsoft, GitHub); modo visitante sem conta, com limites. | Componente maduro, gratuito e no mesmo processo; visitante mantém o “só abrir o site”. | Provedor de identidade separado (ex.: Keycloak), pesado para o PC do laboratório. |
| D14 | Planta salva (dono, cenário, configurações, estado) separada da instância em execução; instância hiberna sem ninguém conectado e retoma do mesmo estado. | Muitos usuários cadastrados com poucas plantas rodando ao mesmo tempo; o servidor modesto aguenta. | Plantas sempre rodando. |
| D15 | Colaboração com papéis (dono, operador, observador); vale a última escrita; toda ação registrada com autor e horário. | Simples, previsível e auditável, como numa sala de controle com vários operadores. | Bloqueio de edição por usuário. |
| D16 | Idiomas: textos em arquivos de recurso (i18next no frontend, recursos .NET no backend); idioma do navegador; pt-BR, en e es; traduções iniciais geradas com IA e revisadas; termos técnicos (coil, holding register, HART) mantidos. | Tradução de qualidade e sem custo em tempo de uso; adicionar idioma = adicionar um arquivo. | Tradução automática em tempo real (custo, latência, erros em termos técnicos). |
| D17 | Plataforma aberta ao público, com limites por conta e por IP, fila quando o servidor lotar, termos de uso e política de privacidade (LGPD). | Cumpre o objetivo de abrir o software a qualquer pessoa sem derrubar o servidor do laboratório. | Acesso restrito à universidade. |
| D18 | Código escrito do zero; do protótipo, só os modelos 3D são aproveitados. | A arquitetura é outra (servidor autoritativo, web); código novo nasce testado e coerente com as decisões deste documento. | Portar código do protótipo Unity. |
| D19 | Design numa trilha própria (D1: prototipagem e teste com usuários; D2: sistema de design), entre a Etapa 7 e a Etapa 10. Até lá, telas funcionais simples, já usando tokens (variáveis CSS). IHM segue a ISA-101. | Decisões de interface baseadas em teste com usuários; o redesenho vira troca de tokens e componentes, sem refazer telas; as telas da Fase 2 já nascem no design final. | Desenhar cada tela na etapa que a cria; design só no fim do projeto. |
| D20 | (11/11/2026, Etapa 0) Testes .NET com xUnit v3 (4.x) sobre a Microsoft Testing Platform, ativada no `global.json` (`"test": {"runner": "Microsoft.Testing.Platform"}`), que também fixa o SDK 10 (`rollForward: latestFeature`). | É o caminho padrão do xUnit 4 com o SDK 10; dispensa `Microsoft.NET.Test.Sdk` e `xunit.runner.visualstudio`; local e CI usam o mesmo SDK, então os avisos tratados como erro são os mesmos nos dois. | VSTest (o xUnit 4 o trata como legado no SDK 10); deixar o SDK livre (o SDK 11 local daria avisos diferentes dos do CI). |
| D21 | (11/11/2026, Etapa 0) TypeScript fixado em 6.0.x até o typescript-eslint aceitar o TypeScript 7; stylelint com `stylelint-declaration-strict-value` obriga tokens (`var(--…)`) em cor, fonte, espaçamento e raio. | O typescript-eslint 8.72 exige TypeScript abaixo de 6.1; a regra de tokens passa a ser verificada no lint, e não só combinada. | TypeScript 7 sem lint com tipos; tokens só por convenção. |
| D22 | (14/11/2026, Etapa 1) Gerador pseudoaleatório próprio para o ruído: xoshiro256\*\* com a semente expandida por SplitMix64 (`IndustrialSim.Core.Numerics.SeededRandom`), com teste contra vetores de referência. | A sequência do `System.Random` com semente não é garantida entre versões do .NET, e a sequência do ruído faz parte do determinismo (RNF02). | `System.Random` com semente. |

## Arquitetura e estrutura do repositório

Um único processo .NET no servidor do laboratório mantém todas as plantas; navegador, CLP e PACTware do usuário apenas se conectam a ele.

```text
PC do usuário                                Servidor do laboratório (.NET 10, Docker)
┌─────────────────────────────┐              ┌──────────────────────────────────────────┐
│ Navegador                   │ HTTPS :443   │ Proxy HTTPS (Caddy)                      │
│  3D e VR (Babylon.js)       │─────────────►│        ▼                                 │
│  IHM, turmas e editor       │              │ ASP.NET Core e WebSocket ──► PostgreSQL  │
│  Chrome, Edge ou Quest      │              │        ▼                                 │
│                             │              │ Planta em execução (uma por planta aberta)│
│ Mestre Modbus               │ Modbus TCP   │  ├ Imagem de I/O    ├ Planta (10 ms)     │
│  CLP, CODESYS, SCADA, script│─────────────►│  ├ Modbus :15xxx    ├ Soft-PLC (Fase 4)  │
│ PACTware                    │ HART-IP      │  └ HART-IP :16xxx   └ Gravação CSV       │
│  DTM HART-IP (a validar)    │─────────────►│                                          │
└─────────────────────────────┘              └──────────────────────────────────────────┘
```

Dentro de cada sessão, planta, Modbus, HART-IP e soft-PLC leem e escrevem a mesma imagem de I/O; o navegador recebe o estado da planta pelo WebSocket.

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
├─ deploy/        (Dockerfile, docker-compose.yml com PostgreSQL, Caddyfile)
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

- **Compatibilidade:** Modbus TCP padrão, sem extensão de fabricante. Funciona com CLPs Siemens (`MB_CLIENT`), CODESYS (Modbus TCP Master), OpenPLC, SCADAs, Node-RED, Python (`pymodbus`) e, na Fase 4, o soft-PLC do site. Nas tabelas, “CLP” = qualquer mestre.
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

- Revisão do protocolo: HART 7 por padrão; HART 5 se o DTM genérico exigir.
- Topologia no HART-IP: um gateway com subdispositivos (comandos 74, 84 e 77) ou um dispositivo por conexão, conforme o que o DTM suportar.
- Identificação (fabricante e tipo de dispositivo): valores que façam o PACTware usar o DTM HART genérico, sem imitar marca real.

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

- **HART-IP (conforme o portão de decisão da Etapa 2):** TCP e UDP. Uma porta por sessão (faixa 16000–16099), com o mesmo pareamento do Modbus. Se o DTM só aceitar a porta padrão 5094, o isolamento fica só pelo pareamento por IP.
- **Serial (opcional, fora do fluxo principal):** 1200 baud, 8 bits, paridade ímpar, 1 stop bit, via um conector local instalável para quem precisar.
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
- Cada instância recebe uma porta Modbus (padrão 15000–15999) e uma porta HART-IP (padrão 16000–16999). Ao reabrir, o servidor tenta devolver a mesma porta, para o mestre não precisar ser reconfigurado.

**Compartilhamento**

| Papel | Pode |
| --- | --- |
| Dono | Tudo: configurar, compartilhar, excluir |
| Operador | Operar pela IHM, parear mestres Modbus e HART, provocar distúrbios |
| Observador | Apenas ver a planta, a IHM e o monitor |

- Convite por link (com papel e validade) ou pelo e-mail de um usuário cadastrado; o acesso pode ser revogado.
- Várias pessoas operam ao mesmo tempo: vale a última escrita, e cada ação fica registrada com autor e horário. A IHM mostra quem está na planta.
- Vários mestres Modbus podem ser pareados na mesma planta; o monitor mostra quem escreveu cada registrador e avisa conflitos. O dono pode exigir um mestre por vez.

**Turmas e professor**

- Qualquer usuário cria uma turma e vira professor dela; alunos entram por código ou link.
- O professor cria atividades (cenário e configuração inicial, por exemplo com uma falha), e cada aluno ou equipe recebe sua planta.
- Painel do professor: todas as plantas da turma ao vivo, com abertura como observador, injeção de falhas e distúrbios em uma ou em todas, e exportação dos ensaios. Plantas pessoais dos alunos, fora da turma, não aparecem.

**Pareamento de um mestre externo (Modbus ou HART-IP)**

1. O site mostra o IP do servidor e a porta da planta, para configurar no mestre (ou no PACTware).
2. Na primeira conexão de um IP novo, o servidor a segura e pergunta ao dono e aos operadores: “Aceitar conexão de 200.18.4.42?”.
3. Aceita, aquele IP fica autorizado na planta; conexões de IPs não autorizados são recusadas e viram aviso na IHM.
4. Recusada ou sem resposta em 60 s, a conexão é fechada.
5. Dono e operadores podem retirar a autorização a qualquer momento.

## Configurações e diagnóstico da planta

O usuário vê e ajusta tudo o que um integrador ajustaria ao ligar um controlador a um equipamento, sem mexer no modelo físico.

| Configuração | Quem altera | Observação |
| --- | --- | --- |
| IP do servidor | Ninguém (só leitura) | É o endereço do servidor do laboratório. No futuro aplicativo desktop, o usuário escolherá o IP e a porta locais. |
| Porta Modbus e porta HART-IP | Atribuídas pelo servidor | Mostradas na tela; reaproveitadas ao reabrir a planta. |
| Unit ID | Dono | Padrão 1. |
| Formato analógico | Dono | 0–27648 (padrão), 0–10000, 0–32767 ou `float32` em unidades de engenharia. |
| Ordem das palavras do `float32` | Dono | Normal (big-endian) ou invertida (word swap). |
| Notação de endereços exibida | Cada usuário | Base 0 ou clássica (1, 10001, 30001, 40001). |
| Watchdog | Dono | Ligado ou desligado, e tempo limite. |
| Mestres permitidos | Dono | Um por vez ou vários ao mesmo tempo. |
| Bloco de diagnóstico em unidades de engenharia | Dono ou professor | Pode ser desligado em avaliações. |
| Faixa, amortecimento e unidade dos instrumentos | Via HART | Pelo configurador do site ou pelo PACTware. |
| Distúrbios e falhas | Operador e professor | Pela IHM ou pelo painel do professor. |

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
- **Capacidade:** se a demanda passar do que o PC do laboratório aguenta, a mesma imagem Docker roda numa VM (servidor da universidade ou nuvem).
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

- Seguir as decisões D01 a D22. Se algo pedir para contrariá-las, apontar o conflito e propor a mudança explicitamente; nunca mudar em silêncio.
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

Os dois maiores riscos são externos ao código: o PACTware aceitar HART-IP e a TI liberar o acesso de fora. Por isso eles são testados cedo (Etapas 2 e 9).

| Risco | Impacto | Mitigação | Etapa |
| --- | --- | --- | --- |
| PACTware não conectar via HART-IP sem instalar nada | Alto | Portão de decisão na Etapa 2 com três resultados já planejados; configurador HART no site garante o uso em qualquer caso | 2, 17, 18 |
| TI não liberar IP público, portas ou certificado | Alto | Conversar com a TI antes da Etapa 9; plano B: a mesma imagem Docker numa VM | 9, 13 |
| Abuso ou ataque com o site aberto ao público | Alto | Limites por conta e por IP, pareamento, varredura de segurança, monitoramento e alertas | 13 |
| Escopo grande para uma iniciação científica | Alto | Marcos usáveis ao fim de cada fase; Fases 2 e 3 podem trocar de ordem; Fase 4 só no fim | Todas |
| Demanda maior que o PC do laboratório | Médio | Hibernação de plantas, fila de espera, migração para VM | 10, 13 |
| Usuário sem mestre Modbus disponível (o PLCSIM comum não faz Modbus TCP) | Médio | Guias para opções gratuitas (CODESYS, OpenPLC, Python); soft-PLC do site na Fase 4 | 6, 21 |
| Dados pessoais e LGPD | Médio | Dados mínimos, termos e política de privacidade, exportação e exclusão de conta | 10, 13 |
| Redes que bloqueiam portas diferentes da 443 | Médio | Testar de redes externas; documentar; portas configuráveis | 9, 13 |
| Desempenho de VR no Quest, que tem processador de celular | Médio | Orçamento de polígonos, níveis de detalhe e texturas KTX2; medir no aparelho | 14, 19, 20 |
| Modelo da caldeira instável com dt = 10 ms | Médio | Verificação cruzada com integrador de passo variável; subpassos se necessário | 15 |
| PC do laboratório desligado ou indisponível | Médio | Serviço com reinício automático em Docker; backup diário do banco; configuração versionada | 9 |
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
| Portão de decisão | Ponto do plano em que um resultado medido decide qual caminho seguir (ex.: HART-IP na Etapa 2). |
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
