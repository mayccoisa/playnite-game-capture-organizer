using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GameCaptureOrganizer.Gallery;

namespace GameCaptureOrganizer.Ui
{
    /// <summary>
    /// O painel lateral: a pasta organizada vista de dentro do Playnite, sem abrir o Explorador,
    /// com a configuracao da extensao na aba ao lado.
    ///
    /// Ele LE o disco e nao guarda banco proprio. A pasta organizada e a verdade — um indice
    /// paralelo discordaria dela no primeiro arquivo que a pessoa mover na mao, e ai o painel
    /// passaria a mentir com autoridade.
    /// </summary>
    public partial class GalleryView : UserControl
    {
        private const int ThumbnailWidth = 240;
        private const int IconWidth = 40;

        private readonly CaptureOrganizerPlugin plugin;
        private readonly ObservableCollection<GalleryCard> cards = new ObservableCollection<GalleryCard>();
        private List<GalleryItem> todos = new List<GalleryItem>();
        private CancellationTokenSource thumbnailWork;
        private CancellationTokenSource iconWork;

        private SettingsView settings;
        private bool editingSettings;

        /// <summary>
        /// Ligado enquanto a tela se monta e enquanto os controles sao preenchidos com a
        /// preferencia gravada.
        ///
        /// NASCE LIGADO de proposito. O `IsSelected="True"` do primeiro item de cada ComboBox
        /// dispara SelectionChanged durante o InitializeComponent — ou seja, ANTES de o corpo do
        /// construtor rodar, com `plugin` ainda nulo e `FolderTree` ainda nao criado. Sem esta
        /// trava, abrir o painel morre com NullReferenceException dentro do proprio construtor.
        /// </summary>
        private bool restoring = true;

        /// <summary>
        /// O caminho da pasta aberta, guardado como TEXTO e nao como referencia ao no: recarregar
        /// monta uma arvore nova, e o objeto antigo viraria uma selecao que nao existe mais. Quem
        /// clicou em "Atualizar" espera continuar na mesma pasta.
        /// </summary>
        private string selectedPath;
        private bool selectedIsAll = true;
        private bool selectedIsLoose;

        public GalleryView(CaptureOrganizerPlugin plugin)
        {
            InitializeComponent();

            this.plugin = plugin;
            CardList.ItemsSource = cards;

            RestorePreferences();
            ApplyMode();

            Load();
        }

        // ---------------------------------------------------------------- as duas visualizacoes

        /// <summary>
        /// O painel tem DUAS visualizacoes, e uma nao substitui a outra.
        ///
        /// A **grade corrida** e como o painel nasceu: tudo que foi organizado, do mais recente
        /// para o mais antigo, sem coluna nenhuma no caminho. E a resposta para "o que saiu hoje",
        /// que nao tem pasta — a captura de agora pode estar em qualquer uma.
        ///
        /// A **arvore** e para cacar dentro de uma pasta especifica, com todos os niveis.
        ///
        /// Trocar uma pela outra foi erro de leitura meu numa rodada anterior: quem quer olhar o
        /// que acabou de sair nao quer navegar pasta nenhuma, e quem quer um print de tres meses
        /// atras nao quer rolar uma grade de quatrocentos cartoes.
        /// </summary>
        private bool GroupByFolder
        {
            get { return ModeBox.SelectedIndex == 0; }
        }

        private void RestorePreferences()
        {
            try
            {
                var config = plugin.Settings;
                ModeBox.SelectedIndex = config.GalleryGroupByFolder ? 0 : 1;
                SubfoldersBox.IsChecked = config.GalleryIncludeSubfolders;
            }
            catch (Exception)
            {
                ModeBox.SelectedIndex = 0;
                SubfoldersBox.IsChecked = true;
            }
            finally
            {
                restoring = false;
            }
        }

        /// <summary>
        /// Mostra ou esconde o que so pertence a visualizacao por pasta. A COLUNA vai a zero junto
        /// com a arvore: esconder so a arvore deixaria 260 pixels de buraco entre a borda e os
        /// cartoes, que na largura do Ally e um terco da tela.
        /// </summary>
        private void ApplyMode()
        {
            var porPasta = GroupByFolder;

            TreeColumn.Width = porPasta ? new GridLength(260) : new GridLength(0);
            FolderTree.Visibility = porPasta ? Visibility.Visible : Visibility.Collapsed;
            SubfoldersBox.Visibility = porPasta ? Visibility.Visible : Visibility.Collapsed;

            HeaderText.Text = porPasta
                ? "Abra a pasta na árvore à esquerda. Clique duas vezes para abrir a captura."
                : "Tudo que já foi organizado, do mais recente para o mais antigo. Clique duas vezes para abrir a captura.";
        }

