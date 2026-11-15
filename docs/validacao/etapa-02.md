# Validação da Etapa 2 — Prova de conceito HART serial

Testes feitos em 14 e 15/11/2026. Resultado do portão de decisão: **A provisório** (ver o fim do arquivo).

## Critérios da etapa

| # | Critério | Como foi verificado | Resultado |
| --- | --- | --- | --- |
| 1 | com0com instalado e funcionando, com versão, assinatura e Secure Boot registrados; par COM10/COM11 | `Get-PnpDevice`, assinatura do catálogo, registro do Windows, teste direto do par | Atendido com a versão 2.2.2.0 (a 3.0.0.0 reprova) |
| 2 | A página de ponte abre a COM11 e repassa bytes nos dois sentidos | `hart-serial echo` na COM10 com o servidor em eco, em Chrome e Edge, com e sem emulação de baud | Atendido |
| 3 | O PACTware encontra o instrumento e lê PV, corrente e faixa | Capturas do PACTware (identificação, PV, faixa) e log do servidor (comandos 0, 2, 3, 9 e 15) | Atendido; a corrente foi conferida pelo log, sem captura de tela |
| 4 | O PACTware escreve faixa e amortecimento, com efeito no log e na leitura seguinte | Log do servidor (comandos 35, 34 e 15) e `/api/status` | Atendido |
| 5 | Tempo de resposta com o servidor local e em outro PC, comparado ao time-out do DTM | `hart-serial measure` na COM10 | **Parcial:** só a parte local; a parte com outro PC fica para a Etapa 9 |
| 6 | 30 minutos conectado sem queda nem erro no PACTware | Log do servidor e confirmação do usuário | Atendido (33 minutos) |
| 7 | Resultado do portão registrado na D09, com revisão HART e parâmetros da serial | `docs/descritivo.md` | Registrado como A provisório |

## Ambiente do teste

| Item | Valor |
| --- | --- |
| Sistema | Windows 11 Pro, versão 10.0.26300 |
| Secure Boot | Ligado (`HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled = 1`) |
| .NET SDK | 10.0.404 |
| Navegadores | Google Chrome 157.0.8103.79 e Microsoft Edge |
| PACTware | 6.3 (6.3.00029) |
| DTMs | Serial HART CommDTM 1.0.0 (FieldComm Group); CodeWrights iDTM-FDI PACTware 1.8.295 |

## Partes A e B — código da prova (14/11/2026)

Comandos, na pasta `tools/hart-poc`:

```text
dotnet restore HartPoc.sln --locked-mode
dotnet build HartPoc.sln --no-restore --configuration Release -warnaserror   → Compilação com êxito
dotnet test --no-build --configuration Release                               → total: 34, falhou: 0
```

Com o servidor em `http://localhost:5210`, um comando 0 mandado pelo WebSocket `/ws/hart` (log do servidor):

```text
RX FFFFFFFFFF0280000082
   pedido: comando 0, endereço 80, dados , 5 preâmbulos, verificação ok -> código de resposta 0
TX FFFFFFFFFF0680000E0020FE3E010505030108000E2E0241
```

A resposta traz `FE 3E 01`: fabricante `0x3E` (Smar) e tipo `0x01` (LD301), HART 5.

## Critério 1 / S1 — com0com com Secure Boot

### Tentativa 1: com0com 3.0.0.0 "signed" — reprovada (14/11/2026)

O instalador roda, mas o Windows recusa o driver e nenhuma porta COM é criada.

```text
Get-PnpDevice -InstanceId 'ROOT\COM0COM\*'
ROOT\COM0COM\0000: CM_PROB_UNSIGNED_DRIVER (código 52), driver 3.0.0.0, oem55.inf
ROOT\COM0COM\0001: CM_PROB_UNSIGNED_DRIVER (código 52), driver 3.0.0.0, oem55.inf

[System.IO.Ports.SerialPort]::GetPortNames()
COM1
```

Assinatura de `C:\Windows\System32\drivers\com0com.sys` (`Get-AuthenticodeSignature`):

