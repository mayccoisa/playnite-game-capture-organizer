using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameCaptureOrganizer.AutoCapture
{
    /// <summary>
    /// O relogio da captura automatica: enquanto um jogo aberto pelo Playnite estiver rodando,
    /// pede um print de tempos em tempos. Fora de sessao ele nao existe — nada e disparado com o
    /// Playnite parado na biblioteca, e essa e a diferenca entre uma pasta de lembrancas e uma
    /// pasta de prints da area de trabalho.
    ///
    /// Uma sessao por vez, de proposito: o Playnite so tem um jogo em primeiro plano, e o atalho
    /// do Game Bar vale para a janela ativa. Comecar a segunda encerra a primeira.
    /// </summary>
    public sealed class CaptureScheduler : IDisposable
    {
        private readonly ICaptureTrigger trigger;
        private readonly TriggerLog triggers;
        private readonly Func<OrganizerSettings> settings;
        private readonly Action<string> log;
        private readonly object gate = new object();

        private CancellationTokenSource cancellation;
        private Task loop;
        private Task clipLoop;
        private string gameId;
        private string gameName;

        public CaptureScheduler(ICaptureTrigger trigger, TriggerLog triggers,
                                Func<OrganizerSettings> settings, Action<string> log)
        {
            this.trigger = trigger;
            this.triggers = triggers;
            this.settings = settings;
            this.log = log ?? (_ => { });
        }

        public bool Running
        {
            get { lock (gate) { return cancellation != null; } }
        }

        /// <summary>
        /// Liga o relogio com o plano JA RESOLVIDO (padrao + excecao do jogo). O agendador nao
        /// resolve regra: quem sabe da excecao e o plugin, que tem o id do jogo na mao.
        /// </summary>
        public void Start(string gameId, string gameName, EffectiveCapture plan)
        {
            if (plan == null || !plan.Enabled)
            {
                return;
            }

            var minutosPrint = plan.ScreenshotMinutes;
            var minutosClipe = plan.ClipMinutes;

            lock (gate)
            {
                StopCore();

                this.gameId = gameId;
                this.gameName = gameName;
                cancellation = new CancellationTokenSource();
                var token = cancellation.Token;

                // Dois relógios independentes, e não um só com contagem: print a cada 5 minutos e
                // clipe a cada 30 não têm divisor comum útil, e amarrar os dois faria o intervalo
                // de um puxar o do outro.
                if (minutosPrint > 0)
                {
                    loop = Task.Run(() => Loop(minutosPrint, false, token), token);
                }

                if (minutosClipe > 0)
                {
                    clipLoop = Task.Run(() => Loop(minutosClipe, true, token), token);
                }
            }

            if (minutosPrint <= 0 && minutosClipe <= 0)
            {
                log("Captura automática ligada, mas com os dois intervalos zerados: nada será disparado sozinho.");
                return;
            }

            log(string.Format("Captura automática ligada para \"{0}\" pelo {1}: print {2}, clipe {3}.",
                              gameName,
                              trigger.Name,
                              minutosPrint > 0 ? "a cada " + minutosPrint + " min" : "desligado",
                              minutosClipe > 0 ? "a cada " + minutosClipe + " min" : "desligado"));
        }

        public void Stop()
        {
            lock (gate)
            {
                StopCore();
            }
        }

        private void StopCore()
        {
            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
                // Espera curta: o laco so dorme e dispara atalho, e segurar o fim do jogo por
                // causa disso atrasaria a organizacao, que e o que a pessoa esta esperando ver.
                if (loop != null)
                {
                    loop.Wait(TimeSpan.FromSeconds(2));
                }

                if (clipLoop != null)
                {
                    clipLoop.Wait(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                try { cancellation.Dispose(); } catch (Exception) { }
                cancellation = null;
                loop = null;
                clipLoop = null;
                gameId = null;
                gameName = null;
            }
        }

        /// <summary>Dispara na hora, por pedido da pessoa (menu ou tecla). Vale mesmo sem sessao aberta.</summary>
        public bool CaptureNow(CaptureReason reason)
        {
            return Capture(reason, false, false);
        }

        /// <summary>A captura de uma conquista: print e, se pedido, o clipe dos ultimos segundos.</summary>
        public bool CaptureForAchievement(bool alsoClip)
        {
            return Capture(CaptureReason.Conquista, alsoClip, false);
        }

        /// <summary>
        /// Uma captura, com o motivo carimbado no caderno de gatilhos.
        ///
        /// Quando sai print E clipe, os dois atalhos saem com respiro entre eles. Mandar
        /// Win+Alt+PrtScn e Win+Alt+G colados faz o Game Bar tratar a segunda combinacao como
        /// repeticao da primeira, e o clipe nao sai — sem erro nenhum, o que e pior do que falhar.
        /// </summary>
        public bool Capture(CaptureReason reason, bool clip, bool clipOnly)
        {
            var feito = false;

            if (!clipOnly)
            {
                var quandoPrint = DateTime.UtcNow;
                if (trigger.TakeScreenshot())
                {
                    triggers.Add(reason, gameId, gameName, quandoPrint);
                    feito = true;
                }
            }

            if (clip || clipOnly)
            {
                if (!clipOnly)
                {
                    Thread.Sleep(700);
                }

                var quandoClipe = DateTime.UtcNow;
                if (trigger.SaveClip())
                {
                    triggers.Add(reason, gameId, gameName, quandoClipe);
                    feito = true;
                }
            }

            if (feito)
            {
                Confirm();
            }

            return feito;
        }

        /// <summary>
        /// A confirmacao de que fomos NOS que pedimos. Sai depois do atalho, nunca antes: um bipe
        /// que toca e nao vira captura ensina a pessoa a confiar no som errado.
        ///
        /// Falha aqui e engolida de proposito — maquina sem saida de audio, ou com o esquema de
        /// sons do Windows mudo, nao pode derrubar a captura em si.
        /// </summary>
        private void Confirm()
        {
            var current = settings();
            if (current == null || !current.PlaySoundOnCapture)
            {
                return;
            }

            try
            {
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// O relogio. <paramref name="clip"/> decide se a volta pede clipe (Win+Alt+G) ou print.
        /// </summary>
        private async Task Loop(int minutes, bool clip, CancellationToken token)
        {
            var interval = TimeSpan.FromMinutes(minutes);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Dorme ANTES da primeira captura: o jogo acabou de abrir e a tela e a de
                    // carregamento, que nao e lembranca de nada. No clipe isso vale duas vezes —
                    // o buffer do Game Bar mal comecou a encher.
                    try
                    {
                        await Task.Delay(interval, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }

                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    var current = settings();
                    if (current == null || !current.AutoCaptureEnabled)
                    {
                        // Desligou a chave com o jogo aberto: o laco morre na proxima volta em vez
                        // de continuar disparando ate o fim da sessao.
                        break;
                    }

                    Capture(CaptureReason.Periodico, false, clip);
                }
            }
            catch (Exception ex)
            {
                // O laco roda solto: excecao aqui morreria em silencio e a captura pararia sem
                // ninguem saber por que.
                log("O laço da captura automática parou: " + ex.Message);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
