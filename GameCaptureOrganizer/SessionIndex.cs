using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace GameCaptureOrganizer
{
    /// <summary>Uma sessao de jogo do Playnite, do inicio ao fim. Sempre em UTC.</summary>
    public class PlaySession
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string Platform { get; set; }
        public string Source { get; set; }
        public DateTime StartedUtc { get; set; }

        /// <summary>Nulo enquanto o jogo esta aberto.</summary>
        public DateTime? EndedUtc { get; set; }
    }

    /// <summary>
    /// O caderno de sessoes: e o que permite dizer "esta captura das 21h14 foi no Elden Ring"
    /// sem depender do nome que o Game Bar deu ao arquivo.
    ///
    /// Fica em disco porque a captura pode ser organizada muito depois da sessao (o Playnite
    /// reinicia, o Ally hiberna) e porque a varredura de inicializacao pega o que ficou para tras.
    /// </summary>
    public class SessionIndex
    {
        private readonly string filePath;
        private readonly object gate = new object();
        private List<PlaySession> sessions = new List<PlaySession>();

        public SessionIndex(string filePath)
        {
            this.filePath = filePath;
        }

        public IList<PlaySession> Sessions
        {
            get { lock (gate) { return sessions.ToList(); } }
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
                        sessions = JsonConvert.DeserializeObject<List<PlaySession>>(json) ?? new List<PlaySession>();
                    }
                }
                catch (Exception)
                {
                    // Caderno corrompido nao pode derrubar a extensao: perde-se o casamento por
                    // sessao e a organizacao segue pelo nome do arquivo.
                    sessions = new List<PlaySession>();
                }
            }
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

                File.WriteAllText(filePath, JsonConvert.SerializeObject(sessions, Formatting.Indented));
            }
        }

        public void Begin(string gameId, string gameName, string platform, string source, DateTime startedUtc)
        {
            lock (gate)
            {
                sessions.Add(new PlaySession
                {
                    GameId = gameId,
                    GameName = gameName,
                    Platform = platform,
                    Source = source,
                    StartedUtc = startedUtc
                });

                sessions = Prune(sessions, DateTime.UtcNow);
            }

            Save();
        }

        public void End(string gameId, DateTime endedUtc)
        {
            lock (gate)
            {
                var open = sessions.LastOrDefault(s => s.GameId == gameId && s.EndedUtc == null);
                if (open != null)
                {
                    open.EndedUtc = endedUtc;
                }
            }

            Save();
        }

        /// <summary>Sessao aberta mais recente, se houver. Usada pela varredura pos-jogo.</summary>
        public PlaySession LastFinished()
        {
            lock (gate)
            {
                return sessions.Where(s => s.EndedUtc != null).OrderBy(s => s.EndedUtc.Value).LastOrDefault();
            }
        }

        public PlaySession Match(DateTime whenUtc, TimeSpan tolerance)
        {
            lock (gate)
            {
                return Match(sessions, whenUtc, tolerance, DateTime.UtcNow);
            }
        }

        /// <summary>
        /// Qual sessao cobre este instante. A tolerancia existe dos dois lados porque o relogio do
        /// arquivo e o do evento nao sao o mesmo: o Game Bar carimba o video quando comeca a
        /// gravar e o "ultimos 30 segundos" carimba quando salva, ja com o jogo fechando.
        ///
        /// Havendo mais de uma candidata (dois jogos na mesma janela de tolerancia), vence a de
        /// inicio MAIS RECENTE antes da captura — que e o jogo que estava na frente.
        /// </summary>
        public static PlaySession Match(IEnumerable<PlaySession> sessions, DateTime whenUtc, TimeSpan tolerance, DateTime nowUtc)
        {
            if (sessions == null)
            {
                return null;
            }

            PlaySession best = null;
            foreach (var session in sessions)
            {
                var start = session.StartedUtc - tolerance;
                var end = (session.EndedUtc ?? nowUtc) + tolerance;
                if (whenUtc < start || whenUtc > end)
                {
                    continue;
                }

                if (best == null || session.StartedUtc > best.StartedUtc)
                {
                    best = session;
                }
            }

            return best;
        }

        /// <summary>
        /// Guarda os ultimos 90 dias, no maximo 500 sessoes. Sessao aberta nunca e podada: o jogo
        /// pode estar rodando ha horas, e joga-la fora perderia justamente a captura em curso.
        /// </summary>
        public static List<PlaySession> Prune(IEnumerable<PlaySession> sessions, DateTime nowUtc, int maxDays = 90, int maxItems = 500)
        {
            var kept = sessions
                .Where(s => s.EndedUtc == null || s.EndedUtc.Value >= nowUtc.AddDays(-maxDays))
                .OrderBy(s => s.StartedUtc)
                .ToList();

            if (kept.Count > maxItems)
            {
                kept = kept.Skip(kept.Count - maxItems).ToList();
            }

            return kept;
        }
    }
}
