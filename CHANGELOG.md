# Changelog

## 0.9.0 — 2026-09-10

O painel **Capturas** vira o lugar onde tudo acontece: a configuração passa a morar nele, a pasta
organizada passa a ser navegável por inteiro, e dá para pedir um print sem esperar o intervalo.

### Capturar agora, na própria tela

Botão **Capturar agora** na barra do painel. Ele já existia no menu principal; o que mudou é estar
onde a pessoa está quando quer conferir alguma coisa.

O motivo é o ciclo de conferência. Com o intervalo em quinze minutos, mexer numa configuração e
descobrir se ela valeu custava quinze minutos — e quem está ajustando o padrão de pasta faz isso
várias vezes seguidas. Agora custa um clique: o atalho sai, o gatilho é registrado, e a próxima
passada carimba `{Motivo}` no arquivo.

**A grade não recarrega sozinha depois do clique**, de propósito. O Game Bar grava o arquivo depois
de o botão voltar; recarregar naquele instante mostraria a pasta sem a captura recém-pedida, o que
parece falha do botão. Use **Atualizar** um segundo depois.

### Duas visualizações, com seletor

O painel passou a ter **duas** maneiras de olhar a mesma pasta, escolhidas no seletor à esquerda da
barra. Uma **não** substitui a outra:

- **Todas as capturas** — a grade corrida, como o painel sempre foi: tudo que foi organizado, do
  mais recente para o mais antigo, sem coluna nenhuma no caminho. É a resposta para "o que saiu
  hoje", que não tem pasta — a captura de agora pode estar em qualquer uma.
- **Agrupadas por pasta** — a árvore nova, para caçar dentro de uma pasta específica.

A escolha **fica gravada**: é gosto, não tarefa, e reabrir o Playnite na outra visualização
obrigaria a trocar toda vez. O texto do cabeçalho acompanha — falar em "árvore à esquerda" na grade
corrida mandaria procurar uma coluna que não está lá.

### A árvore de pastas

A lista da esquerda, na visualização agrupada, virou **árvore**. Antes ela enxergava só o
**primeiro** nível abaixo do destino: com o padrão de fábrica `{Jogo}\{Tipo}`, dava para abrir
"Elden Ring" e nada mais — tudo que o padrão separou depois disso (o tipo, e a data em quem usa
padrão com data) não tinha como ser aberto. Agora todos os níveis aparecem.

- **Os números são acumulados.** "Elden Ring · 2 prints · 1 vídeo" conta o que está nas subpastas.
  Contar só o que está imediatamente dentro mostraria a pasta do jogo como vazia no padrão de
  fábrica, porque ali dentro só existem as duas subpastas.
- **A caixa "Incluir subpastas"** decide entre as duas leituras dentro da árvore. Marcada — que é o
  padrão — a pasta do jogo mostra tudo que há embaixo dela; desmarcada, só o que está diretamente
  nela. Ela some na grade corrida, onde não há pasta escolhida para ter subpasta.
- **O cartão passou a dizer de que pasta veio.** Na grade corrida, e sempre que as subpastas estão
  incluídas, a grade mistura origens e o cartão sozinho não dizia de onde era.
- **A árvore sai do disco, não do padrão configurado.** O padrão pode ter mudado ontem, e as
  capturas antigas continuam onde o padrão antigo as colocou. Derivar a árvore da configuração
  esconderia justamente as mais antigas.
- **Atualizar não perde o seu lugar.** A pasta aberta é reencontrada pelo caminho; se ela sumiu do
  disco, a seleção cai em "Tudo" em vez de ficar vazia sem explicação.

### O ícone do jogo ao lado da pasta

A pasta de primeiro nível mostra o ícone do jogo na biblioteca do Playnite. Os ícones entram
**depois**, fora da thread de interface, pelo mesmo motivo das miniaturas: uma pasta organizada com
duzentos jogos são duzentas leituras de PNG, e fazê-las na montagem da árvore travaria o app ao
abrir o painel. Só a pasta de primeiro nível procura jogo — "Screenshots" e "2026-09" não são
títulos, e procurá-los na biblioteca só gastaria tempo para não achar nada.

