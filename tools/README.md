# Ferramentas

Implementações de referência independentes (Python), usadas para validação cruzada dos modelos do núcleo, por exemplo o integrador RK4 (Etapa 1) e a caldeira de Åström & Bell (Etapa 15).

`hart-poc/` é a prova de conceito da Etapa 2 (HART serial com com0com + Web Serial), em .NET; veja o README da pasta.

`design/contraste.mjs` (trilha de design, Etapa D1) confere o contraste WCAG 2.2 AA da paleta em `docs/design/tokens.json`, nos temas claro e escuro; testes com `node --test tools/design/`. Resultado em `docs/design/contraste.md`.

Nada aqui roda no servidor nem no site.
