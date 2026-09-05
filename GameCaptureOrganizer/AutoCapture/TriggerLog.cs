using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace GameCaptureOrganizer.AutoCapture
{
    /// <summary>Um pedido de captura que a extensao fez, e que o Game Bar aceitou.</summary>
    public class CaptureTriggerRecord
    {
        public DateTime WhenUtc { get; set; }
        public string Reason { get; set; }
        public string GameId { get; set; }
        public string GameName { get; set; }
    }

    /// <summary>
    /// O caderno dos gatilhos, irmao do <see cref="SessionIndex"/>: a sessao diz de QUEM e a
    /// captura, o gatilho diz POR QUE ela existe.
    ///
    /// O casamento e por horario, e nao por interceptar o arquivo, porque o Game Bar escreve
    /// quando quer — o clipe dos ultimos segundos leva segundos para fechar, e ficar esperando
    /// por ele seguraria a rodada. Gatilho sem arquivo (o Game Bar recusou) simplesmente nao casa
    /// com nada, e o arquivo sem gatilho (a pessoa apertou o atalho na mao) sai sem motivo — os
    /// dois casos sao normais, nenhum e erro.
    /// </summary>
    public class TriggerLog
    {
        private readonly string filePath;
        private readonly object gate = new object();
        private List<CaptureTriggerRecord> records = new List<CaptureTriggerRecord>();

        public TriggerLog(string filePath)
        {
            this.filePath = filePath;
        }

        public IList<CaptureTriggerRecord> Records
        {
            get { lock (gate) { return records.ToList(); } }
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
                        records = JsonConvert.DeserializeObject<List<CaptureTriggerRecord>>(json)
                                  ?? new List<CaptureTriggerRecord>();
                    }
                }
                catch (Exception)
                {
                    // Caderno corrompido custa o marcador {Motivo}, nunca a organizacao.
                    records = new List<CaptureTriggerRecord>();
                }
            }
        }

        public void Add(CaptureReason reason, string gameId, string gameName, DateTime whenUtc)
        {
            lock (gate)
            {
                records.Add(new CaptureTriggerRecord
                {
                    WhenUtc = whenUtc,
                    Reason = CaptureReasons.Label(reason),
                    GameId = gameId,
                    GameName = gameName
                });

                records = Prune(records, DateTime.UtcNow);
            }

            Save();
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

                File.WriteAllText(filePath, JsonConvert.SerializeObject(records, Formatting.Indented));
            }
        }

        public string Match(DateTime whenUtc, TimeSpan tolerance)
        {
            lock (gate)
            {
                return Match(records, whenUtc, tolerance);
            }
        }

        /// <summary>
        /// O motivo do gatilho mais proximo do carimbo do arquivo, dentro da tolerancia. "Mais
        /// proximo" e nao "o ultimo antes" de proposito: o print do Game Bar nasce com o horario
        /// de quando a tecla chegou, e o clipe dos ultimos segundos nasce carimbado ANTES do
        /// gatilho, porque o video comecou la atras.
        /// </summary>
        public static string Match(IEnumerable<CaptureTriggerRecord> records, DateTime whenUtc, TimeSpan tolerance)
        {
            if (records == null)
            {
                return null;
            }

            CaptureTriggerRecord best = null;
            var bestDistance = TimeSpan.MaxValue;

            foreach (var record in records)
            {
                var distance = record.WhenUtc > whenUtc ? record.WhenUtc - whenUtc : whenUtc - record.WhenUtc;
                if (distance > tolerance)
                {
                    continue;
                }

                if (best == null || distance < bestDistance)
                {
                    best = record;
                    bestDistance = distance;
                }
            }

            return best == null ? null : best.Reason;
        }

        /// <summary>Trinta dias, no maximo 2000 gatilhos. Um print a cada 5 minutos cabe folgado.</summary>
        public static List<CaptureTriggerRecord> Prune(IEnumerable<CaptureTriggerRecord> records, DateTime nowUtc,
                                                      int maxDays = 30, int maxItems = 2000)
        {
            var kept = records
                .Where(r => r.WhenUtc >= nowUtc.AddDays(-maxDays))
                .OrderBy(r => r.WhenUtc)
                .ToList();

            if (kept.Count > maxItems)
            {
                kept = kept.Skip(kept.Count - maxItems).ToList();
            }

            return kept;
        }
    }
}