| Campo | Valor |
| --- | --- |
| Estado da assinatura Authenticode | Válida |
| Assinante | CyberCircuits (Nova Zelândia) |
| Certificado | Emitido pela COMODO RSA Code Signing CA, válido de 03/01/2016 a 26/01/2018 |
| Carimbo de tempo | COMODO SHA-1 Time Stamping Signer |

Leitura do resultado (interpretação, a confirmar com a tentativa 2): a assinatura é válida como Authenticode, mas é
de um certificado emitido em 2016, sem assinatura da Microsoft. Com Secure Boot ligado, o Windows 10 (1607 em diante)
e o Windows 11 só carregam drivers de kernel assinados pela Microsoft ou assinados com certificado emitido antes de
29/07/2015. Há relatos iguais na lista do projeto e em fóruns, que indicam a versão 2.2.2.0 assinada como alternativa.

### Tentativa 2: com0com 2.2.2.0 assinada — funcionou (14/11/2026)

```text
Get-PnpDevice (classe CNCPorts)
OK  CM_PROB_NONE  com0com - serial port emulator                                   COM0COM\PORT\CNCA0
OK  CM_PROB_NONE  com0com - serial port emulator                                   COM0COM\PORT\CNCB0
OK  CM_PROB_NONE  com0com - bus for serial port pair emulator 0 (COM10 <-> COM11)  ROOT\CNCPORTS\0000

[System.IO.Ports.SerialPort]::GetPortNames()
COM1  COM10  COM11
```

| Item | Valor |
| --- | --- |
| Versão do driver | 2.2.2.0 (data do driver: 24/01/2011) |
| Assinatura | Pelo catálogo `com0com.cat`: válida, de Steven William Hatchett, certificado de 22/09/2010 a 23/09/2011, com carimbo de tempo. O `com0com.sys` não tem assinatura embutida. |
| Secure Boot | Ligado (`UEFISecureBootEnabled = 1`) |
| Modo de teste de drivers | Desligado (`SystemStartOptions` sem `TESTSIGNING`) |
| Integridade de memória (HVCI) | Ligada (`HypervisorEnforcedCodeIntegrity\Enabled = 1`) |
| Parâmetros do par | `CNCA0: PortName=COM10`, `CNCB0: PortName=COM11`, sem `EmuBR` (emulação de baud desligada) |

O certificado é anterior a 29/07/2015, o que é coerente com a leitura da tentativa 1.

Teste direto do par, sem navegador (as duas pontas em 1200 baud, 8 bits, paridade ímpar, 1 stop bit):

```text
COM11 vê: CTS=True DSR=True DCD=True      (com DTR e RTS ligados na COM10)
COM10 -> COM11: 256 bytes em 7 ms, iguais: True
COM11 -> COM10: 256 bytes em 0 ms, iguais: True
```

Os sinais chegam cruzados (RTS→CTS, DTR→DSR e DCD) e, sem `EmuBR`, os bytes passam sem o atraso de 1200 baud.

## Critério 2 / S2 — ponte Web Serial com o servidor em eco

Chrome 157, página em `http://localhost:5210`, modo eco, COM11 aberta pela Web Serial em 1200 8O1; a ferramenta
`hart-serial` na COM10 envia 200 blocos de 1 a 64 bytes (todos os valores de byte) e compara a volta:

```text
dotnet run --project src\HartPoc.SerialTool --no-build -- echo --port COM10 --count 200
COM10 aberta em 1200 8O1. CTS=1 DSR=1 DCD=1
Eco: 200 de 200 blocos iguais byte a byte (6786 bytes enviados).
```

Repetições em 15/11/2026, com o Edge 157.0.4341.60:

| Navegador | `EmuBR` | Resultado |
| --- | --- | --- |
| Chrome 157 | desligada | 200 de 200 blocos iguais (6786 bytes), cerca de 3 s |
| Edge 157 | desligada | 200 de 200 blocos iguais (6786 bytes) |
| Edge 157 | ligada | 100 de 100 blocos iguais (3489 bytes), 73 s |

