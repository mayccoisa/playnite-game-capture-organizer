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

        public void Start(string gameId, string gameName)
        {
            var current = settings();
            if (current == null || !current.AutoCaptureEnabled)
            {
                return;
            }

            var minutes = current.ScreenshotIntervalMinutes;
            if (minutes <= 0)
            {
                log("Captura automática ligada, mas com intervalo zerado: nada será disparado.");
                return;
            }

            lock (gate)
            {
                StopCore();

                this.gameId = gameId;
                this.gameName = gameName;
                cancellation = new CancellationTokenSource();
                var token = cancellation.Token;
                loop = Task.Run(() => Loop(minutes, token), token);
            }

            log(string.Format("Captura automática ligada para \"{0}\": print a cada {1} min pelo {2}.",
                              gameName, minutes, trigger.Name));
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
            }
            catch (Exception)
            {
            }
            finally
            {
                try { cancellation.Dispose(); } catch (Exception) { }
                cancellation = null;
                loop = null;
                gameId = null;
                gameName = null;
            }
        }

        /// <summary>Dispara na hora, por pedido da pessoa (menu ou tecla). Vale mesmo sem sessao aberta.</summary>
        public bool CaptureNow(CaptureReason reason)
        {
            var quando = DateTime.UtcNow;
            if (!trigger.TakeScreenshot())
            {
                return false;
            }

            triggers.Add(reason, gameId, gameName, quando);
            Confirm();
            return true;
        }

        /// <summary>
        /// A captura de uma conquista: print e, se pedido, o clipe dos ultimos segundos.
        ///
        /// Os dois atalhos saem com respiro entre eles. Mandar Win+Alt+PrtScn e Win+Alt+G colados
        /// faz o Game Bar tratar a segunda combinacao como repeticao da primeira, e o clipe nao
        /// sai — sem erro nenhum, o que e pior do que falhar.
        /// </summary>
        public bool CaptureForAchievement(bool alsoClip)
        {
            var feito = false;

            var quandoPrint = DateTime.UtcNow;
            if (trigger.TakeScreenshot())
            {
                triggers.Add(CaptureReason.Conquista, gameId, gameName, quandoPrint);
                feito = true;
            }

            if (alsoClip)
            {
                Thread.Sleep(700);

                var quandoClipe = DateTime.UtcNow;
                if (trigger.SaveClip())
                {
                    triggers.Add(CaptureReason.Conquista, gameId, gameName, quandoClipe);
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

        private async Task Loop(int minutes, CancellationToken token)
        {
            var interval = TimeSpan.FromMinutes(minutes);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Dorme ANTES do primeiro print: o jogo acabou de abrir e a tela e a de
                    // carregamento, que nao e lembranca de nada.
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

                    var quando = DateTime.UtcNow;
                    if (trigger.TakeScreenshot())
                    {
                        triggers.Add(CaptureReason.Periodico, gameId, gameName, quando);
                        Confirm();
                    }
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
