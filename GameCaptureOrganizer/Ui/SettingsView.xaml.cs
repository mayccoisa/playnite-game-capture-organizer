using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GameCaptureOrganizer.Ui
{
    /// <summary>
    /// A tela de configuracao. Toda a estrutura mora no XAML; aqui ficam so os botoes que
    /// precisam do plugin (escolher pasta, organizar agora, atualizar).
    /// </summary>
    public partial class SettingsView : UserControl
    {
        private readonly CaptureOrganizerPlugin plugin;
        private readonly UpdateChecker updateChecker;

        public SettingsView(CaptureOrganizerPlugin plugin)
        {
            InitializeComponent();

            this.plugin = plugin;
            DataContext = plugin.SettingsModel;

            updateChecker = new UpdateChecker(
                plugin.PlayniteApi,
                PluginIdentity.Repo,
                PluginIdentity.ExtensionId,
                UpdateChecker.DefaultExtensionDir);

            VersionText.Text = updateChecker.CurrentVersion;
            UpdatePreview();
            RefreshGameBarStatus();
            RefreshSteamStatus();
        }

        // ---------------------------------------------------------------- captura automatica

        /// <summary>
        /// Le o estado do Game Bar e escreve na tela. Roda na abertura e no botao: descobrir que a
        /// gravacao estava desligada depois de duas horas de jogo e descobrir tarde demais.
        /// </summary>
        private void RefreshGameBarStatus()
        {
            try
            {
                var estado = plugin.ReadGameBarState();
                GameBarStatusText.Text = estado.Explain();
                GameBarFolderText.Text = string.IsNullOrWhiteSpace(estado.CapturesFolder)
                    ? string.Empty
                    : "O Windows salva as capturas em " + estado.CapturesFolder +
                      ". Essa pasta precisa estar na lista de origem, na aba Pastas.";
            }
            catch (Exception ex)
            {
                GameBarStatusText.Text = "Não consegui ler a configuração do Game Bar: " + ex.Message;
                GameBarFolderText.Text = string.Empty;
            }
        }

        private void OnCheckGameBar(object sender, RoutedEventArgs e)
        {
            RefreshGameBarStatus();
        }

        /// <summary>
        /// Diz se a Steam foi encontrada E se há progresso local para ler. As duas coisas são
        /// diferentes: a Steam pode estar instalada e a pasta de estatísticas ainda estar vazia
        /// porque nenhum jogo com conquista foi aberto naquele perfil.
        /// </summary>
        private void RefreshSteamStatus()
        {
            try
            {
                var steam = GameCaptureOrganizer.Achievements.SteamStats.ResolveSteamPath(Settings.SteamFolder);
                if (string.IsNullOrWhiteSpace(steam))
                {
                    SteamStatusText.Text = "Não encontrei a Steam nesta máquina. Se ela existe, escreva a pasta acima " +
                                           "(a que tem steam.exe). Sem isso, a captura por conquista fica desligada e o " +
                                           "resto continua funcionando.";
                    return;
                }

                var stats = GameCaptureOrganizer.Achievements.SteamStats.StatsFolder(steam);
                var arquivos = 0;
                try
                {
                    if (System.IO.Directory.Exists(stats))
                    {
                        arquivos = System.IO.Directory.GetFiles(stats, "UserGameStats_*.bin").Length;
                    }
                }
                catch (Exception)
                {
                }

                SteamStatusText.Text = arquivos > 0
                    ? "Steam em " + steam + " · " + arquivos + " jogo(s) com progresso local para ler."
                    : "Steam em " + steam + ", mas ainda não há progresso local em appcache\\stats. Ele aparece " +
                      "quando um jogo com conquista roda pela Steam.";
            }
            catch (Exception ex)
            {
                SteamStatusText.Text = "Não consegui procurar a Steam: " + ex.Message;
            }
        }

        private void OnCheckSteam(object sender, RoutedEventArgs e)
        {
            RefreshSteamStatus();
        }

        private void OnCaptureNow(object sender, RoutedEventArgs e)
        {
            plugin.CaptureNow();
        }

        private OrganizerSettings Settings
        {
            get { return plugin.SettingsModel.Settings; }
        }

        // ---------------------------------------------------------------- pastas

        private void OnAddSource(object sender, RoutedEventArgs e)
        {
            var folder = plugin.PlayniteApi.Dialogs.SelectFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            var atual = Settings.SourceFolders ?? string.Empty;
            Settings.SourceFolders = atual.TrimEnd('\r', '\n').Length == 0
                ? folder
                : atual.TrimEnd('\r', '\n') + Environment.NewLine + folder;
        }

        private void OnResetSource(object sender, RoutedEventArgs e)
        {
            Settings.SourceFolders = OrganizerSettings.CreateDefault().SourceFolders;
        }

        private void OnPickDestination(object sender, RoutedEventArgs e)
        {
            var folder = plugin.PlayniteApi.Dialogs.SelectFolder();
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Settings.DestinationFolder = folder;
            }
        }

        private void OnOpenDestination(object sender, RoutedEventArgs e)
        {
            plugin.OpenFolder(Settings.DestinationFolder);
        }

        // ---------------------------------------------------------------- padroes

        private void OnResetPatterns(object sender, RoutedEventArgs e)
        {
            Settings.FolderPattern = CaptureCore.DefaultFolderPattern;
            Settings.FilePattern = CaptureCore.DefaultFilePattern;
            UpdatePreview();
        }

        private void OnPreview(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        /// <summary>
        /// Mostra o caminho que o padrao atual produziria. Existe porque padrao de nome so se
        /// entende vendo o resultado — e um marcador escrito errado aparece aqui antes de virar
        /// uma pasta com nome esquisito.
        /// </summary>
        private void UpdatePreview()
        {
            try
            {
                var exemplo = new CaptureContext
                {
                    GameName = "Elden Ring",
                    Platform = "PC (Windows)",
                    Source = "Steam",
                    Kind = CaptureKind.Screenshot,
                    Timestamp = DateTime.Now,
                    OriginalName = "Elden Ring 15_02_2026 20_30_12"
                };

                var pasta = CaptureCore.BuildRelativeFolder(Settings.FolderPattern, exemplo);
                var nome = CaptureCore.BuildFileName(Settings.FilePattern, exemplo, ".png");
                PreviewText.Text = string.IsNullOrEmpty(pasta) ? nome : Path.Combine(pasta, nome);
            }
            catch (Exception ex)
            {
                PreviewText.Text = "padrão inválido: " + ex.Message;
            }
        }

        // ---------------------------------------------------------------- acoes

        private void OnOrganizeNow(object sender, RoutedEventArgs e)
        {
            plugin.RunInteractive();
        }

        private void OnOpenLog(object sender, RoutedEventArgs e)
        {
            plugin.OpenFile(plugin.LogPath);
        }

        private void OnCheckUpdates(object sender, RoutedEventArgs e)
        {
            if (updateChecker != null)
            {
                updateChecker.CheckInteractive();
            }
        }
    }
}
