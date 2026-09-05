using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GameCaptureOrganizer.Gallery;

namespace GameCaptureOrganizer.Ui
{
    /// <summary>
    /// O painel lateral: a pasta organizada vista de dentro do Playnite, sem abrir o Explorador.
    ///
    /// Ele LE o disco e nao guarda banco proprio. A pasta organizada e a verdade — um indice
    /// paralelo discordaria dela no primeiro arquivo que a pessoa mover na mao, e ai o painel
    /// passaria a mentir com autoridade.
    /// </summary>
    public partial class GalleryView : UserControl
    {
        private const int ThumbnailWidth = 240;

        private readonly CaptureOrganizerPlugin plugin;
        private readonly ObservableCollection<GalleryCard> cards = new ObservableCollection<GalleryCard>();
        private List<GalleryItem> todos = new List<GalleryItem>();
        private CancellationTokenSource thumbnailWork;

        public GalleryView(CaptureOrganizerPlugin plugin)
        {
            InitializeComponent();

            this.plugin = plugin;
            CardList.ItemsSource = cards;

            Load();
        }

        // ---------------------------------------------------------------- leitura

        private void Load()
        {
            var settings = plugin.Settings;
            var destino = settings.DestinationFolder;

            try
            {
                todos = GalleryScanner.Scan(
                    destino,
                    CaptureCore.ParseExtensionList(settings.ImageExtensions),
                    CaptureCore.ParseExtensionList(settings.VideoExtensions));
            }
            catch (Exception)
            {
                todos = new List<GalleryItem>();
            }

            var grupos = GalleryScanner.Group(todos);

            // "Tudo" primeiro: quem abre o painel logo depois de jogar quer ver a captura de agora,
            // que pode estar em qualquer pasta.
            var lista = new List<object> { TodosLabel(grupos) };
            foreach (var grupo in grupos)
            {
                lista.Add(grupo);
            }

            GroupList.ItemsSource = lista;
            GroupList.SelectedIndex = 0;

            var vazio = todos.Count == 0;
            EmptyState.Visibility = vazio ? Visibility.Visible : Visibility.Collapsed;
            if (vazio)
            {
                EmptyText.Text = string.IsNullOrWhiteSpace(destino)
                    ? "A pasta de destino ainda não foi escolhida. Configure em Configurações da extensão › Pastas."
                    : "Nada encontrado em " + destino + ". Use \"Organizar agora\" para trazer o que o Game Bar já salvou.";
            }
        }

        private static GalleryGroup TodosLabel(List<GalleryGroup> grupos)
        {
            return new GalleryGroup
            {
                Name = "Tudo",
                Screenshots = grupos.Sum(g => g.Screenshots),
                Videos = grupos.Sum(g => g.Videos),
                LastCapture = grupos.Count == 0 ? DateTime.MinValue : grupos.Max(g => g.LastCapture)
            };
        }

        private void Show()
        {
            var grupo = GroupList.SelectedItem as GalleryGroup;
            var filtro = FilterBox.SelectedIndex;

            IEnumerable<GalleryItem> itens = todos;

            if (grupo != null && GroupList.SelectedIndex > 0)
            {
                itens = itens.Where(i => string.Equals(i.Group, grupo.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (filtro == 1)
            {
                itens = itens.Where(i => i.Kind == CaptureKind.Screenshot);
            }
            else if (filtro == 2)
            {
                itens = itens.Where(i => i.Kind == CaptureKind.Video);
            }

            var selecionados = itens.ToList();

            cards.Clear();
            foreach (var item in selecionados)
            {
                cards.Add(new GalleryCard(item));
            }

            CountText.Text = selecionados.Count == 1
                ? "1 captura"
                : selecionados.Count + " capturas";

            LoadThumbnails();
        }

        /// <summary>
        /// As miniaturas entram uma a uma, fora da thread de UI. A troca de pasta CANCELA o que
        /// estava carregando: sem isso, clicar rapidinho em cinco jogos deixa cinco leituras
        /// disputando o disco para desenhar cartoes que ninguem esta mais olhando.
        /// </summary>
        private void LoadThumbnails()
        {
            if (thumbnailWork != null)
            {
                try { thumbnailWork.Cancel(); } catch (Exception) { }
                try { thumbnailWork.Dispose(); } catch (Exception) { }
            }

            thumbnailWork = new CancellationTokenSource();
            var token = thumbnailWork.Token;
            var pendentes = cards.ToList();

            Task.Run(() =>
            {
                foreach (var card in pendentes)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    var thumb = GalleryCard.LoadThumbnail(card.Item, ThumbnailWidth);
                    if (thumb == null || token.IsCancellationRequested)
                    {
                        continue;
                    }

                    // A imagem ja esta congelada, entao so a atribuicao precisa da thread de UI.
                    var alvo = card;
                    Dispatcher.BeginInvoke(new Action(() => alvo.Thumbnail = thumb));
                }
            }, token);
        }

        // ---------------------------------------------------------------- botoes

        private void OnGroupChanged(object sender, SelectionChangedEventArgs e)
        {
            Show();
        }

        private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded && cards.Count == 0)
            {
                return;
            }

            Show();
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            Load();
        }

        private void OnOrganize(object sender, RoutedEventArgs e)
        {
            plugin.RunInteractive();
            Load();
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            var grupo = GroupList.SelectedItem as GalleryGroup;
            var destino = plugin.Settings.DestinationFolder;

            if (grupo != null && GroupList.SelectedIndex > 0 && !string.IsNullOrWhiteSpace(destino) &&
                grupo.Name != GalleryScanner.RootGroup)
            {
                plugin.OpenFolder(System.IO.Path.Combine(destino, grupo.Name));
                return;
            }

            plugin.OpenFolder(destino);
        }

        private void OnOpenCapture(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var card = CardList.SelectedItem as GalleryCard;
            if (card == null)
            {
                return;
            }

            plugin.OpenCapture(card.Item.Path);
        }
    }
}
