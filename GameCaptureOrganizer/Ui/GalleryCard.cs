using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using GameCaptureOrganizer.Gallery;

namespace GameCaptureOrganizer.Ui
{
    /// <summary>
    /// Uma captura na grade. A miniatura chega DEPOIS, carregada fora da thread de UI: uma pasta
    /// com trezentos prints de 4K travaria o Playnite por segundos se a decodificacao acontecesse
    /// junto com a montagem da lista.
    /// </summary>
    public class GalleryCard : INotifyPropertyChanged
    {
        private BitmapSource thumbnail;

        public GalleryCard(GalleryItem item)
        {
            Item = item;
        }

        public GalleryItem Item { get; private set; }

        public string FileName { get { return Item.FileName; } }

        public string Group { get { return Item.Group; } }

        public bool IsVideo { get { return Item.Kind == CaptureKind.Video; } }

        /// <summary>Data e hora legiveis, mais o tamanho. E o que a pessoa usa para achar a captura certa.</summary>
        public string Caption
        {
            get
            {
                return Item.WhenLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) +
                       " · " + Size(Item.SizeBytes);
            }
        }

        public BitmapSource Thumbnail
        {
            get { return thumbnail; }
            set
            {
                thumbnail = value;
                var handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs("Thumbnail"));
                    handler(this, new PropertyChangedEventArgs("HasThumbnail"));
                }
            }
        }

        public bool HasThumbnail { get { return thumbnail != null; } }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Le a imagem em tamanho pequeno e CONGELA, que e o que permite ela ter sido criada numa
        /// thread e ser desenhada na de UI. Video nao tem miniatura: extrair um quadro exigiria um
        /// decodificador que a extensao nao tem, e desenhar um quadro falso seria inventar.
        /// </summary>
        public static BitmapSource LoadThumbnail(GalleryItem item, int width)
        {
            if (item == null || item.Kind != CaptureKind.Screenshot || !File.Exists(item.Path))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;   // solta o arquivo no fim da leitura
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = width;                 // decodifica pequeno, nao encolhe depois
                bitmap.UriSource = new Uri(item.Path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception)
            {
                // Arquivo corrompido, meio arquivo ainda sendo escrito pelo Game Bar, formato que o
                // WPF nao le: o cartao fica sem miniatura, e o resto da grade continua de pe.
                return null;
            }
        }

        private static string Size(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (bytes / 1024d / 1024d / 1024d).ToString("0.0", CultureInfo.CurrentCulture) + " GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.CurrentCulture) + " MB";
            }

            return Math.Max(1, bytes / 1024L) + " KB";
        }
    }
}
