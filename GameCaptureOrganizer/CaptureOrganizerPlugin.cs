using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace GameCaptureOrganizer
{
    public class CaptureOrganizerPlugin : GenericPlugin, ILibraryLookup
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse(PluginIdentity.PluginGuid);

        public OrganizerSettingsViewModel SettingsModel { get; }
        public OrganizerSettings Settings { get { return SettingsModel.Settings; } }

        private readonly SessionIndex sessions;
        private readonly OrganizerService organizer;
        private readonly AutoCapture.TriggerLog triggers;
        private readonly AutoCapture.ICaptureTrigger trigger;
        private readonly AutoCapture.CaptureScheduler scheduler;
        private Achievements.SteamAchievementWatcher achievements;
        private Achievements.RetroAchievementsWatcher retro;
        private Input.GlobalHotkey hotkey;
        private readonly AutoCapture.GameRules rules;
        private readonly string logPath;
        private int pendingRun;

        public CaptureOrganizerPlugin(IPlayniteAPI api) : base(api)
        {
            SettingsModel = new OrganizerSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };

            var dataPath = GetPluginUserDataPath();
            logPath = Path.Combine(dataPath, "organizador.log");

            sessions = new SessionIndex(Path.Combine(dataPath, "sessoes.json"));
            sessions.Load();

            triggers = new AutoCapture.TriggerLog(Path.Combine(dataPath, "gatilhos.json"));
            triggers.Load();

            rules = new AutoCapture.GameRules(Path.Combine(dataPath, "regras-por-jogo.json"));
            rules.Load();

            organizer = new OrganizerService(sessions, this, logPath, msg => logger.Info("[Capturas] " + msg), triggers);

            trigger = new AutoCapture.GameBarTrigger(msg => logger.Info("[Captura automática] " + msg));
            scheduler = new AutoCapture.CaptureScheduler(
                trigger, triggers, () => Settings, msg => logger.Info("[Captura automática] " + msg));
        }

        /// <summary>Para a tela de configuração dizer, em português, o que o Windows permite hoje.</summary>
        public AutoCapture.GameBarState ReadGameBarState()
        {
            return AutoCapture.GameBarState.Read();
        }

        public string LogPath { get { return logPath; } }

        // ---------------------------------------------------------------- ciclo de vida

        // ---------------------------------------------------------------- atalho de teclado

        /// <summary>
        /// (Re)registra a tecla de captura. Roda na thread de UI porque a janela de mensagens que
        /// recebe o aviso do Windows nasce presa à thread que a criou.
        /// </summary>
        public void RefreshHotkey()
        {
            try
            {
                PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                {
                    if (hotkey == null)
                    {
                        hotkey = new Input.GlobalHotkey(
                            () => CaptureByHotkey(),
                            msg => logger.Info("[Atalho] " + msg));
                    }

                    if (!Settings.HotkeyEnabled)
                    {
                        hotkey.Unregister();
                        return;
                    }

                    hotkey.Register(Settings.HotkeyCtrl, Settings.HotkeyAlt, Settings.HotkeyShift, Settings.HotkeyKey);
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Não consegui preparar o atalho de captura.");
            }
        }

        /// <summary>
        /// A tecla foi apertada. O trabalho sai da thread de UI na hora: o Playnite está no meio da
        /// fila de mensagens do Windows, e mandar dois atalhos com espera entre eles ali dentro
        /// congelaria a janela por quase um segundo.
        /// </summary>
        private void CaptureByHotkey()
        {
            var comClipe = Settings.HotkeySavesClip;
            Task.Run(() =>
            {
                try
                {
                    scheduler.Capture(AutoCapture.CaptureReason.Manual, comClipe, false);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "A captura pelo atalho falhou.");
                }
            });
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            RefreshHotkey();

            if (!Settings.OrganizeOnStartup)
            {
                return;
            }

            // Fire and forget: varrer pasta fria (ou de cartao SD no Ally) leva segundos, e isso
            // nao pode segurar a abertura do Playnite.
            Task.Run(() => RunQuietly());
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            var game = args.Game;
            if (game == null)
            {
                return;
            }

            try
            {
                sessions.Begin(
                    game.Id.ToString(),
                    game.Name,
                    NameOfPlatform(game),
                    NameOfSource(game),
                    DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Não consegui registrar o início da sessão.");
            }

            try
            {
                StartAutoCapture(game);
            }
            catch (Exception ex)
            {
                // Captura automática que falha não pode levar junto o registro da sessão, que é o
                // que faz a organização saber de quem é cada arquivo.
                logger.Error(ex, "Não consegui ligar a captura automática.");
            }
        }

        /// <summary>
        /// Liga o relógio da captura automática e, quando ele vai valer, confere o que o Windows
        /// permite. O aviso sai UMA vez, no começo da sessão: descobrir que o Game Bar estava
        /// desligado depois de duas horas de jogo é descobrir tarde demais.
        /// </summary>
        private void StartAutoCapture(Game game)
        {
            if (!Settings.AutoCaptureEnabled)
            {
                return;
            }

            var estado = AutoCapture.GameBarState.Read();
            if (!estado.CanTakeScreenshot)
            {
                logger.Warn("[Captura automática] " + estado.Explain());
                if (Settings.ShowNotification)
                {
                    Notify(estado.Explain());
                }

                return;
            }

            var plano = AutoCapture.EffectiveCapture.Resolve(Settings, rules.Get(game.Id.ToString()));
            if (!plano.Enabled)
            {
                logger.Info("[Captura automática] \"" + game.Name + "\" está marcado para não capturar.");
                return;
            }

            scheduler.Start(game.Id.ToString(), game.Name, plano);
            StartAchievementWatch(game, estado, plano);
        }

        /// <summary>
        /// Liga a vigia de conquistas do jogo: a da Steam quando ele vem da biblioteca Steam, a do
        /// RetroAchievements no resto.
        ///
        /// São dois caminhos porque as fontes são opostas: a Steam grava um arquivo local, que dá
        /// para observar de graça e no instante; o RetroAchievements só existe no servidor deles,
        /// e exige credencial e consulta de tempos em tempos. Um "if" no meio de um watcher só
        /// faria a falha de rede derrubar o vigia de arquivo.
        ///
        /// Jogo que não tem conquista em nenhum dos dois continua com o print de tempos em tempos,
        /// que era o pedido original — e isso não é falha.
        /// </summary>
        private void StartAchievementWatch(Game game, AutoCapture.GameBarState estado, AutoCapture.EffectiveCapture plano)
        {
            if (!plano.Achievements)
            {
                return;
            }

            var salvarClipeNaConquista = Settings.AchievementSavesClip && estado.CanSaveClip;

            string appId;
            if (!Achievements.SteamStats.TryGetAppId(game.PluginId, game.GameId, out appId))
            {
                // Não é jogo da Steam: a conquista, se existir, é do RetroAchievements.
                StartRetroWatch(salvarClipeNaConquista);
                return;
            }

            var pasta = Achievements.SteamStats.StatsFolder(
                Achievements.SteamStats.ResolveSteamPath(Settings.SteamFolder));
            if (string.IsNullOrWhiteSpace(pasta))
            {
                logger.Info("[Conquistas] Não achei a Steam (registro nem pasta configurada); nada a vigiar.");
                return;
            }

            var salvarClipe = Settings.AchievementSavesClip && estado.CanSaveClip;
            if (Settings.AchievementSavesClip && !estado.CanSaveClip)
            {
                // Dito uma vez, no começo: descobrir no fim do jogo que nenhum clipe saiu é tarde.
                logger.Warn("[Conquistas] " + estado.Explain());
                if (Settings.ShowNotification)
                {
                    Notify("As conquistas vão render só print: " + estado.Explain());
                }
            }

            achievements = new Achievements.SteamAchievementWatcher(
                pasta,
                Achievements.SteamStats.StatsFilter(appId),
                _ => scheduler.CaptureForAchievement(salvarClipe),
                msg => logger.Info("[Conquistas] " + msg));

            achievements.Start();
        }

        /// <summary>
        /// Vigia do RetroAchievements. Sem credencial ela nem começa — e o silêncio aqui é
        /// deliberado: quem não configurou nada não fez nada errado, e um aviso por sessão de jogo
        /// de emulador viraria ruído em quem só quer o print periódico.
        /// </summary>
        private void StartRetroWatch(bool salvarClipe)
        {
            if (string.IsNullOrWhiteSpace(Settings.RetroUser) || string.IsNullOrWhiteSpace(Settings.RetroApiKey))
            {
                return;
            }

            retro = new Achievements.RetroAchievementsWatcher(
                Settings.RetroUser,
                Settings.RetroApiKey,
                Settings.RetroPollSeconds,
                _ => scheduler.CaptureForAchievement(salvarClipe),
                msg => logger.Info("[RetroAchievements] " + msg));

            retro.Start();
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                scheduler.Stop();

                if (achievements != null)
                {
                    achievements.Dispose();
                    achievements = null;
                }

                if (retro != null)
                {
                    retro.Dispose();
                    retro = null;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não consegui parar a captura automática.");
            }

            var game = args.Game;
            if (game != null)
            {
                try
                {
                    sessions.End(game.Id.ToString(), DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Não consegui registrar o fim da sessão.");
                }
            }

            if (!Settings.OrganizeAfterGame)
            {
                return;
            }

            var espera = Math.Max(0, Settings.DelaySeconds);
            Task.Run(() =>
            {
                // A espera existe porque o Game Bar ainda esta fechando o arquivo do video quando
                // o jogo morre. Ela e um amortecedor, nao a garantia: quem garante e o teste de
                // arquivo em uso, que devolve o arquivo para a proxima passada em vez de falhar.
                Thread.Sleep(TimeSpan.FromSeconds(espera));
                RunQuietly();
            });
        }

        // ---------------------------------------------------------------- execucao

        /// <summary>
        /// Passada silenciosa: nada aqui abre janela. O fim de jogo dispara exatamente quando a
        /// pessoa volta para o Playnite, muitas vezes em tela cheia num portatil, onde um modal e
        /// uma armadilha. Falha vira log e, no maximo, notificacao do proprio Playnite.
        /// </summary>
        public void RunQuietly()
        {
            // Duas passadas concorrentes disputariam o mesmo arquivo; a segunda so espera.
            if (Interlocked.CompareExchange(ref pendingRun, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var result = organizer.Run(Settings);
                if (result.Organized > 0 && Settings.ShowNotification)
                {
                    Notify(result.Summary());
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Falha ao organizar as capturas.");
                if (Settings.ShowNotification)
                {
                    Notify("Não consegui organizar as capturas: " + ex.Message);
                }
            }
            finally
            {
                Interlocked.Exchange(ref pendingRun, 0);
            }
        }

        /// <summary>Passada pedida pela pessoa: aqui pode ter barra de progresso e resposta na tela.</summary>
        public void RunInteractive()
        {
            OrganizeResult result = null;
            Exception failure = null;

            PlayniteApi.Dialogs.ActivateGlobalProgress(
                progress =>
                {
                    try
                    {
                        result = organizer.Run(Settings);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                },
                new GlobalProgressOptions("Organizando capturas...", false));

            if (failure != null)
            {
                logger.Error(failure, "Falha ao organizar as capturas.");
                PlayniteApi.Dialogs.ShowErrorMessage(failure.Message, PluginIdentity.DisplayName);
                return;
            }

            PlayniteApi.Dialogs.ShowMessage(result.Summary(), PluginIdentity.DisplayName);
        }

        private void Notify(string message)
        {
            try
            {
                // A colecao de notificacoes e observada pela UI: tocar nela fora da thread de UI
                // derruba o app, e o fim de jogo NAO roda na thread de UI.
                PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        "gamecapture-" + Guid.NewGuid().ToString("N"),
                        message,
                        NotificationType.Info)));
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não consegui exibir a notificação.");
            }
        }

        public void OnSettingsSaved()
        {
            // O serviço lê a configuração a cada passada, de propósito: guardar uma cópia aqui é o
            // que faria "salvei e continuou no caminho antigo". A exceção é o atalho, que vive no
            // Windows e não numa variável — trocar a tecla exige tirar a antiga do registro.
            RefreshHotkey();
        }

        public override void Dispose()
        {
            try
            {
                if (hotkey != null)
                {
                    hotkey.Dispose();
                    hotkey = null;
                }

                scheduler.Dispose();

                if (achievements != null)
                {
                    achievements.Dispose();
                    achievements = null;
                }

                if (retro != null)
                {
                    retro.Dispose();
                    retro = null;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Falha ao encerrar a captura automática.");
            }

            base.Dispose();
        }

        // ---------------------------------------------------------------- biblioteca

        private Dictionary<string, GameHint> index;
        private DateTime indexBuiltAt;

        /// <summary>
        /// Casa o titulo lido do arquivo com um jogo da biblioteca, comparando pela forma
        /// normalizada. O indice e reconstruido a cada 5 minutos: biblioteca grande nao pode ser
        /// varrida por arquivo, e uma copia eterna perderia o jogo importado hoje.
        /// </summary>
        public GameHint Find(string rawName)
        {
            var key = CaptureCore.NormalizeName(rawName);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                if (index == null || DateTime.UtcNow - indexBuiltAt > TimeSpan.FromMinutes(5))
                {
                    var novo = new Dictionary<string, GameHint>();
                    foreach (var game in PlayniteApi.Database.Games)
                    {
                        var k = CaptureCore.NormalizeName(game.Name);
                        if (string.IsNullOrEmpty(k) || novo.ContainsKey(k))
                        {
                            continue;
                        }

                        novo[k] = new GameHint
                        {
                            Name = game.Name,
                            Platform = NameOfPlatform(game),
                            Source = NameOfSource(game),
                            IconPath = FullPathOf(game.Icon)
                        };
                    }

                    index = novo;
                    indexBuiltAt = DateTime.UtcNow;
                }

                GameHint hint;
                return index.TryGetValue(key, out hint) ? hint : null;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não consegui consultar a biblioteca.");
                return null;
            }
        }

        /// <summary>
        /// Último recurso antes do nome cru: o único jogo da biblioteca cujo título começa com o
        /// texto lido. Resolve o título de janela abreviado — a janela do Palworld se chama "Pal".
        /// Havendo mais de um candidato, devolve nulo: escolher seria inventar.
        /// </summary>
        public GameHint FindByPrefix(string rawName)
        {
            var key = CaptureCore.NormalizeName(rawName);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                // Reaproveita o índice montado pelo Find (e o reconstrói se ainda não existir).
                Find(rawName);
                if (index == null)
                {
                    return null;
                }

                var unico = CaptureCore.PickUniquePrefixMatch(key, index.Keys);
                if (unico == null)
                {
                    return null;
                }

                GameHint hint;
                return index.TryGetValue(unico, out hint) ? hint : null;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não consegui consultar a biblioteca por prefixo.");
                return null;
            }
        }

        /// <summary>Jogo pode ter mais de uma plataforma, e pode nao ter nenhuma. Nada de assumir a primeira sem checar.</summary>
        private static string NameOfPlatform(Game game)
        {
            if (game == null || game.Platforms == null || game.Platforms.Count == 0)
            {
                return null;
            }

            var first = game.Platforms.FirstOrDefault();
            return first == null ? null : first.Name;
        }

        private static string NameOfSource(Game game)
        {
            return game == null || game.Source == null ? null : game.Source.Name;
        }

        /// <summary>
        /// O caminho no disco de uma imagem da biblioteca. O campo guardado e RELATIVO a pasta de
        /// dados do Playnite ("abc123\icon.png"), entao entregar ele cru a um Image do WPF nao
        /// desenha nada e tambem nao reclama — some em silencio, que e o pior jeito de falhar.
        ///
        /// Jogo da Steam costuma trazer o icone como URL; nesse caso nao ha arquivo local e o
        /// painel simplesmente nao mostra icone, em vez de baixar imagem por conta propria.
        /// </summary>
        private string FullPathOf(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                return null;
            }

            if (databasePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                databasePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var caminho = PlayniteApi.Database.GetFullFilePath(databasePath);
                return !string.IsNullOrWhiteSpace(caminho) && File.Exists(caminho) ? caminho : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// O icone do jogo cujo nome bate com o da pasta, para o painel desenhar ao lado dela.
        /// Passa pelo mesmo indice de <see cref="Find"/>, entao a pasta escrita pelo organizador
        /// acha o jogo pelo mesmo criterio que a nomeou — nenhum segundo casamento para divergir.
        /// </summary>
        public string GameIconPath(string folderName)
        {
            var hint = Find(folderName);
            return hint == null ? null : hint.IconPath;
        }

        // ---------------------------------------------------------------- menus e tela

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            const string sec = PluginIdentity.MenuSection;
            yield return new MainMenuItem { MenuSection = sec, Description = "Organizar capturas agora", Action = _ => RunInteractive() };
            yield return new MainMenuItem { MenuSection = sec, Description = "Tirar print agora (Game Bar)", Action = _ => CaptureNow() };
            yield return new MainMenuItem { MenuSection = sec, Description = "Abrir a pasta organizada", Action = _ => OpenFolder(Settings.DestinationFolder) };
            yield return new MainMenuItem { MenuSection = sec, Description = "Abrir o log", Action = _ => OpenFile(logPath) };
        }

        /// <summary>
        /// Print pedido na hora. Serve de teste do caminho inteiro sem esperar o intervalo: o
        /// atalho sai, o gatilho é registrado, e a próxima passada carimba {Motivo} no arquivo.
        /// </summary>
        public void CaptureNow()
        {
            var estado = AutoCapture.GameBarState.Read();
            if (!estado.CanTakeScreenshot)
            {
                PlayniteApi.Dialogs.ShowMessage(estado.Explain(), PluginIdentity.DisplayName);
                return;
            }

            if (scheduler.CaptureNow(AutoCapture.CaptureReason.Manual))
            {
                Notify("Print pedido ao Game Bar. Ele aparece organizado na próxima passada.");
                return;
            }

            PlayniteApi.Dialogs.ShowMessage(
                "O Windows recusou o atalho do Game Bar. O caso comum é o jogo estar rodando como " +
                "administrador e o Playnite não — entrada sintética não sobe de nível.",
                PluginIdentity.DisplayName);
        }

        // ---------------------------------------------------------------- regras por jogo

        /// <summary>
        /// As exceções por jogo, no menu de contexto da biblioteca — que é onde a pessoa está
        /// quando pensa "neste aqui eu não quero".
        ///
        /// O item de ligar/desligar mostra o estado ATUAL no próprio texto. Menu que não diz o que
        /// está valendo obriga a abrir a configuração para descobrir, e aí ele não serviu para nada.
        /// </summary>
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            const string sec = PluginIdentity.MenuSection;
            var jogos = args.Games == null ? new List<Game>() : args.Games.ToList();
            if (jogos.Count == 0)
            {
                yield break;
            }

            var regra = jogos.Count == 1 ? rules.Get(jogos[0].Id.ToString()) : new AutoCapture.GameRule();
            var desligado = regra.Enabled.HasValue && !regra.Enabled.Value;

            yield return new GameMenuItem
            {
                MenuSection = sec,
                Description = desligado
                    ? "Voltar a capturar neste jogo"
                    : "Não capturar neste jogo",
                Action = _ => SetRule(jogos, r => r.Enabled = desligado ? (bool?)null : false)
            };

            yield return new GameMenuItem
            {
                MenuSection = sec,
                Description = "Intervalo do print neste jogo…",
                Action = _ => AskInterval(jogos, true)
            };

            yield return new GameMenuItem
            {
                MenuSection = sec,
                Description = "Intervalo do clipe neste jogo…",
                Action = _ => AskInterval(jogos, false)
            };

            yield return new GameMenuItem
            {
                MenuSection = sec,
                Description = "Usar o padrão neste jogo",
                Action = _ =>
                {
                    foreach (var jogo in jogos)
                    {
                        rules.Clear(jogo.Id.ToString());
                    }

                    Notify(jogos.Count == 1
                        ? "\"" + jogos[0].Name + "\" voltou a seguir o padrão."
                        : jogos.Count + " jogos voltaram a seguir o padrão.");
                }
            };
        }

        /// <summary>Uma linha da lista de exceções, na tela de configuração.</summary>
        public class RuleRow
        {
            public string GameId { get; set; }
            public string Text { get; set; }
        }

        /// <summary>
        /// As exceções com o NOME do jogo resolvido pela biblioteca. Regra cujo jogo não existe
        /// mais aparece como órfã em vez de sumir: é o que permite limpá-la, e some sozinha do
        /// disco quando a pessoa manda voltar ao padrão.
        /// </summary>
        public IList<RuleRow> RuleRows()
        {
            var linhas = new List<RuleRow>();

            foreach (var par in rules.All())
            {
                var nome = par.Key;
                try
                {
                    Guid id;
                    if (Guid.TryParse(par.Key, out id))
                    {
                        var jogo = PlayniteApi.Database.Games.Get(id);
                        nome = jogo != null ? jogo.Name : "(jogo removido da biblioteca)";
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Não consegui resolver o nome do jogo da regra.");
                }

                linhas.Add(new RuleRow { GameId = par.Key, Text = nome + " — " + par.Value.Summary() });
            }

            return linhas.OrderBy(l => l.Text, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public void ClearRule(string gameId)
        {
            rules.Clear(gameId);
        }

        private void SetRule(IList<Game> jogos, Action<AutoCapture.GameRule> alterar)
        {
            foreach (var jogo in jogos)
            {
                var id = jogo.Id.ToString();
                var regra = rules.Get(id);
                alterar(regra);
                rules.Set(id, regra);
            }

            if (jogos.Count == 1)
            {
                var regra = rules.Get(jogos[0].Id.ToString());
                Notify("\"" + jogos[0].Name + "\": " + regra.Summary() + ".");
            }
            else
            {
                Notify(jogos.Count + " jogos atualizados.");
            }
        }

        /// <summary>
        /// Pergunta o intervalo. Texto vazio devolve o jogo ao padrão — por isso a caixa já abre
        /// com o valor que está valendo, e não vazia: assim a pessoa vê o que vai mudar.
        /// </summary>
        private void AskInterval(IList<Game> jogos, bool print)
        {
            var atual = jogos.Count == 1
                ? (print ? rules.Get(jogos[0].Id.ToString()).ScreenshotIntervalMinutes
                         : rules.Get(jogos[0].Id.ToString()).ClipIntervalMinutes)
                : null;

            var padrao = print ? Settings.ScreenshotIntervalMinutes : Settings.ClipIntervalMinutes;

            var resposta = PlayniteApi.Dialogs.SelectString(
                (print ? "Minutos entre os prints" : "Minutos entre os clipes") +
                " neste jogo. Deixe vazio para usar o padrão (" + padrao + " min); 0 desliga.",
                PluginIdentity.DisplayName,
                atual.HasValue ? atual.Value.ToString() : string.Empty);

            if (!resposta.Result)
            {
                return;
            }

            int? minutos;
            if (!AutoCapture.GameRules.TryParseInterval(resposta.SelectedString, out minutos))
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Não entendi \"" + resposta.SelectedString + "\". Use um número de 0 a 1440, ou deixe vazio para o padrão.",
                    PluginIdentity.DisplayName);
                return;
            }

            SetRule(jogos, r =>
            {
                if (print)
                {
                    r.ScreenshotIntervalMinutes = minutos;
                }
                else
                {
                    r.ClipIntervalMinutes = minutos;
                }
            });
        }

        public void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não consegui abrir a pasta.");
            }
        }

        public void OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    PlayniteApi.Dialogs.ShowMessage("Ainda não há log: nenhuma passada foi registrada.", PluginIdentity.DisplayName);
                    return;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não consegui abrir o log.");
            }
        }

        /// <summary>
        /// O painel lateral. Uma entrada só, e ela abre a galeria da pasta organizada — é a
        /// resposta para "ver as capturas sem sair do Playnite".
        /// </summary>
        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = "Capturas",
                Type = SiderbarItemType.View,
                Icon = SidebarIcon(),
                Opened = () => new Ui.GalleryView(this)
            };
        }

        /// <summary>
        /// O icone da barra lateral. Era o emoji 📷, e emoji na barra lateral tem dois defeitos que
        /// so aparecem na maquina do outro: ele e desenhado pela fonte de emoji do Windows, entao
        /// sai COLORIDO no meio de uma barra de simbolos monocromaticos, e o tamanho dele nao
        /// acompanha o do tema — no ROG Ally, com a barra estreita, ficava visivelmente maior que
        /// os vizinhos.
        ///
        /// O vetor resolve os dois: pinta com a cor de texto do tema em uso e estica junto com a
        /// barra. O furo da lente e o EvenOdd ("F0"), e nao um segundo desenho por cima — por cima
        /// exigiria saber a cor do FUNDO, que muda com o tema.
        /// </summary>
        private System.Windows.FrameworkElement SidebarIcon()
        {
            const string data =
                "F0 M3,7 L7,7 L8.5,4.5 L15.5,4.5 L17,7 L21,7 " +
                "A2,2 0 0 1 23,9 L23,19 A2,2 0 0 1 21,21 L3,21 " +
                "A2,2 0 0 1 1,19 L1,9 A2,2 0 0 1 3,7 Z " +
                "M8,14 A4,4 0 1 1 16,14 A4,4 0 1 1 8,14 Z";

            System.Windows.Media.Brush cor = null;
            try
            {
                cor = PlayniteApi.Resources.GetResource("TextBrush") as System.Windows.Media.Brush;
            }
            catch (Exception)
            {
                cor = null;
            }

            var desenho = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse(data),
                // Sem cor do tema o Fill fica nulo e o icone some SEM ERRO NENHUM: a barra lateral
                // mostra um espaco em branco clicavel. O branco de reserva e feio num tema claro,
                // mas visivel — e visivel ganha de invisivel.
                Fill = cor ?? System.Windows.Media.Brushes.White,
                Stretch = System.Windows.Media.Stretch.Uniform
            };

            return new Viewbox
            {
                Child = desenho,
                Width = 20,
                Height = 20,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
        }

        /// <summary>Abre a captura no programa padrão do Windows.</summary>
        public void OpenCapture(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    // Arquivo movido ou apagado por fora depois da varredura: avisa, em vez de a
                    // pessoa achar que o clique não funcionou.
                    PlayniteApi.Dialogs.ShowMessage(
                        "Essa captura não está mais no disco. Use \"Atualizar\" para reler a pasta.",
                        PluginIdentity.DisplayName);
                    return;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não consegui abrir a captura.");
            }
        }

        /// <summary>
        /// Grava a configuracao como ela esta. Existe para o painel guardar a preferencia de
        /// visualizacao, que e escolhida fora da tela de configuracao e nao passa pelo ciclo de
        /// BeginEdit/EndEdit do Playnite.
        /// </summary>
        public void SaveSettings()
        {
            SavePluginSettings(Settings);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsModel;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new Ui.SettingsView(this);
        }
    }
}