Jogo cujo ícone é uma URL (comum na Steam) fica sem ícone: a extensão não baixa imagem por conta
própria.

### Configuração dentro do painel

Aba **Configuração**, ao lado de Capturas. É a **mesma tela** que o Playnite abre em Complementos ›
Configuração, hospedada ali — e não uma segunda cópia dos campos, que divergiria no primeiro campo
acrescentado só de um lado.

Como aqui não existe o rodapé de OK/Cancelar do Playnite, a aba tem **Salvar** e **Desfazer**
próprios: sem eles, mexer num campo e trocar de aba perderia a edição sem avisar. **Salvar** passa
pelas mesmas validações da janela do Playnite — gravar daqui uma configuração que a janela
recusaria deixaria a extensão num estado impossível de alcançar por lá.

Voltar para a aba Capturas relê o disco, porque a pasta de destino pode ter acabado de mudar.

### Ícone novo na barra lateral

O emoji 📷 virou desenho vetorial. Emoji na barra lateral tem dois defeitos que só aparecem na
máquina do outro: ele é desenhado pela fonte de emoji do Windows, então sai **colorido** no meio de
uma barra de símbolos monocromáticos, e o tamanho não acompanha o do tema — no ROG Ally, com a
barra estreita, ficava visivelmente maior que os vizinhos. O vetor pinta com a cor de texto do tema
e estica junto com a barra.

### Por baixo

- O `TreeViewItem` do WPF ganhou template próprio na `Theme.xaml`. O padrão **não acompanha tema
  escuro**: a seta expandida é desenhada com `#FF595959` sobre `#FF262626`, que no fundo desta
  extensão é cinza-escuro sobre cinza-escuro — a seta some e a pasta parece não ter nada dentro.
  Trocar só o `Foreground` não resolve, porque a seta não lê o `Foreground`.
- A montagem da árvore (`Gallery/GalleryTree.cs`) é **pura**: sem WPF, sem Playnite. O ícone entra
  nela como `object` justamente para ela continuar exercitável fora do app, e são 30 verificações
  novas na suíte.

## 0.8.0 — 2026-09-05

Conquistas de **emulador**, pelo RetroAchievements — a última peça da camada de captura automática.

### Como funciona

Diferente da Steam, aqui **não existe arquivo local para observar**: o emulador fala direto com o
servidor do RetroAchievements. Então o caminho é outro, e o preço está à vista:

- precisa da **credencial da sua conta** (usuário e chave de API web do site), que você informa na
  aba Captura automática — não há descoberta automática possível, e credencial não se inventa;
- é **consulta de tempos em tempos** (60 segundos por padrão, mínimo 30), não aviso instantâneo;
- só roda enquanto um jogo está aberto pelo Playnite, e só nos jogos que não são da Steam — esses
  já têm o caminho local, que é melhor.

O botão **Testar a conexão** prova a credencial contra o servidor de verdade, em vez de só
validar o formato: chave com um caractere trocado passa em qualquer validação local e falharia
calada na hora do jogo.

### O que você precisa saber antes de ligar

- **A consulta pergunta pelas conquistas recentes da CONTA, sem filtrar por jogo.** O código do
  jogo no RetroAchievements não existe na biblioteca do Playnite, e adivinhá-lo pelo nome erraria
  em silêncio. Consequência real: conquista que a mesma conta destravar em outro aparelho ao mesmo
  tempo também dispara captura aqui.
- **A chave fica em texto puro** no arquivo de configuração da extensão, como todas as outras
  opções. Ela nunca aparece no log — a URL da consulta a carrega no meio, e log é coisa que se
  compartilha.
- **Erro de rede não dispara nada e não derruba a vigia.** A internet cair no meio do jogo é
  ocorrência esperada; o intervalo dobra a cada falha seguida, até dez minutos, e volta ao normal
  quando o servidor responde. Credencial recusada é dita uma vez, não a cada minuto.
- **Resposta de erro não é "nenhuma conquista".** Credencial errada devolve um objeto com
  mensagem, e não uma lista vazia; tratar os dois como iguais faria a consulta seguinte ver tudo
  como novo e o jogo inteiro viraria uma rajada de capturas.

### Com esta versão, a camada de captura automática está completa

