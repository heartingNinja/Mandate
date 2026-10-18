// SeatColors — the colour of each seat, and the coloured name tag used in rich-text labels and log lines.
//
// Seats are named P1..Pn, and seat k uses palette[k - 1] (the playerColors list in the inspector). Nodes nobody
// owns use the neutral colour. Tag("P2") gives "<color=#...><b>P2</b></color>" so a name in the log matches the
// nodes on the board.

using UnityEngine;

namespace Mandate.Viewer
{
    public sealed class SeatColors
    {
        readonly Color[] palette;
        readonly Color neutral;

        public SeatColors(Color[] palette, Color neutral)
        {
            this.palette = palette;
            this.neutral = neutral;
        }

        /// <summary>Colour for a seat name like "P3". Null means no owner and gives the neutral colour.</summary>
        public Color Of(string player)
        {
            if (player == null) return neutral;
            int i = SeatIndex(player);
            return i >= 0 && i < palette.Length ? palette[i] : Color.magenta;
        }

        /// <summary>The seat name in bold, coloured like its nodes, for rich-text labels.</summary>
        public string Tag(string player) => $"<color=#{ColorUtility.ToHtmlStringRGB(Of(player))}><b>{player}</b></color>";

        // "P3" -> 2. Anything else -> -1.
        static int SeatIndex(string player) =>
            player.Length > 1 && int.TryParse(player.Substring(1), out int n) ? n - 1 : -1;
    }
}
