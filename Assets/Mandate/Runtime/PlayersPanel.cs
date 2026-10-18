// PlayersPanel — one card per player down the left of the screen.
//
// Each card shows the seat's colour swatch, name and kind (and "thinking…" while an AI seat is answering),
// production / node count / fortification, the objective and whether it is met right now, the last action
// (illegal ones are flagged), and the score once the game is over. Below the cards: which objectives are in
// play this game, and a key to the board. Watching an all-AI game you see every card (the referee's view). When you
// play, you see only your own card until the game ends, like everyone else at the table. Hover a card for its meaning.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mandate.Viewer
{
    public sealed class PlayersPanel
    {
        readonly MandateGameView view;
        Vector2 scroll;

        public PlayersPanel(MandateGameView view) { this.view = view; }

        public void Draw()
        {
            var runner = view.Runner;
            var st = runner.State;
            var waiting = runner.PhasePending ? new HashSet<string>(runner.Waiting) : null;

            GUILayout.BeginArea(new Rect(0, Ui.TopH, Ui.LeftW, Ui.CanvasH - Ui.TopH), Ui.Panel);
            bool secret = view.HumanSeat != null && !runner.Finished;   // you are playing: other cards stay hidden until the end
            GUILayout.Label(secret ? "<b>Players</b>  <size=11>you see only your own objective until the end</size>"
                                   : "<b>Players</b>  <size=11>spectator view - every objective is visible</size>", Ui.Small);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var p in st.Players)
            {
                GUILayout.BeginVertical(Ui.Card);

                // Name row: swatch, name, kind. The numbers go on their own row below, so a long model name
                // can't squeeze them into wrapping over each other.
                GUILayout.BeginHorizontal();
                GUI.color = view.Colors.Of(p);
                GUILayout.Box("", Ui.Swatch, GUILayout.Width(14), GUILayout.Height(14));
                GUI.color = Color.white;
                string you = p == view.HumanSeat ? "  <color=#ffd27f><b>(you)</b></color>" : "";
                GUILayout.Label($"<b>{p}</b>{you}  <size=11>{runner.Seats[p].Kind}</size>", Ui.Label);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Numbers row: production / nodes / fortification, and the thinking marker on the right.
                GUILayout.BeginHorizontal();
                int prod = st.Nodes.Where(n => n.Owner == p).Sum(n => n.Production);
                int cnt = st.Nodes.Count(n => n.Owner == p);
                int fort = st.Nodes.Where(n => n.Owner == p).Sum(n => n.Fortify);
                GUILayout.Label($"prod <b>{prod}</b>  nodes <b>{cnt}</b>  fort <b>{fort}</b>", Ui.Small);
                GUILayout.FlexibleSpace();
                if (waiting != null && waiting.Contains(p))
                    GUILayout.Label("<color=#ffd27f>thinking...</color>", Ui.Small);
                GUILayout.EndHorizontal();

                // Objective, or the draft options while still drafting.
                if (secret && p != view.HumanSeat)
                    GUILayout.Label("<color=#9a9aa6><i>objective secret until the end</i></color>", Ui.Small);
                else if (st.Assigned.TryGetValue(p, out var obj))
                {
                    bool met = obj.Achieved(st, p);
                    GUILayout.BeginHorizontal();
                    CardIcons.Name(obj.Id, Ui.Small);
                    GUILayout.Label(met ? "<color=#7fdc7f>(met now)</color>" : "<color=#ffb37f>(not met)</color>", Ui.Small);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Label(obj.Text, Ui.Wrap);
                }
                else if (st.DraftOptions.TryGetValue(p, out var opts))
                    GUILayout.Label("<i>drafting:</i> " + string.Join("  |  ", opts.Select(o => o.Id)), Ui.Wrap);

                var last = st.ActionLog.LastOrDefault(a => a.player == p);
                if (last.action != null)
                    GUILayout.Label($"<size=11>last: round {last.round} {last.action}{(last.wasLegal ? "" : " <color=#ff6b6b>(illegal, became pass)</color>")}</size>", Ui.Small);
                if (runner.Scores != null)
                    GUILayout.Label($"<b>score {runner.Scores[p]}</b>", Ui.Label);

                GUILayout.EndVertical();
            }
            GUILayout.Space(6);
            GUILayout.Label("<size=11><b>In play this game</b> (public, what guesses are drawn from). Hover a card to see what it means.</size>", Ui.Wrap);
            CardIcons.NameRows(st.Dealt.Select(o => o.Id), Ui.LeftW - 40f, Ui.Small);
            GUILayout.Space(4);
            GUILayout.Label("<size=11>Board: colour = owner, number = node id, +n = production, white pips = fortification, white ring = changed hands last round, \"you\" = your nodes. Node 0 is the hub.</size>", Ui.Wrap);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
