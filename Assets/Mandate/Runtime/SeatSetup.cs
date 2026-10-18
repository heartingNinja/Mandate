// SeatSetup — one seat's setup as it appears in the inspector and on the Table screen.
//
// The MandateGameView keeps an array of these (element 0 = P1). The Table screen edits them at run time; the
// inspector shows the same values. When a game starts each one is converted to a Mandate.Agents.SeatConfig,
// which is what the agent layer understands. Blank fields mean "use the kind's default" (see Docs/Agents.md).

using System;
using UnityEngine;
using Mandate.Agents;

namespace Mandate.Viewer
{
    [Serializable]
    public sealed class SeatSetup
    {
        [Tooltip("Seat kind. 'you' = a person at this screen (seat 1 only). Blank = use Bot Kind. See Docs/Agents.md for the list and each kind's defaults.")]
        public string kind = "";

        [Tooltip("Display name on the player card and in the log, e.g. Claude, GPT, Gemini. Up to 18 characters. Picking a model from the list fills it in (unless you typed your own). Blank = kind:model.")]
        public string name = "";

        [Tooltip("Model id. Blank = the kind's default (claude-opus-5, gemini-3.8-flash, gpt-5.6-luna, a free OpenRouter model, ...).")]
        public string model = "";

        [Tooltip("Base URL. Blank = the kind's default. For http bots: the full URL the view is POSTed to.")]
        public string endpoint = "";

        [Tooltip("Environment variable holding the API key. Blank = the kind's default (OPENAI_API_KEY, ANTHROPIC_API_KEY, GEMINI_API_KEY, OPENROUTER_API_KEY, ...). Keys typed on the Table screen take precedence.")]
        public string apiKeyEnv = "";

        [Tooltip("API key pasted directly. Prefer the Table screen or an environment variable: this text is saved into the scene file.")]
        public string apiKey = "";

        [Tooltip("Extra instructions for the model: a personality, a play style, a language. Appended to the rules prompt.")]
        [TextArea(1, 4)] public string persona = "";

        [Tooltip("Reasoning effort where the provider supports it: low, medium, high. Blank = the kind's default.")]
        public string effort = "";

        [Tooltip("OpenAI-compatible servers only: ask for strict JSON output (response_format json_object). Not every server supports it.")]
        public bool jsonMode = false;

        /// <summary>Longest seat name, so names fit on the player cards and read well in the log.</summary>
        public const int MaxNameLength = 18;

        /// <summary>A short name made from a model id: "~google/gemma-4-31b-it:free" -> "gemma-4-31b-it",
        /// "deepseek/deepseek-v4-flash-latest" -> "deepseek-v4-flash". Never longer than MaxNameLength.</summary>
        public static string NameFromModel(string model)
        {
            string n = (model ?? "").Trim().TrimStart('~');
            int slash = n.LastIndexOf('/');
            if (slash >= 0) n = n.Substring(slash + 1);            // drop the vendor ("google/")
            int colon = n.IndexOf(':');
            if (colon > 0) n = n.Substring(0, colon);               // drop a variant (":free")
            if (n.EndsWith("-latest")) n = n.Substring(0, n.Length - "-latest".Length);
            if (n.Length > MaxNameLength) n = n.Substring(0, MaxNameLength).TrimEnd('-', '.', '_');
            return n;
        }

        /// <summary>Sets the model, and the name to match unless the player typed a name of their own.
        /// defaultModel is the kind's default, used when the model is left blank.</summary>
        public void ChooseModel(string newModel, string defaultModel)
        {
            string before = NameFromModel(string.IsNullOrWhiteSpace(model) ? defaultModel : model);
            bool nameWasAuto = string.IsNullOrWhiteSpace(name) || name.Trim() == before;
            model = newModel ?? "";
            if (nameWasAuto) name = NameFromModel(string.IsNullOrWhiteSpace(model) ? defaultModel : model);
        }

        /// <summary>The kind, trimmed and lower-cased, so "Greedy " and "greedy" are the same seat.</summary>
        public string NormalizedKind() => (kind ?? "").Trim().ToLowerInvariant();

        /// <summary>True when a person at this screen holds the seat ("you" or "human").</summary>
        public bool IsHuman() { var k = NormalizedKind(); return k == "you" || k == "human"; }

        /// <summary>The agent-layer config for this seat. Blank fields become null so the kind's defaults apply.</summary>
        public SeatConfig ToConfig() => new SeatConfig
        {
            Kind = NormalizedKind(), Name = Blank(name), Model = Blank(model), Endpoint = Blank(endpoint),
            ApiKeyEnv = Blank(apiKeyEnv), ApiKey = Blank(apiKey), Persona = Blank(persona), Effort = Blank(effort), JsonMode = jsonMode,
        };

        static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
