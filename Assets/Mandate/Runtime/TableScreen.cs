// TableScreen — who sits where, with which model, and the keys to reach them.
//
// Opens on Play (openTableOnStart) and from the Table button. One card per seat with a kind picker, name and
// model (or URL for http bots), and an advanced row (endpoint, effort, json mode, persona). Below that: one
// password field per provider in use, seed and bots, the options, and Start. Everything here writes straight into the
// view's inspector fields (players, seats, seed, botKind, shuffleSeats, revealAgentNotes) plus the Keys dictionary,
// so the
// inspector and the screen always agree. Start validates every AI seat without a network call first; "Start
// anyway" lets failed seats fall back to scripted bots.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mandate.Agents;
using Mandate.Core;

namespace Mandate.Viewer
{
    public sealed class TableScreen
    {
        readonly MandateGameView view;

        /// <summary>True while the screen is showing. The game is paused meanwhile.</summary>
        public bool Open { get; private set; }

        /// <summary>API keys typed here: environment-variable name -> key. Used for every seat of that provider.</summary>
        public readonly Dictionary<string, string> Keys = new Dictionary<string, string>();

        bool advanced;                  // show the endpoint / effort / json mode / persona row
        int expandedSeat = -1;          // seat whose kind picker is unfolded
        readonly ModelPicker models;    // the model list that unfolds under a seat's "list" button
        readonly List<string> errors = new List<string>();
        Vector2 scroll;
        string seedText;                // the seed box is kept as text so a half-typed number is not lost
        int shownSeed;                  // the seed the box last showed, so a change from elsewhere (Play again) updates it

        // Kinds offered on the Table screen, in the order they appear. Aliases (chatgpt, claude, google, grok, bot) are left out.
        static readonly string[] KindChoices =
        {
            "greedy", "random", "openrouter", "openai", "gemini", "anthropic", "groq", "together",
            "deepseek", "xai", "mistral", "ollama", "lmstudio", "vllm", "compatible", "http",
        };

        public TableScreen(MandateGameView view) { this.view = view; models = new ModelPicker(view); }

        public void Show() { Open = true; errors.Clear(); }
        public void Hide() { Open = false; errors.Clear(); }

