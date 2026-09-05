using System;

namespace GameCaptureOrganizer.AutoCapture
{
    /// <summary>Por que esta captura foi disparada. Vira o marcador {Motivo} no nome do arquivo.</summary>
    public enum CaptureReason
    {
        Manual = 0,
        Periodico = 1,
        Conquista = 2
    }

    /// <summary>
    /// Quem APERTA o botao de captura. A extensao nao grava nada por conta propria: ela decide o
    /// QUANDO e delega o COMO.
    ///
    /// A interface existe para a decisao de motor ser reversivel. Hoje a unica implementacao e o
    /// <see cref="GameBarTrigger"/>, que aciona o Xbox Game Bar — ele ja resolve tela cheia
    /// exclusiva, DirectX 12 e HDR, que e exatamente onde um motor caseiro por PrintWindow salva
    /// imagem preta sem perceber. Trocar por um motor proprio (Windows Graphics Capture + Media
    /// Foundation) e escrever outra implementacao daqui, sem encostar no agendador.
    /// </summary>
    public interface ICaptureTrigger
    {
        /// <summary>Nome do motor, para o log e para a tela de configuracao.</summary>
        string Name { get; }

        /// <summary>Tira um print. Devolve false quando o motor recusou o pedido.</summary>
        bool TakeScreenshot();

        /// <summary>
        /// Salva o clipe dos ultimos segundos. Depende de o motor manter gravacao continua —
        /// no Game Bar, a "gravacao em segundo plano". Sem ela o pedido nao faz nada.
        /// </summary>
        bool SaveClip();
    }

    /// <summary>Rotulos dos motivos. Ficam aqui porque entram no nome do arquivo e no log.</summary>
    public static class CaptureReasons
    {
        public const string Manual = "manual";
        public const string Periodico = "periodico";
        public const string Conquista = "conquista";

        public static string Label(CaptureReason reason)
        {
            switch (reason)
            {
                case CaptureReason.Periodico: return Periodico;
                case CaptureReason.Conquista: return Conquista;
                case CaptureReason.Manual: return Manual;
                default: return string.Empty;
            }
        }
    }
}
