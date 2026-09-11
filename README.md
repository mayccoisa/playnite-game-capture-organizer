# Organizador de Capturas — extensão do Playnite

Organiza os prints e os vídeos que o **Xbox Game Bar** salva, guardando cada captura na pasta do
jogo certo. Substitui o script de PowerShell que rodava pelo Playnite, com uma diferença que é o
motivo de ela existir: **a extensão sabe qual jogo estava rodando**, em vez de adivinhar pelo nome
do arquivo.

Roda igual no **PC** e no **ROG Ally** — o que muda entre os dois é só a configuração das pastas,
e não o código.

---

## O que ela faz

| | |
|---|---|
| **Origem** | Uma ou mais pastas do Game Bar (o padrão é `Vídeos\Captures`) |
| **Destino** | Uma pasta base, com a estrutura decidida por um padrão configurável |
| **Quando** | Ao fechar um jogo, ao abrir o Playnite, e no botão "Organizar capturas agora" |
| **Como** | Move (ou copia, se você preferir manter o original) |

Estrutura padrão:

```
Capturas Organizadas\
  Elden Ring\
    Screenshots\Elden Ring_2026-02-15_20-30-12.png
    Videos\Elden Ring_2026-02-15_20-31-00.mp4
```

Pasta e nome saem de dois padrões, editáveis na tela de configuração:

- pasta: `{Jogo}\{Tipo}`
- arquivo: `{Jogo}_{Data}_{Hora}`

Marcadores: `{Jogo}` `{Tipo}` `{Data}` `{Hora}` `{Ano}` `{Mes}` `{AnoMes}` `{Plataforma}`
`{Fonte}` `{Original}`. Marcador escrito errado **aparece como está** no resultado, em vez de
sumir — é assim que o erro de digitação fica visível antes de virar uma pasta com nome estranho.

---

## O painel Capturas

Uma entrada na barra lateral do Playnite, com duas abas.

**Capturas** mostra a pasta organizada sem abrir o Explorador, de duas maneiras, no seletor da
barra:

| Visualização | O que é | Para quê |
|---|---|---|
| **Todas as capturas** | Grade corrida, do mais recente para o mais antigo | "O que saiu hoje" — que não tem pasta |
| **Agrupadas por pasta** | Árvore à esquerda, grade à direita | Caçar dentro de uma pasta específica |

A escolha fica gravada. A árvore tem **todos os níveis** — com o padrão de fábrica, o jogo e dentro
dele Screenshots e Vídeos — e os números de cada pasta somam o que está nas subpastas. A caixa
**Incluir subpastas** alterna entre ver o jogo inteiro e ver só o que está diretamente na pasta
aberta. A pasta de primeiro nível mostra o ícone do jogo na biblioteca.

Na barra: **Capturar agora** pede um print ao Game Bar na hora, que é como conferir uma mudança de
configuração sem esperar o intervalo; **Organizar agora**, **Atualizar** e **Abrir a pasta** fazem
o que dizem.

**Configuração** é a mesma tela de Complementos › Configuração, hospedada ali dentro, com **Salvar**
e **Desfazer** próprios — a aba não tem o rodapé de OK/Cancelar do Playnite, e sem eles trocar de
aba perderia a edição calada.

O painel **lê o disco** e não guarda banco próprio: a pasta organizada é a verdade, e um índice
paralelo discordaria dela no primeiro arquivo movido à mão.

---

## De onde vem o nome do jogo

Em ordem de confiança, cada etapa desligável:

1. **A sessão do Playnite.** A extensão anota início e fim de cada jogo; a captura cujo horário
   cai dentro de uma sessão recebe o nome exato da biblioteca, com plataforma e fonte. É o dado
   mais confiável porque **não depende do título da janela**, que o Game Bar às vezes escreve
   diferente do nome do jogo.
2. **A biblioteca.** O título lido do arquivo é procurado na biblioteca do Playnite pela forma
   normalizada (sem acento, sem pontuação, minúsculo). É o que casa `Marvel's Spider-Man` com
   `Marvels Spider Man`.
3. **O nome do arquivo.** O carimbo de data/hora do Game Bar é cortado e o resto vira o título.

**Captura sem nome confiável fica na origem**, de propósito: uma pasta "Sem nome" cheia de coisa
perdida é pior do que o arquivo continuar onde estava.

---

## Instalação

Baixe o `.pext` da [última release](https://github.com/mayccoisa/playnite-game-capture-organizer/releases)
e dê dois cliques **com o Playnite fechado**. Depois disso, as atualizações saem pela própria
extensão: **Configurações › Avançado › Verificar atualizações** baixa a nova versão, fecha o
Playnite, troca os arquivos e reabre sozinho.

---

## Desenvolvimento

```bash
dotnet build GameCaptureOrganizer/GameCaptureOrganizer.csproj -c Release
```

```bash
dotnet run --project tests/OrganizerTests/OrganizerTests.csproj -c Debug
```

A suíte exercita a lógica pura (nome, padrão, sanitização, casamento por sessão) **e** uma passada
real em disco numa pasta temporária. Ela roda na CI antes do build: pacote com lógica quebrada não
chega a ser gerado.

Release completo (testes → build → `.pext` → GitHub):

```bash
powershell -ExecutionPolicy Bypass -File release.ps1 -Version 0.2.0 -Commit
```

### Onde está o quê

| Arquivo | O que é |
|---|---|
| `CaptureCore.cs` | Lógica pura: nomes, padrões, sanitização. Sem Playnite, sem WPF, sem disco |
| `SessionIndex.cs` | O caderno de sessões e o casamento captura ↔ jogo |
| `OrganizerService.cs` | A varredura e a movimentação dos arquivos |
| `CaptureOrganizerPlugin.cs` | Os ganchos do Playnite, os menus e a consulta à biblioteca |
| `Gallery/` | A leitura da pasta organizada e a árvore do painel. Pura: sem Playnite, sem WPF |
| `Ui/` | O painel, a tela de configuração e o tema (o mesmo do Playnite Hub) |
| `UpdateChecker.cs` | Atualização pela própria extensão |
