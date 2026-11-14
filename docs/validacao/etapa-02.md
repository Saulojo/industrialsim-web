# Validação da Etapa 2 — Prova de conceito HART serial (rascunho)

Em andamento. Nenhum critério da etapa está marcado ainda; este arquivo registra o que já foi medido.

## Ambiente do teste

| Item | Valor |
| --- | --- |
| Sistema | Windows 11 Pro, versão 10.0.26300 |
| Secure Boot | Ligado (`HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State\UEFISecureBootEnabled = 1`) |
| .NET SDK | 10.0.404 |
| Navegadores | Google Chrome 157.0.8103.79 e Microsoft Edge |
| PACTware | 6.3 (6.3.00029), sem nenhum DTM instalado até aqui |

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

### Tentativa 2: com0com 2.2.2.0 assinada — pendente

## Critérios 2 a 7

Pendentes: dependem do par COM10/COM11 e dos DTMs do PACTware.

## Pontos em aberto

- **DTMs:** o PACTware 6.3 foi instalado sem o DTM de comunicação HART e sem o DTM HART genérico. Falta confirmar
  de onde baixar cada um e se o genérico tem licença gratuita permanente (critério S6).
- **Segundo PC:** ainda não há outro computador para medir o tempo de resposta remoto (S7).
