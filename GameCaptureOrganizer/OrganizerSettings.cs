using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace GameCaptureOrganizer
{
    /// <summary>
    /// O que a pessoa configura. Tudo que muda de maquina para maquina mora aqui — e essa e a
    /// razao de a extensao existir: o PC e o ROG Ally rodam o MESMO codigo e so discordam nestes
    /// campos, em vez de manter dois scripts com caminhos escritos na mao.
    /// </summary>
    public class OrganizerSettings : ObservableObject
    {
        private string sourceFolders;
        private string destinationFolder;
        private string folderPattern = CaptureCore.DefaultFolderPattern;
        private string filePattern = CaptureCore.DefaultFilePattern;
        private string imageExtensions = CaptureCore.DefaultImageExtensions;
        private string videoExtensions = CaptureCore.DefaultVideoExtensions;
        private bool moveFiles = true;
        private bool organizeAfterGame = true;
        private bool organizeOnStartup = true;
        private int delaySeconds = 15;
        private int toleranceMinutes = 10;
        private bool matchBySession = true;
        private bool matchByLibrary = true;
        private bool fallbackToFileName = true;
        private bool showNotification = true;
        private bool writeLog = true;
        private bool matchByPrefix = true;
        // O Palworld ja vem na tabela porque foi ele que a originou: a janela dele se chama
        // "Pal", entao a captura nascia com esse nome. Nao e chute — foi medido numa passada real.
        private string nameAliases = "Pal = Palworld";

        /// <summary>Uma pasta por linha. O Game Bar sabe gravar em mais de um lugar (video e print separados).</summary>
        public string SourceFolders
        {
            get { return sourceFolders; }
            set { SetValue(ref sourceFolders, value); }
        }

        public string DestinationFolder
        {
            get { return destinationFolder; }
            set { SetValue(ref destinationFolder, value); }
        }

        public string FolderPattern
        {
            get { return folderPattern; }
            set { SetValue(ref folderPattern, value); }
        }

        public string FilePattern
        {
            get { return filePattern; }
            set { SetValue(ref filePattern, value); }
        }

        public string ImageExtensions
        {
            get { return imageExtensions; }
            set { SetValue(ref imageExtensions, value); }
        }

        public string VideoExtensions
        {
            get { return videoExtensions; }
            set { SetValue(ref videoExtensions, value); }
        }

        /// <summary>Mover libera espaco, que e o que importa no Ally. Copiar existe para testar sem risco.</summary>
        public bool MoveFiles
        {
            get { return moveFiles; }
            set { SetValue(ref moveFiles, value); }
        }

        public bool OrganizeAfterGame
        {
            get { return organizeAfterGame; }
            set { SetValue(ref organizeAfterGame, value); }
        }

        public bool OrganizeOnStartup
        {
            get { return organizeOnStartup; }
            set { SetValue(ref organizeOnStartup, value); }
        }

        /// <summary>Segundos de espera depois que o jogo fecha, para o Game Bar terminar de gravar o video.</summary>
        public int DelaySeconds
        {
            get { return delaySeconds; }
            set { SetValue(ref delaySeconds, value); }
        }

        /// <summary>Folga, em minutos, dos dois lados da sessao ao casar uma captura com um jogo.</summary>
        public int ToleranceMinutes
        {
            get { return toleranceMinutes; }
            set { SetValue(ref toleranceMinutes, value); }
        }

        public bool MatchBySession
        {
            get { return matchBySession; }
            set { SetValue(ref matchBySession, value); }
        }

        /// <summary>Depois de ler o nome no arquivo, procura o titulo equivalente na biblioteca do Playnite.</summary>
        public bool MatchByLibrary
        {
            get { return matchByLibrary; }
            set { SetValue(ref matchByLibrary, value); }
        }

        public bool FallbackToFileName
        {
            get { return fallbackToFileName; }
            set { SetValue(ref fallbackToFileName, value); }
        }

        public bool ShowNotification
        {
            get { return showNotification; }
            set { SetValue(ref showNotification, value); }
        }

        public bool WriteLog
        {
            get { return writeLog; }
            set { SetValue(ref writeLog, value); }
        }

        /// <summary>Casa "Pal" com "Palworld" quando UM unico jogo da biblioteca comeca com o texto lido.</summary>
        public bool MatchByPrefix
        {
            get { return matchByPrefix; }
            set { SetValue(ref matchByPrefix, value); }
        }

        /// <summary>Tabela "titulo da janela = nome do jogo", um por linha.</summary>
        public string NameAliases
        {
            get { return nameAliases; }
            set { SetValue(ref nameAliases, value); }
        }

        // ---------------------------------------------------------------- captura automatica

        private bool autoCaptureEnabled;
        private int screenshotIntervalMinutes = 15;
        private int triggerToleranceSeconds = 90;
        private bool playSoundOnCapture = true;
        private bool achievementCaptureEnabled = true;
        private bool achievementSavesClip = true;
        private string steamFolder = string.Empty;

        /// <summary>
        /// Pasta da Steam, quando a descoberta automatica falha (instalacao portatil, outro
        /// perfil). Vazio significa "descubra sozinho", que e o caso normal.
        /// </summary>
        public string SteamFolder
        {
            get { return steamFolder; }
            set { SetValue(ref steamFolder, value); }
        }

        /// <summary>
        /// Capturar quando uma conquista da Steam e destravada. Depende da chave mestra
        /// <see cref="AutoCaptureEnabled"/>, e por isso pode nascer ligada: quem liga a captura
        /// automatica quer exatamente isto, e quem nao liga nada continua sem nada.
        /// </summary>
        public bool AchievementCaptureEnabled
        {
            get { return achievementCaptureEnabled; }
            set { SetValue(ref achievementCaptureEnabled, value); }
        }

        /// <summary>
        /// Alem do print, salvar o clipe dos ultimos segundos na conquista. Exige a gravacao em
        /// segundo plano do Game Bar ligada; sem ela o atalho nao faz nada, e quem avisa e a
        /// propria tela de configuracao.
        /// </summary>
        public bool AchievementSavesClip
        {
            get { return achievementSavesClip; }
            set { SetValue(ref achievementSavesClip, value); }
        }

        /// <summary>
        /// Um bipe curto quando a extensao pede a captura. O Game Bar ja mostra o aviso dele por
        /// cima do jogo, mas aquele aviso prova que o GAME BAR capturou — nao que foi a extensao.
        /// O som e a unica confirmacao de que o nosso relogio disparou, sem sair do jogo.
        /// </summary>
        public bool PlaySoundOnCapture
        {
            get { return playSoundOnCapture; }
            set { SetValue(ref playSoundOnCapture, value); }
        }

        /// <summary>
        /// Desligada por padrao de proposito: quem ja usa a extensao instalou para ORGANIZAR, e uma
        /// atualizacao que comeca a disparar print sozinha seria surpresa, nao melhoria.
        /// </summary>
        public bool AutoCaptureEnabled
        {
            get { return autoCaptureEnabled; }
            set { SetValue(ref autoCaptureEnabled, value); }
        }

        /// <summary>De quantos em quantos minutos pedir um print enquanto o jogo roda. Zero desliga.</summary>
        public int ScreenshotIntervalMinutes
        {
            get { return screenshotIntervalMinutes; }
            set { SetValue(ref screenshotIntervalMinutes, value); }
        }

        /// <summary>
        /// Quanto o carimbo do arquivo pode se afastar do gatilho e ainda ser o mesmo evento. O
        /// print sai em menos de um segundo; o clipe dos ultimos segundos nasce carimbado la atras,
        /// e e por causa dele que a folga e generosa.
        /// </summary>
        public int TriggerToleranceSeconds
        {
            get { return triggerToleranceSeconds; }
            set { SetValue(ref triggerToleranceSeconds, value); }
        }

        /// <summary>
        /// Caminhos padrao do proprio aparelho. Nunca ha caminho de outra maquina escrito no
        /// codigo: e assim que a mesma versao serve o PC e o Ally sem edicao.
        /// </summary>
        public static OrganizerSettings CreateDefault()
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            return new OrganizerSettings
            {
                SourceFolders = Path.Combine(videos, "Captures"),
                DestinationFolder = Path.Combine(pictures, "Capturas Organizadas")
            };
        }

        public List<string> SourceFolderList()
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(SourceFolders))
            {
                return list;
            }

            foreach (var line in SourceFolders.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var folder = line.Trim().Trim('"');
                if (folder.Length > 0 && !list.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(folder);
                }
            }

            return list;
        }
    }

}