Na primeira rodada no Edge, 1 dos 200 blocos veio precedido de um resto de pedido do PACTware
(`FFFFFFFFFF82BE010E2E020908…`) que tinha ficado no buffer da COM11 de quando o PACTware estava conectado; os bytes
do bloco em si voltaram certos. A rodada seguinte, com o buffer limpo, deu 200 de 200.

Com `EmuBR=yes` (`change CNCA0 EmuBR=yes` e `change CNCB0 EmuBR=yes`; no registro, `EmuBR=4294967295` nas duas
pontas), o par direto leva 1114 ms para 120 bytes, contra 1100 ms teóricos a 1200 baud.

Nota: uma primeira tentativa foi feita com a página ainda no modo HART; os 5226 bytes chegaram intactos ao
servidor, mas nada voltou, como esperado nesse modo. A página passou a montar o caminho do WebSocket a partir do
modo escolhido e a registrar no log onde conectou.

## Critério 5 / S7 — tempo de resposta (parte local)

Servidor no mesmo PC, Chrome, sem `EmuBR`, `hart-serial` na COM10 no lugar do PACTware. O cronômetro parte do
retorno da escrita na porta e vai até o primeiro byte e até o quadro completo da resposta.

```text
dotnet run --project src\HartPoc.SerialTool --no-build -- measure --port COM10 --count 100 --command 0
Comando 0: 100 respostas válidas em 100 pedidos de 10 bytes.
Até o primeiro byte da resposta:   mín. 10,4 ms, mediana 15,0 ms, p95 21,4 ms, máx. 24,0 ms
Até o quadro completo da resposta: mín. 10,8 ms, mediana 15,3 ms, p95 21,8 ms, máx. 24,4 ms

dotnet run --project src\HartPoc.SerialTool --no-build -- measure --port COM10 --count 100 --command 3
Comando 3: 100 respostas válidas em 100 pedidos de 14 bytes.
Até o primeiro byte da resposta:   mín. 9,3 ms, mediana 18,4 ms, p95 26,1 ms, máx. 27,9 ms
Até o quadro completo da resposta: mín. 9,8 ms, mediana 18,7 ms, p95 26,6 ms, máx. 28,2 ms
```

Contadores do servidor ao fim: 202 quadros recebidos, 202 respondidos, 0 erros de verificação, 0 bytes descartados.

Todas as medições locais (100 pedidos cada, 100% de respostas válidas), até o primeiro byte da resposta:

| Navegador | `EmuBR` | Comando | Mínimo | Mediana | p95 | Máximo | Quadro completo (mediana) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Chrome | desligada | 0 | 10,4 ms | 15,0 ms | 21,4 ms | 24,0 ms | 15,3 ms |
| Chrome | desligada | 3 | 9,3 ms | 18,4 ms | 26,1 ms | 27,9 ms | 18,7 ms |
| Edge | desligada | 0 | 22,4 ms | 28,6 ms | 35,5 ms | 45,8 ms | 29,1 ms |
| Edge | desligada | 3 | 25,1 ms | 32,4 ms | 39,2 ms | 51,7 ms | 33,0 ms |
| Edge | ligada | 0 | 188,8 ms | 205,0 ms | 222,1 ms | 250,8 ms | 504,2 ms |
| Edge | ligada | 3 | 248,7 ms | 266,5 ms | 270,3 ms | 327,9 ms | 485,6 ms |

Comparação com o DTM: o Serial HART CommDTM não tem campo de time-out; na varredura, as tentativas num endereço
vazio saem a cada 0,44 s (medido sem `EmuBR`), e são 4 por endereço (3 retentativas).

PACTware com `EmuBR` ligada (15/11/2026, 00:51 a 00:57): conectou, abriu a "Online Parametrization" e seguiu
lendo. No log, 120 pedidos nessa conexão: os mesmos da sessão sem emulação, todos respondidos (fora os 60 da
varredura dos endereços vazios), nenhum erro de verificação e nenhum pedido repetido por retentativa. Os pedidos
chegam ao servidor em pedaços de 1 a 9 bytes e são remontados sem perda. O ciclo de leitura (comando 9) passou de
cerca de 2,6 s para 3,5 s; o usuário percebeu a tela mais lenta.

