# IndustrialSim Web

Simulador de plantas industriais de processo contínuo que roda no navegador, para ensino e pesquisa em automação, instrumentação e controle. A planta roda no servidor e é controlada por qualquer mestre Modbus TCP (CLP real, PLCSIM Advanced, CODESYS, OpenPLC, SCADA ou script). Os instrumentos falam HART.

- Requisitos, decisões e arquitetura: [docs/descritivo.md](docs/descritivo.md)
- Etapas e critérios de validação: [docs/plano.md](docs/plano.md)
- Evidências de cada etapa: [docs/validacao/](docs/validacao/)

> Estado: Etapa 0 (fundação do repositório e CI). Ainda não há simulação; o site só mostra a versão do servidor.

## Pré-requisitos

| Ferramenta | Versão | Como conferir |
| --- | --- | --- |
| Git | qualquer recente | `git --version` |
| .NET SDK | **10.0** (a versão mínima está em [global.json](global.json)) | `dotnet --list-sdks` deve listar uma linha `10.0.x` |
| Node.js | **24 ou mais novo** (o CI usa a versão do [.nvmrc](.nvmrc)) | `node -v` |
| npm | vem com o Node | `npm -v` |

Downloads: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) e [Node.js](https://nodejs.org/). No Windows também dá para instalar pelo terminal:

```bash
winget install Microsoft.DotNet.SDK.10
```

```bash
winget install OpenJS.NodeJS.LTS
```

Ter outros SDKs .NET instalados (8, 9, 11…) não atrapalha: o `global.json` escolhe o 10.

## Rodar pela primeira vez

Os comandos abaixo valem para Windows (Git Bash ou PowerShell), Linux e macOS.

1. Clone o repositório:

   ```bash
   git clone https://github.com/Saulojo/industrialsim-web.git
   ```

   ```bash
   cd industrialsim-web
   ```

2. Backend: compile e rode os testes.

   ```bash
   cd server
   ```

   ```bash
   dotnet build
   ```

   ```bash
   dotnet test
   ```

   O esperado é `Compilação com êxito`/`Build succeeded`, `0 Aviso(s)`/`0 Warning(s)`, e todos os testes aprovados.

3. Frontend: instale, verifique, teste e gere o site. Volte para a raiz do repositório e entre em `web`:

   ```bash
   cd ../web
   ```

   ```bash
   npm ci
   ```

   ```bash
   npm run lint
   ```

   ```bash
   npm test
   ```

   ```bash
   npm run build
   ```

   O `npm run build` grava o site em `server/src/IndustrialSim.Server/wwwroot/`.

4. Suba o servidor:

   ```bash
   cd ../server/src/IndustrialSim.Server
   ```

   ```bash
   dotnet run
   ```

   Abra <http://localhost:5080>. A página deve mostrar **Versão: 0.1.0+…** e a hora do servidor. Para parar, use `Ctrl+C`.

## Desenvolvimento do dia a dia

Para editar a interface com recarga automática, use dois terminais:

- Terminal 1, servidor (a partir da raiz):

  ```bash
  dotnet run --project server/src/IndustrialSim.Server
  ```

- Terminal 2, Vite (a partir de `web/`):

  ```bash
  npm run dev
  ```

Abra o endereço que o Vite mostrar (normalmente <http://localhost:5173>). As chamadas para `/api` são repassadas ao servidor em `localhost:5080`.

| Comando (em `web/`) | O que faz |
| --- | --- |
| `npm run dev` | Servidor de desenvolvimento do Vite |
| `npm run lint` | ESLint (inclui texto fixo proibido), stylelint (só tokens de design) e Prettier |
| `npm run format` | Formata o código com o Prettier |
| `npm test` | Testes com Vitest |
| `npm run build` | Checagem de tipos e build para o `wwwroot` do servidor |

| Comando (em `server/`) | O que faz |
| --- | --- |
| `dotnet build` | Compila; avisos viram erros |
| `dotnet test` | Roda os testes xUnit (Microsoft Testing Platform) |

## Estrutura

```text
industrialsim-web/
├─ server/      solução .NET 10 (IndustrialSim.sln): src/ e tests/
├─ web/         site: Vite, React, TypeScript, i18next
├─ scenarios/   pacotes de cenário (a partir da Etapa 3)
├─ tools/       implementações de referência para validação cruzada
├─ deploy/      Docker, Caddy e PostgreSQL (Etapa 9)
└─ docs/        descritivo, plano, guias e evidências de validação
```

## Regras que o lint e o CI conferem

- **Nenhum texto de interface no código.** Todo texto vem de `web/src/locales/<idioma>/translation.json` e é usado com `t('chave')`. Texto fixo no JSX ou em `title`, `alt`, `placeholder` e `aria-label` faz o `npm run lint` falhar.
- **Nenhuma cor, fonte, espaçamento ou raio fora dos tokens.** Use `var(--…)` de [web/src/design/tokens.css](web/src/design/tokens.css); valores fixos fazem o stylelint falhar.
- **Avisos do .NET são erros** (`TreatWarningsAsErrors`).
- **Versões travadas:** `package-lock.json` e `packages.lock.json`. Para adicionar um pacote, confira a licença (MIT, Apache-2.0 ou BSD) e faça commit dos arquivos de lock.
- O CI (GitHub Actions) roda tudo isso a cada push e pull request; merge na `main` só com o CI verde.

## Problemas comuns

| Sintoma | Causa e solução |
| --- | --- |
| `A compatible .NET SDK was not found` | Falta o SDK 10. Instale-o (veja os pré-requisitos) e confira com `dotnet --list-sdks`. |
| `NU1004` / erro de lock file no restore | Um pacote mudou sem atualizar o `packages.lock.json`. Rode `dotnet restore --force-evaluate` em `server/` e faça commit dos locks. |
| `npm ci` reclama da versão do Node | Instale o Node 24 ou mais novo. |
| A página mostra "Não foi possível falar com o servidor." | O servidor não está rodando em `localhost:5080`, ou você abriu o Vite sem subir o servidor. |
| Página em branco em `localhost:5080` | Falta o build do web: rode `npm run build` em `web/` e recarregue a página. |
| Prettier acusa diferença de fim de linha | O repositório usa LF (`.gitattributes`). Rode `git add --renormalize .` ou faça um clone novo. |

## Licença

A definir.
