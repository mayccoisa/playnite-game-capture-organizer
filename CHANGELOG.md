# Changelog

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
