// ModelPicker — the list of models that unfolds under a seat when you press "list" next to its model box.
// Screen text sticks to plain Latin characters: the web build's built-in font has no arrows, bullets or dashes.
//
// Asks the seat's provider which models it offers (ModelCatalog in Mandate.Agents), then shows them with a search
// box. OpenRouter needs no key for this and includes prices, so the list shows free models first, then the
// cheapest, with the price per million tokens. Other providers need their key pasted first and report no prices.
// Each list is fetched once and shared by every seat on the same provider; "Refresh" fetches it again.
// Picking a model also names the seat after it (short form, SeatSetup.NameFromModel) unless you typed a name.
// The fetch runs in the background, so the screen never freezes while it waits.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Mandate.Agents;

namespace Mandate.Viewer
{
    public sealed class ModelPicker
    {
        const int MaxRows = 150;        // rows drawn at once; the search box narrows the rest
        const float ListH = 210f;       // height of the scrolling list, in virtual points

        readonly MandateGameView view;
        int seat = -1;                  // seat whose list is unfolded, -1 = none
        string search = "";
        bool freeOnly;
        Vector2 scroll;
        GUIStyle rowStyle;              // a left-aligned button, built on first draw

        // Provider + endpoint + "has a key" -> the fetch for that provider. Shared by every seat that matches.
        readonly Dictionary<string, Task<List<ModelInfo>>> lists = new Dictionary<string, Task<List<ModelInfo>>>();

        public ModelPicker(MandateGameView view) { this.view = view; }

        /// <summary>True if this seat's provider can list its models (scripted bots, people and http bots cannot).</summary>
        public static bool CanList(string kind) =>
            Seats.Kinds.TryGetValue(kind ?? "", out var k) && (k.Family == "openai" || k.Family == "anthropic" || k.Family == "gemini");

        /// <summary>True while seat i's list is unfolded.</summary>
        public bool IsOpenFor(int i) => seat == i;

        /// <summary>Unfolds seat i's list, or folds it if it is already open.</summary>
        public void Toggle(int i) { seat = seat == i ? -1 : i; search = ""; freeOnly = false; scroll = Vector2.zero; }

        /// <summary>Folds any open list.</summary>
        public void Close() { seat = -1; }

        /// <summary>Draws seat i's list. Call from TableScreen inside the seat's card, only when IsOpenFor(i).</summary>
        public void Draw(int i, SeatSetup s)
        {
            if (rowStyle == null) rowStyle = new GUIStyle(Ui.Button) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };

            var cfg = view.ConfigFor(i);
            string cacheKey = $"{cfg.Kind}|{cfg.Endpoint}|{(string.IsNullOrWhiteSpace(cfg.ApiKey) ? "" : "keyed")}";
            if (!lists.TryGetValue(cacheKey, out var task))
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                task = ModelCatalog.Fetch(cfg, cts.Token);
                lists[cacheKey] = task;
            }