Print de tempos em tempos, clipe periódico, tecla de captura, conquista da Steam, conquista de
emulador, regras por jogo e o painel lateral para ver tudo — todos em cima do Xbox Game Bar, que
continua sendo quem grava.

## 0.7.0 — 2026-09-05

Regras por jogo: dá para desligar a captura automática num jogo específico, ou dar a ele um
intervalo diferente do padrão.

### Onde se define

No **menu de contexto do jogo**, na biblioteca — que é onde você está quando pensa "neste aqui eu
não quero":

- **Não capturar neste jogo** (o item mostra o estado atual e vira "Voltar a capturar")
- **Intervalo do print neste jogo…**
- **Intervalo do clipe neste jogo…**
- **Usar o padrão neste jogo**

Vale para vários jogos selecionados de uma vez.

### O que é herdado, e o que é exceção

Cada campo da regra é opcional, e **vazio significa "usa o padrão"** — não "desligado". É a
diferença que faz o jogo sem regra própria acompanhar quando você muda o intervalo global; se a
extensão gravasse o valor de hoje onde você não escolheu nada, ela congelaria o padrão daquele dia.

**A chave mestra vence qualquer regra.** Com a captura automática desligada na configuração, um
jogo marcado como "capturar" continua sem capturar. "Desliguei e continuou capturando" faria você
procurar o defeito no lugar errado.

Marcar um jogo como "não capturar" tira também a captura por conquista dele — é uma decisão só.

### Onde elas ficam visíveis

Na aba **Captura automática**, uma lista dos jogos com regra própria, com o resumo do que cada uma
faz e o botão para voltar ao padrão. Regra invisível é regra esquecida: sem essa lista, um jogo
silenciado há três meses vira "a extensão parou de funcionar neste jogo".

Regra cujo jogo saiu da biblioteca aparece como órfã em vez de sumir — é o que permite limpá-la.

## 0.6.0 — 2026-09-05

Tecla de captura que funciona **dentro do jogo**, e clipe de vídeo de tempos em tempos.

### Tecla de captura

Uma combinação registrada no Windows dispara print — e o clipe dos últimos segundos, se você
quiser — com o jogo em primeiro plano. O padrão é **Ctrl+Shift+F12**, e dá para trocar na aba
Captura automática (F1 a F12, PrintScreen, Insert, Home, End, PageUp, PageDown).

O padrão não é F12 sozinho de propósito: essa é a tecla do overlay da Steam, e roubar o atalho de
quem já usa é a receita de "parou de funcionar e não sei por quê". Pelo mesmo motivo, se outro
programa já tiver registrado a combinação escolhida, a extensão **não** toma a tecla — ela avisa
na tela de configuração para você escolher outra.

A tecla é registrada no Windows (`RegisterHotKey`), e não por um gancho global de teclado. O
gancho veria todas as teclas do sistema, precisaria devolver rápido para não engasgar a digitação
inteira do Windows, e é a assinatura que antivírus trata como keylogger.

### Clipe de tempos em tempos

Um segundo intervalo, independente do print, que salva os últimos segundos pelo Game Bar. Nasce
**desligado** (0 minutos): clipe periódico ocupa disco de verdade, e depende da gravação em
segundo plano do Game Bar estar ligada.

Os dois relógios são independentes: print a cada 5 minutos e clipe a cada 30 não têm divisor comum
útil, e amarrar um ao outro faria o intervalo de um puxar o do outro.

### Detalhe que evita clipe que não sai

Quando print e clipe saem juntos, há um respiro entre os dois atalhos. Mandar `Win+Alt+PrtScn` e
`Win+Alt+G` colados faz o Game Bar tratar a segunda combinação como repetição da primeira, e o
clipe não sai — sem erro nenhum, o que é pior do que falhar.

## 0.5.0 — 2026-09-05

Captura quando uma **conquista da Steam** é destravada: print na hora e, se a gravação em segundo
plano do Game Bar estiver ligada, o clipe dos segundos anteriores.

### Como ela sabe da conquista

Lendo o arquivo de progresso local da Steam (`appcache\stats\UserGameStats_*_<appId>.bin`, no
formato KeyValues binário da Valve). **Sem API, sem chave, sem internet** — a Steam reescreve esse
arquivo no instante do desbloqueio, e a extensão fica de olho nele enquanto o jogo roda.

