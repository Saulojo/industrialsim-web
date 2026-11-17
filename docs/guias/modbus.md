# Conectar um mestre Modbus à planta

Este guia vale para qualquer mestre (CLP, CODESYS, OpenPLC, SCADA, script). Os guias de cada ferramenta partem daqui:

- [Python (`pymodbus`)](python.md)
- [OpenPLC](openplc.md)
- [CODESYS](codesys.md)
- [TIA Portal (`MB_CLIENT`)](tia-portal.md)

## 1. Dados da conexão

| Campo | Valor |
| --- | --- |
| IP | O do servidor do simulador. No PC de desenvolvimento, `127.0.0.1`. |
| Porta | A da planta, entre 15000 e 15999. Cada planta tem a sua, mostrada ao criá-la. |
| Unit ID | 1 (padrão; pode ser trocado por planta) |
| Protocolo | Modbus TCP padrão: funções 1, 2, 3, 4, 5, 6, 15 e 16 |

Até a IHM chegar (Etapa 7), a planta é criada e o pareamento é feito pela API REST. No PowerShell, com o servidor em `http://localhost:5080`:

```powershell
$api = "http://localhost:5080/api"
$p = Invoke-RestMethod -Method Post "$api/plants" -ContentType "application/json" -Body '{"scenarioId":"caldeira"}'
"Planta $($p.id), porta Modbus $($p.modbusPort)"
```

## 2. Pareamento

A planta só atende mestres autorizados. A primeira conexão de um IP novo fica pendente: o servidor a mantém aberta, mas **não responde** às requisições até alguém aceitar o pedido. O mestre vê isso como time-out.

1. Ligue o mestre (ele vai dar time-out por enquanto).
2. Veja o pedido e aceite:

   ```powershell
   $m = Invoke-RestMethod "$api/plants/$($p.id)/modbus"
   $m.pendingRequests          # IP do mestre e prazo (60 s)
   Invoke-RestMethod -Method Post "$api/plants/$($p.id)/modbus/pairing/$($m.pendingRequests[0].id)/accept"
   ```

3. A partir daí o IP fica autorizado nessa planta; as próximas conexões dele são atendidas na hora.

Outras operações:

| O quê | Como |
| --- | --- |
| Recusar um pedido | `POST $api/plants/{id}/modbus/pairing/{pedido}/refuse`. A conexão é fechada e o IP fica 60 s sem poder pedir de novo. |
| Autorizar um IP antes de ele conectar | `POST $api/plants/{id}/modbus/masters` com `{"address":"127.0.0.1"}` |
| Retirar a autorização | `DELETE $api/plants/{id}/modbus/masters/127.0.0.1`. As conexões desse IP são fechadas. |
| Trocar o Unit ID | `PUT $api/plants/{id}/modbus/unit-id` com `{"unitId":255}` |
| Ver conexões, autorizados e contadores | `GET $api/plants/{id}/modbus` |
| Ver quem escreveu cada registrador | `GET $api/plants/{id}/modbus/writes` |

Sem resposta em 60 s, a conexão pendente é fechada; o mestre reconecta e um novo pedido aparece.

## 3. Mapa da caldeira

Endereços em base 0 (CODESYS, OpenPLC, `pymodbus`) e na notação clássica (Siemens `MB_CLIENT` e muitos SCADAs).

| Área | Base 0 | Clássica | Sinal | Faixa (4–20 mA) |
| --- | --- | --- | --- | --- |
| Coil | 0 | 1 | P-101: liga a bomba | — |
| Entrada discreta | 0 | 10001 | P-101 em operação | — |
| Entrada discreta | 1 | 10002 | LSLL-101: nível muito baixo | — |
| Entrada discreta | 2 | 10003 | LSHH-101: nível muito alto | — |
| Entrada discreta | 3 | 10004 | PSH-101: pressão alta | — |
| Entrada discreta | 4 | 10005 | PSV-101 aberta | — |
| Entrada discreta | 5 | 10006 | Watchdog disparado | — |
| Holding register | 0 | 40001 | FV-101: comando da válvula de água | 0–100 % |
| Holding register | 1 | 40002 | FV-102: comando da válvula de combustível | 0–100 % |
| Holding register | 10 | 40011 | Heartbeat do mestre (basta mudar) | — |
| Input register | 0 | 30001 | LT-101: nível do tubulão | 0–1,2 m |
| Input register | 1 | 30002 | PT-101: pressão do vapor | 0–16 bar |
| Input register | 2 | 30003 | FT-101: vazão de água | 0–4 kg/s |
| Input register | 3 | 30004 | FT-102: vazão de vapor | 0–5 kg/s |
| Input register | 4 | 30005 | FV-101: posição real | 0–100 % |
| Input register | 5 | 30006 | FV-102: posição real | 0–100 % |
| Input register | 100–109 | 30101–30110 | Diagnóstico em `float32` (nível, pressão, vazões, FV-103) | engenharia |

## 4. Escala

No formato padrão (0–27648, igual ao cartão analógico Siemens), 4 mA = 0 e 20 mA = 27648. Leia os registradores como `INT` (com sinal): abaixo de 4 mA o valor é negativo.

```text
valor = LRV + contagens / 27648 × (URV − LRV)
contagens = (valor − LRV) / (URV − LRV) × 27648
```

Exemplo: LT-101 = 13824 contagens → 0 + 13824 / 27648 × 1,2 = 0,6 m. Para abrir FV-101 em 50 %, escreva 13824 no holding register 0.

## 5. Erros comuns

| Sintoma | Causa provável |
| --- | --- |
| Time-out em toda requisição | Pareamento pendente: aceite o pedido. |
| Conexão fechada logo ao conectar | IP recusado há menos de 60 s, ou limite de conexões da planta (8). |
| Exceção 02 (endereço ilegal) | Endereço fora do mapa (ex.: input register 200). A conexão continua válida. |
| Exceção 0B | Unit ID diferente do configurado na planta. |
| Exceção 03 | Quantidade inválida (ex.: mais de 125 registradores numa leitura). |
| Válvula não se mexe | O mestre escreve num endereço errado, ou o watchdog disparou (entrada discreta 5). |

O Modbus TCP não tem senha nem criptografia: a proteção é o pareamento por IP.
