# Guia de identidade visual

Etapa D1, parte C. Proposta para aprovação: falta escolher o conceito de logotipo. Os valores ficam em [`tokens.json`](tokens.json), que é a fonte da Etapa D2; este guia explica como usá-los. Prancha visual: canvas "IndustrialSim · Identidade visual" (link em [`README.md`](README.md)).

## Nome

**IndustrialSim Web**, mantido pelo autor em 23/11/2026. No logotipo, "IndustrialSim" em semibold e "Web" em peso normal, na cor de texto secundário.

## Logotipo

Três conceitos, em [`logo/`](logo/): cada arquivo tem o símbolo e o logotipo em texto, na versão clara.

| Conceito | Símbolo | A favor | Contra |
| --- | --- | --- | --- |
| A · Tubulão ([SVG](logo/conceito-a-tubulao.svg)) | O tubulão da caldeira com o nível de água e a saída de vapor | O mais legível em 16 px; diz "planta de processo" de imediato | Preso à caldeira quando vierem outros cenários |
| B · Instrumento ([SVG](logo/conceito-b-instrumento.svg)) | O balão de instrumento da ISA-5.1 com uma resposta ao degrau | Fala com quem é da área e vale para qualquer cenário | A curva some em 16 px (fica só o balão) |
| C · Malha fechada ([SVG](logo/conceito-c-malha.svg)) | A seta de realimentação em volta do tubulão | Conta a ideia do produto: o seu controle fecha a malha | Lembra o ícone de "atualizar" em tamanho pequeno |

**Regras de uso**, valem para o conceito escolhido:

- **Versões:**
  - clara: traço em `text` e acento em `accent`;
  - escura: traço em `text` do tema escuro e acento em `accent` do tema escuro;
  - monocromática: tudo em `text`.
- **Área de proteção:** um quarto da altura do símbolo em volta de tudo.
- **Tamanho mínimo:** o símbolo sozinho em 16 px (favicon); o logotipo completo com 120 px de largura.
- **Não fazer:** girar, esticar, mudar cores fora das versões, pôr sombra ou gradiente, colocar o símbolo sobre fotos sem fundo liso.

**Arquivo final (com o autor):** escolhido o conceito, o SVG daqui serve de base. O texto do logotipo está como `<text>` e precisa virar curvas, para não depender da fonte instalada:
- no Inkscape: selecionar o texto e usar **Caminho › Objeto para caminho**, depois salvar como "SVG simples";
- no Figma: **Outline stroke** / **Flatten**.

A partir desse arquivo saem favicon (16, 32 e 48 px), ícone de app (192 e 512 px, para o PWA da Fase 4) e a versão para o cabeçalho do mapa de I/O impresso.

## Paleta

Valores exatos em [`tokens.json`](tokens.json); cada chave de `color` vira a variável CSS `--color-<chave>`, nos temas claro e escuro. O contraste de cada combinação usada foi conferido por script: **98 pares, todos passam no WCAG 2.2 AA** ([`contraste.md`](contraste.md)).

| Grupo | Tokens | Uso |
| --- | --- | --- |
| Neutros | `bg`, `surface`, `surface-sunken`, `border`, `border-strong`, `text`, `text-muted`, `text-disabled` | Fundo cinza da IHM, painéis brancos (claro) ou cinza-escuro (escuro), textos. `border` só divide (decorativa); `border-strong` contorna campos e controles (3:1). |
| Interação | `accent`, `accent-hover`, `on-accent`, `focus` | Links, botão principal, abas ativas, indicador de foco. **Nunca no desenho do processo.** |
| Processo | `equipment`, `equipment-on`, `water` | Traço de equipamentos e tubulações; equipamento em operação (preenchido); água no tubulão (o nível é o traço em `equipment-on`). |
| Alarme 1, 2, 3 | `alarm-N`, `on-alarm-N`, `alarm-N-text`, `alarm-N-surface`, `alarm-N-edge` (2 e 3) | Indicador (preenchimento), número dentro dele, texto do alarme, fundo da linha do alarme, borda do indicador. |
| Outros estados anormais | `forced`, `forced-surface`, `fault`, `fault-surface` | Valor forçado (magenta) e falha de instrumento (ciano-escuro). |
| Tendências | `trend-1` a `trend-5` | Séries dos gráficos, sem os tons de alarme. |
| Sobreposição | `overlay` | Fundo atrás de diálogos (com transparência; fora da verificação). |

### Regras de cor na IHM (ISA-101)

Fonte: ANSI/ISA-101.01-2015, consultada por meio de Hollifield et al., *The High Performance HMI Handbook* (2008). A conferência contra o texto da norma fica pendente se não houver acesso a ela.

