// Mandate.Forecast — plain-language help for reading the board, so a seat never misjudges a wall count.
//
// AI seats kept misreading the board in live games: attacking with equal walls (ties go to the defender),
// forgetting that a sabotage cancelled their fortify, believing chat over the board. The referee already knows
// all of this, so it spells it out. Built into every PlayerView during negotiate and act:
//   Previews  what each of your moves and sabotages would do on the board as it stands now
//   Dangers   every way an opponent could capture or sabotage one of your nodes this round
// Both read the board at the start of the act phase. Everyone acts at once, so another player's action can still
// change the outcome; the lines say how. Order of resolution: trades, moves, releases, fortify and develop, sabotage.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Mandate.Core
{
    public static class Forecast
    {
        /// <summary>What each of p's possible moves and sabotages would do if nobody else interfered.
        /// Fortify, develop, release, trade and pass are left out: their effect is in the action itself.</summary>
        public static List<string> Previews(GameState s, string p, List<GameAction> legal)
        {
            var lines = new List<string>();
            foreach (var a in legal.Where(a => a.Type == ActionType.Move))
            {
                var from = s.Nodes[a.From];
                var to = s.Nodes[a.To];
                if (to.Owner == null)
                    lines.Add($"move({a.From}->{a.To}): takes neutral node {a.To} (prod {to.Production}), unless another player also moves into it with fort {from.Fortify} or more (equal movers cancel, the stronger one wins)");
                else if (from.Fortify > to.Fortify)
                    lines.Add($"move({a.From}->{a.To}): CAPTURES {to.Owner}'s node {a.To} (prod {to.Production}): your node {a.From} has fort {from.Fortify}, theirs has fort {to.Fortify}. Walls they build this round come too late to stop it. Fails only if another player attacks it with equal or more fort.");
                else
                    lines.Add($"move({a.From}->{a.To}): FAILS: your node {a.From} has fort {from.Fortify} and {to.Owner}'s node {a.To} has fort {to.Fortify}; you need MORE fort than the defender (a tie goes to the defender)");
            }
            foreach (var a in legal.Where(a => a.Type == ActionType.Sabotage))
            {
                var n = s.Nodes[a.To];
                lines.Add(n.Fortify > 0
                    ? $"sabotage({a.To}): {n.Owner}'s node {a.To} loses a wall, fort {n.Fortify} -> {n.Fortify - 1} (less if they fortify it this round: walls are built before sabotage)"
                    : $"sabotage({a.To}): {n.Owner}'s node {a.To} has no walls, so it becomes NEUTRAL; {n.Owner} can save it by fortifying it this round (walls are built before sabotage)");
            }
            return lines;
        }

        /// <summary>Every capture or sabotage an opponent could aim at one of p's nodes this round, from their own legal actions.</summary>
        public static List<string> Dangers(GameState s, string p, Func<string, List<GameAction>> legalFor)
        {
            var lines = new List<string>();
            foreach (var q in s.Players.Where(q => q != p))
            {
                foreach (var a in legalFor(q))
                {
                    if (a.Type == ActionType.Move && s.Nodes[a.To].Owner == p && s.Nodes[a.From].Fortify > s.Nodes[a.To].Fortify)
                        lines.Add($"{q} can CAPTURE your node {a.To} from their node {a.From} (their fort {s.Nodes[a.From].Fortify} > your fort {s.Nodes[a.To].Fortify}). Moves resolve before fortify, so only walls already on node {a.To} protect it this round.");
                    else if (a.Type == ActionType.Sabotage && s.Nodes[a.To].Owner == p)
                        lines.Add(s.Nodes[a.To].Fortify > 0
                            ? $"{q} can sabotage your node {a.To}: fort {s.Nodes[a.To].Fortify} -> {s.Nodes[a.To].Fortify - 1}"
                            : $"{q} can sabotage your node {a.To}, which has no walls, making it NEUTRAL; fortifying it this round saves it");
                }
            }
            return lines;
        }
    }
}
