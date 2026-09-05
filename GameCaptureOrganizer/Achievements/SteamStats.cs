using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace GameCaptureOrganizer.Achievements
{
    /// <summary>
    /// O arquivo de progresso local da Steam, lido para uma pergunta so: **destravou conquista
    /// agora?**
    ///
    /// A extensao de referencia (PlayniteAchievements) le tambem o
    /// <c>UserGameStatsSchema_&lt;appId&gt;.bin</c> para traduzir cada bit no NOME da conquista.
    /// Aqui isso nao entra: para disparar uma captura basta saber que o conjunto de bits ligados
    /// cresceu. Metade do codigo, e uma dependencia a menos num arquivo que pode nem existir.
    ///
    /// Nada disso usa API, chave nem internet: e leitura de arquivo local, entao funciona offline
    /// e responde no instante do unlock.
    /// </summary>
    public static class SteamStats
    {
        /// <summary>O plugin de biblioteca Steam do Playnite. E o que diz que o GameId e um appId.</summary>
        public static readonly Guid SteamLibraryPluginId = Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        /// <summary>
        /// Onde a Steam esta instalada. Em ordem: o que a pessoa escreveu na configuracao, a chave
        /// do usuario, e a da maquina.
        ///
        /// Sao tres tentativas porque uma so nao cobre: a chave do usuario
        /// (<c>HKCU\Software\Valve\Steam\SteamPath</c>) e a mais confiavel, mas ela simplesmente
        /// NAO EXISTE em maquina onde a Steam nunca rodou naquele perfil — medido em 05/09/2026
        /// no PC do dono, onde nao havia nem chave nem pasta. O campo manual e a saida honesta
        /// para instalacao portatil ou perfil diferente, em vez de a extensao dizer "nao achei" e
        /// nao dar caminho nenhum a quem sabe onde esta.
        /// </summary>
        public static string ResolveSteamPath(string overridePath = null)
        {
            var manual = Clean(overridePath);
            if (manual != null)
            {
                return manual;
            }

            var doUsuario = FromRegistry(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            if (doUsuario != null)
            {
                return doUsuario;
            }

            return FromRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                   ?? FromRegistry(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        }

        private static string FromRegistry(RegistryKey root, string path, string name)
        {
            try
            {
                using (var key = root.OpenSubKey(path))
                {
                    return key == null ? null : Clean(key.GetValue(name) as string);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Clean(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var limpo = path.Trim().Trim('"');
                return Directory.Exists(limpo) ? Path.GetFullPath(limpo) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string StatsFolder(string steamPath)
        {
            return string.IsNullOrWhiteSpace(steamPath)
                ? null
                : Path.Combine(steamPath, "appcache", "stats");
        }

        /// <summary>
        /// O filtro do arquivo deste jogo, com a conta em curinga.
        ///
        /// O curinga e deliberado: a alternativa seria descobrir o id da conta
        /// (<c>ActiveProcess\ActiveUser</c>), que vale zero com a Steam fechada e muda quando a
        /// pessoa troca de perfil. Casar por appId acerta em qualquer conta, inclusive quando ha
        /// duas no mesmo PC — cada uma tem o proprio arquivo, e cada arquivo tem a propria linha
        /// de base.
        /// </summary>
        public static string StatsFilter(string appId)
        {
            return "UserGameStats_*_" + appId + ".bin";
        }

        /// <summary>O appId sai do GameId do Playnite, e so vale se veio da biblioteca Steam.</summary>
        public static bool TryGetAppId(Guid libraryPluginId, string gameId, out string appId)
        {
            appId = null;
            if (libraryPluginId != SteamLibraryPluginId || string.IsNullOrWhiteSpace(gameId))
            {
                return false;
            }

            var limpo = gameId.Trim();
            uint numero;
            if (!uint.TryParse(limpo, NumberStyles.Integer, CultureInfo.InvariantCulture, out numero) || numero == 0)
            {
                return false;
            }

            appId = numero.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// Os bits ligados no arquivo, como "grupo:bit". Cada bit ligado e uma conquista destravada.
        ///
        /// Devolve nulo — e nao um conjunto vazio — quando a leitura falha. A diferenca decide
        /// tudo: conjunto vazio significa "nenhuma conquista", e comparar isso com a linha de base
        /// faria uma leitura no meio da escrita parecer que TODAS as conquistas foram perdidas, e
        /// a leitura seguinte, que todas foram destravadas de uma vez.
        /// </summary>
        public static HashSet<string> ReadUnlockedBits(string statsPath)
        {
            SteamKvNode raiz;
            if (!SteamKeyValues.TryRead(statsPath, out raiz))
            {
                return null;
            }

            var cache = FindDescendant(raiz, "cache");
            if (cache == null)
            {
                return null;
            }

            var bits = new HashSet<string>(StringComparer.Ordinal);
            foreach (var grupo in cache.Children)
            {
                int indiceGrupo;
                if (grupo == null || !int.TryParse(grupo.Name, NumberStyles.Integer,
                                                   CultureInfo.InvariantCulture, out indiceGrupo))
                {
                    continue;
                }

                var data = grupo.Child("data");
                if (data == null || !data.IntegerValue.HasValue)
                {
                    continue;
                }

                var mascara = unchecked((uint)data.IntegerValue.Value);
                for (var bit = 0; bit < 32; bit++)
                {
                    if ((mascara & (1U << bit)) != 0)
                    {
                        bits.Add(indiceGrupo.ToString(CultureInfo.InvariantCulture) + ":" +
                                 bit.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            return bits;
        }

        /// <summary>
        /// Quantas conquistas apareceram entre uma leitura e a seguinte.
        ///
        /// So conta o que ENTROU. Bit que sumiu (a pessoa apagou o progresso, ou a Steam
        /// reescreveu o arquivo pela metade) nao vira numero negativo nem dispara nada — e o
        /// disparo tem que sair de evidencia de conquista nova, nunca de o arquivo ter mudado.
        /// </summary>
        public static int CountNew(HashSet<string> baseline, HashSet<string> current)
        {
            if (baseline == null || current == null)
            {
                return 0;
            }

            return current.Count(bit => !baseline.Contains(bit));
        }

        private static SteamKvNode FindDescendant(SteamKvNode node, string name)
        {
            if (node == null)
            {
                return null;
            }

            var pilha = new Stack<SteamKvNode>();
            pilha.Push(node);
            while (pilha.Count > 0)
            {
                var atual = pilha.Pop();
                if (atual == null)
                {
                    continue;
                }

                if (string.Equals(atual.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return atual;
                }

                for (var i = atual.Children.Count - 1; i >= 0; i--)
                {
                    pilha.Push(atual.Children[i]);
                }
            }

            return null;
        }
    }
}