        /// <summary>
        /// Guarda a escolha. Gravar no disco enquanto a aba de configuracao esta com uma edicao
        /// aberta desceria junto os campos meio digitados, entao nesse caso a preferencia fica so
        /// na memoria e e gravada no proximo Salvar de la.
        /// </summary>
        private void PersistPreferences()
        {
            try
            {
                var config = plugin.Settings;
                config.GalleryGroupByFolder = GroupByFolder;
                config.GalleryIncludeSubfolders = SubfoldersBox.IsChecked == true;

                if (!editingSettings)
                {
                    plugin.SaveSettings();
                }
            }
            catch (Exception)
            {
                // Preferencia de tela nao vale derrubar o painel.
            }
        }

        // ---------------------------------------------------------------- leitura

        private void Load()
        {
            var config = plugin.Settings;
            var destino = config.DestinationFolder;

            try
            {
                todos = GalleryScanner.Scan(
                    destino,
                    CaptureCore.ParseExtensionList(config.ImageExtensions),
                    CaptureCore.ParseExtensionList(config.VideoExtensions));
            }
            catch (Exception)
            {
                todos = new List<GalleryItem>();
            }

            var arvore = GalleryTree.Build(todos);
            FolderTree.ItemsSource = arvore;

            RestoreSelection(arvore);
            LoadIcons(arvore);

            var vazio = todos.Count == 0;
            EmptyState.Visibility = vazio ? Visibility.Visible : Visibility.Collapsed;
            if (vazio)
            {
                EmptyText.Text = string.IsNullOrWhiteSpace(destino)
                    ? "A pasta de destino ainda não foi escolhida. Escolha em Configuração › Pastas, na aba ao lado."
                    : "Nada encontrado em " + destino + ". Use \"Organizar agora\" para trazer o que o Game Bar já salvou.";
            }

            Show();
        }