Limites destas medições:

- São com a ferramenta de teste, não com o PACTware (ele ocupa a COM10 quando está aberto). Do PACTware há o
  resultado funcional acima, não um tempo medido.
- Com `EmuBR` ligada, a resposta chega perto do time-out estimado do DTM (até 328 ms contra cerca de 440 ms). A
  folga para a latência de rede é pequena; com `EmuBR` desligada (padrão do com0com), sobra mais de 380 ms.
- Falta medir com o servidor em outro PC.

## Critério 3 / S3 e S4 — PACTware encontra e lê o instrumento (em andamento)

PACTware 6.3 com o "Serial HART Modem" (Serial HART CommDTM 1.0.0, FieldComm Group) na COM10 e o DTM de
dispositivo "Generic H7" (FieldComm Group, 1.8.295; DD 7.12.15). Não há DTM da Smar instalado: o PACTware leu a
identificação e caiu no genérico.

Parâmetros do DTM de comunicação (tela de configuração): mestre primário, 5 preâmbulos, 3 retentativas, busca até
o endereço 15. Não há campo de time-out; pelo log, o intervalo entre tentativas num endereço vazio é de cerca de
0,44 s (4 tentativas por endereço).

| Tentativa | Instrumento | Resultado |
| --- | --- | --- |
| 1 | HART 5, sem o comando 50 | Varredura encontra o instrumento (comandos 0, 12 e 13). Ao abrir a parametrização online, o DTM pede o comando 50, recebe o código 64 e a tela fica vazia. |
| 2 | HART 5, com o comando 50 | O comando 50 é respondido, mas o DTM não pede mais nada e a tela continua vazia. |
| 3 | **HART 7** | A tela "Online Parametrization" abre com as abas e os dados do instrumento. |

Conclusão para a D09: com o Generic H7, o instrumento precisa se declarar **HART 7**.

Valores mostrados pelo PACTware na tentativa 3 (aba "Informação do dispositivo"): fabricante 3E / Smar, tipo
3E01 / LD301, identificação 929282 (= 0x0E2E02), tag PT-101, tag longa "PT-101 LD301 SIMULADO", data 14/11/2026,
descrição "LD301 SIMULADO", mensagem "INDUSTRIALSIM WEB - PROVA HART", revisão do protocolo 7, revisões de
dispositivo, software e hardware 3, 1 e 1. Todos iguais aos do simulador.

Comandos pedidos pelo DTM ao abrir (log `hart-poc-20261114-125235-387.log`):

- Respondidos: 0, 9, 12, 13, 14, 15, 16, 20 e 50.
- Sem implementação (código 64): 31, 40, 44, 45, 47, 52, 53, 54, 72, 76 e 105. A tela abriu mesmo assim.
- Nenhum erro de verificação e nenhum byte descartado.

Leitura nas abas "Device Variables" e "Analog Channel" (capturas do usuário, 14/11/2026): PV de 38,842 kPa e
depois 44,890 kPa (a senoide do simulador vai de 5 a 45 kPa), faixa 0,000–50,000 kPa, amortecimento 0,000 s,
função de transferência linear, alarme alto, limites do sensor ±50,000 kPa e alcance mínimo 0,417 kPa — todos
iguais aos do simulador. A corrente é lida pelo comando 2 (no log, 9,67 mA e 35,4% às 13:02:09).

Pendente para fechar o critério: captura da tela do PACTware que mostra a corrente de malha.

## Critério 4 / S5 — PACTware escreve faixa e amortecimento

Na aba "Analog Channel", valor digitado e botão "Apply" do rodapé. Log do servidor:

