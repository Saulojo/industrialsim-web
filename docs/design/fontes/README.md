# Fontes dos Artifacts da D1

Código das três peças publicadas como Artifacts do tipo Design no claude.ai, guardado aqui para não depender da cópia local de quem publicou e para os scripts de conferência (`tools/design/`).

| Pasta | Artifact |
| --- | --- |
| `wireframes/` | Wireframes de baixa fidelidade (parte B): https://claude.ai/artifact/5mtQasLaySPNUq3wMcSQLY |
| `identidade/` | Identidade visual (parte C): https://claude.ai/artifact/FNXXhg5dJGu3YXVAS4ZRsk |
| `prototipo/` | Protótipo navegável (parte D): https://claude.ai/artifact/BATxECGZStkjMLSwVSitZk |

- **Estrutura:** cada pasta tem `project/canvas.json` (pranchas, páginas e posições no canvas) e um arquivo `*.dc.html` por prancha ou componente.
- **Folha de estilos do protótipo:** fica em `prototipo/proto.css`. Nas pranchas ela aparece como `/_blob/c8dfaff3c31198ac92f85c7937c771d0`, o endereço do arquivo enviado ao Artifact.
- **Abrir fora do claude.ai:** os arquivos só funcionam dentro do tipo Design, que fornece o `support.js` e o canvas. Fora dele, servem como referência de estrutura, textos e valores para a D2.
- **Não é código de produção:** nada aqui é importado pelo `web/` (D1 não muda `web/src`).
- **Atualização:** ao republicar um Artifact, atualize a pasta correspondente no mesmo commit.
