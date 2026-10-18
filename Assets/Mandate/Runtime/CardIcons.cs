// CardIcons — a small coloured badge for every objective card, and a hover tooltip saying what the card means.
//
// Cards are grouped by what they ask of you, and each group has a colour and a one- or two-letter code:
//   P  gold    production         N  green   nodes            H  blue    the hub and gap nodes
//   W  grey    walls              L/R/LR purple  your neighbours (which side)   !  red  how you play
// Hovering a badge, or a card name drawn with Name(), shows the card's full text plus a line about its group.
// Call DrawTooltip() once at the end of OnGUI so the tooltip sits on top of every panel. It also shows the
// tooltips of the top bar and Table screen controls.
// These are placeholder visuals: swap Badge() for sprites when real card art exists.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mandate.Core;

namespace Mandate.Viewer
{
    public static class CardIcons
    {
        sealed class Group
        {
            public string Code;      // the letters on the badge
            public Color Colour;     // the badge background
            public string Hint;      // second line of the tooltip
        }

        static readonly Group Production = new Group { Code = "P", Colour = new Color(0.93f, 0.74f, 0.25f), Hint = "Production: the sum of the production on the nodes a player owns. Ties share a place." };
        static readonly Group Nodes = new Group { Code = "N", Colour = new Color(0.45f, 0.80f, 0.45f), Hint = "Nodes: how many nodes players own at the end." };
        static readonly Group Places = new Group { Code = "H", Colour = new Color(0.40f, 0.65f, 0.95f), Hint = "Places: holding particular nodes. Node 0 is the hub; gap nodes sit between neighbouring seats." };
        static readonly Group Walls = new Group { Code = "W", Colour = new Color(0.70f, 0.70f, 0.74f), Hint = "Walls: fortification on the board at the end." };
        static readonly Group Conduct = new Group { Code = "!", Colour = new Color(0.92f, 0.45f, 0.42f), Hint = "Conduct: what you do (or never do) during the game, plus a check at the end." };
        static readonly Group Unknown = new Group { Code = "?", Colour = Color.gray, Hint = "" };

        static Group Neighbour(string code) => new Group
        {
            Code = code, Colour = new Color(0.72f, 0.55f, 0.92f),
            Hint = "Neighbours: about where your neighbour finishes. Left = the next seat, right = the previous seat.",
        };

        // Which group each card is in. A card missing here still works; it just gets a grey "?" badge.
        static readonly Dictionary<string, Group> groupOf = new Dictionary<string, Group>
        {
            ["most_production"] = Production, ["production_eight"] = Production, ["top_two"] = Production,
            ["most_nodes"] = Nodes, ["four_nodes"] = Nodes, ["contiguous_four"] = Nodes, ["balance"] = Nodes, ["spread"] = Nodes,
            ["hold_center"] = Places, ["quiet_hub"] = Places, ["hold_left_gap"] = Places, ["hold_right_gap"] = Places,
            ["hold_both_gaps"] = Places, ["two_gaps"] = Places,
            ["fortress"] = Walls, ["no_walls"] = Walls,
            ["left_second"] = Neighbour("L"), ["left_wins"] = Neighbour("L"), ["left_last"] = Neighbour("L"),
            ["right_last"] = Neighbour("R"), ["right_second"] = Neighbour("R"), ["beat_right"] = Neighbour("R"),
            ["neighbours_below"] = Neighbour("LR"),
            ["never_lose"] = Conduct, ["pacifist"] = Conduct, ["trader"] = Conduct, ["saboteur"] = Conduct,
        };

        static Dictionary<string, string> textOf;   // card id -> card text, from the rules engine
        static GUIStyle badgeStyle, tipStyle;

        /// <summary>The tooltip for a card: its id, its full text, and what its group is about.</summary>
        public static string Tooltip(string id)
        {
            if (textOf == null) textOf = Objectives.Pool().ToDictionary(o => o.Id, o => o.Text);
            var g = GroupOf(id);
            string text = textOf.TryGetValue(id, out var t) ? t : "";
            return g.Hint.Length > 0 ? $"{id}\n{text}\n\n{g.Hint}" : $"{id}\n{text}";
        }

        /// <summary>Draws the card's badge in the current GUILayout row. Hover shows the tooltip.</summary>
        public static void Badge(string id)
        {
            EnsureStyles();
            var g = GroupOf(id);
            var old = GUI.backgroundColor;
            GUI.backgroundColor = g.Colour;
            Ui.TipLabel(g.Code, Tooltip(id), badgeStyle, GUILayout.Width(24), GUILayout.Height(18));
            GUI.backgroundColor = old;
        }

        /// <summary>Badge plus the card id in bold, in the current GUILayout row. Both show the tooltip on hover.</summary>
        public static void Name(string id, GUIStyle style)
        {
            Badge(id);
            Ui.TipLabel($"<b>{id}</b>", Tooltip(id), style, GUILayout.ExpandWidth(false));
        }

        /// <summary>A row of badges with names, wrapped to the given width. Used for "in play this game".</summary>
        public static void NameRows(IEnumerable<string> ids, float width, GUIStyle style)
        {
            float used = 0f;
            GUILayout.BeginHorizontal();
            foreach (var id in ids)
            {
                float w = 30f + style.CalcSize(new GUIContent($"<b>{id}</b>")).x + 8f;
                if (used > 0f && used + w > width) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); used = 0f; }
                Name(id, style);
                GUILayout.Space(6);
                used += w;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>Draws whatever tooltip the mouse is over (Ui.HoverTip), beside the mouse. Call last in OnGUI, outside any area.</summary>
        public static void DrawTooltip()
        {
            if (string.IsNullOrEmpty(Ui.HoverTip)) return;
            EnsureStyles();
            var content = new GUIContent(Ui.HoverTip);
            float w = 300f, h = tipStyle.CalcHeight(content, w);
            var m = Event.current.mousePosition;
            float x = Mathf.Min(m.x + 16f, Ui.CanvasW - w - 4f), y = Mathf.Min(m.y + 16f, Ui.CanvasH - h - 4f);
            GUI.Label(new Rect(x, y, w, h), content, tipStyle);
        }

        static Group GroupOf(string id) => id != null && groupOf.TryGetValue(id, out var g) ? g : Unknown;

        static void EnsureStyles()
        {
            if (badgeStyle != null && badgeStyle.normal.background != null) return;
            var white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            white.SetPixel(0, 0, Color.white); white.Apply();
            badgeStyle = new GUIStyle(GUI.skin.box) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 4, 2, 2) };
            badgeStyle.normal.background = white;
            badgeStyle.normal.textColor = new Color(0.08f, 0.08f, 0.1f);

            var dark = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            dark.SetPixel(0, 0, new Color(0.02f, 0.02f, 0.03f, 0.96f)); dark.Apply();
            tipStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 6, 6) };
            tipStyle.normal.background = dark;
            tipStyle.normal.textColor = new Color(0.95f, 0.95f, 0.97f);
        }
    }
}
