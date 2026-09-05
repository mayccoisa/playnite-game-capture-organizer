# Especificação técnica — Captura automática no Organizador de Capturas

Extensão: `GameCaptureOrganizer` (Playnite, net48). Versão alvo: 0.3.0.

---

## Origem

Hoje a extensão **organiza** o que o Xbox Game Bar já salvou: ela sabe qual jogo estava rodando
(caderno de sessões) e guarda cada arquivo na pasta certa. Ela nunca **dispara** uma captura.

A PlayniteAchievements e a PlayniteMemories fazem a metade que falta — capturar sozinho, por
tempo e por conquista — e cada uma com um limite que a nossa não precisa herdar:

| | PlayniteAchievements 3.1.3 | PlayniteMemories |
|---|---|---|
| Print periódico | não tem | sim, `IntervalMinutes` (padrão 15) |
| Clipe por conquista | sim, buffer rolante próprio | não |
| Motor | WGC + Media Foundation, ~9.300 linhas | `PrintWindow` / `BitBlt` sobre a janela do jogo |
| Cobertura | jogos das lojas que ela integra | qualquer jogo, mas o print sai **preto** onde `PrintWindow` falha (fullscreen exclusivo, DX acelerado) — e ela não percebe: `IsValidScreenshot` foi neutralizada e devolve `true` sempre (`ScreenCapture.cs`) |
| Onde grava | pasta do plugin | `Imagens\Playnite\<jogo>` |

A decisão que muda o custo: **o motor de captura não é nosso**. Quem grava é o Game Bar, que já
resolve fullscreen exclusivo, DX12 e HDR — e cujo resultado a nossa extensão já sabe arquivar.
A camada nova só decide **quando** apertar o botão.

---

## O que fica de fora desta versão

- Motor de captura próprio (WGC + Media Foundation). Fica atrás de uma interface, para entrar
  depois sem refazer o resto.
- Áudio separado, overlay de conquista embutido no vídeo, HDR tone map — tudo o que a
  PlayniteAchievements faz no re-encode.
- Conquista de Xbox, GOG, Epic, PSN e afins. Só Steam e RetroAchievements nesta versão.
- Visualizador de galeria dentro do Playnite.

---

## Fronteiras

| Fronteira | A regra que a sustenta |
|---|---|
| `ICaptureTrigger` (`SalvarClipe`, `TirarPrint`) com uma implementação `GameBarTrigger` | Foi a escolha de motor. Trocar Game Bar por WGC depois não pode encostar no agendador nem nos gatilhos |
| Quem dispara **não** arquiva | O arquivamento já existe e é bom: `OrganizerService` + `SessionIndex`. Duplicar a nomeação criaria a segunda régua que a extensão nasceu para eliminar |
| O casamento gatilho ↔ arquivo é por **horário**, não por interceptação | O Game Bar escreve o arquivo quando quer; esperar por ele travaria a sessão. Mesmo princípio da tolerância que o organizador já usa |
| Estado do Game Bar é **lido e avisado**, nunca corrigido | Mexer em `HKCU\...\GameDVR` no lugar do dono é alterar configuração de sistema sem ele pedir |
| Cada fonte de conquista é um `IAchievementSource` | Steam local e RetroAchievements têm ciclo de vida oposto (arquivo × rede). Um `if` no meio do agendador faria a falha de rede derrubar o watcher de arquivo |

---

## Onde a mudança bate

| Arquivo | O que muda |
|---|---|
| `CaptureOrganizerPlugin.cs` | `OnGameStarted` liga a sessão de captura; `OnGameStopped` desliga. Item de menu "Capturar agora" |
| `OrganizerSettings.cs` | Campos novos: ligar/desligar, intervalo de print, intervalo de clipe, tecla do gatilho manual, conquista ligada, credencial do RetroAchievements |
| `Ui/SettingsView.xaml` | Aba nova "Captura automática" |
| `CaptureCore.cs` | Marcador `{Motivo}` (`periodico`, `conquista`, `manual`) nos padrões de pasta e arquivo |
| `SessionIndex.cs` | A sessão passa a guardar os gatilhos disparados (horário + motivo), para o organizador carimbar o arquivo |
| **novo** `AutoCapture/ICaptureTrigger.cs`, `GameBarTrigger.cs` | `SendInput` de `Win+Alt+PrtScn` e `Win+Alt+G` |
| **novo** `AutoCapture/CaptureScheduler.cs` | Laço por sessão, um timer por tipo |
| **novo** `AutoCapture/GameBarState.cs` | Lê o registro e responde se dá para gravar |
| **novo** `Achievements/SteamStatsWatcher.cs`, `SteamBinaryKeyValues.cs` | Watcher + parser do `.bin` |
| **novo** `Achievements/RetroAchievementsSource.cs` | Polling da API durante sessão de emulador |
| **novo** `Input/GlobalHotkey.cs` | Hook de teclado global para o gatilho manual |