        /// <summary>
        /// Reabre a pasta em que a pessoa estava. Quando ela sumiu do disco desde a ultima leitura,
        /// cai em "Tudo" em vez de ficar sem selecao nenhuma — grade vazia sem explicacao parece
        /// defeito.
        /// </summary>
        private void RestoreSelection(List<GalleryFolder> arvore)
        {
            var raiz = arvore.FirstOrDefault();
            if (raiz == null)
            {
                return;
            }

            GalleryFolder alvo = null;
            if (!selectedIsAll)
            {
                alvo = Walk(raiz).FirstOrDefault(f =>
                    !f.IsAll &&
                    f.IsLoose == selectedIsLoose &&
                    string.Equals(f.RelativePath ?? string.Empty, selectedPath ?? string.Empty,
                                  StringComparison.OrdinalIgnoreCase));
            }

            if (alvo == null)
            {
                alvo = raiz;
            }

            // Abrir os ancestrais e o que faz o no existir na tela: um TreeViewItem de pasta
            // fechada nem chega a ser criado, e selecionar o que nao existe nao faz nada.
            foreach (var ancestral in AncestorsOf(raiz, alvo))
            {
                ancestral.IsExpanded = true;
            }

            SetSelection(alvo);

            // A selecao do TreeView e do CONTAINER, nao do dado, e o container so nasce depois do
            // layout. Marcar agora acertaria o no raiz e erraria todos os outros.
            Dispatcher.BeginInvoke(new Action(() => SelectContainer(alvo)),
                                   System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static IEnumerable<GalleryFolder> Walk(GalleryFolder raiz)
        {
            yield return raiz;
            foreach (var filho in raiz.Children)
            {
                foreach (var neto in Walk(filho))
                {
                    yield return neto;
                }
            }
        }

        private static List<GalleryFolder> AncestorsOf(GalleryFolder raiz, GalleryFolder alvo)
        {
            var caminho = new List<GalleryFolder>();
            FillPath(raiz, alvo, caminho);
            return caminho;
        }

        private static bool FillPath(GalleryFolder atual, GalleryFolder alvo, List<GalleryFolder> caminho)
        {
            if (ReferenceEquals(atual, alvo))
            {
                return true;
            }

            foreach (var filho in atual.Children)
            {
                if (FillPath(filho, alvo, caminho))
                {
                    caminho.Insert(0, atual);
                    return true;
                }
            }

            return false;
        }

        private void SelectContainer(GalleryFolder alvo)
        {
            try
            {
                var container = ContainerOf(FolderTree, alvo);
                if (container != null)
                {
                    container.IsSelected = true;
                    container.BringIntoView();
                }
            }
            catch (Exception)
            {
                // Selecao e conforto: se o container ainda nao existe, a grade ja esta certa.
            }
        }

        private static TreeViewItem ContainerOf(ItemsControl pai, GalleryFolder alvo)
        {
            if (pai == null)
            {
                return null;
            }

            var direto = pai.ItemContainerGenerator.ContainerFromItem(alvo) as TreeViewItem;
            if (direto != null)
            {
                return direto;
            }

            foreach (var item in pai.Items)
            {
                var filho = pai.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (filho == null)
                {
                    continue;
                }

                filho.UpdateLayout();
                var achado = ContainerOf(filho, alvo);
                if (achado != null)
                {
                    return achado;
                }
            }

            return null;
        }

        private void SetSelection(GalleryFolder folder)
        {
            if (folder == null)
            {
                selectedIsAll = true;
                selectedIsLoose = false;
                selectedPath = null;
                return;
            }

            selectedIsAll = folder.IsAll;
            selectedIsLoose = folder.IsLoose;
            selectedPath = folder.RelativePath;
        }

        private GalleryFolder SelectedFolder()
        {
            return FolderTree.SelectedItem as GalleryFolder;
        }

        private void Show()
        {
            // Na grade corrida a pasta selecionada na arvore e IGNORADA de proposito: a arvore
            // continua com a ultima pasta marcada para quando a pessoa voltar, e usar essa marca
            // aqui faria a grade "corrida" mostrar so um jogo.
            IEnumerable<GalleryItem> itens = GroupByFolder
                ? GalleryTree.ItemsOf(todos, SelectedFolder(), SubfoldersBox.IsChecked == true)
                : todos;

            var filtro = FilterBox.SelectedIndex;
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

        /// <summary>
        /// Os icones dos jogos, tambem fora da thread de UI e pelo mesmo motivo das miniaturas:
        /// uma pasta organizada com duzentos jogos significa duzentas leituras de PNG, e fazer
        /// isso na montagem da arvore travaria o Playnite ao abrir o painel.
        ///
        /// So a pasta de PRIMEIRO nivel procura jogo. "Screenshots" e "2026-09" nao sao titulos, e
        /// procura-los na biblioteca so gastaria tempo para nao achar nada.
        /// </summary>
        private void LoadIcons(List<GalleryFolder> arvore)
        {
            if (iconWork != null)
            {
                try { iconWork.Cancel(); } catch (Exception) { }
                try { iconWork.Dispose(); } catch (Exception) { }
            }

            iconWork = new CancellationTokenSource();
            var token = iconWork.Token;

            var raiz = arvore.FirstOrDefault();
            if (raiz == null)
            {
                return;
            }

            var pastas = Walk(raiz)
                .Where(f => !string.IsNullOrEmpty(f.GameName))
                .ToList();

            if (pastas.Count == 0)
            {
                return;
            }

            Task.Run(() =>
            {
                foreach (var pasta in pastas)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    string caminho;
                    try
                    {
                        caminho = plugin.GameIconPath(pasta.GameName);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    var imagem = LoadIcon(caminho);
                    if (imagem == null || token.IsCancellationRequested)
                    {
                        continue;
                    }

                    var alvo = pasta;
                    Dispatcher.BeginInvoke(new Action(() => alvo.Icon = imagem));
                }
            }, token);
        }

        /// <summary>
        /// Le o icone pequeno e CONGELA, que e o que permite ele ter nascido numa thread e ser
        /// desenhado na de UI. Icone quebrado, ou em formato que o WPF nao le, deixa a pasta sem
        /// icone e a arvore continua de pe.
        /// </summary>
        private static BitmapSource LoadIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = IconWidth;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- botoes da galeria

        private void OnFolderChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SetSelection(e.NewValue as GalleryFolder);
            Show();
        }

        private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (restoring)
            {
                return;
            }

            Show();
        }

        /// <summary>
        /// A troca entre a arvore e a grade corrida. Nao rele o disco: os arquivos sao os mesmos,
        /// e so muda o recorte mostrado — reler daria meio segundo de espera para nada.
        /// </summary>
        private void OnModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (restoring)
            {
                return;
            }

            ApplyMode();
            PersistPreferences();
            Show();
        }

