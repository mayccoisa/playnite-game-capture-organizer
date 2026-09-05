# Camada de captura automática — registro das versões 0.3.0 a 0.8.0

Produto: **Organizador de Capturas** (extensão Playnite, `mayccoisa/playnite-game-capture-organizer`).
Rodada de 05/09/2026. Documento pronto para publicar no Product Hub assim que o MCP voltar.

A extensão deixou de só **arquivar** captura e passou a **pedir** captura. Quem grava continua
sendo o **Xbox Game Bar** — a extensão decide o quando e arquiva o resultado no jogo certo. Foi
decisão do dono e não economia: o Game Bar resolve tela cheia exclusiva, DirectX 12 e HDR, que é
exatamente onde captura por janela (o caminho de outras extensões parecidas) salva imagem preta
sem perceber. O motor fica atrás da interface `ICaptureTrigger`, para trocar por Windows Graphics
Capture + Media Foundation depois sem encostar no resto.

## As seis versões

| Versão | Fatia | O que entrou |
|---|---|---|
| **0.3.0** | F1 | Print de tempos em tempos enquanto o jogo roda; marcador `{Motivo}` (`periodico`/`conquista`/`manual`); som curto de confirmação; aba **Captura automática**; diagnóstico do que o Windows permite |
| **0.4.0** | F7 | Painel **Capturas** na barra lateral do Playnite: pastas à esquerda, grade de miniaturas à direita, filtro por tipo, clique duplo abre |
| **0.5.0** | F3 | Conquista da **Steam** pelo arquivo local `appcache\stats\UserGameStats_*_<appId>.bin` — sem API, sem chave, sem internet |
| **0.6.0** | F2 | **Tecla de captura** que funciona dentro do jogo (padrão Ctrl+Shift+F12) e **clipe periódico** em relógio próprio |
| **0.7.0** | F5 | **Regras por jogo**: não capturar, ou intervalo diferente, pelo menu de contexto da biblioteca |
| **0.8.0** | F6 | Conquista de emulador pelo **RetroAchievements**, com credencial do usuário e consulta a cada 60 s |

## As decisões que não devem ser "simplificadas"

Cada uma nasceu de um modo de falhar concreto, e desfazê-la traz o modo de falhar de volta.

- **A primeira leitura de conquista é a linha de base, e é silenciosa.** Vale para Steam e para
  RetroAchievements. Sem isso, abrir um jogo com duzentas conquistas antigas dispararia captura
  como se todas tivessem acabado de sair.
- **Leitura que falha devolve NULO, nunca conjunto vazio.** O `.bin` da Steam é lido enquanto ela
  escreve, e o RetroAchievements responde um objeto de erro quando a credencial está errada. Tratar
  os dois como "nenhuma conquista" faria a leitura seguinte ver tudo como novo, e o jogo inteiro
  viraria uma rajada de capturas.
- **Watcher de arquivo tem uma segunda perna.** O aviso do sistema de arquivos perde evento em
  silêncio, então há releitura a cada 30 s. Só o watcher significaria conquista que não vira
  captura de vez em quando, sem sinal nenhum de que faltou.
- **Uma captura por momento.** Janela de 15 s entre capturas por conquista: cinco conquistas na
  mesma cena são um momento só, não cinco prints do mesmo instante.
- **Print e clipe saem com respiro entre os atalhos.** Colados, o Game Bar trata a segunda
  combinação como repetição da primeira e o clipe não sai — sem erro nenhum, o que é pior do que
  falhar.
- **Campo de regra vazio é herança, não "desligado".** Gravar o valor de hoje onde ninguém escolheu
  nada congelaria o padrão daquele dia, e mudar o global depois não alcançaria o jogo. Regra que
  fica vazia sai do disco.
- **A chave mestra vence qualquer regra por jogo.** "Desliguei e continuou capturando" mandaria
  procurar defeito no lugar errado.
- **A extensão só LÊ a configuração do Game Bar.** Ligar captura de jogo no lugar do dono é mexer
  em ajuste do sistema sem ele pedir. E "a chave não existe" ≠ "está desligado": medido nesta
  máquina, `AppCaptureEnabled` não existia e havia clipes gravados — ausência significa "no padrão".
- **O painel lê o disco e não guarda índice próprio.** Um índice paralelo discordaria da pasta no
  primeiro arquivo movido na mão, e passaria a mentir com autoridade. Vídeo não ganha miniatura
  falsa: extrair um quadro exigiria decodificador que a extensão não tem.
- **A tecla é `RegisterHotKey`, não gancho global de teclado.** O gancho veria todas as teclas do
  sistema, precisaria devolver rápido para não engasgar a digitação inteira do Windows, e é a
  assinatura que antivírus trata como keylogger. Combinação já usada por outro programa falha e
  avisa, em vez de roubar a tecla.
- **A consulta ao RetroAchievements é da CONTA, sem filtrar por jogo.** O código do jogo no RA não
  existe na biblioteca do Playnite, e adivinhá-lo pelo nome erraria em silêncio. Consequência
  registrada na tela: conquista da mesma conta em outro aparelho também dispara captura aqui.
- **A chave de API nunca vai para o log.** A URL da consulta a carrega no meio, e log é coisa que
  se compartilha.

## O que está provado, e o que não está

**Provado:** build sem aviso nem erro; suíte de 124 verificações com 0 falhas; CI verde nas seis
tags; cada release com `draft=false` e 1 asset; e o `.pext` publicado aberto por dentro, com a
versão certa no `extension.yaml` e os tipos novos presentes na DLL que a CI compilou.

**Não provado:** nada foi exercitado com um jogo aberto. Em 05/09/2026 a extensão sequer estava
instalada no Playnite do dono — o que explica o painel lateral não aparecer na busca dele. A Steam
não está instalada nesta máquina (sem chave de registro, sem `appcache\stats`), então a conquista
da Steam tem como prova apenas o parser rodando contra um `.bin` sintético montado no teste. E a
gravação em segundo plano do Game Bar segue desligada: sem ela, nenhum clipe sai.

## Pendências

1. Testar com um jogo aberto: intervalo em 1 minuto, conferir print, som e organização.
2. Ligar "Gravar o que aconteceu" em Configurações › Jogos › Capturas, para o clipe existir.
3. Validar a conquista da Steam quando a Steam existir na máquina.
4. Preencher usuário e chave do RetroAchievements e usar "Testar a conexão".
