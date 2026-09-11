using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace GameCaptureOrganizer.Gallery
{
    /// <summary>
    /// Uma pasta da arvore do painel. Ela existe porque a pasta organizada tem mais de um nivel: o
    /// padrao de fabrica e {Jogo}\{Tipo}, e um padrao com data chega a tres. O agrupamento antigo
    /// so enxergava o PRIMEIRO nivel, entao tudo que o padrao separou depois dele nao tinha como
    /// ser aberto — a pessoa via o jogo inteiro de uma vez ou nao via nada.
    ///
    /// Os numeros sao ACUMULADOS (a pasta mais os descendentes). Contar so o que esta imediatamente
    /// dentro faria "Elden Ring" aparecer como vazia no padrao de fabrica, porque ali dentro so ha
    /// as pastas Screenshots e Videos.
    /// </summary>
    public class GalleryFolder : INotifyPropertyChanged
    {
        private bool expanded;
        private object icon;

        public GalleryFolder()
        {
            Children = new List<GalleryFolder>();
        }

        /// <summary>O nome deste nivel, que e o que aparece na arvore.</summary>
        public string Name { get; set; }

        /// <summary>
        /// O caminho relativo ao destino ate esta pasta ("Elden Ring\Screenshots"). Vazio no no
        /// "Tudo" e no no da raiz solta, que se distinguem por <see cref="IsAll"/> e
        /// <see cref="IsLoose"/> — comparar por caminho vazio confundiria os dois.
        /// </summary>
        public string RelativePath { get; set; }

        /// <summary>O no de cima, que mostra a pasta organizada inteira.</summary>
        public bool IsAll { get; set; }

        /// <summary>O no do que ficou solto na raiz do destino, sem pasta de jogo.</summary>
        public bool IsLoose { get; set; }

        /// <summary>Profundidade a partir da raiz: 0 e a pasta do jogo, com o padrao de fabrica.</summary>
        public int Depth { get; set; }

        public List<GalleryFolder> Children { get; private set; }

        public int Screenshots { get; set; }
        public int Videos { get; set; }
        public DateTime LastCapture { get; set; }

        public int Total
        {
            get { return Screenshots + Videos; }
        }

        /// <summary>
        /// O icone do jogo, preenchido depois pela tela (o Playnite nao e alcancavel daqui). Fica
        /// como <see cref="object"/> para esta camada continuar sem WPF e seguir testavel fora do app.
        /// </summary>
        public object Icon
        {
            get { return icon; }
            set
            {
                icon = value;
                Raise("Icon");
                Raise("HasIcon");
            }
        }

        public bool HasIcon
        {
            get { return icon != null; }
        }

        /// <summary>Nome do jogo a procurar na biblioteca: so a pasta de primeiro nivel tem um.</summary>
        public string GameName
        {
            get { return Depth == 0 && !IsAll && !IsLoose ? Name : null; }
        }

        /// <summary>A arvore abre no primeiro nivel e para ai: expandir tudo esconde o que importa.</summary>
        public bool IsExpanded
        {
            get { return expanded; }
            set
            {
                expanded = value;
                Raise("IsExpanded");
            }
        }

        /// <summary>O que aparece embaixo do nome.</summary>
        public string Summary
        {
            get
            {
                var partes = new List<string>();
                if (Screenshots > 0)
                {
                    partes.Add(Screenshots == 1 ? "1 print" : Screenshots + " prints");
                }

                if (Videos > 0)
                {
                    partes.Add(Videos == 1 ? "1 vídeo" : Videos + " vídeos");
                }

                return partes.Count == 0 ? "vazia" : string.Join(" · ", partes);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    /// <summary>
    /// Monta a arvore de pastas a partir das capturas ja lidas do disco.
    ///
    /// A arvore sai do que EXISTE no disco, e nao do padrao configurado: o padrao pode ter mudado
    /// ontem, e as capturas de antes continuam onde o padrao antigo as colocou. Derivar a arvore da
    /// configuracao faria o painel esconder exatamente as capturas mais antigas.
    /// </summary>
    public static class GalleryTree
    {
        public const string AllLabel = "Tudo";

        public static List<GalleryFolder> Build(IEnumerable<GalleryItem> items)
        {
            var lista = items == null ? new List<GalleryItem>() : items.ToList();

            var todos = new GalleryFolder
            {
                Name = AllLabel,
                RelativePath = string.Empty,
                IsAll = true,
                Depth = -1,
                IsExpanded = true
            };

            // Indice por caminho relativo para nao varrer a arvore a cada arquivo: uma pasta com
            // quatrocentas capturas faria isso quatrocentas vezes.
            var porCaminho = new Dictionary<string, GalleryFolder>(StringComparer.OrdinalIgnoreCase);
            var raizes = new List<GalleryFolder>();
            GalleryFolder soltos = null;

            foreach (var item in lista)
            {
                var relativo = item.RelativeFolder == null ? string.Empty : item.RelativeFolder.Trim();

                if (relativo.Length == 0)
                {
                    if (soltos == null)
                    {
                        soltos = new GalleryFolder
                        {
                            Name = GalleryScanner.RootGroup,
                            RelativePath = string.Empty,
                            IsLoose = true,
                            Depth = 0
                        };
                        raizes.Add(soltos);
                    }

                    Count(soltos, item);
                    continue;
                }

                var partes = relativo.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                var caminho = string.Empty;
                GalleryFolder pai = null;

                for (var nivel = 0; nivel < partes.Length; nivel++)
                {
                    caminho = nivel == 0 ? partes[0] : caminho + "\\" + partes[nivel];

                    GalleryFolder no;
                    if (!porCaminho.TryGetValue(caminho, out no))
                    {
                        no = new GalleryFolder
                        {
                            Name = partes[nivel],
                            RelativePath = caminho,
                            Depth = nivel
                        };

                        porCaminho[caminho] = no;
                        if (pai == null)
                        {
                            raizes.Add(no);
                        }
                        else
                        {
                            pai.Children.Add(no);
                        }
                    }

                    // Todo nivel do caminho conta o arquivo: e isso que faz o total da pasta do
                    // jogo bater com a soma do que ha dentro dela.
                    Count(no, item);
                    pai = no;
                }
            }

            foreach (var raiz in raizes)
            {
                Count(todos, raiz);
                Sort(raiz);
            }

            var ordenadas = raizes
                .OrderByDescending(f => f.LastCapture)
                .ToList();

            todos.Children.AddRange(ordenadas);
            return new List<GalleryFolder> { todos };
        }

        /// <summary>
        /// As capturas que pertencem a pasta escolhida. Com <paramref name="includeSubfolders"/>
        /// a pasta do jogo mostra tudo que ha embaixo dela — que e como o painel sempre se
        /// comportou, e por isso e o padrao; sem ele, mostra so o que esta imediatamente dentro,
        /// que e o "ver por pasta" de verdade.
        /// </summary>
        public static List<GalleryItem> ItemsOf(IEnumerable<GalleryItem> items, GalleryFolder folder,
                                                bool includeSubfolders)
        {
            var lista = items == null ? new List<GalleryItem>() : items.ToList();
            if (folder == null || folder.IsAll)
            {
                return lista;
            }

            if (folder.IsLoose)
            {
                return lista
                    .Where(i => string.IsNullOrEmpty(i.RelativeFolder))
                    .ToList();
            }

            var alvo = folder.RelativePath ?? string.Empty;
            var prefixo = alvo + "\\";

            return lista
                .Where(i =>
                {
                    var relativo = i.RelativeFolder ?? string.Empty;
                    if (string.Equals(relativo, alvo, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    return includeSubfolders &&
                           relativo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
        }

        private static void Count(GalleryFolder folder, GalleryItem item)
        {
            if (item.Kind == CaptureKind.Video)
            {
                folder.Videos++;
            }
            else
            {
                folder.Screenshots++;
            }

            if (item.WhenLocal > folder.LastCapture)
            {
                folder.LastCapture = item.WhenLocal;
            }
        }

        private static void Count(GalleryFolder folder, GalleryFolder child)
        {
            folder.Screenshots += child.Screenshots;
            folder.Videos += child.Videos;
            if (child.LastCapture > folder.LastCapture)
            {
                folder.LastCapture = child.LastCapture;
            }
        }

        /// <summary>Mais recente primeiro, em todos os niveis — a mesma regra da lista antiga.</summary>
        private static void Sort(GalleryFolder folder)
        {
            if (folder.Children.Count == 0)
            {
                return;
            }

            var ordenadas = folder.Children.OrderByDescending(f => f.LastCapture).ToList();
            folder.Children.Clear();
            folder.Children.AddRange(ordenadas);

            foreach (var filho in folder.Children)
            {
                Sort(filho);
            }
        }
    }
}
