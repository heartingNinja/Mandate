// Ui — the shared IMGUI frame: where the panels go and how they look.
//
// Layout is designed on a virtual canvas 1000 points tall and scaled to the real window, so it reads the same on
// a laptop, a 4K monitor, or a WebGL canvas with a high device pixel ratio. Every panel size below is in virtual
// points; Scale converts to real pixels. MandateGameView.OnGUI applies the scale once (GUI.matrix), after which
// every panel simply draws in points. Styles are built on first use inside OnGUI because GUI.skin is only valid there.

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
        /// <summary>Gives the control just drawn a hover tooltip. For sliders and text boxes, which cannot carry one
        /// themselves (buttons, toggles and labels take a GUIContent with the tooltip instead). It lays an invisible
        /// label over the control; labels ignore clicks, so the control underneath still works.</summary>
        public static void Tip(string tooltip)
        {
            if (Event.current.type != EventType.Repaint) return;   // the last rect is only real while repainting
            GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent("", tooltip), GUIStyle.none);
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
