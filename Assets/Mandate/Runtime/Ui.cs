// Ui — the shared IMGUI frame: where the panels go and how they look.
//
// Layout is designed on a virtual canvas 1000 points tall and scaled to the real window, so it reads the same on
// a laptop, a 4K monitor, or a WebGL canvas with a high device pixel ratio. Every panel size below is in virtual
// points; Scale converts to real pixels. MandateGameView.OnGUI applies the scale once (GUI.matrix), after which
// every panel simply draws in points. Styles are built on first use inside OnGUI because GUI.skin is only valid there.

using System.Collections.Generic;
using UnityEngine;

namespace Mandate.Viewer
{
    public static class Ui
    {
        // ------------------------------------------------------------------ layout (virtual points)
        public const float DesignH = 1000f;    // the virtual canvas is always this tall
        public const float LeftW = 330f;       // players panel
        public const float RightW = 410f;      // log panel
        public const float TopH = 74f;         // top bar
        public const float HumanH = 230f;      // your controls at the bottom when you hold P1

        /// <summary>Real pixels per virtual point.</summary>
        public static float Scale => Mathf.Clamp(Screen.height / DesignH, 0.6f, 4f);

        /// <summary>Virtual canvas width, in points.</summary>
        public static float CanvasW => Screen.width / Scale;

        /// <summary>Virtual canvas height, in points.</summary>
        public static float CanvasH => Screen.height / Scale;

        /// <summary>Height of the final scoreboard: a header plus one row per player.</summary>
        public static float ScoreboardH(int players) => 96f + 26f * players;

        // ------------------------------------------------------------------ styles
        public static GUIStyle Panel, Card, Title, Label, Small, Wrap, LogLine, Button, Toggle, NodeId, NodeSmall, Swatch, Field;

        /// <summary>Builds the styles once. Call at the top of OnGUI before drawing anything.</summary>
        public static void EnsureStyles()
        {
            // Rebuild if never built, or if Unity's unused-asset sweep destroyed the generated background textures.
            if (Panel != null && Panel.normal.background != null) return;

            var panelBg = SolidTexture(new Color(0.06f, 0.07f, 0.09f, 0.92f));
            var cardBg = SolidTexture(new Color(0.14f, 0.15f, 0.19f, 0.95f));

            Panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
            Panel.normal.background = panelBg;
            Card = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(0, 0, 4, 4) };
            Card.normal.background = cardBg;

            Title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, richText = true, alignment = TextAnchor.MiddleLeft };
            Label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, alignment = TextAnchor.MiddleLeft };
            Small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, alignment = TextAnchor.MiddleLeft };
            Wrap = new GUIStyle(Small) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            LogLine = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true, alignment = TextAnchor.UpperLeft, margin = new RectOffset(0, 0, 1, 1) };
            Button = new GUIStyle(GUI.skin.button) { fontSize = 13, richText = true };
            Toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 13, richText = true };
            Field = new GUIStyle(GUI.skin.textField) { fontSize = 13 };
            NodeId = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            NodeSmall = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            Swatch = new GUIStyle(GUI.skin.box) { margin = new RectOffset(0, 6, 4, 0) };
            Swatch.normal.background = SolidTexture(Color.white);

            foreach (var s in new[] { Title, Label, Small, Wrap, LogLine, NodeSmall })
                s.normal.textColor = new Color(0.92f, 0.92f, 0.94f);
            NodeId.normal.textColor = Color.white;
        }

        // ------------------------------------------------------------------ tooltips
        // The game tracks hover itself rather than using Unity's GUI.tooltip, which in the web build kept showing the
        // last tooltip after the mouse had moved away. Every repaint starts with no tip, Tip() sets it when the mouse
        // is over the control just drawn, and CardIcons.DrawTooltip draws it last, on top of every panel.

        /// <summary>The hover text for this repaint, or "" when the mouse is over nothing with a tooltip.</summary>
        public static string HoverTip = "";

        // One entry per scroll view being drawn: is the mouse over its visible part? Rows scrolled out of sight
        // still have rects, so without this they could claim the mouse while it is over something above or below.
        static readonly Stack<bool> mouseInScroll = new Stack<bool>();

        /// <summary>Call at the top of OnGUI, before anything is drawn: every repaint starts with no tip.</summary>
        public static void BeginTips()
        {
            mouseInScroll.Clear();
            if (Event.current.type == EventType.Repaint) HoverTip = "";
        }

        /// <summary>Gives the control just drawn a hover tooltip. Call it straight after the control.</summary>
        public static void Tip(string tooltip)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(tooltip)) return;   // the last rect is only real while repainting
            if (mouseInScroll.Contains(false)) return;
            if (GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) HoverTip = tooltip;
        }

        /// <summary>A button with a hover tooltip.</summary>
        public static bool TipButton(string text, string tooltip, params GUILayoutOption[] options)
        {
            bool clicked = GUILayout.Button(text, Button, options);
            Tip(tooltip);
            return clicked;
        }

        /// <summary>A toggle with a hover tooltip.</summary>
        public static bool TipToggle(bool value, string text, string tooltip, params GUILayoutOption[] options)
        {
            bool result = GUILayout.Toggle(value, text, Toggle, options);
            Tip(tooltip);
            return result;
        }

        /// <summary>A label with a hover tooltip.</summary>
        public static void TipLabel(string text, string tooltip, GUIStyle style, params GUILayoutOption[] options)
        {
            GUILayout.Label(text, style, options);
            Tip(tooltip);
        }

        /// <summary>Hover tooltips for the cells of the GUILayout.SelectionGrid just drawn, one per cell.</summary>
        public static void GridTips(string[] tooltips, int xCount)
        {
            if (Event.current.type != EventType.Repaint || mouseInScroll.Contains(false)) return;
            var r = GUILayoutUtility.GetLastRect();
            var m = Event.current.mousePosition;
            if (!r.Contains(m)) return;
            int rows = (tooltips.Length + xCount - 1) / xCount;
            int col = Mathf.Min(xCount - 1, (int)((m.x - r.x) / r.width * xCount));
            int row = Mathf.Min(rows - 1, (int)((m.y - r.y) / r.height * rows));
            int i = row * xCount + col;
            if (i < tooltips.Length && !string.IsNullOrEmpty(tooltips[i])) HoverTip = tooltips[i];
        }

        /// <summary>GUILayout.BeginScrollView that keeps hover tips to the part you can see. Pass a Rect field kept
        /// for this scroll view; EndScroll fills it in.</summary>
        public static Vector2 BeginScroll(Vector2 scroll, Rect lastRect)
        {
            mouseInScroll.Push(lastRect.Contains(Event.current.mousePosition));   // still in the panel's coordinates here
            return GUILayout.BeginScrollView(scroll);
        }

        /// <summary>Ends a scroll view started with BeginScroll, and remembers where it sat on screen.</summary>
        public static void EndScroll(ref Rect lastRect)
        {
            GUILayout.EndScrollView();
            if (mouseInScroll.Count > 0) mouseInScroll.Pop();
            if (Event.current.type == EventType.Repaint) lastRect = GUILayoutUtility.GetLastRect();
        }

        // A 1x1 texture of one colour, used as a panel background. HideAndDontSave keeps it out of the scene file.
        static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