```text
13:02:09.650 Comando 35: faixa de 0–50 kPa para 0–40 kPa (unidade recebida: código 12)
   comando 35, dados 0C4220000000000000 -> código de resposta 0
13:02:09.650 TX FFFFFFFFFF86BE010E2E02230B00400C42200000000000001D
13:02:26.964 RX FFFFFFFFFF82BE010E2E020F0010                                  (comando 15: leitura seguinte)
13:02:26.964 TX FFFFFFFFFF86BE010E2E020F14004000000C422000000000000000000000FBFA002F
13:02:51.356 Comando 34: amortecimento de 0 s para 2 s
   comando 34, dados 40000000 -> código de resposta 0
13:02:51.357 TX FFFFFFFFFF86BE010E2E0222060044400000003B
```

- A leitura seguinte (comando 15) traz o valor superior `42200000` = 40,0 kPa.
- O status passa a `0x40` (configuração alterada) depois da primeira escrita.
- Estado do simulador em `/api/status` depois das escritas: `upperRangeKpa: 40`, `dampingS: 2`,
  `configurationChangeCounter: 3` (o PACTware mandou o comando 35 duas vezes, com o mesmo valor).
- Efeito na saída: com a PV em 25,19 kPa, a corrente foi a 14,08 mA (62,97% de 0–40 kPa); na faixa antiga seria
  12,06 mA.

Pendente: captura do PACTware com os valores novos e uma leitura do amortecimento (comando 15) depois da escrita.

Observação: o botão "Aplicar valores" da mesma aba não grava o valor digitado; ele abre o assistente que usa a
pressão aplicada como ponto de 4 ou 20 mA (comandos 36 e 37, que a prova não implementa: código 64, mostrado pelo
DTM como "0x40 Undefined").

## Critério 6 / S8 — 30 minutos conectado (15/11/2026)

Servidor iniciado pelo usuário no próprio terminal (HART 7), Chrome com a ponte na COM11, PACTware 6.3 com a
"Online Parametrization" do instrumento aberta, PC em uso normal durante o teste (aba da ponte em segundo plano).
Log `hart-poc-20261115-000830-758.log`:

| Medida | Valor |
| --- | --- |
| Ponte conectada | 00:08:44, uma única vez; nenhuma desconexão até o fim do log (00:43:10) |
| Leitura cíclica do PACTware (comando 9) | De 00:10:20 a 00:43:10: cerca de 33 minutos, 764 pedidos, todos com código de resposta 0 |
| Maior intervalo entre dois pedidos nesse período | 4,4 s (o ciclo normal do DTM fica entre 2,5 e 3,5 s) |
| Pedidos por janela de 5 minutos | 104, 108, 127, 127, 125, 125 (de 00:10 a 00:40) |
| Quadros recebidos / respondidos | 879 / 819 |
| Pedidos sem resposta | 60, todos do comando 0 em endereço curto na varredura inicial dos endereços vazios (1 a 15) |
| Erros de verificação, bytes descartados, quadros abandonados | 0, 0 e 0 |
| Bytes recebidos / enviados | 18.192 / 66.344 |

Pelo lado do servidor, não houve queda nem erro de comunicação em 33 minutos. O usuário confirmou que o PACTware
não mostrou aviso de comunicação: a barra de estado seguiu em "Conectado". O único indicador é o símbolo amarelo
"≠" ("base de dados do DTM é diferente da base de dados do instrumento"), que compara os dados guardados no
projeto com os do instrumento e não é erro de comunicação.

## S6 — Custo

| Programa | Origem | Licença |
| --- | --- | --- |
| com0com 2.2.2.0 | `com0com-2.2.2.0-x64-fre-signed.zip`, da página do projeto | Software livre (GPL). É um driver instalado no PC de quem usa o PACTware; não é dependência do projeto nem é distribuído com ele. |
| PACTware 6.3 (6.3.00029) | Pacote `PACTware_6.3.0.zip` | "License Agreement for PACTware Provided by FieldComm Group" (`PWEULAENG.RTF`): concede cópia e uso nos computadores do usuário; o texto não menciona pagamento nem prazo. |
| Serial HART CommDTM 1.0.0 | Mesmo pacote, `AdditionalSetupsHARTCommDtm` | Vem no pacote do PACTware; a instalação não pediu chave. |
| CodeWrights iDTM-FDI 1.8.295 (traz o "Generic H7") | Mesmo pacote, `AdditionalSetupsFDISetup` | Vem no pacote do PACTware; a instalação não pediu chave. O `Licenses.zip` lista só componentes de terceiros. |

