# IndustrialSim Web — instruções para o Claude Code

Simulador de plantas industriais de processo contínuo que roda no navegador, aberto ao público, para ensino e pesquisa (iniciação científica). A planta é controlada por qualquer mestre Modbus TCP (CLP real, PLCSIM Advanced, CODESYS, OpenPLC, SCADA, script) e tem instrumentos HART.

## Fontes da verdade

- `docs/descritivo.md` — requisitos, decisões D01–D19, arquitetura, mapa Modbus, HART, convenções.
- `docs/plano.md` — etapas, escopo e critérios de validação.

Leia o descritivo inteiro e, do plano, "Como usar este plano" e a seção completa da etapa em questão antes de começar. Se algo aqui contradisser esses documentos, os documentos valem.

## Como trabalhar

- Uma etapa do plano por sessão. Identifique a etapa pela mensagem do usuário ou pela linha "Estado atual" do descritivo; se não estiver claro, pergunte.
- Siga as decisões D01–D19. Para contrariar alguma, aponte o conflito e proponha a mudança antes de alterar código.
- Código escrito do zero. Do protótipo Unity (Saulojo/IC_Proccess_Simulation) só os modelos 3D são aproveitados; o código dele é apenas referência.
- Trabalhe numa branch por etapa (`etapa-NN-nome`), com commits pequenos e mensagens em português.
- Escreva os testes junto com o código e rode-os antes de dizer que algo funciona (`dotnet test`, `npm test`, `npm run lint`).
- Se a etapa for grande, divida em partes (A, B…), cada uma compilando e com testes passando.
- Não invente APIs de bibliotecas: na dúvida, consulte a documentação ou diga a versão assumida.
- Para equações e parâmetros físicos, cite a fonte.
- Responda em português; nomes no código em inglês.

## Nunca

- Calcular a planta no cliente web (a física roda só no servidor).
- Colocar texto de interface fora dos arquivos de idioma, ou cores e fontes fora dos tokens de design.
- Abrir portas de rede além das definidas no descritivo.
- Adicionar dependência com licença GPL ou comercial sem decisão registrada.
- Marcar uma etapa como concluída com critério de validação não verificado.

## Ao terminar uma etapa

1. Liste cada critério de validação, como foi verificado e o resultado.
2. Escreva `docs/validacao/etapa-NN.md` com comandos, saídas, números e capturas.
3. Com a confirmação do usuário: marque os critérios em `docs/plano.md`, atualize "Estado atual" e registre decisões novas em `docs/descritivo.md`, e abra o pull request.
