// Mandate.Agents.Seats — the factory: SeatConfig -> IAgent, plus the table of known seat kinds and their defaults.
//
// Adding a provider that speaks the OpenAI chat format is one line in Kinds. A new API family is a new IChatClient
// in Mandate.Agents.Clients.cs plus one case in Make. Nothing else in the project needs to know.

using System;
using System.Collections.Generic;
using Mandate.Sim;

namespace Mandate.Agents
{
    public static class Seats
    {
        public sealed class KindInfo
        {
            public string Family;      // bot | openai | anthropic | gemini | http | human
            public string Endpoint;    // default base URL
            public string KeyEnv;      // default environment variable holding the API key; null = no key needed
            public string Model;       // default model id
            public string Effort;      // default reasoning effort, where the family supports it
            public string About;       // one line for docs and error messages
        }

        static KindInfo Bot(string about) => new KindInfo { Family = "bot", About = about };
        static KindInfo OpenAi(string endpoint, string keyEnv, string model, string about, string effort = null) =>
            new KindInfo { Family = "openai", Endpoint = endpoint, KeyEnv = keyEnv, Model = model, Effort = effort, About = about };
        static KindInfo Anthropic(string about) =>
            new KindInfo { Family = "anthropic", Endpoint = "https://api.anthropic.com", KeyEnv = "ANTHROPIC_API_KEY", Model = "claude-opus-5", Effort = "medium", About = about };
        static KindInfo Gemini(string about) =>
            new KindInfo { Family = "gemini", Endpoint = "https://generativelanguage.googleapis.com", KeyEnv = "GEMINI_API_KEY", Model = "gemini-3.8-flash", About = about };

        // Default model ids were checked against the providers' published lists on 2026-09-13. They will go stale.
        // Every one can be overridden per seat, and a wrong id comes back as a clear HTTP error in the game log.
        public static readonly Dictionary<string, KindInfo> Kinds = new Dictionary<string, KindInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["greedy"]     = Bot("scripted: one-round heuristic keyed on its objective"),
            ["random"]     = Bot("scripted: a random legal action"),
            ["human"]      = new KindInfo { Family = "human", About = "a person at the Unity panel (seat P1)" },

            ["openai"]     = OpenAi("https://api.openai.com/v1", "OPENAI_API_KEY", "gpt-5.6-luna", "OpenAI. gpt-6-astra is the flagship, gpt-5.6-luna the cheap default"),
            ["chatgpt"]    = OpenAi("https://api.openai.com/v1", "OPENAI_API_KEY", "gpt-5.6-luna", "alias of openai"),
            ["openrouter"] = OpenAi("https://openrouter.ai/api/v1", "OPENROUTER_API_KEY", "nvidia/nemotron-3.5-lightning:free", "hundreds of models behind one key; ids ending in :free cost nothing. Effort defaults to low: cheap thinking models otherwise run past the phase timeout", effort: "low"),
            ["groq"]       = OpenAi("https://api.groq.com/openai/v1", "GROQ_API_KEY", "llama-3.3-70b-versatile", "fast open-weight models"),
            ["together"]   = OpenAi("https://api.together.xyz/v1", "TOGETHER_API_KEY", "meta-llama/Llama-3.3-70B-Instruct-Turbo", "open-weight models"),
            ["deepseek"]   = OpenAi("https://api.deepseek.com/v1", "DEEPSEEK_API_KEY", "deepseek-chat", "DeepSeek"),
            ["xai"]        = OpenAi("https://api.x.ai/v1", "XAI_API_KEY", "grok-4.6", "xAI Grok"),
            ["grok"]       = OpenAi("https://api.x.ai/v1", "XAI_API_KEY", "grok-4.6", "alias of xai"),
            ["mistral"]    = OpenAi("https://api.mistral.ai/v1", "MISTRAL_API_KEY", "mistral-small-latest", "Mistral"),
            ["ollama"]     = OpenAi("http://localhost:11434/v1", null, "llama3.1", "local models through Ollama; set model to one you have pulled"),
            ["lmstudio"]   = OpenAi("http://localhost:1234/v1", null, "local-model", "local models through LM Studio's server"),
            ["vllm"]       = OpenAi("http://localhost:8000/v1", null, null, "a local vLLM server; model is required"),
            ["compatible"] = OpenAi(null, null, null, "any other OpenAI-compatible server; endpoint and model are required"),