1. **O normal é cinza.** Fundo neutro, equipamentos em cinza, valores em cor de texto. Nada colorido quando o processo está normal.
2. **Cor só para o anormal**, e cada cor tem um único significado:

   | Cor | Significado |
   | --- | --- |
   | Vermelho | Alarme de prioridade 1 |
   | Laranja | Alarme de prioridade 2 |
   | Amarelo | Alarme de prioridade 3 |
   | Magenta | Valor forçado |
   | Ciano-escuro | Falha de instrumento |

3. **Nunca só a cor** (WCAG 1.4.1).
   - A prioridade de alarme também vem pela forma e pelo número: círculo com 1, triângulo com 2, quadrado com 3.
   - Forçado e falha também vêm escritos ("FORÇADO", "FALHA").
   - Planta pausada é texto e borda forte, sem cor própria.
4. **Sem verde e vermelho para ligado e desligado.** Equipamento em operação é cinza-escuro preenchido; parado é só o contorno.
5. **O acento azul não entra no sinóptico.** Ele marca o que é clicável na interface, não o estado do processo.
6. **Sem piscar.** O sistema não tem reconhecimento de alarme. Piscar contínuo exigiria um controle para parar (WCAG 2.2.2) e poderia passar do limite de 3 piscadas por segundo (2.3.1).
7. **As séries de tendência se distinguem também pelo traço** (contínuo, tracejado, pontilhado), não só pela cor.

## Tipografia

- **IBM Plex Sans** para a interface e **IBM Plex Mono** para valores, endereços, tags e contagens.
  - As duas têm licença SIL Open Font License 1.1 (permissiva, não é GPL nem comercial).
  - Na D2, os arquivos das fontes são servidos pelo próprio site, sem carregar do Google Fonts durante o uso. Assim não há pedido a terceiros (LGPD) e tudo funciona na rede do laboratório.
- **Números sempre em mono:** os dígitos têm a mesma largura, então colunas e valores não "dançam" a 20 atualizações por segundo. A unidade vem depois do valor, em peso normal. O separador decimal segue o idioma; as unidades não mudam.

| Token | Tamanho | Peso | Uso |
| --- | --- | --- | --- |
| `font-size-2xl` | 32 px | 600 | Título de página |
| `font-size-xl` | 24 px | 600 | Título de seção, diálogo |
| `font-size-lg` | 20 px | 600 | Título de painel |
| `font-size-md` | 16 px | 400 | Texto corrido |
| `font-size-sm` | 14 px | 400 | Rótulos, tabelas, IHM |
| `font-size-xs` | 12 px | 600 | Rótulos de seção em caixa alta (com espaçamento de letras) |
| `font-size-value-lg` | 40 px | 600 (mono) | Valor principal do detalhe de instrumento |

## Espaçamento, forma e foco

- **Espaçamento:** escala de 4 px (`space-1` a `space-12`). **Raios:** 4 px (controles), 8 px (painéis), 12 px (cartões e painéis em VR).
- **Foco:** contorno de 2 px em `focus`, afastado 2 px do elemento, em todo controle (WCAG 2.4.7). Nenhum elemento fixo (barra de abas, aviso) pode cobrir o elemento em foco (WCAG 2.4.11).
- **Alvos:**
  - mínimo de 24 × 24 px (WCAG 2.5.8);
  - 44 px nos controles principais e na tela de toque;
  - 64 px nos painéis em VR (`size-target-*`).
- **Arrastar** (reordenar abas, zoom num trecho do gráfico) sempre com uma alternativa de um clique (WCAG 2.5.7): menu da aba com "mover para a esquerda/direita"; botões de zoom no gráfico.

## Ícones e símbolos

- **Interface:** [Lucide](https://lucide.dev), licença ISC (alguns ícones herdados do Feather, licença MIT). Traço de 2 px, 20 px. Lista prevista na prancha "Tipografia, símbolos e ícones". A versão exata é fixada na D2.
- **Ícone sozinho** só nos botões de aba (×, +) e no ⌂, sempre com nome acessível (`aria-label`).
- **Processo:** símbolos próprios, no estilo da ISA-5.1, traço em `equipment`: bomba, válvula de controle, balão de instrumento, tubulão, chave. O ícone de interface nunca substitui o símbolo de processo.

## Para a D2

- `web/src/design/tokens.css` passa a ser gerado ou copiado de `tokens.json`.
- **Nomes que mudam:**
  - `--color-alarm` e `--color-alarm-surface` viram `--color-alarm-1` e `--color-alarm-1-surface`;
  - `--color-trend-1` a `--color-trend-4` ganham valores novos e a série 5.
- **Nomes novos:**
  - neutros e interação: `surface-sunken`, `border-strong`, `text-disabled`, `accent-hover`, `on-accent`, `focus`;
  - alarmes: `on-alarm-N`, `alarm-N-text`, `alarm-N-edge`;
  - falha: `fault` e `fault-surface`;
  - famílias de fonte, escala de tamanhos, `line-width-*` e `size-target-*`.
- **Contraste no CI:** o verificador `tools/design/contraste.mjs` pode entrar no CI na D2, para uma mudança de cor nunca reprovar em silêncio.
