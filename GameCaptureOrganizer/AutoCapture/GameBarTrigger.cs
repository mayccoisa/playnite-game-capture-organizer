using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GameCaptureOrganizer.AutoCapture
{
    /// <summary>
    /// Aperta os atalhos do Xbox Game Bar por <c>SendInput</c>:
    /// <c>Win+Alt+PrtScn</c> para o print e <c>Win+Alt+G</c> para salvar o clipe dos ultimos
    /// segundos.
    ///
    /// Por que atalho e nao API: o Game Bar nao expoe uma. O atalho e o unico caminho publico, e
    /// ele tem a vantagem de o Windows tratar a captura no compositor — funciona em tela cheia
    /// exclusiva e com HDR, onde captura por janela sai preta.
    ///
    /// O que ele NAO alcanca, e nao adianta insistir:
    /// - jogo rodando elevado (como administrador) com o Playnite normal. O Windows recusa entrada
    ///   sintetica de processo menos privilegiado, em silencio, e nao existe contorno por codigo;
    /// - processo que o proprio Game Bar recusa reconhecer como jogo;
    /// - <c>Win+Alt+G</c> com a gravacao em segundo plano desligada: o atalho existe, nao faz nada.
    ///   Quem avisa disso e o <see cref="GameBarState"/>.
    /// </summary>
    public sealed class GameBarTrigger : ICaptureTrigger
    {
        private const int VkLWin = 0x5B;
        private const int VkLMenu = 0xA4;   // Alt esquerdo
        private const int VkSnapshot = 0x2C; // PrtScn
        private const int VkG = 0x47;

        private const uint InputKeyboard = 1;
        private const uint KeyEventKeyUp = 0x0002;

        /// <summary>
        /// Duas teclas soltas no mesmo milissegundo chegam fora de ordem em jogo que le entrada por
        /// polling. 20 ms e imperceptivel para quem joga e suficiente para o atalho ser reconhecido.
        /// </summary>
        private const int KeyGapMs = 20;

        private readonly Action<string> log;

        public GameBarTrigger(Action<string> log = null)
        {
            this.log = log ?? (_ => { });
        }

        public string Name
        {
            get { return "Xbox Game Bar"; }
        }

        public bool TakeScreenshot()
        {
            return Send("print (Win+Alt+PrtScn)", VkSnapshot);
        }

        public bool SaveClip()
        {
            return Send("clipe (Win+Alt+G)", VkG);
        }

        private bool Send(string what, int key)
        {
            try
            {
                var inputs = new[]
                {
                    Key(VkLWin, false),
                    Key(VkLMenu, false),
                    Key(key, false),
                    Key(key, true),
                    Key(VkLMenu, true),
                    Key(VkLWin, true)
                };

                // Enviado um a um, com respiro entre eles: um bloco de seis eventos no mesmo
                // SendInput chega como rajada e o Game Bar perde a combinacao com o jogo em foco.
                var size = Marshal.SizeOf(typeof(Input));
                foreach (var input in inputs)
                {
                    var sent = SendInput(1, new[] { input }, size);
                    if (sent != 1)
                    {
                        var erro = Marshal.GetLastWin32Error();
                        log(string.Format(
                            "O Windows recusou o atalho de {0} (erro {1}). O caso comum é o jogo estar " +
                            "rodando como administrador e o Playnite não: entrada sintética não sobe de nível.",
                            what, erro));
                        return false;
                    }

                    Thread.Sleep(KeyGapMs);
                }

                log("Atalho de " + what + " enviado ao Game Bar.");
                return true;
            }
            catch (Exception ex)
            {
                log("Falha ao enviar o atalho de " + what + ": " + ex.Message);
                return false;
            }
        }

        private static Input Key(int vk, bool up)
        {
            return new Input
            {
                type = InputKeyboard,
                union = new InputUnion
                {
                    keyboard = new KeyboardInput
                    {
                        vk = (ushort)vk,
                        scan = 0,
                        flags = up ? KeyEventKeyUp : 0,
                        time = 0,
                        extraInfo = IntPtr.Zero
                    }
                }
            };
        }

        // ---------------------------------------------------------------- interop

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint type;
            public InputUnion union;
        }

        // A uniao precisa dos tres membros: o tamanho da struct e o do MAIOR deles, e e esse
        // tamanho que o SendInput confere. Declarar so o teclado faz a chamada falhar em x64.
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput mouse;
            [FieldOffset(0)] public KeyboardInput keyboard;
            [FieldOffset(0)] public HardwareInput hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort vk;
            public ushort scan;
            public uint flags;
            public uint time;
            public IntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            public uint msg;
            public ushort paramL;
            public ushort paramH;
        }
    }
}