            ["anthropic"]  = Anthropic("Claude through the Messages API"),
            ["claude"]     = Anthropic("alias of anthropic"),
            ["gemini"]     = Gemini("Google Gemini"),
            ["google"]     = Gemini("alias of gemini"),

            ["http"]       = new KindInfo { Family = "http", About = "bring your own bot: the view is POSTed to your URL as JSON" },
            ["bot"]        = new KindInfo { Family = "http", About = "alias of http" },
            ["relay"]      = new KindInfo { Family = "relay", About = "prompts written to files in the endpoint folder; whoever writes the reply files plays (e.g. a Claude Code subagent). Editor and console only" },
        };

        /// <summary>Builds the seat. Fills the config's blank Endpoint / Model / ApiKeyEnv / Effort from the kind's defaults.</summary>
        public static IAgent Make(SeatConfig cfg, int seed)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            string kind = (cfg.Kind ?? "").Trim();
            if (!Kinds.TryGetValue(kind, out var k))
                throw new ArgumentException($"unknown seat kind '{cfg.Kind}'. Known kinds: {string.Join(", ", Kinds.Keys)}");
            cfg.Kind = kind.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(cfg.Endpoint)) cfg.Endpoint = k.Endpoint;
            if (string.IsNullOrWhiteSpace(cfg.Model)) cfg.Model = k.Model;
            if (string.IsNullOrWhiteSpace(cfg.ApiKeyEnv)) cfg.ApiKeyEnv = k.KeyEnv;
            if (string.IsNullOrWhiteSpace(cfg.Effort)) cfg.Effort = k.Effort;

            switch (k.Family)
            {
                case "bot":
                    return new BotAgent(Bots.Make(cfg.Kind, seed));
                case "human":
                    throw new ArgumentException("human seats are provided by the host (the Unity panel), not by Seats.Make");
                case "http":
                    if (string.IsNullOrWhiteSpace(cfg.Endpoint)) throw new ArgumentException("an http seat needs an endpoint: the URL to POST the view to");
                    return new HttpBotAgent(cfg, Key(cfg, required: false));
                case "openai":
                    if (string.IsNullOrWhiteSpace(cfg.Endpoint)) throw new ArgumentException($"seat kind '{cfg.Kind}' needs an endpoint");
                    if (string.IsNullOrWhiteSpace(cfg.Model)) throw new ArgumentException($"seat kind '{cfg.Kind}' needs a model id");
                    return new LlmAgent(cfg, new OpenAiChatClient(cfg, Key(cfg, required: k.KeyEnv != null)));
                case "anthropic":
                    return new LlmAgent(cfg, new AnthropicClient(cfg, Key(cfg, required: true)));
                case "gemini":
                    return new LlmAgent(cfg, new GeminiClient(cfg, Key(cfg, required: true)));
                case "relay":
                    if (string.IsNullOrWhiteSpace(cfg.Endpoint)) throw new ArgumentException("a relay seat needs an endpoint: the folder its prompt and reply files go in (one folder per seat)");
                    return new LlmAgent(cfg, new RelayClient(cfg.Endpoint));
                default:
                    throw new ArgumentException("unhandled seat family " + k.Family);
            }
        }

        // Literal key first, then the environment variable. Gemini also accepts GOOGLE_API_KEY. Also used by ModelCatalog.
        internal static string Key(SeatConfig cfg, bool required)
        {
            if (!string.IsNullOrWhiteSpace(cfg.ApiKey)) return cfg.ApiKey.Trim();
            string env = cfg.ApiKeyEnv;
            string key = string.IsNullOrWhiteSpace(env) ? null : Environment.GetEnvironmentVariable(env);
            if (string.IsNullOrWhiteSpace(key) && env == "GEMINI_API_KEY") key = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
            if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
            if (!required) return null;
            throw new InvalidOperationException(
                $"seat kind '{cfg.Kind}' needs an API key: paste it in the Keys section of the Table screen, set the environment variable {env ?? "ApiKeyEnv"}, or put it in ApiKey on the seat. " +
                "Unity reads environment variables at launch, so restart Unity and Unity Hub after setting one.");
        }
    }
}
