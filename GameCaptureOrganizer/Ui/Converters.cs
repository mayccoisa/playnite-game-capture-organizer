using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GameCaptureOrganizer.Ui
{
    /// <summary>Caixa marcada vira a palavra que aparece na coluna da direita dos cards.</summary>
    public class BoolToOnOffConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return IsTrue(value) ? "Ativado" : "Desativado";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        internal static bool IsTrue(object value)
        {
            return value is bool && (bool)value;
        }
    }

    /// <summary>
    /// A cor do valor: verde quando ligado, cinza quando desligado.
    ///
    /// As cores saem do próprio dicionário, e não de constantes aqui, para não existirem dois
    /// verdes que precisem ser mudados juntos.
    /// </summary>
    public class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return UiKit.Brush(BoolToOnOffConverter.IsTrue(value) ? "GreenBrush" : "TextMutedBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Texto vazio mostra o elemento. É o que faz o aviso de "não vinculado" aparecer.</summary>
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            return string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>O oposto: só mostra quando há texto. Serve para linha de erro.</summary>
    public class FilledStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Data e hora local, ou um travessão quando nunca aconteceu.
    ///
    /// Travessão e não "0" nem data vazia: ausência de registro não é o mesmo que registro zerado,
    /// e é a diferença entre "nunca enviei" e "enviei e não veio nada".
    /// </summary>
    public class NullableDateToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime)
            {
                return ((DateTime)value).ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            }
            return "—";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Texto, ou um travessão quando vazio. Campo em branco na coluna da direita ficaria
    /// indistinguível de valor que não carregou.</summary>
    public class TextOrDashConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Visibilidade por booleano. Passe "inverse" no parâmetro para trocar o sentido.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var show = BoolToOnOffConverter.IsTrue(value);
            if (string.Equals(parameter as string, "inverse", StringComparison.OrdinalIgnoreCase))
            {
                show = !show;
            }
            return show ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
