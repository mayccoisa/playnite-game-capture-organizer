using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GameCaptureOrganizer
{
    /// <summary>O que a biblioteca do Playnite sabe de um jogo, sem arrastar o SDK para ca.</summary>
    public class GameHint
    {
        public string Name { get; set; }
        public string Platform { get; set; }
        public string Source { get; set; }
    }

    /// <summary>
    /// Consulta a biblioteca por titulo. Interface (e nao chamada direta ao Playnite) para o
    /// servico continuar exercitavel sem o app aberto.
    /// </summary>
    public interface ILibraryLookup
    {
        /// <summary>Casamento exato pela forma normalizada do titulo.</summary>
        GameHint Find(string rawName);

        /// <summary>
        /// Casamento por prefixo, e so quando ele e UNICO na biblioteca. E o que resolve o titulo
        /// da janela abreviado ("Pal" -> "Palworld") sem apelido configurado.
        /// </summary>
        GameHint FindByPrefix(string rawName);
    }

    public class OrganizeResult
    {
        public int Organized { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int InUse { get; set; }
        public List<string> Games { get; private set; }

        public OrganizeResult()
        {
            Games = new List<string>();
        }

        public string Summary()
        {
            if (Organized == 0 && Failed == 0 && InUse == 0)
            {
                return "Nenhuma captura nova para organizar.";
            }

            var texto = new StringBuilder();
            texto.Append(Organized == 1 ? "1 captura organizada" : string.Format("{0} capturas organizadas", Organized));

            if (Games.Count > 0)
            {
                texto.Append(" (").Append(string.Join(", ", Games.Take(4).ToArray()));
                if (Games.Count > 4)
                {
                    texto.Append(string.Format(" e mais {0}", Games.Count - 4));
                }

                texto.Append(")");
            }

            if (InUse > 0)
            {
                texto.Append(string.Format(". {0} ainda em uso, ficam para a próxima passada", InUse));
            }

            if (Failed > 0)
            {
                texto.Append(string.Format(". {0} falharam, veja o log", Failed));
            }

            return texto.Append(".").ToString();
        }
    }

    /// <summary>
    /// Quem de fato varre as pastas e move os arquivos. Depende de disco, mas nao do Playnite nem
    /// de WPF: o que vem do app entra por <see cref="ILibraryLookup"/> e pelo caderno de sessoes.
    /// </summary>
    public class OrganizerService
    {
        private readonly SessionIndex sessions;
        private readonly ILibraryLookup library;
        private readonly string logPath;
        private readonly Action<string> logger;
        private readonly object gate = new object();

        /// <summary>
        /// Opcional: sem ele a organizacao roda igual, so sem o marcador {Motivo}. E de proposito —
        /// quem instalou a extensao para organizar nao pode perder a organizacao porque a camada de
        /// captura automatica falhou em ler o proprio caderno.
        /// </summary>
        private readonly AutoCapture.TriggerLog triggers;

        public OrganizerService(SessionIndex sessions, ILibraryLookup library, string logPath, Action<string> logger,
                                AutoCapture.TriggerLog triggers = null)
        {
            this.sessions = sessions;
            this.library = library;
            this.logPath = logPath;
            this.logger = logger;
            this.triggers = triggers;
        }

        /// <summary>
        /// Uma passada completa nas pastas de origem. Serializada: a varredura pos-jogo e o botao
        /// "Organizar agora" podem cair ao mesmo tempo, e duas passadas concorrentes disputariam
        /// o mesmo arquivo.
        /// </summary>
        public OrganizeResult Run(OrganizerSettings settings)
        {
            lock (gate)
            {
                return RunCore(settings);
            }
        }

        private OrganizeResult RunCore(OrganizerSettings settings)
        {
            var result = new OrganizeResult();
            if (settings == null)
            {
                return result;
            }

            var images = CaptureCore.ParseExtensionList(settings.ImageExtensions);
            var videos = CaptureCore.ParseExtensionList(settings.VideoExtensions);
            var tolerance = TimeSpan.FromMinutes(Math.Max(0, settings.ToleranceMinutes));
            var destinoRaiz = settings.DestinationFolder;

            if (string.IsNullOrWhiteSpace(destinoRaiz))
            {
                Log(settings, "Pasta de destino não configurada; nada a fazer.");
                return result;
            }

            foreach (var origem in settings.SourceFolderList())
            {
                if (!Directory.Exists(origem))
                {
                    Log(settings, string.Format("Pasta de origem inexistente, ignorada: {0}", origem));
                    continue;
                }

                string[] arquivos;
                try
                {
                    arquivos = Directory.GetFiles(origem);
                }
                catch (Exception ex)
                {
                    Log(settings, string.Format("Não consegui listar {0}: {1}", origem, ex.Message));
                    result.Failed++;
                    continue;
                }

                foreach (var caminho in arquivos)
                {
                    try
                    {
                        ProcessFile(settings, caminho, images, videos, tolerance, result);
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        Log(settings, string.Format("ERRO em {0}: {1}", Path.GetFileName(caminho), ex.Message));
                    }
                }
            }

            return result;
        }

        private void ProcessFile(OrganizerSettings settings, string caminho, List<string> images, List<string> videos,
                                 TimeSpan tolerance, OrganizeResult result)
        {
            var info = new FileInfo(caminho);
            var kind = CaptureCore.ClassifyKind(info.Extension, images, videos);
            if (kind == CaptureKind.Unknown)
            {
                result.Skipped++;
                return;
            }

            // O arquivo em uso NAO e erro: e o video que o Game Bar ainda esta fechando. Deixar
            // para a proxima passada e melhor do que o Start-Sleep as cegas do script antigo, que
            // ora esperava demais, ora de menos.
            if (IsLocked(info))
            {
                result.InUse++;
                Log(settings, string.Format("Ainda em uso, fica para a próxima: {0}", info.Name));
                return;
            }

            var quando = info.CreationTimeUtc;
            if (quando > DateTime.UtcNow || quando.Year < 2000)
            {
                // Alguns sistemas de arquivo devolvem criacao zerada em arquivo copiado.
                quando = info.LastWriteTimeUtc;
            }

            var contexto = BuildContext(settings, info, kind, quando, tolerance);
            if (contexto == null)
            {
                result.Skipped++;
                Log(settings, string.Format("Sem nome de jogo confiável, mantido na origem: {0}", info.Name));
                return;
            }

            var subpasta = CaptureCore.BuildRelativeFolder(settings.FolderPattern, contexto);
            var destino = string.IsNullOrEmpty(subpasta) ? settings.DestinationFolder : Path.Combine(settings.DestinationFolder, subpasta);
            Directory.CreateDirectory(destino);

            var nome = CaptureCore.BuildFileName(settings.FilePattern, contexto, info.Extension);
            var final = CaptureCore.ResolveCollision(destino, nome, File.Exists);

            if (settings.MoveFiles)
            {
                File.Move(info.FullName, final);
            }
            else
            {
                File.Copy(info.FullName, final, false);
            }

            result.Organized++;
            if (!result.Games.Contains(contexto.GameName))
            {
                result.Games.Add(contexto.GameName);
            }

            Log(settings, string.Format("{0} -> {1} [{2}]", info.Name, final, contexto.Origin));
        }

        /// <summary>
        /// De quem e esta captura. Em ordem: a sessao do Playnite que cobre o horario (o dado mais
        /// confiavel, porque nao depende do titulo da janela), o titulo lido do arquivo casado com
        /// a biblioteca, e por fim o titulo cru do arquivo.
        /// Devolve nulo quando nada resolve e o fallback esta desligado — e ai a captura FICA na
        /// origem, que e melhor do que criar pasta "Sem nome" cheia de coisa perdida.
        /// </summary>
        /// <summary>
        /// O motivo do gatilho que casa com o horario do arquivo, se houver. Nulo aqui e o caso
        /// comum, nao a excecao: captura tirada na mao pelo proprio Game Bar nao tem gatilho nosso.
        /// </summary>
        private string MatchReason(OrganizerSettings settings, DateTime quandoUtc)
        {
            if (triggers == null)
            {
                return null;
            }

            try
            {
                var segundos = Math.Max(0, settings.TriggerToleranceSeconds);
                return triggers.Match(quandoUtc, TimeSpan.FromSeconds(segundos));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private CaptureContext BuildContext(OrganizerSettings settings, FileInfo info, CaptureKind kind,
                                            DateTime quandoUtc, TimeSpan tolerance)
        {
            var contexto = new CaptureContext
            {
                Kind = kind,
                Timestamp = quandoUtc.ToLocalTime(),
                OriginalName = Path.GetFileNameWithoutExtension(info.Name),
                Reason = MatchReason(settings, quandoUtc)
            };

            if (settings.MatchBySession && sessions != null)
            {
                var sessao = sessions.Match(quandoUtc, tolerance);
                if (sessao != null && !string.IsNullOrWhiteSpace(sessao.GameName))
                {
                    contexto.GameName = sessao.GameName;
                    contexto.Platform = sessao.Platform;
                    contexto.Source = sessao.Source;
                    contexto.Origin = CaptureCore.OriginSession;
                    return contexto;
                }
            }

            var doArquivo = CaptureCore.ExtractGameNameFromFileName(info.Name);
            if (string.IsNullOrWhiteSpace(doArquivo))
            {
                return null;
            }

            // O apelido vem ANTES da biblioteca de proposito: ele traduz o titulo da janela para
            // o nome do jogo, e e a partir do nome traduzido que a biblioteca acha a plataforma e
            // a fonte. Aplicado depois, "Pal" nunca chegaria a "Palworld".
            var apelido = CaptureCore.ApplyAlias(doArquivo, CaptureCore.ParseAliases(settings.NameAliases));
            var origemDoNome = CaptureCore.OriginFileName;
            if (!string.IsNullOrWhiteSpace(apelido))
            {
                doArquivo = apelido;
                origemDoNome = CaptureCore.OriginAlias;
            }

            if (settings.MatchByLibrary && library != null)
            {
                var hint = library.Find(doArquivo);
                if (hint != null && !string.IsNullOrWhiteSpace(hint.Name))
                {
                    contexto.GameName = hint.Name;
                    contexto.Platform = hint.Platform;
                    contexto.Source = hint.Source;
                    contexto.Origin = CaptureCore.OriginLibrary;
                    return contexto;
                }

                if (settings.MatchByPrefix)
                {
                    var porPrefixo = library.FindByPrefix(doArquivo);
                    if (porPrefixo != null && !string.IsNullOrWhiteSpace(porPrefixo.Name))
                    {
                        contexto.GameName = porPrefixo.Name;
                        contexto.Platform = porPrefixo.Platform;
                        contexto.Source = porPrefixo.Source;
                        contexto.Origin = CaptureCore.OriginLibraryPrefix;
                        return contexto;
                    }
                }
            }

            if (!settings.FallbackToFileName)
            {
                return null;
            }

            contexto.GameName = doArquivo;
            contexto.Origin = origemDoNome;
            return contexto;
        }

        /// <summary>Abre com exclusividade so para descobrir se alguem mais esta com o arquivo.</summary>
        public static bool IsLocked(FileInfo file)
        {
            try
            {
                using (file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        private void Log(OrganizerSettings settings, string message)
        {
            if (logger != null)
            {
                logger(message);
            }

            if (settings == null || !settings.WriteLog || string.IsNullOrEmpty(logPath))
            {
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // O log e cortado quando passa de 1 MB. Log que cresce sem limite e o que faz o
                // proprio organizador virar o arquivo mais pesado da pasta.
                var arquivo = new FileInfo(logPath);
                if (arquivo.Exists && arquivo.Length > 1024 * 1024)
                {
                    var linhas = File.ReadAllLines(logPath);
                    File.WriteAllLines(logPath, linhas.Skip(Math.Max(0, linhas.Length - 2000)).ToArray(), Encoding.UTF8);
                }

                File.AppendAllText(logPath,
                    string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine),
                    Encoding.UTF8);
            }
            catch (Exception)
            {
                // Log que estoura nao pode derrubar a organizacao.
            }
        }
    }
}
