// Mandate.Agents.Models — asks a provider which models it offers, so the Table screen can show a list to pick from
// instead of making you type model ids from memory.
//
//   openai family   GET {endpoint}/models              OpenRouter (no key needed, and it includes prices),
//                                                      OpenAI, Groq, Together, DeepSeek, xAI, Mistral, Ollama,
//                                                      LM Studio, vLLM (most need the key)
//   anthropic       GET {endpoint}/v1/models           needs the key
//   gemini          GET {endpoint}/v1beta/models       needs the key; only models that can generate text are kept
//
// Only OpenRouter reports prices. For the others the price columns stay empty.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Mandate.Agents
{
    /// <summary>One model a provider offers.</summary>
    public sealed class ModelInfo
    {
        public string Id;               // what goes in the seat's model field
        public string Name;             // friendly name, or the id again
        public double InPerMillion = -1;    // US dollars per million prompt tokens; -1 = unknown
        public double OutPerMillion = -1;   // US dollars per million reply tokens; -1 = unknown

        public bool PriceKnown => InPerMillion >= 0 && OutPerMillion >= 0;
        public bool Free => InPerMillion == 0 && OutPerMillion == 0;
    }

    public static class ModelCatalog
    {
        /// <summary>
        /// Fetches the model list for a seat's provider, using the seat's endpoint and key (or the kind's defaults).
        /// Sorted free first, then cheapest, then by id. Throws with a readable message if the provider refuses.
        /// </summary>
        public static async Task<List<ModelInfo>> Fetch(SeatConfig seat, CancellationToken ct)
        {
            string kind = (seat.Kind ?? "").Trim();
            if (!Seats.Kinds.TryGetValue(kind, out var k)) throw new ArgumentException($"unknown seat kind '{seat.Kind}'");

            // Work on a copy so the seat's own config is not filled with defaults.
            var cfg = new SeatConfig
            {
                Kind = kind,
                Endpoint = string.IsNullOrWhiteSpace(seat.Endpoint) ? k.Endpoint : seat.Endpoint,
                ApiKey = seat.ApiKey,
                ApiKeyEnv = string.IsNullOrWhiteSpace(seat.ApiKeyEnv) ? k.KeyEnv : seat.ApiKeyEnv,
            };
            if (string.IsNullOrWhiteSpace(cfg.Endpoint)) throw new ArgumentException("set the endpoint first");
            string key = Seats.Key(cfg, required: false);

            List<ModelInfo> list;
            switch (k.Family)
            {
                case "openai":
                    list = ParseOpenAi(await Http.GetJson(Http.Join(cfg.Endpoint, "models"), ct,
                        ("Authorization", key == null ? null : "Bearer " + key)).ConfigureAwait(false));
                    break;
                case "anthropic":
                    if (key == null) throw new InvalidOperationException("paste the key first, then list the models");
                    list = ParseAnthropic(await Http.GetJson(Http.Join(cfg.Endpoint, "v1/models?limit=1000"), ct,
                        ("x-api-key", key), ("anthropic-version", "2023-06-01"), AnthropicClient.BrowserHeader).ConfigureAwait(false));
                    break;
                case "gemini":
                    if (key == null) throw new InvalidOperationException("paste the key first, then list the models");
                    list = ParseGemini(await Http.GetJson(Http.Join(cfg.Endpoint, "v1beta/models?pageSize=1000"), ct,
                        ("x-goog-api-key", key)).ConfigureAwait(false));
                    break;
                default:
                    throw new ArgumentException($"seat kind '{kind}' has no model list");
            }
            return Sort(list);
        }

        /// <summary>Free first, then cheapest (prompt + reply price), then unknown prices, each group by id.</summary>
        public static List<ModelInfo> Sort(IEnumerable<ModelInfo> models) =>
            models.OrderBy(m => m.Free ? 0 : m.PriceKnown ? 1 : 2)
                  .ThenBy(m => m.PriceKnown ? m.InPerMillion + m.OutPerMillion : 0)
                  .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                  .ToList();

        // { "data": [ { "id", "name"?, "pricing"?: { "prompt": "0.000001", "completion": "0.000002" } } ] }
        // OpenRouter prices are dollars per token, as strings; -1 means "varies" (routers).
        public static List<ModelInfo> ParseOpenAi(string json)
        {
            var list = new List<ModelInfo>();
            foreach (var m in JObject.Parse(json)["data"] as JArray ?? new JArray())
            {
                string id = m["id"]?.ToString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                list.Add(new ModelInfo
                {
                    Id = id,
                    Name = m["name"]?.ToString() ?? id,
                    InPerMillion = PerMillion(m["pricing"]?["prompt"]),
                    OutPerMillion = PerMillion(m["pricing"]?["completion"]),
                });
            }
            return list;
        }

        // { "data": [ { "id", "display_name" } ] }
        public static List<ModelInfo> ParseAnthropic(string json) =>
            (JObject.Parse(json)["data"] as JArray ?? new JArray())
                .Where(m => !string.IsNullOrWhiteSpace(m["id"]?.ToString()))
                .Select(m => new ModelInfo { Id = m["id"].ToString(), Name = m["display_name"]?.ToString() ?? m["id"].ToString() })
                .ToList();

        // { "models": [ { "name": "models/gemini-...", "displayName", "supportedGenerationMethods": [...] } ] }
        public static List<ModelInfo> ParseGemini(string json) =>
            (JObject.Parse(json)["models"] as JArray ?? new JArray())
                .Where(m => (m["supportedGenerationMethods"] as JArray)?.Any(x => x.ToString() == "generateContent") ?? true)
                .Select(m => m["name"]?.ToString() ?? "")
                .Where(n => n != "")
                .Select(n => n.StartsWith("models/") ? n.Substring(7) : n)
                .Select(id => new ModelInfo { Id = id, Name = id })
                .ToList();

        // A per-token price string to dollars per million tokens. Missing, unreadable or negative -> -1 (unknown).
        static double PerMillion(JToken price)
        {
            if (price == null) return -1;
            if (!double.TryParse(price.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var perToken) || perToken < 0) return -1;
            return Math.Round(perToken * 1_000_000, 4);
        }
    }
}
