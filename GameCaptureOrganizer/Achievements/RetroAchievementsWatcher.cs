using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameCaptureOrganizer.Achievements
{
    /// <summary>
    /// Consulta o RetroAchievements de tempos em tempos enquanto o jogo de emulador roda, e avisa
    /// quando aparece conquista nova.
    ///
    /// A primeira consulta e a linha de base, e e SILENCIOSA — mesma regra da Steam. Aqui ela
    /// importa ainda mais: a consulta pergunta pelas conquistas dos ultimos minutos, entao quem
    /// acabou de jogar noutro lugar abriria a sessao com uma rajada de capturas do que ja tinha
    /// destravado.
    ///
    /// Erro de rede NAO derruba a vigia e NAO dispara nada: a internet cair no meio do jogo e
    /// ocorrencia esperada. O intervalo dobra a cada falha seguida, ate um teto, para nao
    /// martelar um servidor que ja disse que nao pode responder.
    /// </summary>
    public sealed class RetroAchievementsWatcher : IDisposable
    {
        /// <summary>Janela consultada. Precisa ser maior que o intervalo, senao um atraso perde conquista.</summary>
        private const int WindowMinutes = 10;

        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

        private readonly string user;
        private readonly string apiKey;
        private readonly int pollSeconds;
        private readonly Action<int> onUnlock;
        private readonly Action<string> log;
        private readonly object gate = new object();

        private CancellationTokenSource cancellation;
        private HashSet<string> baseline;
        private DateTime lastFiredUtc = DateTime.MinValue;

        public RetroAchievementsWatcher(string user, string apiKey, int pollSeconds,
                                        Action<int> onUnlock, Action<string> log)
        {
            this.user = user;
            this.apiKey = apiKey;
            this.pollSeconds = Math.Max(30, pollSeconds);
            this.onUnlock = onUnlock;
            this.log = log ?? (_ => { });
        }

        public void Start()
        {
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(apiKey))
            {
                return;
            }

            lock (gate)
            {
                Stop();
                cancellation = new CancellationTokenSource();
                var token = cancellation.Token;
                Task.Run(() => Loop(token), token);
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                if (cancellation == null)
                {
                    return;
                }

                try { cancellation.Cancel(); } catch (Exception) { }
                try { cancellation.Dispose(); } catch (Exception) { }
                cancellation = null;
                baseline = null;
                lastFiredUtc = DateTime.MinValue;
            }
        }

        private async Task Loop(CancellationToken token)
        {
            var intervalo = TimeSpan.FromSeconds(pollSeconds);
            var espera = intervalo;
            var falhasSeguidas = 0;
            var avisouFalha = false;

            try
            {
                // A linha de base sai ANTES da primeira espera: começar a contar só daqui a um
                // minuto deixaria a conquista do primeiro minuto de jogo passar como "já existia".
                var inicial = RetroAchievements.Read(user, apiKey, WindowMinutes);
                if (inicial.Ok)
                {
                    baseline = inicial.Ids;
                    log(string.Format("De olho no RetroAchievements ({0} conquista(s) recente(s) na linha de base).",
                                      baseline.Count));
                }
                else
                {
                    log("Não consegui iniciar a vigia do RetroAchievements: " + inicial.Error);
                }

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(espera, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    var leitura = RetroAchievements.Read(user, apiKey, WindowMinutes);
                    if (!leitura.Ok)
                    {
                        falhasSeguidas++;
                        espera = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, espera.Ticks * 2));

                        // Uma vez por sessão: o log não pode virar uma linha por minuto de internet ruim.
                        if (!avisouFalha)
                        {
                            avisouFalha = true;
                            log("RetroAchievements indisponível (" + leitura.Error +
                                "). Continuo tentando, cada vez mais espaçado.");
                        }

                        continue;
                    }

                    if (falhasSeguidas > 0)
                    {
                        log("RetroAchievements respondeu de novo.");
                        falhasSeguidas = 0;
                        avisouFalha = false;
                        espera = intervalo;
                    }

                    Compare(leitura.Ids);
                }
            }
            catch (Exception ex)
            {
                log("A vigia do RetroAchievements parou: " + ex.Message);
            }
        }

        private void Compare(HashSet<string> atual)
        {
            int novas;
            lock (gate)
            {
                if (baseline == null)
                {
                    // A linha de base falhou lá atrás: esta leitura vira a base, calada.
                    baseline = atual;
                    return;
                }

                novas = RetroAchievements.CountNew(baseline, atual);
                baseline = atual;

                if (novas <= 0)
                {
                    return;
                }

                var agora = DateTime.UtcNow;
                if (agora - lastFiredUtc < Cooldown)
                {
                    return;
                }

                lastFiredUtc = agora;
            }

            log(string.Format("{0} conquista(s) no RetroAchievements. Pedindo captura.", novas));

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
