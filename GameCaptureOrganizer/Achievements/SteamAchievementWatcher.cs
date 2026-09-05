using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GameCaptureOrganizer.Achievements
{
    /// <summary>
    /// Fica de olho no arquivo de progresso da Steam enquanto o jogo roda e avisa quando aparece
    /// conquista nova.
    ///
    /// Sao dois caminhos ao mesmo tempo, de proposito:
    /// - o <see cref="FileSystemWatcher"/>, que responde no instante da escrita;
    /// - uma releitura de seguranca a cada meio minuto, porque watcher PERDE evento (buffer cheio,
    ///   pasta em disco ocupado, arquivo trocado por renomeacao em vez de escrito).
    ///   So o watcher significaria conquista que nao vira captura de vez em quando, sem nenhum
    ///   sinal de que faltou.
    ///
    /// A primeira leitura e SILENCIOSA: ela e a linha de base. Sem isso, abrir um jogo com
    /// duzentas conquistas antigas dispararia captura como se todas tivessem acabado de sair.
    /// </summary>
    public sealed class SteamAchievementWatcher : IDisposable
    {
        /// <summary>Espera depois do evento, para nao ler o arquivo no meio da escrita da Steam.</summary>
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

        /// <summary>A releitura que cobre o evento perdido.</summary>
        private static readonly TimeSpan SafetyInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Uma captura por vez. Cinco conquistas destravadas na mesma cena sao UM momento, e cinco
        /// prints do mesmo instante nao sao cinco lembrancas — sao quatro arquivos a mais.
        /// </summary>
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(15);

        private readonly string folder;
        private readonly string filter;
        private readonly Action<int> onUnlock;
        private readonly Action<string> log;
        private readonly object gate = new object();
        private readonly Dictionary<string, HashSet<string>> baseline =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private FileSystemWatcher watcher;
        private CancellationTokenSource cancellation;
        private DateTime lastFiredUtc = DateTime.MinValue;

        public SteamAchievementWatcher(string folder, string filter, Action<int> onUnlock, Action<string> log)
        {
            this.folder = folder;
            this.filter = filter;
            this.onUnlock = onUnlock;
            this.log = log ?? (_ => { });
        }

        public bool Running
        {
            get { lock (gate) { return watcher != null; } }
        }

        public void Start()
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                log("Não achei a pasta de progresso da Steam; a captura por conquista fica de fora desta sessão.");
                return;
            }

            lock (gate)
            {
                Stop();

                Prime();

                try
                {
                    watcher = new FileSystemWatcher(folder, filter)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                                       NotifyFilters.FileName | NotifyFilters.CreationTime,
                        IncludeSubdirectories = false
                    };

                    watcher.Changed += OnFileEvent;
                    watcher.Created += OnFileEvent;
                    watcher.Renamed += OnFileEvent;
                    watcher.EnableRaisingEvents = true;
                }
                catch (Exception ex)
                {
                    log("Não consegui vigiar a pasta da Steam: " + ex.Message + ". Segue só a releitura periódica.");
                    watcher = null;
                }

                cancellation = new CancellationTokenSource();
                var token = cancellation.Token;
                Task.Run(() => SafetyLoop(token), token);
            }

            log("De olho nas conquistas da Steam em " + Path.Combine(folder, filter) + ".");
        }

        public void Stop()
        {
            lock (gate)
            {
                if (watcher != null)
                {
                    try
                    {
                        watcher.EnableRaisingEvents = false;
                        watcher.Changed -= OnFileEvent;
                        watcher.Created -= OnFileEvent;
                        watcher.Renamed -= OnFileEvent;
                        watcher.Dispose();
                    }
                    catch (Exception)
                    {
                    }

                    watcher = null;
                }

                if (cancellation != null)
                {
                    try { cancellation.Cancel(); } catch (Exception) { }
                    try { cancellation.Dispose(); } catch (Exception) { }
                    cancellation = null;
                }

                baseline.Clear();
                lastFiredUtc = DateTime.MinValue;
            }
        }

        /// <summary>A foto do comeco da sessão. Tudo que já estava destravado entra aqui, calado.</summary>
        private void Prime()
        {
            foreach (var arquivo in Matching())
            {
                var bits = SteamStats.ReadUnlockedBits(arquivo);
                if (bits != null)
                {
                    baseline[arquivo] = bits;
                }
            }

            var total = 0;
            foreach (var par in baseline)
            {
                total += par.Value.Count;
            }

            log(string.Format("Linha de base: {0} conquista(s) já destravada(s) em {1} arquivo(s).",
                              total, baseline.Count));
        }

        private IEnumerable<string> Matching()
        {
            try
            {
                return Directory.GetFiles(folder, filter, SearchOption.TopDirectoryOnly);
            }
            catch (Exception)
            {
                return new string[0];
            }
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            // O evento chega numa thread do watcher; a leitura sai daqui para nao segurar a fila
            // de notificacoes do Windows, que tem buffer pequeno e descarta o que nao cabe.
            Task.Run(() =>
            {
                try
                {
                    Thread.Sleep(Debounce);
                    Check(e.FullPath);
                }
                catch (Exception ex)
                {
                    log("Falha ao ler o progresso da Steam: " + ex.Message);
                }
            });
        }

        private async Task SafetyLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(SafetyInterval, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    foreach (var arquivo in Matching())
                    {
                        Check(arquivo);
                    }
                }
            }
            catch (Exception ex)
            {
                log("A releitura periódica das conquistas parou: " + ex.Message);
            }
        }

        /// <summary>
        /// Le o arquivo e compara com a linha de base dele. Leitura falha nao mexe em nada: o
        /// arquivo pode estar sendo escrito, e a proxima passada resolve.
        /// </summary>
        private void Check(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var bits = SteamStats.ReadUnlockedBits(path);
            if (bits == null)
            {
                return;
            }

            int novas;
            lock (gate)
            {
                HashSet<string> anterior;
                if (!baseline.TryGetValue(path, out anterior))
                {
                    // Arquivo que apareceu depois do início (conta que entrou agora): vira linha de
                    // base, calado. Tratá-lo como "tudo novo" dispararia captura por conquista de
                    // outra pessoa.
                    baseline[path] = bits;
                    return;
                }

                novas = SteamStats.CountNew(anterior, bits);
                baseline[path] = bits;

                if (novas <= 0)
                {
                    return;
                }

                var agora = DateTime.UtcNow;
                if (agora - lastFiredUtc < Cooldown)
                {
                    log(string.Format("{0} conquista(s) nova(s), mas dentro da janela de {1}s da captura anterior.",
                                      novas, (int)Cooldown.TotalSeconds));
                    return;
                }

                lastFiredUtc = agora;
            }

            log(string.Format("{0} conquista(s) destravada(s). Pedindo captura.", novas));

            try
            {
                var handler = onUnlock;
                if (handler != null)
                {
                    handler(novas);
                }
            }
            catch (Exception ex)
            {
                log("A captura por conquista falhou: " + ex.Message);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