### Como a conquista da Steam é detectada (o achado que barateia a fatia)

A Steam grava o progresso local em
`<Steam>\appcache\stats\UserGameStats_<accountId3>_<appId>.bin`, em KeyValues binário. Um
`FileSystemWatcher` nesse arquivo avisa **no instante** do unlock, sem API, sem chave e offline.
É assim que a PlayniteAchievements faz (`SteamDataProvider.TryRegister` + `SteamLocalStatsReader`),
com poll de segurança porque o watcher perde evento em disco ocupado.

- `appId` vem do próprio Playnite (`Game.GameId` da biblioteca Steam);
- `accountId3` vem de `HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser`;
- o gatilho é o **diff** contra a foto tirada no início da sessão — sem isso, o backlog de
  conquistas antigas dispara um clipe cada na primeira leitura.

---

## Pré-requisito medido nesta máquina

A gravação em segundo plano do Game Bar (o "gravar o que aconteceu") é o que torna o clipe por
conquista possível — é ela que segura os segundos **anteriores** ao gatilho. Varri o registro
aqui e **não achei a chave que a liga** (`HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR`
tem só `KGLRevision`/`LastGameActivity`), o que indica que está desligada. Confirmar em
**Configurações › Jogos › Capturas** e ligar, escolhendo lá a janela (30 s a alguns minutos).

Consequência de projeto: a duração do clipe é **a do Windows**, igual para todos os jogos. Se um
dia isso incomodar, é exatamente o motivo para trocar o motor pelo WGC — não antes.

---

## Fatias

Cada uma fecha sozinha e é verificável com o Playnite aberto.

**F1 · Motor de gatilho + print periódico**
`ICaptureTrigger`, `GameBarTrigger`, `CaptureScheduler`, os campos de configuração e o
`{Motivo}`.
*Pronto quando:* com intervalo de 1 minuto e um jogo aberto pelo Playnite, os prints aparecem
em `Capturas Organizadas\<Jogo>\Screenshots` com `periodico` no nome, e nada é disparado com o
Playnite parado.

**F2 · Gatilho manual e clipe periódico**
Hook global de tecla + timer de clipe.
*Pronto quando:* a tecla configurada salva um clipe do Game Bar durante o jogo, e ele chega
organizado com `manual` no nome. Tecla desligada não engancha o hook.

**F3 · Conquista da Steam**
Watcher, parser do `.bin`, diff de sessão.
*Pronto quando:* uma conquista destravada num jogo Steam aberto pelo Playnite produz clipe e
print em até 2 s, e abrir o jogo com 50 conquistas antigas não dispara nada.

**F4 · Diagnóstico do Game Bar**
`GameBarState` na tela de configuração.
*Pronto quando:* com a gravação em segundo plano desligada, a tela diz isso em português e
ensina onde ligar, em vez de a captura falhar em silêncio.

**F5 · Regras por jogo**
Sobrescrever intervalo e ligar/desligar por jogo, pelo menu de contexto da biblioteca.
*Pronto quando:* um jogo marcado como "sem captura automática" não gera nada, com o global ligado.

**F6 · RetroAchievements**
*Pronto quando:* uma conquista num emulador mapeado no Playnite dispara a captura. Depende de
credencial — o usuário informa, não é inventada.

---

## Riscos conhecidos

- **`SendInput` não alcança janela elevada.** Jogo rodando como administrador com o Playnite
  normal ignora o atalho. Detectável (comparar elevação) e reportável; sem conserto por código.
- **O Game Bar recusa alguns processos** (ele mesmo decide o que é jogo). Onde recusar, o clipe
  não sai e o log precisa dizer isso — não pode virar arquivo faltando sem explicação.
- **`Win+Alt+G` sem gravação em segundo plano** não faz nada, silenciosamente. É a F4.
- **Print por PrintWindow como plano B** só entraria se algum dia sairmos do Game Bar; hoje ele
  seria o defeito que a PlayniteMemories tem (imagem preta salva como se fosse boa).

**F7 · Painel lateral de capturas** (pedido do dono em 05/09/2026, feito antes da F3)
Item próprio na barra lateral do Playnite (`GetSidebarItems`, `SiderbarItemType.View`) com a
galeria da pasta organizada: pastas do primeiro nível à esquerda, grade de miniaturas à direita,
filtro por tipo, e clique duplo abrindo a captura.
*Pronto quando:* com capturas já organizadas, o painel abre listando da mais recente para a mais
antiga, e a pasta de destino vazia mostra o que fazer em vez de uma tela em branco.

Duas coisas que **não devem ser "simplificadas"** aqui: (a) **o painel lê o disco e não guarda
índice próprio** — um índice paralelo discordaria da pasta no primeiro arquivo movido na mão, e
passaria a mentir com autoridade; (b) **vídeo não ganha miniatura falsa**: extrair um quadro
exigiria decodificador que a extensão não tem, então o cartão assume que é vídeo e mostra o
símbolo.
