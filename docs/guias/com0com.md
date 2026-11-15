# com0com: par de portas COM virtuais (rascunho da Etapa 2)

O com0com é um driver gratuito para Windows que cria duas portas COM ligadas entre si: o que entra numa sai na
outra. O PACTware abre uma ponta (COM10) e a página do site abre a outra (COM11) pela Web Serial.

> Rascunho da Etapa 2. Instalação, troca de nomes e a opção `EmuBR` foram verificadas no Windows 11.

## Qual versão instalar

| Versão | Com Secure Boot ligado (Windows 11) | Situação |
| --- | --- | --- |
| 3.0.0.0 "signed" | **Não funciona**: o Windows recusa o driver (código 52) e nenhuma porta é criada | Testada em 14/11/2026 |
| 2.2.2.0 assinada (x64) | **Funciona**, com Secure Boot e integridade de memória ligados e sem modo de teste | Testada em 14/11/2026 no Windows 11 Pro 10.0.26300 |

Não desligue o Secure Boot nem ative o modo de teste de drivers para usar a 3.0.0.0: isso reduz a segurança do PC.

## Instalação

1. Se a 3.0.0.0 estiver instalada, desinstale-a em "Aplicativos instalados" e reinicie o PC.
2. Baixe o pacote assinado de 64 bits da página oficial do projeto no SourceForge (`com0com`).
3. Instale como administrador, aceitando criar o primeiro par de portas.
4. Confira no Gerenciador de Dispositivos, em "com0com - serial port emulators", que não há aviso amarelo.

## Renomear o par para COM10 e COM11

Abra o "Setup Command Prompt" do com0com como administrador:

```text
change CNCA0 PortName=COM10
change CNCB0 PortName=COM11
list
```

## Opções a testar

- `EmuBR` ("emulate baud rate"): faz a porta virtual respeitar a velocidade de 1200 baud. **Recomendado: deixar
  desligada**, que é o padrão da instalação. O PACTware funciona das duas formas, mas com ela ligada a resposta do
  instrumento chega em 200 a 330 ms em vez de 15 a 50 ms, perto do tempo de espera do DTM, e sobra pouca folga para
  a rede. Para desligar ou ligar, no "Setup Command Prompt" como administrador, com as portas fechadas:

  ```text
  change CNCA0 EmuBR=no
  change CNCB0 EmuBR=no
  ```

- Os sinais vêm cruzados por padrão (verificado): o RTS de uma ponta é o CTS da outra, e o DTR é o DSR e o DCD.

## Conferir

No PowerShell:

```powershell
[System.IO.Ports.SerialPort]::GetPortNames()
```

A lista deve trazer `COM10` e `COM11`.
