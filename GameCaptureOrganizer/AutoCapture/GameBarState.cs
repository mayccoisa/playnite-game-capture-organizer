using System;
using Microsoft.Win32;

namespace GameCaptureOrganizer.AutoCapture
{
    /// <summary>Tres estados, porque "a chave nao existe" NAO e "esta desligado".</summary>
    public enum ToggleState
    {
        /// <summary>A chave nao existe: vale o padrao do Windows, que muda de item para item.</summary>
        NotConfigured = 0,
        On = 1,
        Off = 2
    }

    /// <summary>
    /// O que o Windows diz sobre o Game Bar. Leitura pura: esta classe NUNCA escreve no registro —
    /// ligar captura de jogo no lugar do dono da maquina e mexer em configuracao de sistema sem
    /// ele pedir.
    ///
    /// A distincao entre "desligado" e "nao configurado" e o ponto da classe. Medido nesta maquina
    /// em 05/09/2026: <c>AppCaptureEnabled</c> nao existia e mesmo assim havia clipes gravados na
    /// pasta de capturas. Ou seja, ausencia significa "no padrao", e tratar isso como desligado
    /// faria a tela de configuracao acusar um problema que nao existe.
    /// </summary>
    public class GameBarState
    {
        private const string GameDvrKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
        private const string GameConfigStoreKey = @"System\GameConfigStore";
        private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
        private const string UserShellFoldersKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
        private const string CapturesFolderGuid = "{EDC0FE71-98D8-4F4A-B920-C8DC133CB165}";

        /// <summary>Captura de jogo em geral (print e gravacao manual). Padrao do Windows: ligada.</summary>
        public ToggleState Capture { get; set; }

        /// <summary>
        /// A gravacao em segundo plano — "gravar o que aconteceu". Padrao do Windows: DESLIGADA.
        /// E ela que segura os segundos ANTERIORES ao gatilho; sem ela o Win+Alt+G nao salva nada,
        /// e o clipe por conquista deixa de existir.
        /// </summary>
        public ToggleState BackgroundRecording { get; set; }

        /// <summary>Falso quando a politica de grupo bloqueia o Game DVR na maquina inteira.</summary>
        public bool AllowedByPolicy { get; set; }

        /// <summary>Para onde o Windows manda as capturas. E a pasta que a aba Pastas precisa ter como origem.</summary>
        public string CapturesFolder { get; set; }

        public bool CanTakeScreenshot
        {
            get { return AllowedByPolicy && Capture != ToggleState.Off; }
        }

        public bool CanSaveClip
        {
            get { return CanTakeScreenshot && BackgroundRecording == ToggleState.On; }
        }

        /// <summary>
        /// O que dizer para quem esta olhando a tela. Uma frase por problema, em portugues, com o
        /// caminho da tela do Windows — reclamar sem dizer onde resolver e o mesmo que nao avisar.
        /// </summary>
        public string Explain()
        {
            if (!AllowedByPolicy)
            {
                return "O Game DVR está bloqueado por política nesta máquina. Sem ele o Game Bar não " +
                       "captura nada, e a captura automática não tem como funcionar.";
            }

            if (Capture == ToggleState.Off)
            {
                return "A captura de jogos está desligada no Windows. Ligue em Configurações › Jogos › Capturas.";
            }

            if (BackgroundRecording == ToggleState.On)
            {
                return "Game Bar pronto: print e clipe dos últimos segundos funcionam.";
            }

            var comeco = BackgroundRecording == ToggleState.Off
                ? "A gravação em segundo plano está desligada"
                : "A gravação em segundo plano nunca foi ligada nesta máquina (é o padrão do Windows)";

            return comeco + ". O print automático funciona assim mesmo, mas o clipe não: é ela que " +
                   "guarda os segundos anteriores ao momento. Ligue em Configurações › Jogos › Capturas, " +
                   "em \"Gravar o que aconteceu\".";
        }

        public static GameBarState Read()
        {
            var state = new GameBarState
            {
                AllowedByPolicy = ReadPolicy(),
                Capture = ReadToggle(Registry.CurrentUser, GameDvrKey, "AppCaptureEnabled"),
                BackgroundRecording = ReadToggle(Registry.CurrentUser, GameDvrKey, "HistoricalCaptureEnabled"),
                CapturesFolder = ReadCapturesFolder()
            };

            // O GameConfigStore e o interruptor por usuario que a tela de Jogos tambem mexe;
            // desligado la, nada e capturado, independente do resto.
            var store = ReadToggle(Registry.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled");
            if (store == ToggleState.Off)
            {
                state.Capture = ToggleState.Off;
            }

            return state;
        }

        private static bool ReadPolicy()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(PolicyKey))
                {
                    if (key == null)
                    {
                        return true;
                    }

                    var value = key.GetValue("AllowGameDVR");
                    return value == null || Convert.ToInt32(value) != 0;
                }
            }
            catch (Exception)
            {
                // Sem leitura do registro, a resposta honesta e "nao sei" — e "nao sei" nao pode
                // virar um aviso de bloqueio que manda a pessoa procurar problema onde nao ha.
                return true;
            }
        }

        private static ToggleState ReadToggle(RegistryKey root, string path, string name)
        {
            try
            {
                using (var key = root.OpenSubKey(path))
                {
                    if (key == null)
                    {
                        return ToggleState.NotConfigured;
                    }

                    var value = key.GetValue(name);
                    if (value == null)
                    {
                        return ToggleState.NotConfigured;
                    }

                    return Convert.ToInt32(value) != 0 ? ToggleState.On : ToggleState.Off;
                }
            }
            catch (Exception)
            {
                return ToggleState.NotConfigured;
            }
        }

        private static string ReadCapturesFolder()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UserShellFoldersKey))
                {
                    var value = key == null ? null : key.GetValue(CapturesFolderGuid) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return Environment.ExpandEnvironmentVariables(value);
                    }
                }
            }
            catch (Exception)
            {
            }

            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "Captures");
        }
    }
}
