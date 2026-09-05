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
            ConquistaDaSteam();
            TeclaDeCaptura();
            RegrasPorJogo();
            ConquistaDoRetroAchievements();

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

        /// <summary>
        /// A leitura do progresso local da Steam. O arquivo e montado aqui em bytes, no formato
        /// KeyValues binario da Valve, porque depender de uma instalacao real da Steam faria o
        /// teste passar so na maquina de quem tem o jogo.
        /// </summary>
        private static void ConquistaDaSteam()
        {
            // O appId so vale vindo da biblioteca Steam do Playnite.
            string appId;
            IsTrue(GameCaptureOrganizer.Achievements.SteamStats.TryGetAppId(
                       GameCaptureOrganizer.Achievements.SteamStats.SteamLibraryPluginId, "1245620", out appId),
                   "jogo da Steam devolve appId");
            Eq("1245620", appId);

            IsTrue(!GameCaptureOrganizer.Achievements.SteamStats.TryGetAppId(
                       Guid.NewGuid(), "1245620", out appId),
                   "jogo de outra biblioteca não vira appId");
            IsTrue(!GameCaptureOrganizer.Achievements.SteamStats.TryGetAppId(
                       GameCaptureOrganizer.Achievements.SteamStats.SteamLibraryPluginId, "pasta\\jogo.exe", out appId),
                   "GameId que não é número não vira appId");

            Eq("UserGameStats_*_1245620.bin", GameCaptureOrganizer.Achievements.SteamStats.StatsFilter("1245620"));

            var pasta = Path.Combine(Path.GetTempPath(), "gco-steam-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pasta);
            try
            {
                // Grupo 0 com os bits 0 e 2 ligados: duas conquistas destravadas.
                var caminho = Path.Combine(pasta, "UserGameStats_123_1245620.bin");
                File.WriteAllBytes(caminho, ArquivoDeProgresso(0x05));

                var bits = GameCaptureOrganizer.Achievements.SteamStats.ReadUnlockedBits(caminho);
                IsTrue(bits != null, "arquivo íntegro é lido");
                Eq("2", bits.Count.ToString());
                IsTrue(bits.Contains("0:0") && bits.Contains("0:2"), "os bits ligados são os certos");

                // Mais um bit: e isso que dispara a captura.
                var depois = new HashSet<string>(bits) { "0:5" };
                Eq("1", GameCaptureOrganizer.Achievements.SteamStats.CountNew(bits, depois).ToString());

                // Bit que SUMIU não conta como conquista nova nem vira número negativo: o disparo
                // sai de evidência de conquista, nunca de "o arquivo mudou".
                Eq("0", GameCaptureOrganizer.Achievements.SteamStats.CountNew(depois, bits).ToString());

                // Arquivo cortado no meio (a Steam ainda escrevendo) devolve NULO, não vazio. Vazio
                // faria a leitura seguinte parecer que todas as conquistas foram destravadas de uma
                // vez, e o jogo inteiro viraria uma rajada de capturas.
                var inteiro = ArquivoDeProgresso(0x05);
                var cortado = Path.Combine(pasta, "UserGameStats_123_999.bin");
                File.WriteAllBytes(cortado, inteiro.Take(inteiro.Length / 2).ToArray());
                IsTrue(GameCaptureOrganizer.Achievements.SteamStats.ReadUnlockedBits(cortado) == null,
                       "arquivo cortado no meio devolve nulo, não conjunto vazio");

                // Linha de base ausente também não dispara nada.
                Eq("0", GameCaptureOrganizer.Achievements.SteamStats.CountNew(null, depois).ToString());
            }
            finally
            {
                try { Directory.Delete(pasta, true); } catch { }
            }
        }

        /// <summary>
        /// A traducao do nome da tecla para o codigo do Windows. Nome invalido tem que devolver
        /// ZERO, e nao uma tecla qualquer: registrar a combinacao errada tiraria de outro programa
        /// um atalho que a pessoa nunca pediu.
        /// </summary>
        private static void TeclaDeCaptura()
        {
            IsTrue(GameCaptureOrganizer.Input.GlobalHotkey.VirtualKey("F12") == 0x7B, "F12 é 0x7B");
            IsTrue(GameCaptureOrganizer.Input.GlobalHotkey.VirtualKey("f12") == 0x7B, "o nome não diferencia maiúscula");
            IsTrue(GameCaptureOrganizer.Input.GlobalHotkey.VirtualKey("PrintScreen") == 0x2C, "PrintScreen é 0x2C");
            IsTrue(GameCaptureOrganizer.Input.GlobalHotkey.VirtualKey("banana") == 0, "nome inválido devolve zero");
            IsTrue(GameCaptureOrganizer.Input.GlobalHotkey.VirtualKey("") == 0, "nome vazio devolve zero");
            IsTrue(GameCaptureOrganizer.Input.GlobalHotkey.VirtualKey(null) == 0, "nome nulo devolve zero");

            Eq("Ctrl+Shift+F12", GameCaptureOrganizer.Input.GlobalHotkey.Describe(true, false, true, "F12"));
            Eq("F9", GameCaptureOrganizer.Input.GlobalHotkey.Describe(false, false, false, "F9"));
        }

        /// <summary>
        /// A mistura padrao × excecao do jogo. O que a regra nao diz TEM que continuar vindo do
        /// padrao: guardar o valor de hoje onde a pessoa nao escolheu nada congelaria o padrao
        /// daquele dia, e mudar o global depois nao alcancaria o jogo.
        /// </summary>
        private static void RegrasPorJogo()
        {
            var global = new OrganizerSettings
            {
                AutoCaptureEnabled = true,
                ScreenshotIntervalMinutes = 15,
                ClipIntervalMinutes = 0,
                AchievementCaptureEnabled = true
            };

            // Sem regra: tudo do padrao.
            var padrao = GameCaptureOrganizer.AutoCapture.EffectiveCapture.Resolve(global, null);
            IsTrue(padrao.Enabled, "sem regra, a captura segue o padrão");
            Eq("15", padrao.ScreenshotMinutes.ToString());
            Eq("0", padrao.ClipMinutes.ToString());
            IsTrue(padrao.Achievements, "sem regra, a conquista segue o padrão");

            // Regra que so muda o print: o resto continua herdado.
            var soPrint = new GameCaptureOrganizer.AutoCapture.GameRule { ScreenshotIntervalMinutes = 3 };
            var comPrint = GameCaptureOrganizer.AutoCapture.EffectiveCapture.Resolve(global, soPrint);
            Eq("3", comPrint.ScreenshotMinutes.ToString());
            IsTrue(comPrint.Achievements, "a regra do print não mexe na conquista");

            // Jogo desligado: nem print, nem conquista.
            var desligado = new GameCaptureOrganizer.AutoCapture.GameRule { Enabled = false };
            var semNada = GameCaptureOrganizer.AutoCapture.EffectiveCapture.Resolve(global, desligado);
            IsTrue(!semNada.Enabled, "jogo marcado como sem captura fica sem captura");
            IsTrue(!semNada.Achievements, "jogo sem captura também não captura por conquista");

            // A chave mestra desligada vence QUALQUER regra: "desliguei e continuou capturando"
            // faria a pessoa procurar o defeito no lugar errado.
            var globalDesligado = new OrganizerSettings { AutoCaptureEnabled = false, ScreenshotIntervalMinutes = 15 };
            var ligadoNoJogo = new GameCaptureOrganizer.AutoCapture.GameRule { Enabled = true };
            IsTrue(!GameCaptureOrganizer.AutoCapture.EffectiveCapture.Resolve(globalDesligado, ligadoNoJogo).Enabled,
                   "regra do jogo não liga a captura com a chave mestra desligada");

            // Regra que fica vazia SAI do disco, senão a lista da tela enche de jogo que segue o padrão.
            var arquivo = Path.Combine(Path.GetTempPath(), "gco-regras-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var tabela = new GameCaptureOrganizer.AutoCapture.GameRules(arquivo);
                tabela.Load();
                tabela.Set("jogo-1", new GameCaptureOrganizer.AutoCapture.GameRule { Enabled = false });
                Eq("1", tabela.Count.ToString());

                tabela.Set("jogo-1", new GameCaptureOrganizer.AutoCapture.GameRule());
                Eq("0", tabela.Count.ToString());

                // E sobrevive ao disco.
                tabela.Set("jogo-2", new GameCaptureOrganizer.AutoCapture.GameRule { ClipIntervalMinutes = 30 });
                var relida = new GameCaptureOrganizer.AutoCapture.GameRules(arquivo);
                relida.Load();
                Eq("30", relida.Get("jogo-2").ClipIntervalMinutes.ToString());
                IsTrue(relida.Get("jogo-3").IsEmpty, "jogo sem regra devolve regra vazia, nunca nulo");
            }
            finally
            {
                try { File.Delete(arquivo); } catch { }
            }

            // O texto do intervalo tem TRÊS estados: número, vazio (volta ao padrão) e inválido.
            int? minutos;
            IsTrue(GameCaptureOrganizer.AutoCapture.GameRules.TryParseInterval("5", out minutos) && minutos == 5,
                   "número vira intervalo");
            IsTrue(GameCaptureOrganizer.AutoCapture.GameRules.TryParseInterval("  ", out minutos) && minutos == null,
                   "vazio volta ao padrão");
            IsTrue(!GameCaptureOrganizer.AutoCapture.GameRules.TryParseInterval("dez", out minutos),
                   "texto que não é número é recusado");
            IsTrue(!GameCaptureOrganizer.AutoCapture.GameRules.TryParseInterval("-1", out minutos),
                   "negativo é recusado");
            IsTrue(GameCaptureOrganizer.AutoCapture.GameRules.TryParseInterval("0", out minutos) && minutos == 0,
                   "zero é válido e significa desligar");
        }

        /// <summary>
        /// A leitura da resposta do RetroAchievements. Sem rede: o que se exercita e a
        /// interpretacao do JSON, que e onde mora o erro que faria a extensao capturar errado.
        /// </summary>
        private static void ConquistaDoRetroAchievements()
        {
            var resposta = "[{\"AchievementID\":\"12345\",\"Title\":\"Primeira fase\"}," +
                           "{\"AchievementID\":\"67890\",\"Title\":\"Sem levar dano\"}]";

            var lido = GameCaptureOrganizer.Achievements.RetroAchievements.Parse(resposta);
            IsTrue(lido.Ok, "lista de conquistas é lida");
            Eq("2", lido.Ids.Count.ToString());
            IsTrue(lido.Ids.Contains("12345"), "o id da conquista é o da resposta");

            // Lista vazia e resposta LEGITIMA: ninguem destravou nada na janela.
            var vazia = GameCaptureOrganizer.Achievements.RetroAchievements.Parse("[]");
            IsTrue(vazia.Ok, "lista vazia é resposta válida");
            Eq("0", vazia.Ids.Count.ToString());

            // Credencial errada devolve um OBJETO com mensagem, e nao uma lista. Isso e FALHA, e
            // nao "nenhuma conquista": tratado como vazio, a leitura seguinte veria tudo como novo
            // e o jogo inteiro viraria uma rajada de capturas.
            var erro = GameCaptureOrganizer.Achievements.RetroAchievements.Parse("{\"Error\":\"Invalid API Key\"}");
            IsTrue(!erro.Ok, "objeto de erro não é lista vazia");
            Eq("Invalid API Key", erro.Error);

            IsTrue(!GameCaptureOrganizer.Achievements.RetroAchievements.Parse("").Ok, "resposta vazia é falha");
            IsTrue(!GameCaptureOrganizer.Achievements.RetroAchievements.Parse("<html>502</html>").Ok,
                   "página de erro do servidor é falha, não conquista");

            // Sem credencial, nem tenta a rede.
            var semCredencial = GameCaptureOrganizer.Achievements.RetroAchievements.Read("", "", 10);
            IsTrue(!semCredencial.Ok, "sem usuário e chave a consulta nem sai");

            // Só o que ENTROU conta, igual à Steam.
            var antes = new HashSet<string> { "1", "2" };
            var depois = new HashSet<string> { "1", "2", "3" };
            Eq("1", GameCaptureOrganizer.Achievements.RetroAchievements.CountNew(antes, depois).ToString());
            Eq("0", GameCaptureOrganizer.Achievements.RetroAchievements.CountNew(depois, antes).ToString());
            Eq("0", GameCaptureOrganizer.Achievements.RetroAchievements.CountNew(null, depois).ToString());
        }

        /// <summary>Um UserGameStats_*.bin minimo: raiz > cache > grupo 0 > data (int32).</summary>
        private static byte[] ArquivoDeProgresso(int mascara)
        {
            using (var memoria = new MemoryStream())
            {
                Bloco(memoria, "UserGameStats");
                Bloco(memoria, "cache");
                Bloco(memoria, "0");
                Inteiro(memoria, "data", mascara);
                memoria.WriteByte(0x08); // fecha o grupo
                memoria.WriteByte(0x08); // fecha o cache
                memoria.WriteByte(0x08); // fecha a raiz
                return memoria.ToArray();
            }
        }

        private static void Bloco(Stream destino, string nome)
        {
            destino.WriteByte(0x00);
            Texto(destino, nome);
        }

        private static void Inteiro(Stream destino, string nome, int valor)
        {
            destino.WriteByte(0x02);
            Texto(destino, nome);
            var bytes = BitConverter.GetBytes(valor);
            destino.Write(bytes, 0, bytes.Length);
        }

        private static void Texto(Stream destino, string valor)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(valor);
            destino.Write(bytes, 0, bytes.Length);
            destino.WriteByte(0x00);
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