        private void OnSubfoldersChanged(object sender, RoutedEventArgs e)
        {
            if (restoring)
            {
                return;
            }

            PersistPreferences();
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

        /// <summary>
        /// Um print agora, sem esperar o intervalo. Existe para conferir o caminho inteiro —
        /// atalho, gatilho, organizacao — em segundos, em vez de mexer numa configuracao e so
        /// descobrir quinze minutos depois se ela valeu.
        ///
        /// A grade NAO e recarregada aqui: o Game Bar grava o arquivo depois de o botao voltar, e
        /// recarregar agora mostraria a pasta sem a captura que a pessoa acabou de pedir — que e
        /// pior do que nao recarregar, porque parece que o botao falhou.
        /// </summary>
        private void OnCaptureNow(object sender, RoutedEventArgs e)
        {
            plugin.CaptureNow();
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            var pasta = SelectedFolder();
            var destino = plugin.Settings.DestinationFolder;

            if (pasta != null && !pasta.IsAll && !pasta.IsLoose &&
                !string.IsNullOrWhiteSpace(destino) && !string.IsNullOrWhiteSpace(pasta.RelativePath))
            {
                plugin.OpenFolder(System.IO.Path.Combine(destino, pasta.RelativePath));
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

        // ---------------------------------------------------------------- aba de configuracao

        /// <summary>
        /// A tela de configuracao so e construida quando a aba e aberta pela primeira vez. Monta-la
        /// junto com o painel pagaria a leitura do Game Bar, da Steam e do atalho toda vez que
        /// alguem abrisse a galeria so para ver um print.
        /// </summary>
        private void OnTabChanged(object sender, SelectionChangedEventArgs e)
        {
            // TabControl aninhado: a troca de aba LA DENTRO da configuracao tambem sobe ate aqui,
            // e trata-la como troca de aba daqui reabriria a edicao a cada clique.
            if (!ReferenceEquals(e.OriginalSource, sender))
            {
                return;
            }

            var abas = sender as TabControl;
            if (abas == null)
            {
                return;
            }

            if (abas.SelectedIndex != 1)
            {
                // Voltou para a galeria: a pasta de destino pode ter mudado na aba ao lado.
                if (settings != null)
                {
                    Load();
                }

                return;
            }

            if (settings == null)
            {
                settings = new SettingsView(plugin);
                settings.Embed();
                SettingsHost.Content = settings;
            }

            if (!editingSettings)
            {
                plugin.SettingsModel.BeginEdit();
                editingSettings = true;
            }

            SettingsStatus.Text = string.Empty;
        }

        private void OnSaveSettings(object sender, RoutedEventArgs e)
        {
            List<string> erros;
            if (!plugin.SettingsModel.VerifySettings(out erros))
            {
                // A mesma checagem que a janela do Playnite faria. Gravar configuracao invalida
                // daqui deixaria a extensao num estado que a janela nunca aceitaria.
                SettingsStatus.Text = string.Join("  ·  ", erros);
                return;
            }

            plugin.SettingsModel.EndEdit();

            // Recomeca a edicao para que "Desfazer" passe a valer a partir do que acabou de ser
            // gravado, e nao do estado de quando a aba foi aberta.
            plugin.SettingsModel.BeginEdit();
            SettingsStatus.Text = "Configuração salva.";
        }

        private void OnCancelSettings(object sender, RoutedEventArgs e)
        {
            plugin.SettingsModel.CancelEdit();

            // CancelEdit troca o objeto de configuracao inteiro pelo clone de quando a edicao
            // comecou — e o clone nao sabe da visualizacao trocada na aba ao lado desde entao.
            // Sem reaplicar aqui, desfazer a edicao de um campo de pasta desfaria junto a escolha
            // entre a arvore e a grade corrida, que a pessoa nem lembra de ter feito.
            plugin.SettingsModel.BeginEdit();
            PersistPreferences();

            SettingsStatus.Text = "Alterações desfeitas.";
        }
    }
}
