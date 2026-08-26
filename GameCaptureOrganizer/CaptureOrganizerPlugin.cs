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

            organizer = new OrganizerService(sessions, this, logPath, msg => logger.Info("[Capturas] " + msg));
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
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
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
            yield return new MainMenuItem { MenuSection = sec, Description = "Abrir a pasta organizada", Action = _ => OpenFolder(Settings.DestinationFolder) };
            yield return new MainMenuItem { MenuSection = sec, Description = "Abrir o log", Action = _ => OpenFile(logPath) };
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
