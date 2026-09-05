using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace GameCaptureOrganizer.Input
{
    /// <summary>
    /// Uma tecla de atalho que funciona com o JOGO em primeiro plano, registrada no Windows.
    ///
    /// Por que <c>RegisterHotKey</c> e nao um gancho global de teclado (<c>WH_KEYBOARD_LL</c>, o
    /// caminho da PlayniteMemories): o gancho ve TODAS as teclas do sistema, precisa devolver
    /// rapido para nao engasgar a digitacao inteira do Windows, e e exatamente a assinatura que
    /// antivirus trata como keylogger. O registro pede uma combinacao so, e o Windows entrega
    /// apenas ela.
    ///
    /// O preco e honesto: se outro programa ja registrou a mesma combinacao, o registro FALHA — e
    /// falhar aqui e melhor do que roubar a tecla de quem pediu primeiro. Quem avisa e a tela de
    /// configuracao, com a combinacao escrita.
    /// </summary>
    public sealed class GlobalHotkey : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int HotkeyId = 0xC0DE;

        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;

        /// <summary>Sem isso, segurar a tecla dispara captura dezenas de vezes por segundo.</summary>
        private const uint ModNoRepeat = 0x4000;

        private readonly Action onPressed;
        private readonly Action<string> log;
        private readonly object gate = new object();

        private HwndSource source;
        private bool registered;

        public GlobalHotkey(Action onPressed, Action<string> log)
        {
            this.onPressed = onPressed;
            this.log = log ?? (_ => { });
        }

        public bool Registered
        {
            get { lock (gate) { return registered; } }
        }

        /// <summary>
        /// Registra a combinacao. PRECISA rodar na thread de UI: a janela de mensagens nasce presa
        /// a thread que a criou, e o Windows entrega o aviso da tecla so para ela.
        /// </summary>
        public bool Register(bool ctrl, bool alt, bool shift, string keyName)
        {
            lock (gate)
            {
                Unregister();

                var vk = VirtualKey(keyName);
                if (vk == 0)
                {
                    log("Tecla desconhecida na configuração: \"" + keyName + "\". O atalho ficou desligado.");
                    return false;
                }

                var mods = ModNoRepeat;
                if (ctrl) { mods |= ModControl; }
                if (alt) { mods |= ModAlt; }
                if (shift) { mods |= ModShift; }

                try
                {
                    // HWND_MESSAGE (-3): janela sem pixel nenhum, que existe só para receber aviso.
                    var parametros = new HwndSourceParameters("GameCaptureOrganizerHotkey")
                    {
                        ParentWindow = new IntPtr(-3),
                        Width = 0,
                        Height = 0
                    };

                    source = new HwndSource(parametros);
                    source.AddHook(WndProc);

                    registered = RegisterHotKey(source.Handle, HotkeyId, mods, (uint)vk);
                    if (!registered)
                    {
                        var erro = Marshal.GetLastWin32Error();
                        log(string.Format(
                            "Não consegui registrar {0} (erro {1}). O caso comum é outro programa já estar " +
                            "usando essa combinação — escolha outra na configuração.",
                            Describe(ctrl, alt, shift, keyName), erro));
                        DisposeSource();
                        return false;
                    }

                    log("Atalho de captura: " + Describe(ctrl, alt, shift, keyName) + ".");
                    return true;
                }
                catch (Exception ex)
                {
                    log("Falha ao registrar o atalho: " + ex.Message);
                    DisposeSource();
                    registered = false;
                    return false;
                }
            }
        }

        public void Unregister()
        {
            lock (gate)
            {
                if (source != null && registered)
                {
                    try { UnregisterHotKey(source.Handle, HotkeyId); } catch (Exception) { }
                }

                registered = false;
                DisposeSource();
            }
        }

        private void DisposeSource()
        {
            if (source == null)
            {
                return;
            }

            try
            {
                source.RemoveHook(WndProc);
                source.Dispose();
            }
            catch (Exception)
            {
            }

            source = null;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmHotkey || wParam.ToInt32() != HotkeyId)
            {
                return IntPtr.Zero;
            }

            handled = true;

            try
            {
                var acao = onPressed;
                if (acao != null)
                {
                    acao();
                }
            }
            catch (Exception ex)
            {
                // Exceção aqui subiria pela fila de mensagens do Playnite e derrubaria o app.
                log("A captura pelo atalho falhou: " + ex.Message);
            }

            return IntPtr.Zero;
        }

        /// <summary>Nome da tecla como a pessoa escreveu, traduzido para o código do Windows.</summary>
        public static int VirtualKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName))
            {
                return 0;
            }

            Key key;
            if (!Enum.TryParse(keyName.Trim(), true, out key) || key == Key.None)
            {
                return 0;
            }

            try
            {
                return KeyInterop.VirtualKeyFromKey(key);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static string Describe(bool ctrl, bool alt, bool shift, string keyName)
        {
            var texto = string.Empty;
            if (ctrl) { texto += "Ctrl+"; }
            if (alt) { texto += "Alt+"; }
            if (shift) { texto += "Shift+"; }
            return texto + (string.IsNullOrWhiteSpace(keyName) ? "?" : keyName.Trim());
        }

        public void Dispose()
        {
            Unregister();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