Diferente da extensão que inspirou esta versão, aqui **não** se lê o arquivo de schema: para
disparar uma captura basta saber que o conjunto de conquistas cresceu, não qual delas foi.

### Decisões que evitam captura errada

- **A primeira leitura é a linha de base, e é silenciosa.** Abrir um jogo com duzentas conquistas
  antigas não dispara nada — só o que aparece depois conta.
- **Leitura falha não é "zero conquistas".** O arquivo pode ser lido no meio da escrita da Steam;
  nesse caso a extensão descarta a leitura em vez de concluir que tudo sumiu (e, na leitura
  seguinte, que tudo foi destravado de uma vez).
- **Uma captura por momento.** Cinco conquistas na mesma cena são um momento só: há uma janela de
  15 segundos entre capturas por conquista.
- **Dois caminhos ao mesmo tempo:** o aviso do sistema de arquivos, que responde no instante, e
  uma releitura a cada 30 segundos, porque esse aviso perde evento e a falha seria silenciosa.
- **Bit que some não conta.** O disparo sai de evidência de conquista nova, nunca de "o arquivo
  mudou".

### Onde encontrar

Na aba **Captura automática**, junto com o print periódico: ligar/desligar, salvar clipe também, e
um campo para a pasta da Steam com o botão "Procurar a Steam" — que diz se ela foi encontrada e
quantos jogos têm progresso local para ler.

### Limite conhecido

Vale para jogo da **biblioteca Steam** aberto pelo Playnite, porque é de lá que sai o appId. Jogo
mapeado à mão não tem conquista para observar e continua com o print de tempos em tempos, que era
o pedido original.

## 0.4.0 — 2026-09-05

Painel **Capturas** na barra lateral do Playnite: a pasta organizada vista de dentro do app, sem
abrir o Explorador.

### O que entrou

- **Item próprio na barra lateral.** À esquerda, as pastas do primeiro nível com a conta do que
  há em cada uma (com o padrão de fábrica, são os jogos); à direita, a grade de miniaturas, do
  mais recente para o mais antigo. Clique duplo abre a captura no programa padrão.
- **Filtro** por tudo, só prints ou só vídeos, e os botões "Organizar agora", "Atualizar" e
  "Abrir a pasta" na própria barra do painel.
- **Estado vazio que ensina**: pasta de destino não escolhida e pasta sem nada dizem o que fazer,
  em vez de mostrar uma tela em branco.

### Duas escolhas que valem saber

- **O painel lê o disco, e não guarda índice próprio.** A pasta organizada é a verdade: um índice
  paralelo discordaria dela no primeiro arquivo movido na mão, e passaria a mentir com autoridade.
- **Vídeo não ganha miniatura.** Extrair um quadro exigiria um decodificador que a extensão não
  tem; o cartão assume que é vídeo e mostra o símbolo, em vez de desenhar um quadro inventado.

As miniaturas são decodificadas pequenas e fora da thread de interface, e trocar de pasta cancela
o que ainda estava carregando — é o que impede uma pasta com centenas de prints em 4K de segurar
o Playnite.

## 0.3.0 — 2026-09-05

A extensão deixa de só **arquivar** captura e passa a **pedir** captura: enquanto um jogo aberto
pelo Playnite estiver rodando, ela tira print de tempos em tempos sozinha.

Quem grava continua sendo o **Xbox Game Bar** — a extensão aperta o atalho dele na hora certa e
arquiva o arquivo no jogo certo, como sempre fez. Foi decisão de projeto e não economia: o Game
Bar resolve tela cheia exclusiva, DirectX 12 e HDR, que é exatamente onde captura por janela
(o caminho de outras extensões parecidas) salva imagem preta sem perceber.

### O que entrou

- **Print de tempos em tempos**, na aba nova **Captura automática**. Vem **desligada**: quem
  instalou a extensão para organizar não deve ser surpreendido por print sozinho depois de
  atualizar.
- **Som curto de confirmação** a cada captura pedida pela extensão. O Game Bar já mostra o aviso
  dele por cima do jogo, mas aquele aviso prova que o *Game Bar* capturou; o som é o que diz que
  foi o **intervalo da extensão** que disparou — sem sair do jogo para conferir.