Nenhum dos programas pediu chave, compra ou cadastro, e não apareceu aviso de período de avaliação em dois dias de
uso. Limite desta verificação: não foi encontrada uma frase explícita de "gratuito sem prazo" para os dois DTMs.

## Critério 7 — Portão de decisão: caminho do PACTware

| Critério | Medido | Situação |
| --- | --- | --- |
| S1 Driver | com0com 2.2.2.0 no Windows 11 Pro 10.0.26300, com Secure Boot e integridade de memória ligados, sem modo de teste | Atendido |
| S2 Ponte | Chrome 157 e Edge 157 abrem a COM11 em 1200 baud, 8 bits, paridade ímpar, 1 stop bit; eco byte a byte | Atendido |
| S3 Descoberta | A varredura do PACTware encontra o instrumento no endereço 0 | Atendido |
| S4 Leitura | PV, faixa, amortecimento e limites na tela; corrente pelo log | Atendido |
| S5 Escrita | Faixa 0–50 → 0–40 kPa e amortecimento 0 → 2 s, com efeito na corrente | Atendido |
| S6 Custo | Nenhum programa pediu compra, chave ou cadastro | Atendido, com o limite descrito acima |
| S7 Tempo | Local: 15 a 52 ms (sem emulação de baud) e 189 a 328 ms (com), contra cerca de 440 ms do DTM | **Não medido com outro PC** |
| S8 Estabilidade | 33 minutos, 764 leituras, nenhum erro | Atendido |

**Resultado: A provisório (15/11/2026).** S1 a S6 e S8 atendidos. O S7 será medido na Etapa 9, com o servidor da
prova no PC do laboratório; se a resposta pela rede não couber no time-out do DTM, o resultado passa a B. Nos dois
casos a Etapa 17 implementa a ponte serial; o que muda é o guia e a prioridade da extensão HART-IP. Decisão tomada
com o usuário, registrada na D09 do descritivo.

Definições que saem da prova:

- **Revisão HART:** 7. O "Generic H7" não abre um instrumento que se declara HART 5.
- **Serial:** 1200 baud, 8 bits, paridade ímpar, 1 stop bit, sem controle de fluxo e sem eco.
- **com0com:** versão 2.2.2.0 assinada, emulação de baud desligada (padrão).
- **DTM de comunicação:** mestre primário, 5 preâmbulos, 3 retentativas, cerca de 0,44 s por tentativa.
- **Identificação:** sem DTM específico instalado, o PACTware usa o genérico. A prova usou a identificação do Smar
  LD301 só para teste; a dos instrumentos do produto fica para a Etapa 16.

## Para as etapas seguintes

- **Etapa 9:** medir o S7 (critério novo no plano).
- **Etapa 16:** os logs em `docs/validacao/etapa-02/` têm os quadros reais do PACTware, em hexadecimal, para virar
  vetores de teste (`pactware-leitura-e-escrita.log` e `pactware-30-minutos-e-emulacao-de-baud.log`). Comandos que o Generic H7 usa além da tabela do descritivo:
  7, 8, 9, 20 e 50. A norma de enlace manda responder com erro de comunicação a um quadro com byte de verificação
  errado; o critério da Etapa 16 diz "nenhuma resposta" e precisa ser revisto.
- **Etapa 17:** o RTS e o DTR do DTM não foram observados; a página de ponte registra os sinais e isso pode ser
  visto lá. Limpar o buffer da serial ao conectar evita repassar restos de uma sessão anterior.
- **Não verificado:** valores do LD301 assumidos na prova (limites do sensor, span mínimo, amortecimento máximo).
