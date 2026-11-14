# Prova de conceito HART serial (Etapa 2)

Ferramenta de teste, fora do produto. Responde à pergunta da Etapa 2: o PACTware consegue configurar um
instrumento simulado no servidor pela serial, passando por uma COM virtual (com0com) e pelo navegador (Web Serial)?

```text
PACTware (DTM de modem HART) ──► COM10 ═══ com0com ═══ COM11 ◄── página de ponte (Web Serial)
                                                                      │ WebSocket (bytes crus)
                                                                      ▼
                                                         servidor: instrumento HART simulado
```

## O que tem aqui

| Pasta | O que é |
| --- | --- |
| `src/HartPoc.Core` | Quadros HART, leitor de quadros a partir do fluxo de bytes e o instrumento simulado. Sem rede. |
| `src/HartPoc.Server` | Servidor mínimo: página de ponte e os WebSockets `/ws/hart` e `/ws/echo`. |
| `src/HartPoc.SerialTool` | `hart-serial`: fica na COM10 no lugar do PACTware, para o teste de eco e a medição de tempo. |
| `web` | Página de ponte (HTML e JavaScript puros), servida pelo servidor. |
| `tests/HartPoc.Tests` | Testes automáticos, sem hardware. |

## Instrumento simulado

Imita, só para teste, o transmissor de pressão **Smar LD301**: fabricante `0x3E`, tipo de dispositivo `0x01`
(tipo expandido `0x3E01`), conforme as tabelas 8 e 1 da HCF_SPEC-183 r22.0. A PV é uma senoide de 25 ± 20 kPa
com período de 60 s; a faixa inicial é 0–50 kPa.

- Comandos universais: 0, 1, 2, 3, 7, 8, 9, 11, 12, 13, 14, 15, 16 e 20 (HCF_SPEC-127 r7.1).
- Comandos de prática comum: 33, 34, 35, 38, 48 e 50 (HCF_SPEC-151 r10.0).
- Qualquer outro comando recebe o código de resposta 64 (comando não implementado) e aparece no log.
- Quadros e endereçamento conforme a HCF_SPEC-081 r9.0.

Valores assumidos, a conferir no manual da Smar: limites do sensor (±50 kPa), span mínimo (URL/120),
amortecimento máximo (32 s) e as revisões de dispositivo, software e hardware.

## Como rodar

Na pasta `tools/hart-poc`:

```bash
dotnet test
```

```bash
dotnet run --project src/HartPoc.Server
```

Abra `http://localhost:5210` no Chrome ou no Edge, escolha o modo e clique em "Conectar porta serial" (COM11).

Opções do servidor (linha de comando):

| Opção | Padrão | Para quê |
| --- | --- | --- |
| `--Hart:Revision=7` | `5` | Resposta do comando 0 no formato do HART 7. |
| `--Hart:PollingAddress=1` | `0` | Endereço de polling do instrumento. |
| `--Hart:ResponseDelayMs=100` | `0` | Atraso artificial, para achar o limite do time-out do DTM. |
| `--Hart:LogDirectory=pasta` | `logs` | Onde gravar o log de quadros em hexadecimal. |
| `--urls http://0.0.0.0:5210` | `http://localhost:5210` | Aceitar a ponte de outro PC da rede (só durante o teste). |

No teste com o servidor em outro PC, a página continua aberta em `http://localhost:5210` (a Web Serial exige
contexto seguro) e só o campo "Endereço do WebSocket" aponta para o servidor remoto.

## Ferramenta na COM10

Com o servidor rodando e a página conectada à COM11:

```bash
dotnet run --project src/HartPoc.SerialTool -- echo --port COM10
```

```bash
dotnet run --project src/HartPoc.SerialTool -- measure --port COM10 --count 100 --command 3
```

`echo` exige a página no modo eco; `measure`, no modo HART. A medição parte do retorno da escrita na porta e
vai até o primeiro byte e até o quadro completo da resposta.
