using System;
using System.Collections.Generic;
using System.IO;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace GameCaptureOrganizer
{
    /// <summary>
    /// O que o Playnite conversa: guarda, edita num clone e so grava quando a pessoa confirma.
    /// O clone existe para "Cancelar" cancelar de verdade — sem ele, mexer no campo ja alteraria
    /// a configuracao viva.
    /// </summary>
    public class OrganizerSettingsViewModel : ObservableObject, ISettings
    {
        private readonly CaptureOrganizerPlugin plugin;
        private OrganizerSettings editing;

        public OrganizerSettings Settings
        {
            get { return editing; }
            set { SetValue(ref editing, value); }
        }

        private OrganizerSettings saved;

        public OrganizerSettingsViewModel(CaptureOrganizerPlugin plugin)
        {
            this.plugin = plugin;

            var stored = plugin.LoadPluginSettings<OrganizerSettings>();
            Settings = stored ?? OrganizerSettings.CreateDefault();

            // Configuracao gravada por uma versao anterior pode nao ter os campos novos.
            // Campo vazio aqui viraria caminho vazio, e captura organizada na raiz do disco.
            if (string.IsNullOrWhiteSpace(Settings.SourceFolders) ||
                string.IsNullOrWhiteSpace(Settings.DestinationFolder))
            {
                var padrao = OrganizerSettings.CreateDefault();
                if (string.IsNullOrWhiteSpace(Settings.SourceFolders))
                {
                    Settings.SourceFolders = padrao.SourceFolders;
                }

                if (string.IsNullOrWhiteSpace(Settings.DestinationFolder))
                {
                    Settings.DestinationFolder = padrao.DestinationFolder;
                }
            }
        }

        public void BeginEdit()
        {
            saved = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            if (saved != null)
            {
                Settings = saved;
            }
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            plugin.OnSettingsSaved();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (Settings.SourceFolderList().Count == 0)
            {
                errors.Add("Informe ao menos uma pasta de origem (onde o Game Bar salva as capturas).");
            }

            if (string.IsNullOrWhiteSpace(Settings.DestinationFolder))
            {
                errors.Add("Informe a pasta de destino.");
            }

            if (string.IsNullOrWhiteSpace(Settings.FilePattern))
            {
                errors.Add("O padrão de nome do arquivo não pode ficar vazio.");
            }

            if (Settings.DelaySeconds < 0 || Settings.DelaySeconds > 600)
            {
                errors.Add("A espera depois do jogo deve ficar entre 0 e 600 segundos.");
            }

            if (Settings.ToleranceMinutes < 0 || Settings.ToleranceMinutes > 240)
            {
                errors.Add("A folga de horário deve ficar entre 0 e 240 minutos.");
            }

            // Origem dentro do destino faria a proxima varredura reorganizar o que ja foi
            // organizado, renomeando em cadeia. Barrar aqui e mais barato do que explicar depois.
            var destino = NormalizedPath(Settings.DestinationFolder);
            foreach (var origem in Settings.SourceFolderList())
            {
                var o = NormalizedPath(origem);
                if (o.Equals(destino, StringComparison.OrdinalIgnoreCase) ||
                    o.StartsWith(destino + "\\", StringComparison.OrdinalIgnoreCase) ||
                    destino.StartsWith(o + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(string.Format("A pasta de origem \"{0}\" está dentro da pasta de destino (ou o contrário). Escolha pastas separadas.", origem));
                }
            }

            return errors.Count == 0;
        }

        private static string NormalizedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path).TrimEnd('\\');
            }
            catch (Exception)
            {
                return path.Trim().TrimEnd('\\');
            }
        }
    }
}