        public void Draw()
        {
            view.EnsureSeats(view.players);
            float w = Mathf.Min(Ui.CanvasW - 40f, 900f), h = Mathf.Min(Ui.CanvasH - Ui.TopH - 30f, 820f);
            var rect = new Rect((Ui.CanvasW - w) / 2f, Ui.TopH + 12f, w, h);
            GUILayout.BeginArea(rect, Ui.Panel);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Table</b>  <size=12>who sits where. Keys stay {(WebFiles.InBrowser ? "in this browser" : "on this PC")} and are never shown to the other seats.</size>", Ui.Title);
            GUILayout.FlexibleSpace();
            string playersTip = $"How many players sit at the table: {Rules.MinPlayers} to {Rules.MaxPlayers}. Each one gets a seat card below.";
            GUILayout.Label(new GUIContent("players", playersTip), Ui.Small, GUILayout.Width(52));
            if (GUILayout.Button(new GUIContent("-", playersTip), Ui.Button, GUILayout.Width(24))) { view.players = Mathf.Max(Rules.MinPlayers, view.players - 1); expandedSeat = -1; }
            GUILayout.Label(new GUIContent(view.players.ToString(), playersTip), Ui.Label, GUILayout.Width(20));
            if (GUILayout.Button(new GUIContent("+", playersTip), Ui.Button, GUILayout.Width(24))) { view.players = Mathf.Min(Rules.MaxPlayers, view.players + 1); view.EnsureSeats(view.players); }
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("<size=12>Click a seat's button to choose who plays it: you (first row), a scripted bot, or an AI model "
                + "(OpenRouter, OpenAI, Claude, Gemini, ...). An AI seat needs that provider's API key, pasted in the Keys section that then appears below.</size>", Ui.Wrap);
            DrawSeats();
            DrawKeys();
            DrawGame();
            DrawOptions();
            foreach (var err in errors) GUILayout.Label($"<color=#ff6b6b>{err}</color>", Ui.Wrap);
            GUILayout.EndScrollView();
            DrawButtons();

            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ seats
        void DrawSeats()
        {
            for (int i = 0; i < view.players; i++)
            {
                var s = view.seats[i];
                string kind = s.NormalizedKind();
                Seats.Kinds.TryGetValue(kind, out var info);
                bool isHuman = s.IsHuman();
                bool isAi = info != null && info.Family != "bot" && info.Family != "human";

                GUILayout.BeginVertical(Ui.Card);

                // Kind, name, model (or URL for http bots).
                GUILayout.BeginHorizontal();
                // With shuffling on, the seat (and so the colour) is only known when the game starts.
                GUILayout.Label(view.shuffleSeats ? $"#{i + 1}" : view.Colors.Tag("P" + (i + 1)), Ui.Label, GUILayout.Width(36));
                string kindLabel = isHuman ? "you" : kind == "" ? view.botKind + " (default)" : kind;
                if (GUILayout.Button(new GUIContent(kindLabel + " v", "Choose who plays this seat: you (first row only), a scripted bot, or an AI model."), Ui.Button, GUILayout.Width(150))) { expandedSeat = expandedSeat == i ? -1 : i; models.Close(); }   // plain "v": the web build's font has no arrow characters
                if (isAi || kind == "http")
                {
                    string nameTip = $"The name shown on the player card and in the log, up to {SeatSetup.MaxNameLength} characters. Picking a model from the list fills it in. Blank = kind:model.";
                    GUILayout.Label(new GUIContent("name", nameTip), Ui.Small, GUILayout.Width(38));
                    s.name = GUILayout.TextField(s.name ?? "", SeatSetup.MaxNameLength, Ui.Field, GUILayout.Width(120));
                    Ui.Tip(nameTip);
                    string modelTip = kind == "http"
                        ? "The address of your own bot. Each phase the game sends it the board as JSON and expects an action back."
                        : $"Which model plays this seat, as the provider names it. Blank = the default{(info?.Model != null ? $" ({info.Model})" : "")}.{(ModelPicker.CanList(kind) ? " Press list to pick one." : "")}";
                    GUILayout.Label(new GUIContent(kind == "http" ? "url" : "model", modelTip), Ui.Small, GUILayout.Width(40));
                    if (kind == "http") { s.endpoint = GUILayout.TextField(s.endpoint ?? "", Ui.Field, GUILayout.ExpandWidth(true)); Ui.Tip(modelTip); }
                    else
                    {
                        s.model = GUILayout.TextField(s.model ?? "", Ui.Field, GUILayout.ExpandWidth(true));
                        Ui.Tip(modelTip);
                        if (ModelPicker.CanList(kind) && GUILayout.Button(new GUIContent(models.IsOpenFor(i) ? "close" : "list", "Show this provider's models, with prices where known, and pick one."), Ui.Button, GUILayout.Width(50))) { models.Toggle(i); expandedSeat = -1; }
                    }
                }
                else if (isHuman) GUILayout.Label("<size=12>a person at this screen. The bottom panel is yours.</size>", Ui.Small);
                else GUILayout.Label("<size=12>a scripted bot. Click the button to seat an AI model instead.</size>", Ui.Small);
                GUILayout.EndHorizontal();

                if (info != null && isAi)
                {
                    string hint = string.IsNullOrWhiteSpace(s.model) && info.Model != null ? $"default model: {info.Model}" : info.About;
                    string keyHint = info.KeyEnv == null ? "no key needed" : $"key: {ProviderName(info.KeyEnv)} (below)";
                    GUILayout.Label($"<size=11>{hint}  ·  {keyHint}</size>", Ui.Small);
                    if (models.IsOpenFor(i)) models.Draw(i, s);
                }
                string browserHint = WebFiles.InBrowser ? BrowserHint(kind, string.IsNullOrWhiteSpace(s.endpoint) ? info?.Endpoint : s.endpoint) : null;
                if (browserHint != null) GUILayout.Label($"<size=11><color=#ffd27f>in a browser: {browserHint}</color></size>", Ui.Wrap);

                if (advanced && (isAi || kind == "http"))
                {
                    GUILayout.BeginHorizontal();
                    if (kind != "http")
                    {
                        string endpointTip = $"The provider's base address. Blank = the default{(info?.Endpoint != null ? $" ({info.Endpoint})" : "")}. Change it for a proxy or a server of your own.";
                        GUILayout.Label(new GUIContent("endpoint", endpointTip), Ui.Small, GUILayout.Width(58));
                        s.endpoint = GUILayout.TextField(s.endpoint ?? "", Ui.Field, GUILayout.Width(260));
                        Ui.Tip(endpointTip);
                        string effortTip = $"How hard a thinking model reasons before answering: low, medium or high. Higher is slower and costs more. Blank = the default{(info?.Effort != null ? $" ({info.Effort})" : "")}. Ignored by models that do not support it.";
                        GUILayout.Label(new GUIContent("effort", effortTip), Ui.Small, GUILayout.Width(44));
                        s.effort = GUILayout.TextField(s.effort ?? "", Ui.Field, GUILayout.Width(70));
                        Ui.Tip(effortTip);
                        s.jsonMode = GUILayout.Toggle(s.jsonMode, new GUIContent(" json mode", "Ask the server for strict JSON replies. Can help a model that keeps answering in the wrong format. OpenAI-style servers only, and not all of them support it."), Ui.Toggle, GUILayout.Width(96));
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    string personaTip = "Extra instructions added to this seat's rules prompt: a personality, a play style, a language. Example: \"You are a cautious diplomat who never breaks a deal.\"";
                    GUILayout.Label(new GUIContent("persona", personaTip), Ui.Small, GUILayout.Width(58));
                    s.persona = GUILayout.TextField(s.persona ?? "", Ui.Field, GUILayout.ExpandWidth(true));
                    Ui.Tip(personaTip);
                    GUILayout.EndHorizontal();
                }

                // The kind picker, unfolded under the seat that was clicked.
                if (expandedSeat == i)
                {
                    var choices = i == 0 ? new[] { "you" }.Concat(KindChoices).ToArray() : KindChoices;
                    int current = Array.IndexOf(choices, isHuman ? "you" : kind);
                    var buttons = choices.Select(c => new GUIContent(c, KindTip(c))).ToArray();
                    int picked = GUILayout.SelectionGrid(current, buttons, 6, Ui.Button);
                    if (picked != current && picked >= 0)
                    {
                        s.kind = choices[picked];
                        if (i == 0) view.humanPlays = s.IsHuman();
                        if (Seats.Kinds.TryGetValue(s.NormalizedKind(), out var k2) && k2.Family == "bot") { s.model = ""; s.endpoint = ""; }
                        expandedSeat = -1;
                    }
                    if (Seats.Kinds.TryGetValue(s.NormalizedKind(), out var k3))
                        GUILayout.Label($"<size=11>{k3.About}</size>", Ui.Wrap);
                }

                GUILayout.EndVertical();
            }
            advanced = GUILayout.Toggle(advanced, new GUIContent(" advanced fields (endpoint, effort, json mode, persona)", "Show extra settings on each AI seat. Most people never need them: blank fields use the provider's defaults."), Ui.Toggle);
        }

        // The hover text for one choice in the seat kind picker.
        static string KindTip(string choice)
        {
            if (choice == "you") return "You play this seat. Your controls appear at the bottom of the screen.";
            if (!Seats.Kinds.TryGetValue(choice, out var k)) return null;
            string key = k.KeyEnv == null ? "No key needed." : $"Needs a {ProviderName(k.KeyEnv)} key.";
            return k.Family == "bot" || k.Family == "http" ? k.About : $"{k.About}. {key}";
        }

        // What a seat on the player's own computer needs before a web page may call it, or null if nothing.
        // A browser only lets a page call a server that allows that page (CORS), and from an https page it only
        // allows plain http to this same computer (localhost).
        static string BrowserHint(string kind, string endpoint)
        {
            string url = (endpoint ?? "").Trim().ToLowerInvariant();
            bool plainHttp = url.StartsWith("http://");
            bool thisComputer = url.Contains("://localhost") || url.Contains("://127.0.0.1");
            if (plainHttp && !thisComputer && url != "")
                return "a web page can only reach plain http:// on this computer (localhost). Use https, or run the desktop version.";
            switch (kind)
            {
                case "ollama": return "Ollama must allow this page: set the environment variable OLLAMA_ORIGINS=* and restart Ollama. The browser may ask to allow local network access; say yes.";
                case "lmstudio": return "turn on \"Enable CORS\" in LM Studio's server settings. The browser may ask to allow local network access; say yes.";
                case "vllm": case "compatible": case "http":
                    return thisComputer ? "the server must allow calls from web pages (CORS). The browser may ask to allow local network access; say yes." : null;
                default: return null;
            }
        }

        // ------------------------------------------------------------------ keys, one per provider in use
        void DrawKeys()
        {
            var envs = view.seats.Take(view.players)
                .Select(s => Seats.Kinds.TryGetValue(s.NormalizedKind(), out var k) ? (string.IsNullOrWhiteSpace(s.apiKeyEnv) ? k.KeyEnv : s.apiKeyEnv.Trim()) : null)
                .Where(e => e != null).Distinct().ToList();
            if (envs.Count == 0) return;

            GUILayout.Space(8);
            GUILayout.Label("<b>Keys</b>  <size=12>one per provider; every seat of that provider uses it</size>", Ui.Label);
            foreach (var env in envs)
            {
                GUILayout.BeginHorizontal(Ui.Card);
                GUILayout.Label($"{ProviderName(env)}  <size=11>{env}</size>", Ui.Label, GUILayout.Width(280));
                Keys.TryGetValue(env, out var typed);
                Keys[env] = GUILayout.PasswordField(typed ?? "", '*', Ui.Field, GUILayout.Width(360));
                Ui.Tip($"Paste your {ProviderName(env)} API key. Every {ProviderName(env)} seat uses it, and it is only ever sent to {ProviderName(env)}.");
                bool inEnvironment = !WebFiles.InBrowser && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(env));   // a browser has no environment
                GUILayout.Label(!string.IsNullOrWhiteSpace(Keys[env]) ? "<color=#7fdc7f>set</color>"
                    : inEnvironment ? "<color=#7fdc7f>found in the environment</color>"
                    : "<color=#ffb37f>paste a key</color>", Ui.Small);
                // Clears the box and also removes the key from table.json, so it is gone from this PC (or browser) straight away.
                if (!string.IsNullOrWhiteSpace(Keys[env]) && GUILayout.Button(new GUIContent("Delete", $"Clear this key and remove it from the saved table straight away, so it is gone from this {(WebFiles.InBrowser ? "browser" : "PC")}."), Ui.Button, GUILayout.Width(64)))
                {
                    Keys[env] = "";
                    TableFile.ForgetKey(view.TablePath, env);
                }
                GUILayout.EndHorizontal();
            }
        }

        // ------------------------------------------------------------------ seed and bots (fixed once a game starts)
        void DrawGame()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>Game</b>  <size=12>the rules are the same in every game, so results can be compared.</size>", Ui.Label);

            GUILayout.BeginHorizontal();
            string seedTip = "Any whole number. It decides the board, the objectives and the scripted bots' choices: the same seed gives the same game every time (AI models still vary). Play again moves on to the next seed.";
            GUILayout.Label(new GUIContent("seed", seedTip), Ui.Small, GUILayout.Width(36));
            if (seedText == null || view.seed != shownSeed) { seedText = view.seed.ToString(); shownSeed = view.seed; }
            seedText = GUILayout.TextField(seedText, Ui.Field, GUILayout.Width(80));
            Ui.Tip(seedTip);
            if (int.TryParse(seedText, out int parsed)) { view.seed = parsed; shownSeed = parsed; }
            GUILayout.Space(16);
            if (GUILayout.Button(new GUIContent("scripted bots: " + view.botKind, "The scripted bot used for seats left on the default. Click to cycle: greedy (plays toward its objective), random (any legal move), mixed (greedy and random in turn by seat)."), Ui.Button, GUILayout.Width(170)))
                view.botKind = view.botKind == "greedy" ? "random" : view.botKind == "random" ? "mixed" : "greedy";
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ options
        void DrawOptions()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>Options</b>", Ui.Label);
            view.shuffleSeats = GUILayout.Toggle(view.shuffleSeats, new GUIContent(" shuffle seats each game (so nobody always has the same neighbours; the log says which colour you are)",
                "The shuffle comes from the seed: same seed, same seating. Off = seat cards keep their order and colour."), Ui.Toggle);
            view.revealAgentNotes = GUILayout.Toggle(view.revealAgentNotes, new GUIContent(" show AI thinking in the log during the game (you can flip this any time from the top bar; notes are always revealed at the end)",
                "Each AI seat writes private notes about its plans. On = you read them as it plays. Off = they stay hidden until the game ends."), Ui.Toggle);
            string rememberTip = "Save the seats, rules and options so they are back the next time you open the game. Keys are only saved if you also tick the box below.";
            string keysTip = "Save the API keys with the table so you do not paste them again. They are stored as plain text, so leave this off on a shared computer.";
            if (WebFiles.InBrowser)
            {
                // In a browser the files live in this browser's storage for this page, not in a folder.
                view.rememberTable = GUILayout.Toggle(view.rememberTable, new GUIContent(" remember this table in this browser", rememberTip), Ui.Toggle);
                // Off by default here (see MandateGameView.LoadTable): other games on the same site can read this browser storage.
                string webKeysTip = "Off: your keys are forgotten when you close this tab. On: they are saved as plain text in this browser's storage for this website. "
                                  + "On sites that host many games, like itch.io, the other games there can read that storage too, so only tick this if you accept that risk.";
                if (view.rememberTable)
                    view.rememberKeys = GUILayout.Toggle(view.rememberKeys, new GUIContent(" remember the keys too (not recommended: other games on this website could read them; off = forgotten when you close the tab)", webKeysTip), Ui.Toggle);
                GUILayout.Label("<size=11>Your keys are sent only to the provider each seat uses, straight from this browser. A finished game's transcript, with every AI seat's notes and every prompt and reply, can be downloaded from the scoreboard.</size>", Ui.Wrap);
                return;
            }
            view.rememberTable = GUILayout.Toggle(view.rememberTable, new GUIContent(" remember this table on this PC", rememberTip), Ui.Toggle);
            if (view.rememberTable)
                view.rememberKeys = GUILayout.Toggle(view.rememberKeys, new GUIContent($" remember the keys too (plain text in {view.TablePath})", keysTip), Ui.Toggle);
            GUILayout.Label($"<size=11>Transcripts of finished games, with every AI seat's notes and every prompt and reply, are saved to {view.TranscriptDir}</size>", Ui.Wrap);
        }

