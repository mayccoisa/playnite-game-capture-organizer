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

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
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

            scheduler.Start(game.Id.ToString(), game.Name);
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                scheduler.Stop();
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
            // Nada a recarregar: o servico le a configuracao a cada passada, de proposito.
            // Guardar uma copia aqui e o que faria "salvei e continuou no caminho antigo".
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
                            Source = NameOfSource(game)
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
                Icon = new TextBlock
                {
                    Text = "📷",
                    FontSize = 18,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                },
                Opened = () => new Ui.GalleryView(this)
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
