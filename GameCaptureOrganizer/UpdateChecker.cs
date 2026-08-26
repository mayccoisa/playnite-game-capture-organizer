using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace GameCaptureOrganizer
{
    /// <summary>
    /// Verifica se existe uma versão mais nova da extensão publicada nas Releases do GitHub,
    /// baixa o .pext correspondente e entrega ao Playnite para instalar.
    /// Não depende de biblioteca de JSON: só os poucos campos usados são extraídos da resposta.
    /// </summary>
    public class UpdateChecker
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const string LatestReleaseApi = "https://api.github.com/repos/{0}/releases/latest";
        private const string UserAgent = "Playnite-Extension-Updater";

        private readonly IPlayniteAPI api;
        private readonly string repo;
        private readonly string extensionId;
        private readonly string extensionDir;
        private string currentVersion;

        /// <param name="repo">Repositório no formato "usuario/repositorio".</param>
        /// <param name="extensionId">Id do extension.yaml, usado para escolher o .pext certo quando a release tem vários.</param>
        /// <param name="extensionDir">Pasta onde a extensão está instalada (onde fica o extension.yaml).</param>
        public UpdateChecker(IPlayniteAPI api, string repo, string extensionId, string extensionDir)
        {
            this.api = api;
            this.repo = repo;
            this.extensionId = extensionId;
            this.extensionDir = extensionDir;
        }

        public static string DefaultExtensionDir
        {
            get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        }

        public string ReleasesUrl
        {
            get { return string.Format("https://github.com/{0}/releases", repo); }
        }

        /// <summary>Versão instalada, lida do extension.yaml (cai para a versão do assembly se o arquivo faltar).</summary>
        public string CurrentVersion
        {
            get
            {
                if (currentVersion == null)
                {
                    currentVersion = ReadVersionFromManifest() ?? ReadVersionFromAssembly();
                }

                return currentVersion;
            }
        }

        /// <summary>
        /// Consulta o GitHub, compara com a versão instalada e conduz o usuário pelos diálogos
        /// (já atualizado, nova versão disponível, download e instalação).
        /// </summary>
        public void CheckInteractive()
        {
            string json = null;
            Exception failure = null;

            api.Dialogs.ActivateGlobalProgress(
                (progress) =>
                {
                    try
                    {
                        json = DownloadString(string.Format(LatestReleaseApi, repo));
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                },
                new GlobalProgressOptions("Verificando atualizações...", false));

            if (failure != null)
            {
                logger.Error(failure, string.Format("Falha ao consultar as releases de {0}.", repo));
                api.Dialogs.ShowErrorMessage(DescribeFailure(failure), "Verificar atualizações");
                return;
            }

            var latestTag = ExtractFirstString(json, "tag_name");
            if (string.IsNullOrEmpty(latestTag))
            {
                api.Dialogs.ShowErrorMessage("A resposta do GitHub não trouxe a versão da release.", "Verificar atualizações");
                return;
            }

            if (CompareVersions(latestTag, CurrentVersion) <= 0)
            {
                api.Dialogs.ShowMessage(
                    string.Format("Você já está na versão mais recente ({0}).", CurrentVersion),
                    "Verificar atualizações");
                return;
            }

            var assetUrl = PickPextAsset(json);
            if (string.IsNullOrEmpty(assetUrl))
            {
                var semArquivo = string.Format(
                    "A versão {0} está publicada, mas a release não tem um arquivo .pext anexado.\n\nDeseja abrir a página de releases no navegador?",
                    Normalize(latestTag));

                if (api.Dialogs.ShowMessage(semArquivo, "Atualização disponível", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    OpenUrl(ReleasesUrl);
                }

                return;
            }

            var notes = ExtractReleaseNotes(json);
            var pergunta = new StringBuilder();
            pergunta.AppendLine(string.Format("Versão instalada: {0}", CurrentVersion));
            pergunta.AppendLine(string.Format("Versão disponível: {0}", Normalize(latestTag)));
            if (!string.IsNullOrEmpty(notes))
            {
                pergunta.AppendLine();
                pergunta.AppendLine("Novidades:");
                pergunta.AppendLine(notes);
            }
            pergunta.AppendLine();
            pergunta.Append("Deseja baixar e instalar agora?");

            if (api.Dialogs.ShowMessage(pergunta.ToString(), "Atualização disponível", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }

            DownloadAndInstall(assetUrl);
        }

        private void DownloadAndInstall(string assetUrl)
        {
            var fileName = FileNameOf(assetUrl);
            var target = Path.Combine(Path.GetTempPath(), fileName);
            Exception failure = null;

            api.Dialogs.ActivateGlobalProgress(
                (progress) =>
                {
                    try
                    {
                        DownloadFile(assetUrl, target);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                },
                new GlobalProgressOptions("Baixando a nova versão...", false));

            if (failure != null)
            {
                logger.Error(failure, string.Format("Falha ao baixar {0}.", assetUrl));
                api.Dialogs.ShowErrorMessage("Não foi possível baixar o arquivo: " + failure.Message, "Atualizar extensão");
                return;
            }

            if (ScheduleInstallAfterRestart(target))
            {
                api.Dialogs.ShowMessage(
                    "Baixado. O Playnite vai fechar, trocar os arquivos e abrir de novo sozinho.\n\n" +
                    "Se ele não voltar em alguns segundos, abra pelo atalho: a troca já terá sido feita.",
                    "Atualizando a extensão");

                ShutdownPlaynite();
            }
            else
            {
                api.Dialogs.ShowMessage(
                    string.Format(
                        "O arquivo foi baixado, mas não consegui agendar a troca.\n\n" +
                        "Feche o Playnite e dê um duplo clique em:\n{0}",
                        target),
                    "Atualização baixada");

                RevealInExplorer(target);
            }
        }

        /// <summary>Onde o auxiliar grava o log da troca, para diagnosticar quando ela falha.</summary>
        public static string UpdateLogPath
        {
            get { return Path.Combine(Path.GetTempPath(), "GameCaptureOrganizer-update.log"); }
        }

        /// <summary>
        /// Deixa um auxiliar externo esperando o Playnite encerrar para então extrair o .pext por
        /// cima da pasta da extensão e reabrir o app.
        ///
        /// POR QUE NÃO ENTREGAR O ARQUIVO AO PLAYNITE, que era o que estava aqui. Com o app
        /// ABERTO, subir uma segunda instância com o .pext como argumento não instala nada: a
        /// segunda manda "Focus" pelo pipe da primeira, registra "Application already running,
        /// shutting down" e DESCARTA o arquivo, sem erro nenhum na tela (Playnite#2113). Era esse
        /// o caminho, e por isso a atualização pela tela nunca aplicava e só o download manual
        /// funcionava.
        ///
        /// As outras duas saídas óbvias também não servem: reabrir o Playnite COM o .pext apenas
        /// registra a instalação para uma inicialização seguinte (o app diz que atualizou e a
        /// versão continua a antiga), e extrair por cima com o app rodando esbarra na DLL
        /// carregada e travada.
        ///
        /// Então: processo de fora, que espera o app morrer. Mesmo desenho já validado no
        /// BuscaDeJogosLocais.
        /// </summary>
        private bool ScheduleInstallAfterRestart(string pextPath)
        {
            try
            {
                var current = Process.GetCurrentProcess();
                var exe = current.MainModule.FileName;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe) || string.IsNullOrEmpty(extensionDir))
                {
                    logger.Warn("GameCaptureOrganizer: sem executável ou pasta da extensão, não dá para agendar a troca.");
                    return false;
                }

                var scriptPath = Path.Combine(Path.GetTempPath(), "GameCaptureOrganizer-update.ps1");
                File.WriteAllText(scriptPath, BuildInstallScript(current.Id, exe, pextPath, extensionDir), Encoding.UTF8);

                // -File com um .ps1 gravado, e não -Command com o script inline: aspas aninhadas
                // em -Command já quebraram isto antes.
                var info = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + scriptPath + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(info);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "GameCaptureOrganizer: não foi possível agendar a instalação para o reinício.");
                return false;
            }
        }

        private static string BuildInstallScript(int playniteProcessId, string playniteExe, string pextPath, string destino)
        {
            var script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine("$log = Join-Path $env:TEMP 'GameCaptureOrganizer-update.log'");
            script.AppendLine("function Registrar($m) { \"$(Get-Date -Format 'HH:mm:ss') $m\" | Out-File -FilePath $log -Append -Encoding utf8 }");
            script.AppendLine("try {");
            script.AppendLine(string.Format("  Registrar 'Esperando o Playnite (PID {0}) encerrar...'", playniteProcessId));
            script.AppendLine(string.Format("  try {{ Wait-Process -Id {0} -Timeout 180 }} catch {{ Registrar 'Playnite ja estava fechado ou demorou demais.' }}", playniteProcessId));
            script.AppendLine("  Start-Sleep -Seconds 3");
            script.AppendLine(string.Format("  $pext = '{0}'", Escape(pextPath)));
            script.AppendLine(string.Format("  $destino = '{0}'", Escape(destino)));
            script.AppendLine("  $temp = Join-Path $env:TEMP ('GameCaptureOrganizer-' + [Guid]::NewGuid().ToString('N'))");
            script.AppendLine("  New-Item -ItemType Directory -Path $temp | Out-Null");
            script.AppendLine("  Add-Type -AssemblyName System.IO.Compression.FileSystem");
            script.AppendLine("  [IO.Compression.ZipFile]::ExtractToDirectory($pext, $temp)");
            script.AppendLine("  Registrar \"Pacote extraido em $temp\"");
            // Cópia POR CIMA, nunca apagando a pasta antes: se a cópia falhar no meio, o pior caso
            // é ficar na versão velha, e não ficar sem extensão nenhuma.
            script.AppendLine("  Copy-Item -Path (Join-Path $temp '*') -Destination $destino -Recurse -Force");
            script.AppendLine("  Registrar \"Arquivos copiados para $destino\"");
            script.AppendLine("  Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue");
            script.AppendLine("} catch {");
            script.AppendLine("  Registrar \"FALHA: $($_.Exception.Message)\"");
            script.AppendLine("}");
            // O log existe porque o auxiliar roda DEPOIS que a extensão morreu: sem ele, uma troca
            // que falhou vira adivinhação. Já custou duas rodadas no BuscaDeJogosLocais.
            script.AppendLine("Registrar 'Reabrindo o Playnite...'");
            script.AppendLine(string.Format("Start-Process -FilePath '{0}'", Escape(playniteExe)));
            return script.ToString();
        }

        private static string Escape(string value)
        {
            return value == null ? string.Empty : value.Replace("'", "''");
        }

        private static void ShutdownPlaynite()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(new ProcessStartInfo(exe, "--shutdown") { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "GameCaptureOrganizer: não consegui pedir o encerramento; feche o Playnite para concluir a troca.");
            }
        }

        private static void RevealInExplorer(string path)
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não foi possível abrir o Explorer na pasta do download.");
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não foi possível abrir o navegador.");
            }
        }

        private string ReadVersionFromManifest()
        {
            try
            {
                var manifest = Path.Combine(extensionDir, "extension.yaml");
                if (!File.Exists(manifest))
                {
                    return null;
                }

                foreach (var line in File.ReadAllLines(manifest))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmed.Substring("Version:".Length).Trim().Trim('"', '\'');
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não foi possível ler a versão do extension.yaml.");
            }

            return null;
        }

        private static string ReadVersionFromAssembly()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
            catch (Exception)
            {
                return "0.0.0";
            }
        }

        private static string DownloadString(string url)
        {
            EnsureModernTls();
            using (var client = new WebClient())
            {
                client.Encoding = Encoding.UTF8;
                client.Headers.Add("User-Agent", UserAgent);
                client.Headers.Add("Accept", "application/vnd.github+json");
                return client.DownloadString(url);
            }
        }

        private static void DownloadFile(string url, string target)
        {
            EnsureModernTls();
            using (var client = new WebClient())
            {
                client.Headers.Add("User-Agent", UserAgent);
                client.DownloadFile(url, target);
            }
        }

        private static void EnsureModernTls()
        {
            // O .NET Framework pode negociar TLS antigo por padrão; o GitHub exige 1.2+.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        private static string DescribeFailure(Exception ex)
        {
            var web = ex as WebException;
            var response = web == null ? null : web.Response as HttpWebResponse;
            if (response != null)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return "Nenhuma release publicada foi encontrada neste repositório.";
                }

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return "O GitHub recusou a consulta por excesso de requisições (o limite é por hora e por IP). Tente novamente mais tarde.";
                }
            }

            return "Não foi possível consultar o GitHub: " + ex.Message;
        }

        /// <summary>Escolhe o .pext da release: o que casa com o Id da extensão, ou o primeiro disponível.</summary>
        private string PickPextAsset(string json)
        {
            var urls = new List<string>();
            foreach (Match match in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                var url = match.Groups[1].Value;
                if (url.EndsWith(".pext", StringComparison.OrdinalIgnoreCase))
                {
                    urls.Add(url);
                }
            }

            var doIdCerto = urls.FirstOrDefault(
                u => FileNameOf(u).StartsWith(extensionId, StringComparison.OrdinalIgnoreCase));

            return doIdCerto ?? urls.FirstOrDefault();
        }

        private static string FileNameOf(string url)
        {
            var slash = url.LastIndexOf('/');
            return slash >= 0 ? url.Substring(slash + 1) : url;
        }

        private static string ExtractFirstString(string json, string field)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var match = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return match.Success ? Unescape(match.Groups[1].Value) : null;
        }

        private static string ExtractReleaseNotes(string json)
        {
            var body = ExtractFirstString(json, "body");
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            body = body.Trim();
            const int limite = 1200;
            if (body.Length > limite)
            {
                body = body.Substring(0, limite).TrimEnd() + "...";
            }

            return body;
        }

        private static string Unescape(string value)
        {
            var result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i == value.Length - 1)
                {
                    result.Append(value[i]);
                    continue;
                }

                i++;
                switch (value[i])
                {
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case '/': result.Append('/'); break;
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case 'u':
                        if (i + 4 < value.Length)
                        {
                            int code;
                            if (int.TryParse(value.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                             System.Globalization.CultureInfo.InvariantCulture, out code))
                            {
                                result.Append((char)code);
                                i += 4;
                                break;
                            }
                        }
                        result.Append("\\u");
                        break;
                    default:
                        result.Append('\\').Append(value[i]);
                        break;
                }
            }

            return result.ToString();
        }

        /// <summary>Compara "v0.3.1" com "0.3.0" ignorando prefixos e sufixos não numéricos.</summary>
        private static int CompareVersions(string left, string right)
        {
            var a = ParseVersion(left);
            var b = ParseVersion(right);

            for (int i = 0; i < a.Length; i++)
            {
                var comparison = a[i].CompareTo(b[i]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private static int[] ParseVersion(string version)
        {
            var parts = new int[4];
            if (string.IsNullOrEmpty(version))
            {
                return parts;
            }

            var match = Regex.Match(version, @"(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?");
            if (!match.Success)
            {
                return parts;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                var group = match.Groups[i + 1];
                int value;
                if (group.Success && int.TryParse(group.Value, out value))
                {
                    parts[i] = value;
                }
            }

            return parts;
        }

        private static string Normalize(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return tag;
            }

            return tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
        }
    }
}

