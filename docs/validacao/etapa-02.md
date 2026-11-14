# Validação da Etapa 2 — Prova de conceito HART serial (rascunho)

Em andamento. Nenhum critério da etapa está marcado ainda; este arquivo registra o que já foi medido.

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

Pendente para fechar o critério: repetir no Edge e com `EmuBR=yes`.

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

Limites desta medição:

- É com a ferramenta de teste, não com o PACTware (ele ocupa a COM10 quando está aberto).
- Sem `EmuBR`, o par não gasta o tempo de 1200 baud; numa linha real, um pedido de 14 bytes leva 128 ms só para
  ser transmitido. Falta repetir com `EmuBR=yes`.
- Falta comparar com o time-out configurado no DTM e medir com o servidor em outro PC.

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

## Critérios 6 e 7

Pendentes: 30 minutos conectado e o resultado do portão.

## Pontos em aberto

- **DTMs:** estão instalados o "Serial HART CommDTM 1.0.0" (FieldComm Group) e o "CodeWrights iDTM-FDI PACTware
  1.8.295". Não há um DTM HART genérico clássico; falta ver no PACTware se o iDTM-FDI abre o instrumento sem um
  pacote FDI do fabricante, e confirmar a licença de cada um (critério S6).
- **Edge e `EmuBR`:** falta repetir o eco e a medição.
- **Segundo PC:** ainda não há outro computador para medir o tempo de resposta remoto (S7).
