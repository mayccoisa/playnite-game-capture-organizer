using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameCaptureOrganizer
{
    /// <summary>Que tipo de captura o arquivo e, decidido pela extensao.</summary>
    public enum CaptureKind
    {
        Unknown = 0,
        Screenshot = 1,
        Video = 2
    }

    /// <summary>
    /// Tudo o que se sabe de uma captura na hora de decidir onde ela vai parar.
    /// E o que alimenta os marcadores do padrao de pasta e de nome.
    /// </summary>
    public class CaptureContext
    {
        public string GameName { get; set; }
        public string Platform { get; set; }
        public string Source { get; set; }
        public CaptureKind Kind { get; set; }
        public DateTime Timestamp { get; set; }
        public string OriginalName { get; set; }

        /// <summary>Como o nome do jogo foi descoberto. Vai para o log e para o resumo da rodada.</summary>
        public string Origin { get; set; }
    }

    /// <summary>
    /// Logica pura da organizacao: sem Playnite, sem WPF e sem disco.
    /// E o unico codigo aqui que da para exercitar sem abrir o app (tests/OrganizerTests).
    /// </summary>
    public static class CaptureCore
    {
        public const string DefaultFolderPattern = @"{Jogo}\{Tipo}";
        public const string DefaultFilePattern = "{Jogo}_{Data}_{Hora}";
        public const string DefaultImageExtensions = ".png,.jpg,.jpeg,.bmp";
        public const string DefaultVideoExtensions = ".mp4,.mkv,.wmv,.mov";

        /// <summary>Rotulo da subpasta por tipo. Fixo de proposito: o padrao decide SE usa, nao COMO chama.</summary>
        public const string ScreenshotLabel = "Screenshots";
        public const string VideoLabel = "Videos";

        /// <summary>Como o nome do jogo foi descoberto, em ordem de confianca.</summary>
        public const string OriginSession = "sessao do Playnite";
        public const string OriginLibrary = "biblioteca do Playnite";
        public const string OriginAlias = "apelido configurado";
        public const string OriginLibraryPrefix = "biblioteca (por prefixo)";
        public const string OriginFileName = "nome do arquivo";

        // ---------------------------------------------------------------- apelidos

        /// <summary>
        /// Le a tabela de apelidos ("Pal = Palworld", um por linha) indexada pela forma
        /// normalizada do apelido.
        ///
        /// Ela existe porque o Game Bar nomeia o arquivo pelo TITULO DA JANELA, que as vezes nao
        /// tem relacao com o nome do jogo: a janela do Palworld se chama "Pal", e nenhuma regra
        /// de texto deduz "Palworld" a partir disso sem inventar. Quem sabe a equivalencia e a
        /// pessoa, entao ela declara — e a partir dai vale para sempre, sem adivinhacao.
        /// </summary>
        public static Dictionary<string, string> ParseAliases(string raw)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return map;
            }

            foreach (var line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var texto = line.Trim();
                if (texto.Length == 0 || texto.StartsWith("#"))
                {
                    continue;
                }

                var corte = texto.IndexOf('=');
                if (corte <= 0 || corte == texto.Length - 1)
                {
                    continue;
                }

                var de = NormalizeName(texto.Substring(0, corte));
                var para = texto.Substring(corte + 1).Trim();
                if (de.Length > 0 && para.Length > 0 && !map.ContainsKey(de))
                {
                    map[de] = para;
                }
            }

            return map;
        }

        public static string ApplyAlias(string name, Dictionary<string, string> aliases)
        {
            if (aliases == null || aliases.Count == 0 || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string destino;
            return aliases.TryGetValue(NormalizeName(name), out destino) ? destino : null;
        }

        // ---------------------------------------------------------------- casamento por prefixo

        /// <summary>Tamanho minimo do texto para o casamento por prefixo ser tentado.</summary>
        public const int MinPrefixLength = 3;

        /// <summary>
        /// Acha na biblioteca o unico titulo que COMECA com o texto lido do arquivo. E o que casa
        /// "Pal" (titulo da janela do Palworld) com "Palworld" sem precisar de apelido.
        ///
        /// Duas travas, e nenhuma das duas e conservadorismo decorativo:
        /// - **Menos de 3 caracteres nao tenta.** "GT" casaria com meia biblioteca.
        /// - **Mais de um candidato devolve NULO**, nunca o primeiro. "Pal" com Palworld E Paladins
        ///   instalados e ambiguo, e escolher um seria inventar — a captura cai no nome do arquivo,
        ///   que e visivelmente cru, em vez de ir para a pasta errada em silencio.
        /// </summary>
        public static string PickUniquePrefixMatch(string normalizedKey, IEnumerable<string> normalizedLibraryKeys)
        {
            if (string.IsNullOrEmpty(normalizedKey) || normalizedKey.Length < MinPrefixLength || normalizedLibraryKeys == null)
            {
                return null;
            }

            string unico = null;
            foreach (var candidato in normalizedLibraryKeys)
            {
                if (string.IsNullOrEmpty(candidato) || !candidato.StartsWith(normalizedKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (unico != null)
                {
                    return null;
                }

                unico = candidato;
            }

            return unico;
        }

        // ---------------------------------------------------------------- extensoes

        /// <summary>Le ".png, .jpg" (ou "png;jpg") e devolve a lista normalizada com ponto e minuscula.</summary>
        public static List<string> ParseExtensionList(string raw)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return result;
            }

            foreach (var part in raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var ext = part.Trim().ToLowerInvariant();
                if (ext.Length == 0)
                {
                    continue;
                }

                if (!ext.StartsWith("."))
                {
                    ext = "." + ext;
                }

                if (!result.Contains(ext))
                {
                    result.Add(ext);
                }
            }

            return result;
        }

        public static CaptureKind ClassifyKind(string extension, IEnumerable<string> imageExtensions, IEnumerable<string> videoExtensions)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return CaptureKind.Unknown;
            }

            var ext = extension.ToLowerInvariant();
            if (!ext.StartsWith("."))
            {
                ext = "." + ext;
            }

            if (videoExtensions != null && videoExtensions.Contains(ext))
            {
                return CaptureKind.Video;
            }

            if (imageExtensions != null && imageExtensions.Contains(ext))
            {
                return CaptureKind.Screenshot;
            }

            return CaptureKind.Unknown;
        }

        public static string KindLabel(CaptureKind kind)
        {
            return kind == CaptureKind.Video ? VideoLabel : ScreenshotLabel;
        }

        // ---------------------------------------------------------------- nomes

        /// <summary>
        /// Forma de comparacao de um titulo: sem acento, sem simbolo de marca e so alfanumerico
        /// em minuscula. E o que casa "Marvel's Spider-Man" da janela com "Marvels Spider Man"
        /// da biblioteca. NAO serve para exibir nem para nomear pasta.
        /// </summary>
        public static string NormalizeName(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var decomposed = text.Normalize(NormalizationForm.FormD);
            var stripped = Regex.Replace(decomposed, @"\p{Mn}", string.Empty);
            stripped = Regex.Replace(stripped, "[™®©]", string.Empty);
            stripped = stripped.Replace('—', '-');
            return Regex.Replace(stripped, "[^a-zA-Z0-9]", string.Empty).ToLowerInvariant();
        }

        /// <summary>
        /// Tira o carimbo de data/hora que o Game Bar cola no fim do nome e devolve o titulo.
        /// Cobre os formatos vistos na pratica:
        ///   "Elden Ring 2_15_2026 8_30_12 PM.png"  (Game Bar, formato dos EUA)
        ///   "Elden Ring 15_02_2026 20_30_12.mp4"   (Game Bar, formato do Brasil)
        ///   "Elden Ring 2026-02-15 20-30-12.mp4"   (formato ISO)
        ///   "Elden Ring_20260215203012.png"        (carimbo compacto)
        /// Um nome sem carimbo nenhum volta inteiro: cortar por chute apagaria titulo de verdade.
        /// </summary>
        public static string ExtractGameNameFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            var name = Path.GetFileNameWithoutExtension(fileName);

            // ISO / ano na frente: 2026-02-15 20-30-12
            name = Regex.Replace(name, @"[\s_\-]+\d{4}[-_\.]\d{2}[-_\.]\d{2}([\s_\-].*)?$", string.Empty);
            // dd_mm_yyyy ou m_d_yyyy, com hora e AM/PM opcionais
            name = Regex.Replace(name, @"[\s_\-]+\d{1,2}[-_\.]\d{1,2}[-_\.]\d{4}([\s_\-].*)?$", string.Empty);
            // carimbo compacto de 8 a 14 digitos colado no fim
            name = Regex.Replace(name, @"[\s_\-]+\d{8,14}$", string.Empty);
            // sobra do Game Bar quando ele repete o tipo da captura
            name = Regex.Replace(name, @"[\s_\-]+(Trim|Clip|Captura|Capture)$", string.Empty, RegexOptions.IgnoreCase);

            return name.Trim(' ', '_', '-', '.');
        }

        private static readonly char[] ExtraInvalid = new[] { ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>
        /// Deixa o texto utilizavel como UM segmento de caminho. Caractere proibido some, ponto e
        /// espaco no fim somem (o Windows nao guarda pasta terminada assim) e nome reservado do
        /// DOS ganha sufixo. Texto que sobra vazio vira "Sem nome", nunca string vazia: caminho
        /// com segmento vazio grava a captura na raiz do destino sem ninguem perceber.
        /// </summary>
        public static string SanitizePathSegment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Sem nome";
            }

            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (c == '\\' || c == '/' || Array.IndexOf(ExtraInvalid, c) >= 0 || char.IsControl(c))
                {
                    continue;
                }

                builder.Append(c);
            }

            var clean = builder.ToString().Trim().TrimEnd('.', ' ');
            if (clean.Length == 0)
            {
                return "Sem nome";
            }

            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5",
                                   "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4",
                                   "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            if (reserved.Contains(clean.ToUpperInvariant()))
            {
                clean = clean + "_";
            }

            return clean;
        }

        /// <summary>Sanitiza um caminho RELATIVO inteiro, segmento a segmento, preservando as barras.</summary>
        public static string SanitizeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(p => p.Trim() != "." && p.Trim() != "..")
                            .Select(SanitizePathSegment)
                            .ToArray();

            return string.Join("\\", parts);
        }

        // ---------------------------------------------------------------- padroes

        /// <summary>
        /// Troca os marcadores do padrao pelos dados da captura. Marcador desconhecido fica como
        /// esta, em vez de sumir: assim um erro de digitacao aparece no nome do arquivo em vez de
        /// virar um caminho silenciosamente errado.
        /// </summary>
        public static string RenderPattern(string pattern, CaptureContext context)
        {
            if (string.IsNullOrWhiteSpace(pattern) || context == null)
            {
                return string.Empty;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Jogo", context.GameName },
                { "Tipo", KindLabel(context.Kind) },
                { "Data", context.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "Hora", context.Timestamp.ToString("HH-mm-ss", CultureInfo.InvariantCulture) },
                { "Ano", context.Timestamp.ToString("yyyy", CultureInfo.InvariantCulture) },
                { "Mes", context.Timestamp.ToString("MM", CultureInfo.InvariantCulture) },
                { "AnoMes", context.Timestamp.ToString("yyyy-MM", CultureInfo.InvariantCulture) },
                { "Plataforma", context.Platform },
                { "Fonte", context.Source },
                { "Original", context.OriginalName }
            };

            return Regex.Replace(pattern, @"\{(\w+)\}", match =>
            {
                string value;
                if (!values.TryGetValue(match.Groups[1].Value, out value))
                {
                    return match.Value;
                }

                return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
            });
        }

        /// <summary>Caminho relativo da pasta de destino (ja sanitizado) para esta captura.</summary>
        public static string BuildRelativeFolder(string folderPattern, CaptureContext context)
        {
            var rendered = RenderPattern(folderPattern, context);
            return SanitizeRelativePath(rendered);
        }

        /// <summary>Nome do arquivo (com extensao) para esta captura, ja sanitizado.</summary>
        public static string BuildFileName(string filePattern, CaptureContext context, string extension)
        {
            var rendered = RenderPattern(filePattern, context);
            var name = SanitizePathSegment(rendered);
            var ext = string.IsNullOrEmpty(extension) ? string.Empty : extension.ToLowerInvariant();
            return name + ext;
        }

        /// <summary>
        /// Resolve colisao acrescentando " (2)", " (3)"... O teste de existencia entra por
        /// parametro para o desvio ser testavel sem tocar em disco.
        /// </summary>
        public static string ResolveCollision(string folder, string fileName, Func<string, bool> exists)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var candidate = Path.Combine(folder, fileName);

            var counter = 2;
            while (exists(candidate) && counter < 1000)
            {
                candidate = Path.Combine(folder, string.Format("{0} ({1}){2}", baseName, counter, ext));
                counter++;
            }

            return candidate;
        }
    }
}