- **Marcador `{Motivo}`** nos padrões de pasta e de nome: `periodico`, `conquista` ou `manual`.
  Fica vazio no que você capturou pelo Game Bar na mão, e isso é o caso comum, não erro.
- **"Tirar print agora (Game Bar)"** no menu principal e na tela de configuração, para provar o
  caminho inteiro sem esperar o intervalo.
- **Diagnóstico do Game Bar** na mesma aba: a extensão lê o que o Windows permite e explica em
  português o que ligar e onde. Ela só **lê** — ligar captura de jogo no seu lugar seria mexer em
  ajuste do sistema sem você pedir.

### Antes de testar

O print periódico funciona com a configuração padrão do Windows. **O clipe de vídeo ainda não
existe nesta versão**, e quando existir vai depender da *gravação em segundo plano* do Game Bar
(Configurações › Jogos › Capturas, "Gravar o que aconteceu"), que vem desligada de fábrica — é
ela que segura os segundos anteriores ao momento.

Em jogo rodando **como administrador** com o Playnite normal, o atalho não chega: o Windows recusa
entrada sintética vinda de processo menos privilegiado. A extensão avisa no log em vez de falhar
calada.

## 0.2.0 — 2026-08-26

Corrige o caso em que o Game Bar nomeia o arquivo pelo **título da janela**, que nem sempre é o
nome do jogo. Descoberto no Palworld: a janela dele se chama `Pal`, então as capturas antigas
foram parar numa pasta `Pal`.

### O que entrou

- **Tabela de apelidos** (`Pal = Palworld`, um por linha) na aba Organização. O apelido é aplicado
  antes da busca na biblioteca, então o nome traduzido ainda ganha plataforma e fonte.
- **Casamento por prefixo** com a biblioteca: `Pal` vira `Palworld` quando esse é o **único**
  jogo que começa com o texto lido. Com dois candidatos (`Palworld` e `Paladins`) ele não escolhe
  nenhum — ir para a pasta errada em silêncio é pior do que ficar com o nome cru.
- O log passa a dizer qual dos caminhos resolveu o nome: sessão, apelido, biblioteca, prefixo ou
  nome do arquivo.

### Depois de atualizar

A pasta `Pal` já criada não se move sozinha: renomeie para `Palworld` (ou junte com a existente).
Capturas novas, feitas com o jogo aberto pelo Playnite, já vinham certas pela sessão — o conserto
vale para captura antiga e para captura feita fora do Playnite.

## 0.1.0 — 2026-08-26

Primeira versão. Substitui o script `GameCaptureWatcher.ps1` que rodava pelo Playnite.

### O que entrou

- Organização das capturas do Game Bar por jogo, com padrão de pasta e de nome configuráveis.
- Nome do jogo vindo da **sessão do Playnite** (com fallback para a biblioteca e para o nome do
  arquivo), em vez de depender do título da janela.
- Execução ao fechar um jogo, ao abrir o Playnite e por botão/menu.
- Arquivo ainda em uso é devolvido para a passada seguinte, em vez de virar erro.
- Tela de configuração no tema do Playnite Hub, com prévia do padrão e botão de atualização.
- Log por passada, dizendo para onde cada arquivo foi e de onde veio o nome do jogo.

### O que mudou em relação ao script

| Script | Extensão |
|---|---|
| Caminhos escritos no código (`C:\Users\WEON-ADMIN\...`) | Configuração por aparelho: o mesmo pacote serve PC e ROG Ally |
| Nome do jogo adivinhado pelo nome do arquivo | Sessão do Playnite primeiro; o nome do arquivo é o último recurso |
| `Start-Sleep 2` antes de mover vídeo | Teste de arquivo em uso: o que ainda está gravando fica para a próxima passada |
| Tudo numa pasta por jogo | Padrão configurável (`{Jogo}\{Tipo}` por default) |
| Colisão resolvida com `-Force` (sobrescrevia) | Colisão vira ` (2)`, ` (3)`… — nenhuma captura é perdida |
| `Stop-Process -Id $PID` no fim | Roda dentro do Playnite, sem processo solto |
