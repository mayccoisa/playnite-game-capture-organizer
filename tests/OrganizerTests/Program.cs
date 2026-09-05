using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCaptureOrganizer;

namespace OrganizerTests
{
    internal static class Program
    {
        private static int falhas;
        private static int total;

        private static int Main()
        {
            NomeDoArquivo();
            Sanitizacao();
            Padroes();
            Colisao();
            CasamentoPorSessao();
            Poda();
            PassadaCompleta();
            CapturaForaDeSessaoVaiParaOFallback();
            SemNomeConfiavelFicaNaOrigem();
            ArquivoDeOutroTipoEIgnorado();
            Apelidos();
            CasamentoPorPrefixo();
            ApelidoResolveOTituloDaJanela();
            MotivoDoGatilho();
            PainelDeCapturas();

            Console.WriteLine();
            Console.WriteLine(string.Format("{0} verificações, {1} falha(s).", total, falhas));
            return falhas == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- verificacoes

        private static void NomeDoArquivo()
        {
            // Os quatro formatos que o Game Bar produz na pratica.
            Eq("Elden Ring", CaptureCore.ExtractGameNameFromFileName("Elden Ring 2_15_2026 8_30_12 PM.png"));
            Eq("Elden Ring", CaptureCore.ExtractGameNameFromFileName("Elden Ring 15_02_2026 20_30_12.mp4"));
            Eq("Elden Ring", CaptureCore.ExtractGameNameFromFileName("Elden Ring 2026-02-15 20-30-12.mp4"));
            Eq("Elden Ring", CaptureCore.ExtractGameNameFromFileName("Elden Ring_20260215203012.png"));

            // Titulo sem carimbo volta inteiro: cortar por chute apagaria nome de verdade.
            Eq("Half-Life", CaptureCore.ExtractGameNameFromFileName("Half-Life.png"));
            Eq("PAC-MAN WORLD 2 Re-PAC", CaptureCore.ExtractGameNameFromFileName("PAC-MAN WORLD 2 Re-PAC.png"));

            // Numeral e sigla pontuada nao podem ser confundidos com carimbo.
            Eq("S.T.A.L.K.E.R. 2", CaptureCore.ExtractGameNameFromFileName("S.T.A.L.K.E.R. 2 2026-02-15 20-30-12.png"));
            Eq("Fable II", CaptureCore.ExtractGameNameFromFileName("Fable II 15_02_2026 20_30_12.png"));

            // Normalizacao serve para COMPARAR, e casa o apostrofo e o acento.
            Eq(CaptureCore.NormalizeName("Marvel's Spider-Man"), CaptureCore.NormalizeName("Marvels Spider Man"));
            Eq(CaptureCore.NormalizeName("Pokémon™"), CaptureCore.NormalizeName("Pokemon"));
        }

        private static void Sanitizacao()
        {
            // Apostrofo e & sao validos no Windows: manter preserva o titulo como ele e.
            Eq("Mario's Adventure", CaptureCore.SanitizePathSegment("Mario's Adventure"));
            Eq("Ratchet & Clank", CaptureCore.SanitizePathSegment("Ratchet & Clank"));
            Eq("Hello", CaptureCore.SanitizePathSegment("Hel:lo"));
            Eq("Sem nome", CaptureCore.SanitizePathSegment("   "));
            Eq("Sem nome", CaptureCore.SanitizePathSegment("///"));
            Eq("CON_", CaptureCore.SanitizePathSegment("CON"));
            // Ponto no fim: o Windows nao guarda pasta terminada assim.
            Eq("S.T.A.L.K.E.R", CaptureCore.SanitizePathSegment("S.T.A.L.K.E.R."));
            // Um caminho relativo nao pode escapar do destino.
            Eq("Elden Ring\\Screenshots", CaptureCore.SanitizeRelativePath("Elden Ring/Screenshots"));
            Eq("etc", CaptureCore.SanitizeRelativePath("..\\..\\etc"));
        }

        private static void Padroes()
        {
            var ctx = new CaptureContext
            {
                GameName = "Elden Ring",
                Platform = "PC (Windows)",
                Source = "Steam",
                Kind = CaptureKind.Video,
                Timestamp = new DateTime(2026, 2, 15, 20, 30, 12),
                OriginalName = "original"
            };

            Eq("Elden Ring\\Videos", CaptureCore.BuildRelativeFolder(CaptureCore.DefaultFolderPattern, ctx));
            Eq("Elden Ring_2026-02-15_20-30-12.mp4", CaptureCore.BuildFileName(CaptureCore.DefaultFilePattern, ctx, ".MP4"));
            Eq("Elden Ring\\2026-02", CaptureCore.BuildRelativeFolder(@"{Jogo}\{AnoMes}", ctx));
            Eq("Steam\\Elden Ring", CaptureCore.BuildRelativeFolder(@"{Fonte}\{Jogo}", ctx));

            // Marcador desconhecido FICA visivel, em vez de sumir: o erro de digitacao precisa
            // aparecer no nome, senao vira caminho errado em silencio.
            Eq("{Jogos}", CaptureCore.RenderPattern("{Jogos}", ctx));

            // Extensao classificada pelas listas configuradas.
            var img = CaptureCore.ParseExtensionList(".png, jpg");
            var vid = CaptureCore.ParseExtensionList("mp4;mkv");
            Eq(CaptureKind.Screenshot.ToString(), CaptureCore.ClassifyKind(".PNG", img, vid).ToString());
            Eq(CaptureKind.Video.ToString(), CaptureCore.ClassifyKind("mkv", img, vid).ToString());
            Eq(CaptureKind.Unknown.ToString(), CaptureCore.ClassifyKind(".txt", img, vid).ToString());
        }

        private static void Colisao()
        {
            var existentes = new HashSet<string>(new[] { @"C:\d\a.png", @"C:\d\a (2).png" }, StringComparer.OrdinalIgnoreCase);
            Eq(@"C:\d\a (3).png", CaptureCore.ResolveCollision(@"C:\d", "a.png", p => existentes.Contains(p)));
        }

        private static void CasamentoPorSessao()
        {
            var agora = new DateTime(2026, 2, 15, 20, 0, 0, DateTimeKind.Utc);
            var sessoes = new List<PlaySession>
            {
                new PlaySession { GameId = "1", GameName = "Hades", StartedUtc = agora.AddHours(-3), EndedUtc = agora.AddHours(-2) },
                new PlaySession { GameId = "2", GameName = "Elden Ring", StartedUtc = agora.AddMinutes(-40), EndedUtc = agora.AddMinutes(-5) }
            };

            var tol = TimeSpan.FromMinutes(10);
            Eq("Elden Ring", SessionIndex.Match(sessoes, agora.AddMinutes(-20), tol, agora).GameName);
            Eq("Hades", SessionIndex.Match(sessoes, agora.AddHours(-2.5), tol, agora).GameName);
            // Dentro da folga depois do fim: e a captura salva com o jogo ja fechando.
            Eq("Elden Ring", SessionIndex.Match(sessoes, agora.AddMinutes(-1), tol, agora).GameName);
            // Muito depois de tudo: ninguem cobre, e o servico cai no nome do arquivo.
            IsTrue(SessionIndex.Match(sessoes, agora.AddHours(5), tol, agora) == null, "captura fora de qualquer sessão não casa");

            // Sessao aberta cobre ate agora.
            var aberta = new List<PlaySession> { new PlaySession { GameId = "3", GameName = "Balatro", StartedUtc = agora.AddMinutes(-30) } };
            Eq("Balatro", SessionIndex.Match(aberta, agora.AddMinutes(-2), tol, agora).GameName);
        }

        private static void Poda()
        {
            var agora = new DateTime(2026, 2, 15, 20, 0, 0, DateTimeKind.Utc);
            var lista = new List<PlaySession>
            {
                new PlaySession { GameId = "velha", StartedUtc = agora.AddDays(-200), EndedUtc = agora.AddDays(-200) },
                new PlaySession { GameId = "nova", StartedUtc = agora.AddDays(-1), EndedUtc = agora.AddDays(-1) },
                new PlaySession { GameId = "aberta-antiga", StartedUtc = agora.AddDays(-300) }
            };

            var podada = SessionIndex.Prune(lista, agora);
            IsTrue(podada.All(s => s.GameId != "velha"), "sessão encerrada há 200 dias sai");
            IsTrue(podada.Any(s => s.GameId == "nova"), "sessão recente fica");
            // Sessao ABERTA nunca e podada: o jogo pode estar rodando ha horas.
            IsTrue(podada.Any(s => s.GameId == "aberta-antiga"), "sessão ainda aberta nunca é podada");
        }

        // ---------------------------------------------------------------- passada real em disco

        private static void PassadaCompleta()
        {
            using (var caixa = new Caixa())
            {
                var quando = DateTime.UtcNow.AddMinutes(-10);
                caixa.Criar("Titulo Da Janela 15_02_2026 20_30_12.png", quando);
                caixa.Criar("Titulo Da Janela 15_02_2026 20_31_00.mp4", quando);

                caixa.Sessoes.Begin("1", "Elden Ring", "PC (Windows)", "Steam", DateTime.UtcNow.AddMinutes(-30));
                caixa.Sessoes.End("1", DateTime.UtcNow.AddMinutes(-1));

                var r = caixa.Servico().Run(caixa.Settings);

                Eq("2", r.Organized.ToString());
                IsTrue(File.Exists(Path.Combine(caixa.Destino, "Elden Ring", "Screenshots",
                        "Elden Ring_" + quando.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss") + ".png")),
                    "o print foi para a pasta do jogo da SESSÃO, não do título da janela");
                IsTrue(Directory.Exists(Path.Combine(caixa.Destino, "Elden Ring", "Videos")), "o vídeo foi para a subpasta Videos");
                IsTrue(Directory.GetFiles(caixa.Origem).Length == 0, "mover esvazia a origem");
            }
        }

        private static void CapturaForaDeSessaoVaiParaOFallback()
        {
            using (var caixa = new Caixa())
            {
                caixa.Criar("Hollow Knight 15_02_2026 20_30_12.png", DateTime.UtcNow.AddDays(-9));
                var r = caixa.Servico().Run(caixa.Settings);

                Eq("1", r.Organized.ToString());
                IsTrue(Directory.Exists(Path.Combine(caixa.Destino, "Hollow Knight", "Screenshots")),
                    "sem sessão que cubra o horário, o nome sai do arquivo");
            }
        }

        private static void SemNomeConfiavelFicaNaOrigem()
        {
            using (var caixa = new Caixa())
            {
                caixa.Criar("Hollow Knight 15_02_2026 20_30_12.png", DateTime.UtcNow.AddDays(-9));
                caixa.Settings.FallbackToFileName = false;

                var r = caixa.Servico().Run(caixa.Settings);

                Eq("0", r.Organized.ToString());
                // Ficar na origem e a decisao certa: pasta "Sem nome" cheia de coisa perdida e pior.
                IsTrue(Directory.GetFiles(caixa.Origem).Length == 1, "sem nome confiável, o arquivo não é movido");
            }
        }

        private static void ArquivoDeOutroTipoEIgnorado()
        {
            using (var caixa = new Caixa())
            {
                caixa.Criar("anotacoes.txt", DateTime.UtcNow);
                var r = caixa.Servico().Run(caixa.Settings);

                Eq("0", r.Organized.ToString());
                IsTrue(Directory.GetFiles(caixa.Origem).Length == 1, "extensão desconhecida fica onde está");
            }
        }

        private static void Apelidos()
        {
            var mapa = CaptureCore.ParseAliases("Pal = Palworld\n# comentário\nSTALKER2=S.T.A.L.K.E.R. 2\nlinha inválida\n");

            Eq("Palworld", CaptureCore.ApplyAlias("Pal", mapa));
            // A chave e comparada normalizada: como o arquivo escreve nao importa.
            Eq("Palworld", CaptureCore.ApplyAlias("pal", mapa));
            Eq("S.T.A.L.K.E.R. 2", CaptureCore.ApplyAlias("stalker 2", mapa));
            IsTrue(CaptureCore.ApplyAlias("Hades", mapa) == null, "título sem apelido não é traduzido");
            IsTrue(!mapa.ContainsKey(CaptureCore.NormalizeName("linha inválida")), "linha sem '=' é ignorada");
            IsTrue(!mapa.ContainsKey(CaptureCore.NormalizeName("comentário")), "linha iniciada por # é comentário");
        }

        private static void CasamentoPorPrefixo()
        {
            var biblioteca = new[] { "palworld", "hades", "eldenring" };
            Eq("palworld", CaptureCore.PickUniquePrefixMatch("pal", biblioteca));
            Eq("eldenring", CaptureCore.PickUniquePrefixMatch("elden", biblioteca));

            // Dois candidatos: devolve NULO, nunca o primeiro. Escolher seria inventar, e a
            // captura ir para a pasta errada em silêncio é pior do que ficar com o nome cru.
            var ambigua = new[] { "palworld", "paladins" };
            IsTrue(CaptureCore.PickUniquePrefixMatch("pal", ambigua) == null, "prefixo ambíguo não escolhe nenhum");

            // Curto demais não tenta: "GT" casaria com meia biblioteca.
            IsTrue(CaptureCore.PickUniquePrefixMatch("gt", new[] { "gtavice", "gtsport" }) == null, "prefixo com menos de 3 letras não tenta");
            IsTrue(CaptureCore.PickUniquePrefixMatch("zzz", biblioteca) == null, "prefixo que não casa com nada devolve nulo");
        }

        /// <summary>
        /// O caso que originou tudo: a janela do Palworld se chama "Pal", entao a captura nascia
        /// numa pasta "Pal". Com o apelido, ela vai para "Palworld" mesmo sem sessao registrada.
        /// </summary>
        private static void ApelidoResolveOTituloDaJanela()
        {
            using (var caixa = new Caixa())
            {
                caixa.Criar("Pal 15_02_2026 20_30_12.png", DateTime.UtcNow.AddDays(-30));
                caixa.Settings.NameAliases = "Pal = Palworld";

                var r = caixa.Servico().Run(caixa.Settings);

                Eq("1", r.Organized.ToString());
                IsTrue(Directory.Exists(Path.Combine(caixa.Destino, "Palworld", "Screenshots")),
                    "o apelido levou a captura para a pasta do Palworld, e não para \"Pal\"");
            }
        }

        // ---------------------------------------------------------------- arranjo

        private class Caixa : IDisposable
        {
            public string Raiz { get; private set; }
            public string Origem { get; private set; }
            public string Destino { get; private set; }
            public OrganizerSettings Settings { get; private set; }
            public SessionIndex Sessoes { get; private set; }

            public Caixa()
            {
                Raiz = Path.Combine(Path.GetTempPath(), "gco-" + Guid.NewGuid().ToString("N"));
                Origem = Path.Combine(Raiz, "Captures");
                Destino = Path.Combine(Raiz, "Organizadas");
                Directory.CreateDirectory(Origem);

                Settings = OrganizerSettings.CreateDefault();
                Settings.SourceFolders = Origem;
                Settings.DestinationFolder = Destino;
                Settings.MatchByLibrary = false;
                Settings.WriteLog = false;

                Sessoes = new SessionIndex(Path.Combine(Raiz, "sessoes.json"));
            }

            public OrganizerService Servico()
            {
                return new OrganizerService(Sessoes, null, null, null);
            }

            public void Criar(string nome, DateTime criadoUtc)
            {
                var caminho = Path.Combine(Origem, nome);
                File.WriteAllText(caminho, "x");
                File.SetCreationTimeUtc(caminho, criadoUtc);
                File.SetLastWriteTimeUtc(caminho, criadoUtc);
            }

            public void Dispose()
            {
                try { Directory.Delete(Raiz, true); } catch { }
            }
        }

        /// <summary>
        /// O marcador {Motivo} e o casamento do arquivo com o gatilho que o pediu. Vence o gatilho
        /// mais PROXIMO, e nao o ultimo antes: o clipe dos ultimos segundos nasce carimbado antes
        /// do gatilho, porque o video comecou la atras.
        /// </summary>
        private static void MotivoDoGatilho()
        {
            var ctx = new CaptureContext
            {
                GameName = "Elden Ring",
                Kind = CaptureKind.Screenshot,
                Timestamp = new DateTime(2026, 2, 15, 20, 30, 12),
                Reason = "periodico"
            };

            Eq("Elden Ring_periodico.png", CaptureCore.BuildFileName("{Jogo}_{Motivo}", ctx, ".png"));

            // Captura tirada na mao pelo proprio Game Bar nao tem gatilho nosso: o marcador some,
            // e isso e o caso comum, nao erro.
            ctx.Reason = null;
            Eq("Elden Ring_.png", CaptureCore.BuildFileName("{Jogo}_{Motivo}", ctx, ".png"));

            var agora = new DateTime(2026, 2, 15, 23, 30, 0, DateTimeKind.Utc);
            var registros = new List<GameCaptureOrganizer.AutoCapture.CaptureTriggerRecord>
            {
                new GameCaptureOrganizer.AutoCapture.CaptureTriggerRecord { WhenUtc = agora.AddMinutes(-10), Reason = "periodico" },
                new GameCaptureOrganizer.AutoCapture.CaptureTriggerRecord { WhenUtc = agora.AddSeconds(30), Reason = "conquista" }
            };

            var tolerancia = TimeSpan.FromSeconds(90);
            Eq("conquista", GameCaptureOrganizer.AutoCapture.TriggerLog.Match(registros, agora, tolerancia));
            Eq("periodico", GameCaptureOrganizer.AutoCapture.TriggerLog.Match(registros, agora.AddMinutes(-10), tolerancia));

            // Fora da tolerancia nao casa com nada: melhor sem motivo do que com o motivo errado.
            var semMotivo = GameCaptureOrganizer.AutoCapture.TriggerLog.Match(registros, agora.AddHours(-3), tolerancia);
            Eq("(nulo)", semMotivo ?? "(nulo)");

            // A poda de 30 dias leva os dois embora quando a leitura acontece 40 dias depois.
            var podados = GameCaptureOrganizer.AutoCapture.TriggerLog.Prune(registros, agora.AddDays(40));
            Eq("0", podados.Count.ToString());
        }

        /// <summary>
        /// O painel agrupa pela PRIMEIRA pasta abaixo do destino, porque o padrao de pasta e
        /// configuravel e o disco e a unica verdade sobre onde a captura mora.
        /// </summary>
        private static void PainelDeCapturas()
        {
            const string destino = @"D:\Capturas Organizadas";

            Eq("Elden Ring", GameCaptureOrganizer.Gallery.GalleryScanner.GroupOf(
                destino, destino + @"\Elden Ring\Screenshots\a.png"));

            // Padrao que comeca por data agrupa por data — e o que a pessoa pediu ao escreve-lo.
            Eq("2026-02", GameCaptureOrganizer.Gallery.GalleryScanner.GroupOf(
                destino, destino + @"\2026-02\Elden Ring\a.png"));

            // Captura solta na raiz nao some do painel: ganha grupo proprio.
            Eq(GameCaptureOrganizer.Gallery.GalleryScanner.RootGroup,
               GameCaptureOrganizer.Gallery.GalleryScanner.GroupOf(destino, destino + @"\a.png"));

            // Arquivo de fora do destino nunca e atribuido a um grupo inventado.
            Eq(GameCaptureOrganizer.Gallery.GalleryScanner.RootGroup,
               GameCaptureOrganizer.Gallery.GalleryScanner.GroupOf(destino, @"D:\Outra\a.png"));

            var itens = new List<GameCaptureOrganizer.Gallery.GalleryItem>
            {
                new GameCaptureOrganizer.Gallery.GalleryItem { Group = "Elden Ring", Kind = CaptureKind.Screenshot, WhenLocal = new DateTime(2026, 2, 10) },
                new GameCaptureOrganizer.Gallery.GalleryItem { Group = "Elden Ring", Kind = CaptureKind.Video, WhenLocal = new DateTime(2026, 2, 12) },
                new GameCaptureOrganizer.Gallery.GalleryItem { Group = "Palworld", Kind = CaptureKind.Screenshot, WhenLocal = new DateTime(2026, 3, 1) }
            };

            var grupos = GameCaptureOrganizer.Gallery.GalleryScanner.Group(itens);

            // Quem jogou por ultimo aparece em cima: o painel e aberto para ver o que acabou de sair.
            Eq("Palworld", grupos[0].Name);
            Eq("Elden Ring", grupos[1].Name);
            Eq("1 print · 1 vídeo", grupos[1].Summary);
            Eq("2", grupos[1].Total.ToString());
        }

        private static void Eq(string esperado, string obtido)
        {
            total++;
            if (!string.Equals(esperado, obtido, StringComparison.Ordinal))
            {
                falhas++;
                Console.WriteLine(string.Format("FALHOU: esperava \"{0}\", veio \"{1}\"", esperado, obtido));
            }
        }

        private static void IsTrue(bool condicao, string descricao)
        {
            total++;
            if (!condicao)
            {
                falhas++;
                Console.WriteLine("FALHOU: " + descricao);
            }
        }
    }
}
