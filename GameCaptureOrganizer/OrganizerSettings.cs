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
