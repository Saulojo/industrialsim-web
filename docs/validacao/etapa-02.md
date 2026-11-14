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

## Critérios 3, 4, 6 e 7

Pendentes: dependem das sessões com o PACTware.

## Pontos em aberto

- **DTMs:** estão instalados o "Serial HART CommDTM 1.0.0" (FieldComm Group) e o "CodeWrights iDTM-FDI PACTware
  1.8.295". Não há um DTM HART genérico clássico; falta ver no PACTware se o iDTM-FDI abre o instrumento sem um
  pacote FDI do fabricante, e confirmar a licença de cada um (critério S6).
- **Edge e `EmuBR`:** falta repetir o eco e a medição.
- **Segundo PC:** ainda não há outro computador para medir o tempo de resposta remoto (S7).
