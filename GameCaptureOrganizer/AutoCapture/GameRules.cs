using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace GameCaptureOrganizer.AutoCapture
{
    /// <summary>
    /// A excecao de um jogo. Todo campo e opcional, e NULO significa "usa o padrao" — nao
    /// "desligado".
    ///
    /// A diferenca e o coracao da classe: guardar false onde a pessoa nao escolheu nada
    /// congelaria o padrao daquele dia. Quem muda o intervalo global depois esperaria que o jogo
    /// sem regra propria acompanhasse, e ele nao acompanharia.
    /// </summary>
    public class GameRule
    {
        public bool? Enabled { get; set; }
        public int? ScreenshotIntervalMinutes { get; set; }
        public int? ClipIntervalMinutes { get; set; }
        public bool? AchievementCapture { get; set; }

        [JsonIgnore]
        public bool IsEmpty
        {
            get
            {
                return !Enabled.HasValue && !ScreenshotIntervalMinutes.HasValue &&
                       !ClipIntervalMinutes.HasValue && !AchievementCapture.HasValue;
            }
        }

        /// <summary>O que a regra faz, em portugues, para a lista da tela de configuracao.</summary>
        public string Summary()
        {
            if (IsEmpty)
            {
                return "sem regra própria";
            }

            if (Enabled.HasValue && !Enabled.Value)
            {
                return "sem captura automática";
            }

            var partes = new List<string>();
            if (Enabled.HasValue && Enabled.Value)
            {
                partes.Add("captura ligada");
            }

            if (ScreenshotIntervalMinutes.HasValue)
            {
                partes.Add(ScreenshotIntervalMinutes.Value <= 0
                    ? "sem print periódico"
                    : "print a cada " + ScreenshotIntervalMinutes.Value + " min");
            }

            if (ClipIntervalMinutes.HasValue)
            {
                partes.Add(ClipIntervalMinutes.Value <= 0
                    ? "sem clipe periódico"
                    : "clipe a cada " + ClipIntervalMinutes.Value + " min");
            }

            if (AchievementCapture.HasValue)
            {
                partes.Add(AchievementCapture.Value ? "captura por conquista" : "sem captura por conquista");
            }

            return string.Join(" · ", partes);
        }
    }

    /// <summary>O que vale de verdade para esta sessao, depois de misturar o padrao com a excecao.</summary>
    public class EffectiveCapture
    {
        public bool Enabled { get; set; }
        public int ScreenshotMinutes { get; set; }
        public int ClipMinutes { get; set; }
        public bool Achievements { get; set; }

        /// <summary>
        /// A regra do jogo manda; o que ela nao diz vem do padrao. A chave mestra
        /// (<see cref="OrganizerSettings.AutoCaptureEnabled"/>) e a unica que a regra NAO consegue
        /// contrariar: desligar tudo tem que desligar tudo, senao "desliguei e continuou
        /// capturando" — e a pessoa vai procurar o defeito no lugar errado.
        /// </summary>
        public static EffectiveCapture Resolve(OrganizerSettings global, GameRule rule)
        {
            if (global == null)
            {
                return new EffectiveCapture();
            }

            var ligado = global.AutoCaptureEnabled &&
                         (rule == null || !rule.Enabled.HasValue || rule.Enabled.Value);

            return new EffectiveCapture
            {
                Enabled = ligado,
                ScreenshotMinutes = rule != null && rule.ScreenshotIntervalMinutes.HasValue
                    ? Math.Max(0, rule.ScreenshotIntervalMinutes.Value)
                    : Math.Max(0, global.ScreenshotIntervalMinutes),
                ClipMinutes = rule != null && rule.ClipIntervalMinutes.HasValue
                    ? Math.Max(0, rule.ClipIntervalMinutes.Value)
                    : Math.Max(0, global.ClipIntervalMinutes),
                Achievements = ligado &&
                    (rule != null && rule.AchievementCapture.HasValue
                        ? rule.AchievementCapture.Value
                        : global.AchievementCaptureEnabled)
            };
        }
    }

    /// <summary>
    /// As excecoes por jogo, em disco. Fica fora do arquivo de configuracao de proposito: a
    /// configuracao e o que a pessoa edita na tela, e isto e uma tabela que cresce com a
    /// biblioteca.
    /// </summary>
    public class GameRules
    {
        private readonly string filePath;
        private readonly object gate = new object();
        private Dictionary<string, GameRule> rules = new Dictionary<string, GameRule>(StringComparer.OrdinalIgnoreCase);

        public GameRules(string filePath)
        {
            this.filePath = filePath;
        }

        public void Load()
        {
            lock (gate)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var json = File.ReadAllText(filePath);
                        rules = JsonConvert.DeserializeObject<Dictionary<string, GameRule>>(json)
                                ?? new Dictionary<string, GameRule>(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception)
                {
                    // Tabela corrompida faz todo jogo voltar ao padrao. E ruim, mas e melhor do que
                    // a extensao nao subir — e o padrao e um estado que a pessoa entende.
                    rules = new Dictionary<string, GameRule>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>Nunca devolve nulo: jogo sem excecao tem uma regra vazia, que e "usa o padrao".</summary>
        public GameRule Get(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return new GameRule();
            }

            lock (gate)
            {
                GameRule regra;
                return rules.TryGetValue(gameId, out regra) && regra != null ? regra : new GameRule();
            }
        }

        /// <summary>
        /// Grava a excecao — e APAGA a linha quando ela fica vazia. Guardar regra que nao diz nada
        /// faria a lista da tela encher de jogos que na verdade seguem o padrao.
        /// </summary>
        public void Set(string gameId, GameRule rule)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return;
            }

            lock (gate)
            {
                if (rule == null || rule.IsEmpty)
                {
                    rules.Remove(gameId);
                }
                else
                {
                    rules[gameId] = rule;
                }
            }

            Save();
        }

        public void Clear(string gameId)
        {
            Set(gameId, null);
        }

        public IList<KeyValuePair<string, GameRule>> All()
        {
            lock (gate)
            {
                return rules.ToList();
            }
        }

        public int Count
        {
            get { lock (gate) { return rules.Count; } }
        }

        public void Save()
        {
            lock (gate)
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(filePath, JsonConvert.SerializeObject(rules, Formatting.Indented));
            }
        }

        /// <summary>
        /// Le o numero que a pessoa digitou. Texto vazio devolve nulo, que e "volta ao padrao" —
        /// e por isso o retorno tem tres estados, e nao dois.
        /// </summary>
        public static bool TryParseInterval(string text, out int? minutes)
        {
            minutes = null;
            if (text == null)
            {
                return false;
            }

            var limpo = text.Trim();
            if (limpo.Length == 0)
            {
                return true;
            }

            int valor;
            if (!int.TryParse(limpo, NumberStyles.Integer, CultureInfo.CurrentCulture, out valor) &&
                !int.TryParse(limpo, NumberStyles.Integer, CultureInfo.InvariantCulture, out valor))
            {
                return false;
            }

            if (valor < 0 || valor > 24 * 60)
            {
                return false;
            }

            minutes = valor;
            return true;
        }
    }
}