            GUILayout.BeginVertical(Ui.Card);
            if (!task.IsCompleted)
            {
                GUILayout.Label("<size=12>asking the provider for its models...</size>", Ui.Small);
            }
            else if (task.IsFaulted || task.IsCanceled)
            {
                string why = task.IsCanceled ? "no answer within 20 seconds" : (task.Exception?.GetBaseException().Message ?? "unknown error");
                GUILayout.Label($"<color=#ffb37f>could not list models: {why}</color>", Ui.Wrap);
                GUILayout.BeginHorizontal();
                if (Ui.TipButton("Try again", "Ask the provider for its model list again. Check the key first if it keeps failing.", GUILayout.Width(90))) lists.Remove(cacheKey);
                if (Ui.TipButton("Close", "Close the list. You can still type a model id by hand.", GUILayout.Width(70))) Close();
                GUILayout.EndHorizontal();
            }
            else
            {
                DrawList(task.Result, s, cacheKey);
            }
            GUILayout.EndVertical();
        }

        // The search row, the scrolling list of models, and the buttons under it.
        void DrawList(List<ModelInfo> all, SeatSetup s, string cacheKey)
        {
            bool anyPrices = all.Any(m => m.PriceKnown);

            GUILayout.BeginHorizontal();
            string searchTip = "Type words to filter the list. Every word must appear in the model's id or name, in any order: \"llama free\" finds free Llama models.";
            Ui.TipLabel("search", searchTip, Ui.Small, GUILayout.Width(44));
            search = GUILayout.TextField(search ?? "", Ui.Field, GUILayout.ExpandWidth(true));
            Ui.Tip(searchTip);
            if (anyPrices) freeOnly = Ui.TipToggle(freeOnly, " free only", "Only show models that cost nothing. Free models have daily limits and can be slow when busy.", GUILayout.Width(90));
            GUILayout.EndHorizontal();

            var shown = all.Where(m => (!freeOnly || m.Free) && Matches(m, search)).ToList();
            GUILayout.Label($"<size=11>{shown.Count} of {all.Count} models" +
                            (anyPrices ? " · price is US dollars per million tokens, prompt / reply. The provider's activity page shows what a game really cost." : "") +
                            "</size>", Ui.Wrap);

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(ListH));
            bool? lastFree = null;
            foreach (var m in shown.Take(MaxRows))
            {
                // A heading where each group starts, so cheap paid models are not mistaken for free ones.
                if (anyPrices && lastFree != m.Free)
                    GUILayout.Label(m.Free ? "<b><color=#7fdc7f>Free</color></b>  <size=11>cost nothing, but have daily limits</size>"
                                           : "<b><color=#ffb37f>Paid</color></b>  <size=11>uses your credits, cheapest first</size>", Ui.Small);
                lastFree = m.Free;

                string price = !m.PriceKnown ? "" : m.Free ? "<color=#7fdc7f>free</color>" : $"${m.InPerMillion:0.###} / ${m.OutPerMillion:0.###}";
                string name = m.Name != m.Id ? $"  <color=#9aa0aa>{m.Name}</color>" : "";
                bool current = string.Equals(s.model, m.Id, StringComparison.OrdinalIgnoreCase);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button((current ? "» " : "") + m.Id + name, rowStyle, GUILayout.ExpandWidth(true))) { s.ChooseModel(m.Id, DefaultModel(s)); Close(); }
                if (anyPrices) GUILayout.Label($"<size=11>{price}</size>", Ui.Small, GUILayout.Width(110));
                GUILayout.EndHorizontal();
            }
            if (shown.Count > MaxRows) GUILayout.Label($"<size=11>...and {shown.Count - MaxRows} more. Type in the search box to narrow it down.</size>", Ui.Wrap);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            string defaultModel = DefaultModel(s);
            if (Ui.TipButton("Use the default model", $"Clear the model box so this seat uses the provider's default{(defaultModel != null ? $" ({defaultModel})" : "")}.", GUILayout.Width(170))) { s.ChooseModel("", defaultModel); Close(); }
            if (Ui.TipButton("Refresh", "Ask the provider for its model list again, for new models or changed prices.", GUILayout.Width(80))) lists.Remove(cacheKey);
            GUILayout.FlexibleSpace();
            if (Ui.TipButton("Close", "Close the list. You can still type a model id by hand.", GUILayout.Width(70))) Close();
            GUILayout.EndHorizontal();
        }

        // The seat kind's default model id, or null.
        static string DefaultModel(SeatSetup s) => Seats.Kinds.TryGetValue(s.NormalizedKind(), out var k) ? k.Model : null;

        // Every word typed must appear in the id or the name, in any order. "llama free" finds free Llama models.
        static bool Matches(ModelInfo m, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string hay = (m.Id + " " + m.Name + (m.Free ? " free" : "")).ToLowerInvariant();
            return text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(hay.Contains);
        }
    }
}