        // ------------------------------------------------------------------ buttons
        void DrawButtons()
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("<b>Start game</b>", "Check every AI seat's setup (a missing key or unknown kind shows in red above), then start a new game with this table."), Ui.Button, GUILayout.Width(130), GUILayout.Height(30)))
            {
                Validate();
                if (errors.Count == 0) view.StartFromTable();
            }
            if (errors.Count > 0 && GUILayout.Button(new GUIContent("Start anyway (failed seats become bots)", "Start now. Seats listed in red above are played by scripted bots instead."), Ui.Button, GUILayout.Height(30)))
                view.StartFromTable();
            GUILayout.FlexibleSpace();
            // A browser has no folders to open; there the scoreboard downloads each game's transcript instead.
            if (!WebFiles.InBrowser && GUILayout.Button(new GUIContent("Open transcripts folder", "Open the folder where each finished game's full transcript is saved."), Ui.Button, GUILayout.Height(30))) TranscriptWriter.OpenFolder(view.TranscriptDir);
            if (GUILayout.Button(new GUIContent(view.Runner == null ? "Close" : "Back to game", "Close this screen. Your changes are kept, and seat changes apply from the next game."), Ui.Button, GUILayout.Width(110), GUILayout.Height(30))) Hide();
            GUILayout.EndHorizontal();
        }

        // Builds every AI seat once, without any network call, so a missing key or an unknown kind shows up before the game starts.
        void Validate()
        {
            errors.Clear();
            for (int i = 0; i < view.players; i++)
            {
                var s = view.seats[i];
                if (s.IsHuman()) { if (i != 0) errors.Add($"#{i + 1}: only the first row can be you."); continue; }
                if (s.NormalizedKind() == "") continue;
                try { Seats.Make(view.ConfigFor(i), 0); }
                catch (Exception e) { errors.Add($"#{i + 1} ({s.NormalizedKind()}): {e.Message}"); }
            }
        }

        // Friendly provider name for a key's environment variable.
        static string ProviderName(string env)
        {
            switch (env)
            {
                case "OPENROUTER_API_KEY": return "OpenRouter";
                case "OPENAI_API_KEY": return "OpenAI";
                case "GEMINI_API_KEY": return "Google Gemini";
                case "ANTHROPIC_API_KEY": return "Anthropic (Claude)";
                case "GROQ_API_KEY": return "Groq";
                case "TOGETHER_API_KEY": return "Together";
                case "DEEPSEEK_API_KEY": return "DeepSeek";
                case "XAI_API_KEY": return "xAI";
                case "MISTRAL_API_KEY": return "Mistral";
                default: return env;
            }
        }
    }
}
