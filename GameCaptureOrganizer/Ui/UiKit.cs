using System;
using System.Windows;
using System.Windows.Media;

namespace GameCaptureOrganizer.Ui
{
    /// <summary>
    /// Acesso ao dicionário de estilos a partir de código.
    ///
    /// As telas em XAML mergeiam o Theme.xaml sozinhas, mas três superfícies continuam sendo
    /// desenhadas em C# — os gráficos do painel, a lista de dispositivos e o histórico. Sem este
    /// atalho elas repetiriam os hexadecimais à mão, que é exatamente como as cores fixas
    /// espalhadas apareceram na versão anterior.
    /// </summary>
    public static class UiKit
    {
        /// <summary>
        /// URI absoluta de propósito. O Playnite carrega o plugin como assembly solto, sem
        /// aplicação WPF dona do recurso, e aí um caminho relativo não resolve.
        /// </summary>
        public const string ThemeUri = "pack://application:,,,/GameCaptureOrganizer;component/Ui/Theme.xaml";

        private static ResourceDictionary cached;

        public static ResourceDictionary Theme
        {
            get
            {
                if (cached == null)
                {
                    cached = new ResourceDictionary { Source = new Uri(ThemeUri, UriKind.Absolute) };
                }
                return cached;
            }
        }

        /// <summary>Mergeia o dicionário num elemento montado em código.</summary>
        public static T Themed<T>(T element) where T : FrameworkElement
        {
            if (!element.Resources.MergedDictionaries.Contains(Theme))
            {
                element.Resources.MergedDictionaries.Add(Theme);
            }
            return element;
        }

        public static Brush Brush(string key)
        {
            var found = Theme[key] as Brush;
            return found ?? Brushes.Transparent;
        }

        public static Color Color(string key)
        {
            var found = Theme[key];
            if (found is Color) return (Color)found;
            var brush = found as SolidColorBrush;
            return brush != null ? brush.Color : Colors.Transparent;
        }

        public static Style Style(string key)
        {
            return Theme[key] as Style;
        }

        /// <summary>
        /// O ícone da barra lateral, lido do icon.png que acompanha a extensão.
        ///
        /// Se o arquivo não estiver ao lado da DLL — instalação incompleta, ou pacote gerado sem
        /// ele — cai nas iniciais em texto. Um ícone quebrado na barra lateral é pior do que duas
        /// letras, e a extensão não deve deixar de carregar por causa de uma imagem.
        /// </summary>
        public static FrameworkElement SidebarIcon()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                var file = System.IO.Path.Combine(dir ?? "", "icon.png");
                if (System.IO.File.Exists(file))
                {
                    var source = new System.Windows.Media.Imaging.BitmapImage();
                    source.BeginInit();
                    source.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    source.UriSource = new Uri(file, UriKind.Absolute);
                    source.EndInit();
                    source.Freeze();

                    return new System.Windows.Controls.Image
                    {
                        Source = source,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(2)
                    };
                }
            }
            catch { }

            return new System.Windows.Controls.TextBlock
            {
                Text = "GO",
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>Aplica um estilo nomeado, ignorando chave que não existe em vez de estourar.</summary>
        public static T Apply<T>(T element, string styleKey) where T : FrameworkElement
        {
            var style = Style(styleKey);
            if (style != null) element.Style = style;
            return element;
        }
    }
}
