using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;

namespace GameCaptureOrganizer.Achievements
{
    /// <summary>O que uma consulta ao RetroAchievements devolveu.</summary>
    public class RetroReadResult
    {
        /// <summary>Nulo quando a consulta falhou. Vazio e uma resposta legitima: ninguem destravou nada.</summary>
        public HashSet<string> Ids { get; set; }

        public string Error { get; set; }

        public bool Ok
        {
            get { return Ids != null; }
        }
    }

    /// <summary>
    /// Conversa com o RetroAchievements — as conquistas dos jogos de emulador, que a Steam nao ve.
    ///
    /// Diferente da Steam, aqui NAO existe arquivo local para observar: o emulador conversa direto
    /// com o servidor deles, e a unica fonte e a API web. Por isso este caminho:
    /// - precisa de credencial, que vem do usuario e nunca e inventada;
    /// - custa rede, entao e consulta de tempos em tempos e nao aviso instantaneo;
    /// - so roda enquanto um jogo esta aberto pelo Playnite.
    ///
    /// A consulta e a de conquistas recentes DA CONTA, sem filtrar por jogo. E de proposito: o
    /// codigo do jogo no RetroAchievements nao existe na biblioteca do Playnite, e adivinha-lo
    /// pelo nome erraria em silencio. O preco e conhecido e esta escrito na tela: conquista que a
    /// mesma conta destravar em outro aparelho ao mesmo tempo tambem dispara captura aqui.
    /// </summary>
    public static class RetroAchievements
    {
        private const string Endpoint = "https://retroachievements.org/API/API_GetUserRecentAchievements.php";
        private const string UserAgent = "PlayniteGameCaptureOrganizer";

        /// <summary>As conquistas da conta nos ultimos <paramref name="minutes"/> minutos.</summary>
        public static RetroReadResult Read(string user, string apiKey, int minutes)
        {
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(apiKey))
            {
                return new RetroReadResult { Error = "usuário ou chave de API em branco" };
            }

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", UserAgent);

                    var url = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}?u={1}&y={2}&m={3}",
                        Endpoint,
                        Uri.EscapeDataString(user.Trim()),
                        Uri.EscapeDataString(apiKey.Trim()),
                        Math.Max(1, minutes));

                    var json = client.DownloadString(url);
                    return Parse(json);
                }
            }
            catch (Exception ex)
            {
                // A mensagem vai para o log e para a tela. A URL NAO vai junto em lugar nenhum:
                // ela carrega a chave de API no meio, e log e print sao coisas que se compartilham.
                return new RetroReadResult { Error = Describe(ex) };
            }
        }

        /// <summary>
        /// Os ids da resposta. Resposta que nao e uma lista significa erro do servidor (credencial
        /// errada devolve um objeto com mensagem, e nao uma lista vazia) — e isso e falha, nao
        /// "nenhuma conquista", pela mesma razao da leitura da Steam.
        /// </summary>
        public static RetroReadResult Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new RetroReadResult { Error = "resposta vazia" };
            }

            JToken raiz;
            try
            {
                raiz = JToken.Parse(json);
            }
            catch (Exception)
            {
                return new RetroReadResult { Error = "resposta que não é JSON" };
            }

            var lista = raiz as JArray;
            if (lista == null)
            {
                var mensagem = raiz["Error"] ?? raiz["error"] ?? raiz["Message"];
                return new RetroReadResult
                {
                    Error = mensagem != null
                        ? mensagem.ToString()
                        : "o servidor respondeu algo que não é a lista de conquistas (credencial errada?)"
                };
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in lista.OfType<JObject>())
            {
                var id = item["AchievementID"] ?? item["achievementID"] ?? item["ID"];
                if (id != null && !string.IsNullOrWhiteSpace(id.ToString()))
                {
                    ids.Add(id.ToString());
                }
            }

            return new RetroReadResult { Ids = ids };
        }

        /// <summary>Quantas apareceram entre uma consulta e a seguinte. So conta o que ENTROU.</summary>
        public static int CountNew(HashSet<string> baseline, HashSet<string> current)
        {
            if (baseline == null || current == null)
            {
                return 0;
            }

            return current.Count(id => !baseline.Contains(id));
        }

        /// <summary>
        /// O erro em portugues, sem a URL. 401/403 quase sempre e credencial; e dizer isso poupa a
        /// pessoa de procurar problema de rede que nao existe.
        /// </summary>
        public static string Describe(Exception ex)
        {
            var web = ex as WebException;
            if (web != null)
            {
                var resposta = web.Response as HttpWebResponse;
                if (resposta != null)
                {
                    var codigo = (int)resposta.StatusCode;
                    if (codigo == 401 || codigo == 403)
                    {
                        return "o RetroAchievements recusou a credencial (" + codigo +
                               "). Confira o usuário e a chave de API da sua conta.";
                    }

                    return "o RetroAchievements respondeu " + codigo + ".";
                }

                return "não consegui falar com o RetroAchievements: " + web.Status + ".";
            }

            return ex == null ? "erro desconhecido" : ex.Message;
        }
    }
}
