using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameCaptureOrganizer.Gallery
{
    /// <summary>Uma captura ja organizada, do jeito que o painel precisa mostrar.</summary>
    public class GalleryItem
    {
        public string Path { get; set; }
        public string FileName { get; set; }

        /// <summary>A primeira pasta abaixo do destino. Com o padrao de fabrica, e o nome do jogo.</summary>
        public string Group { get; set; }

        public CaptureKind Kind { get; set; }
        public DateTime WhenLocal { get; set; }
        public long SizeBytes { get; set; }
    }

    /// <summary>Uma pasta do primeiro nivel, com a conta do que tem dentro.</summary>
    public class GalleryGroup
    {
        public string Name { get; set; }
        public int Screenshots { get; set; }
        public int Videos { get; set; }
        public DateTime LastCapture { get; set; }

        public int Total
        {
            get { return Screenshots + Videos; }
        }

        /// <summary>O que aparece embaixo do nome na lista da esquerda.</summary>
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
    }

    /// <summary>
    /// Le a pasta organizada e monta o que o painel lateral mostra.
    ///
    /// O agrupamento e pela PRIMEIRA PASTA abaixo do destino, e nao por um campo guardado em lugar
    /// nenhum: o padrao de pasta e configuravel, entao a unica verdade sobre onde cada captura
    /// mora e o proprio disco. Com o padrao de fabrica ({Jogo}\{Tipo}) o primeiro nivel e o nome
    /// do jogo, que e o caso de quase todo mundo; com um padrao que comece por data, o painel
    /// agrupa por data — que e exatamente o que a pessoa pediu ao escrever aquele padrao.
    ///
    /// Captura solta na raiz do destino entra num grupo proprio em vez de sumir: arquivo que
    /// existe e nao aparece no painel e pior do que arquivo em grupo estranho.
    /// </summary>
    public static class GalleryScanner
    {
        public const string RootGroup = "(solto na raiz)";

        /// <summary>
        /// Teto de arquivos lidos numa passada. Existe porque a pasta organizada cresce para
        /// sempre: com print a cada 5 minutos, mil arquivos e um mes de jogo. O painel mostra os
        /// mais RECENTES, que e o que alguem abre o painel para ver.
        /// </summary>
        public const int DefaultLimit = 400;

        public static List<GalleryItem> Scan(string destination, IEnumerable<string> imageExtensions,
                                             IEnumerable<string> videoExtensions, int limit = DefaultLimit)
        {
            var itens = new List<GalleryItem>();
            if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
            {
                return itens;
            }

            var raiz = new DirectoryInfo(destination);
            var raizPath = raiz.FullName.TrimEnd('\\');

            IEnumerable<FileInfo> arquivos;
            try
            {
                arquivos = raiz.EnumerateFiles("*", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                return itens;
            }

            foreach (var arquivo in arquivos)
            {
                CaptureKind tipo;
                try
                {
                    tipo = CaptureCore.ClassifyKind(arquivo.Extension, imageExtensions, videoExtensions);
                }
                catch (Exception)
                {
                    continue;
                }

                if (tipo == CaptureKind.Unknown)
                {
                    continue;
                }

                itens.Add(new GalleryItem
                {
                    Path = arquivo.FullName,
                    FileName = arquivo.Name,
                    Group = GroupOf(raizPath, arquivo.FullName),
                    Kind = tipo,
                    WhenLocal = arquivo.LastWriteTime,
                    SizeBytes = arquivo.Length
                });
            }

            return itens
                .OrderByDescending(i => i.WhenLocal)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        /// <summary>A primeira pasta abaixo do destino, ou o grupo da raiz quando nao ha nenhuma.</summary>
        public static string GroupOf(string destination, string filePath)
        {
            if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(filePath))
            {
                return RootGroup;
            }

            var raiz = destination.TrimEnd('\\', '/');
            if (filePath.Length <= raiz.Length + 1 ||
                !filePath.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
            {
                return RootGroup;
            }

            var relativo = filePath.Substring(raiz.Length + 1);
            var partes = relativo.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return partes.Length <= 1 ? RootGroup : partes[0];
        }

        /// <summary>
        /// Os grupos, do que tem captura mais recente para o mais antigo. Quem abre o painel quer
        /// ver o que jogou ontem, nao o que jogou em janeiro.
        /// </summary>
        public static List<GalleryGroup> Group(IEnumerable<GalleryItem> items)
        {
            if (items == null)
            {
                return new List<GalleryGroup>();
            }

            return items
                .GroupBy(i => i.Group, StringComparer.OrdinalIgnoreCase)
                .Select(g => new GalleryGroup
                {
                    Name = g.Key,
                    Screenshots = g.Count(i => i.Kind == CaptureKind.Screenshot),
                    Videos = g.Count(i => i.Kind == CaptureKind.Video),
                    LastCapture = g.Max(i => i.WhenLocal)
                })
                .OrderByDescending(g => g.LastCapture)
                .ToList();
        }
    }
}
