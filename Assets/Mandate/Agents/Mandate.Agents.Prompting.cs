// Mandate.Agents.Prompting — what a language model is told. Provider-independent text; every adapter sends the same.
//
// The system prompt is identical for the whole game (rules, scoring, response format, persona), so providers that
// cache prompt prefixes get hits on every phase. The user prompt is the current PlayerView rendered as text plus the
// seat's own notes from earlier phases. Nothing the model is told goes beyond what PlayerView already exposes.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mandate.Core;

namespace Mandate.Agents
{
    public static class Prompting
    {
        public static string SystemPrompt(PlayerView v, SeatConfig cfg)
        {
            var sb = new StringBuilder();
            int ringNodes = 3 * v.Players.Count;
            string gaps = string.Join(", ", Enumerable.Range(1, v.Players.Count).Select(i => 3 * i));
            sb.AppendLine($"You are {cfg.Label()}, seat {v.You}, in Mandate, a negotiation game between AI models (and sometimes a human). Every player has a secret objective card. You score by achieving your objective, by guessing the other players' objectives, and by keeping yours hidden.");
            sb.AppendLine();
            sb.AppendLine("THE MAP");
            sb.AppendLine($"- Ring nodes 1 to {ringNodes} form a circle. Each ring node is next to the ring node on either side of it, and node {ringNodes} is next to node 1.");
            sb.AppendLine("- Node 0 is the HUB, in the centre of the ring. It is worth 3 production and is next to the gap nodes only.");
            sb.AppendLine($"- GAP NODES are ring nodes {gaps}. Each one sits between two neighbouring seats and starts neutral. The SEATS table in every prompt says which gap nodes are yours to think about.");
            sb.AppendLine($"- Each seat starts owning 2 ring nodes, worth {Rules.StartingProduction} production together. All other nodes start neutral (owned by nobody).");
            sb.AppendLine($"- Every node has PRODUCTION (1-{Rules.ProductionCap}: what it is worth to its owner) and FORT (short for fortification, 0-{Rules.FortifyCap}: its walls). The BOARD lists every node with its owner, production, fort and the nodes next to it (\"adj\").");
            sb.AppendLine();
            sb.AppendLine("ROUNDS");
            sb.AppendLine($"- The game lasts {Rules.MinRounds}, {Rules.MaxRounds - 1} or {Rules.MaxRounds} rounds, picked in secret before the start, each equally likely. Nobody is told which. You only find out the game is over when a round ends and no new round starts. Round {Rules.MaxRounds} is always the last. From round {Rules.MinRounds} on, any round may be the last one.");
            sb.AppendLine("- Each round has two phases: NEGOTIATE, then ACT.");
            sb.AppendLine($"- NEGOTIATE: send up to {Rules.MaxPublicMessages} public and {Rules.MaxPrivateMessages} private messages, at most {Rules.MaxMessageChars} characters each. Nothing you say is binding. You may also make trade offers, which ARE binding (see TRADES).");
            sb.AppendLine("- ACT: everyone secretly picks ONE action at the same time, and may also accept trade offers made to them. Then everything happens in this order: 1. trades, 2. moves, 3. releases, 4. fortify and develop, 5. sabotage.");
            sb.AppendLine("- When the round ends, every action and every completed trade is shown to everyone. Trade offers stay private between the two players. Objective cards stay secret until the game is over.");
            sb.AppendLine();
            sb.AppendLine("ACTIONS (one per round)");
            sb.AppendLine("- pass: do nothing.");
            sb.AppendLine("- move X->Y: X is your node and Y is next to it. If Y is neutral, you take it. If Y is owned by someone, you take it only if X's fort is HIGHER than Y's fort; a tie goes to the defender. If two players move into the same node with equal fort, both fail. A captured node drops to fort 0. You keep X: a move never gives up the node you move from.");
            sb.AppendLine($"- fortify X: your node X gets +1 fort (max {Rules.FortifyCap}). Fort is the node's wall, and also its attack strength when you move out of it.");
            sb.AppendLine($"- develop X: your node X gets +1 production (max {Rules.ProductionCap}). The production stays with the node, even if someone takes it later.");
            sb.AppendLine("- sabotage Y: Y is an enemy node next to one of your nodes.");
            sb.AppendLine("    If Y has fort 1 or more: Y loses 1 fort and STAYS THEIRS.");
            sb.AppendLine("    If Y already has fort 0: Y becomes neutral. They lose it, and you do not gain it.");
            sb.AppendLine("    You cannot sabotage the node you sabotaged last round.");
            sb.AppendLine("- release X: your node X becomes neutral, with fort 0 and the same production. Nobody can move into it that round; from the next round anyone next to it can take it. If X is captured that same round, the release does nothing.");
            sb.AppendLine();
            sb.AppendLine("TIMING THAT MATTERS");
            sb.AppendLine("- Moves happen BEFORE fortify. Walls built this round do not stop a capture this round; only walls already on the node count.");
            sb.AppendLine("- Fortify happens BEFORE sabotage. Fortifying a fort-0 node this round stops a sabotage from making it neutral this round.");
            sb.AppendLine($"- A node at fort {Rules.FortifyCap} cannot be captured by any move (attack strength also tops out at {Rules.FortifyCap} and ties go to the defender). It has to be sabotaged down first.");
            sb.AppendLine("- An owned node only becomes neutral two ways: its owner releases it, or it is sabotaged while at fort 0.");
            sb.AppendLine();
            sb.AppendLine("TRADES (free: not an action)");
            sb.AppendLine("- In NEGOTIATE you may offer a player a swap: one of your nodes for one of theirs. At most one offer to each player. Only owned nodes can be traded.");
            sb.AppendLine("- An offer is BINDING the moment you make it: you cannot take it back. An offer that breaks these rules is rejected at once, and your act prompt says why.");
            sb.AppendLine("- In ACT the other player sees your offer and may accept it. Accepting costs nothing: both players still take their normal action. An accepted swap happens first, before moves. Walls stay with the node.");
            sb.AppendLine("- If one node is in two accepted offers, only the offer made first goes through.");
            sb.AppendLine();
            sb.AppendLine("END OF GAME AND SCORING");
            sb.AppendLine("- After the last round everyone guesses each opponent's objective, from the GUESS CANDIDATES list (the cards dealt this game, minus the ones you were offered). Each card belongs to exactly one player, so never use one id twice.");
            sb.AppendLine($"- +{Rules.ObjectivePoints} if your objective is met at the end, +{Rules.ReadPoints} for each opponent you guess right, -{Rules.GotReadPenalty} for each opponent who guesses yours.");
            sb.AppendLine();
            sb.AppendLine("WORDS THE OBJECTIVE CARDS USE");
            sb.AppendLine("- production: the total production of the nodes a player owns.");
            sb.AppendLine("- places: by production, highest first. Ties are shared, never broken. Two players tied for the most are both first, and then nobody is second. Everyone tied for the least is last.");
            sb.AppendLine("- \"more than\" / \"ahead of\": strictly more.");
            sb.AppendLine("- left neighbour / right neighbour, left gap / right gap: given for every seat in the SEATS table. (Left neighbour = the next seat in seat order, right neighbour = the previous one, wrapping round.)");
            sb.AppendLine("- lose a node (never_lose): an enemy captured it, or sabotaged it to neutral. Trading a node away or releasing your own node is NOT a loss.");
            sb.AppendLine("- capture (pacifist): taking someone's owned node with a move. Sabotage and trades are not captures.");
            sb.AppendLine("- Every card is checked once, after the last round, unless its text says \"during the game\".");
            sb.AppendLine();
            sb.AppendLine("HOW TO PLAY WELL");
            sb.AppendLine("- Work out what your objective needs, who can help, who is in the way, and talk to them. Offer deals, make threats, promise things. Nothing is enforced, so decide for yourself what you will honour.");
            sb.AppendLine("- Read the table. Actions and messages reveal objectives. Keep a theory of every opponent in your notes and update it each round.");
            sb.AppendLine("- Never state your objective. Misdirect. Being read costs points: a player who wins the map but was obvious about it can lose to a quieter one.");
            sb.AppendLine("- Private messages you send are not echoed back to you, so write down what you promised and to whom.");
            sb.AppendLine("- Play to the end. The guessing phase can swing the result.");
            sb.AppendLine();
            sb.AppendLine("RESPONSE FORMAT");
            sb.AppendLine("Reply with ONE JSON object and nothing else: no markdown fences, no text before or after. Fields:");
            sb.AppendLine("  \"notes\": string. Your private scratchpad. It is shown back to you next phase and to nobody else. Keep your plan, deals made, and reads on each player. Rewrite it in full each time (max 3000 characters).");
            sb.AppendLine("  Draft phase:     \"draft\": \"<objective id from your draft options>\"");
            sb.AppendLine("  Negotiate phase: \"messages\": [{\"to\": \"P2\", \"text\": \"...\"}, {\"text\": \"...\"}]   (no \"to\" = public; an empty list is allowed)");
            sb.AppendLine("                   \"offers\": [{\"partner\": \"P2\", \"give\": 12, \"get\": 5}]   (optional trade offers, at most one per partner: you give node 12, P2 gives node 5. BINDING: if P2 accepts in the act phase, the swap happens)");
            sb.AppendLine("                   \"beliefs\": {\"objectiveBeliefs\": {\"P2\": \"<objective id>\"}, \"theyThinkIAm\": {\"P2\": \"<objective id>\"}, \"note\": \"...\"}   (optional; never scored; used for post-game analysis. One objective id per opponent, or {\"<id>\": 0.6, \"<other id>\": 0.4} if you are torn)");
            sb.AppendLine("  Act phase:       \"action\": one entry copied exactly from the LEGAL ACTIONS list, e.g. {\"type\":\"move\",\"from\":2,\"to\":3} or {\"type\":\"fortify\",\"node\":2} or {\"type\":\"develop\",\"node\":2} or {\"type\":\"sabotage\",\"node\":4} or {\"type\":\"release\",\"node\":2} or {\"type\":\"pass\"}");
            sb.AppendLine("                   \"accept\": [\"P2\"]   (optional: the players whose trade offers to you this round you accept. Free: you still take your action)");
            sb.AppendLine("  Guess phase:     \"guesses\": {\"P2\": \"<objective id>\", \"P3\": \"<objective id>\", ...}   one per opponent, each from the guess candidates, no id used twice");
            sb.AppendLine("Only the fields for the CURRENT phase are read. Messages in an act reply, or an action in a negotiate reply, are thrown away.");
            sb.AppendLine("A missing or invalid answer counts as: default draft pick / no messages / pass / no guesses.");
            sb.AppendLine("Every answer has a time limit, and a late answer is a missing answer. Think briefly and decide; do not re-check every option.");
            if (!string.IsNullOrWhiteSpace(cfg.Persona))
            {
                sb.AppendLine();
                sb.AppendLine("YOUR PERSONA");
                sb.AppendLine(cfg.Persona.Trim());
            }
            return sb.ToString();
        }

        public static string UserPrompt(PlayerView v, string notes)
        {
            var d = Wire.ToDto(v);
            var sb = new StringBuilder();
            sb.AppendLine($"=== ROUND {v.Round} (the game ends after round {Rules.MinRounds}, {Rules.MaxRounds - 1} or {Rules.MaxRounds}; nobody knows which) - {d.Phase.ToUpperInvariant()} PHASE ===");
            int myLeftGap = Objectives.LeftGap(v.Players, v.You), myRightGap = Objectives.RightGap(v.Players, v.You);
            sb.AppendLine($"You are {v.You}. Seat order: {string.Join(", ", v.Players)}.");
            sb.AppendLine($"Your left neighbour is {d.Left} and your left gap is node {myLeftGap} (between you and {d.Left}). Your right neighbour is {d.Right} and your right gap is node {myRightGap} (between you and {d.Right}).");
            sb.AppendLine("SEATS (every seat's neighbours and gap nodes)");
            foreach (var p in v.Players)
            {
                int i = v.Players.IndexOf(p);
                string left = v.Players[(i + 1) % v.Players.Count], right = v.Players[(i - 1 + v.Players.Count) % v.Players.Count];
                sb.AppendLine($"  {p}{(p == v.You ? " (you)" : "")}: left neighbour {left}, right neighbour {right}, left gap node {Objectives.LeftGap(v.Players, p)}, right gap node {Objectives.RightGap(v.Players, p)}");
            }
            if (v.Phase == Phase.Draft) sb.AppendLine("You have no objective yet: choose one from YOUR DRAFT OPTIONS below.");
            else
            {
                sb.AppendLine($"YOUR SECRET OBJECTIVE: {v.ObjectiveId} - {v.ObjectiveText}");
                // Cards say "left"/"right"; name the actual seat and node so nobody has to work it out.
                var forYou = new List<string>();
                if (v.ObjectiveText.Contains("left")) forYou.Add($"your left neighbour is {d.Left}, your left gap is node {myLeftGap}");
                if (v.ObjectiveText.Contains("right")) forYou.Add($"your right neighbour is {d.Right}, your right gap is node {myRightGap}");
                if (forYou.Count > 0) sb.AppendLine($"  For you: {string.Join("; ", forYou)}.");
            }
            // The referee checks the card for you, so a seat never thinks it has made its objective when it has not.
            if (v.ObjectiveMetNow == true) sb.AppendLine("YOUR OBJECTIVE RIGHT NOW: MET - it would score if the game ended now. Only the board at the end of the last round counts, so keep it.");
            if (v.ObjectiveMetNow == false) sb.AppendLine("YOUR OBJECTIVE RIGHT NOW: NOT MET - it would not score if the game ended now. Only the board at the end of the last round counts.");
            if (v.Phase == Phase.Negotiate || v.Phase == Phase.Act)
            {
                if (v.Round >= Rules.MaxRounds)
                    sb.AppendLine($"FINAL ROUND: round {Rules.MaxRounds} is always the last. Nothing anyone does this round can be answered afterwards.");
                else if (v.Round >= Rules.MinRounds)
                    sb.AppendLine($"THIS MAY BE THE LAST ROUND: the game ends after round {Rules.MinRounds}, {Rules.MaxRounds - 1} or {Rules.MaxRounds}. If it goes on, whatever you do now can be answered next round.");
            }
            sb.AppendLine();

            sb.AppendLine("BOARD (node: owner, production, fort, adj = the nodes next to it)");
            foreach (var n in v.Board)
                sb.AppendLine($"  {n.Id}: {n.Owner ?? "neutral"}, prod {n.Production}, fort {n.Fortify}, adj [{string.Join(",", n.Adjacent)}]{Marker(v, n.Id)}");
            sb.AppendLine("STANDINGS (production / nodes / total fort)");
            foreach (var p in v.Players)
            {
                var mine = v.Board.Where(n => n.Owner == p).ToList();
                sb.AppendLine($"  {p}{(p == v.You ? " (you)" : "")}: {mine.Sum(n => n.Production)} / {mine.Count} / {mine.Sum(n => n.Fortify)}   nodes [{string.Join(",", mine.Select(n => n.Id))}]");
            }
            sb.AppendLine($"PUBLIC RECORD: trades completed: {Counts(v.TradesCompleted)}; lost a node to capture: {Names(v.LostNodeEver)}; captured an owned node: {Names(v.CapturedOwnedEver)}");
            // The referee's own reading of the board, so a seat never attacks on a tie or misses a threat.
            if (v.MovePreviews != null && v.MovePreviews.Count > 0)
            {
                sb.AppendLine("WHAT YOUR MOVES AND SABOTAGES WOULD DO (worked out by the referee from the board above; everyone acts at once, so others can still change the result)");
                foreach (var line in v.MovePreviews) sb.AppendLine("  " + line);
            }
            if (v.Phase == Phase.Negotiate || v.Phase == Phase.Act)
            {
                sb.AppendLine("DANGER TO YOUR NODES THIS ROUND (every capture or sabotage your opponents could make, worked out by the referee)");
                if (v.Dangers == null || v.Dangers.Count == 0) sb.AppendLine("  none: no opponent can capture or sabotage any of your nodes this round");
                else foreach (var line in v.Dangers) sb.AppendLine("  " + line);
                sb.AppendLine("The BOARD, these referee lists and the [notes] in ACTION HISTORY are the truth. Chat and your own earlier notes can be wrong: when they disagree, trust the board.");
            }
            sb.AppendLine();

            sb.AppendLine("OBJECTIVES IN PLAY THIS GAME (public; each card is held by exactly one player)");
            foreach (var o in v.DealtObjectives) sb.AppendLine($"  {o.id} - {o.text}");
            if (v.Phase == Phase.Draft)
            {
                sb.AppendLine("YOUR DRAFT OPTIONS (keep one; the others go back and nobody learns you saw them)");
                foreach (var o in v.DraftOptions) sb.AppendLine($"  {o.id} - {o.text}");
            }
            if (v.Phase == Phase.Guess)
            {
                sb.AppendLine("GUESS CANDIDATES (what your opponents can hold)");
                sb.AppendLine("  " + string.Join(", ", v.GuessCandidates.Select(o => o.id)));
            }
            sb.AppendLine();

            sb.AppendLine("PUBLIC CHAT");
            if (v.PublicLog.Count == 0) sb.AppendLine("  (nothing yet)");
            foreach (var m in v.PublicLog) sb.AppendLine($"  R{m.Round} {m.From}{(m.From == v.You ? " (you)" : "")}: {m.Text}");
            sb.AppendLine("PRIVATE MESSAGES TO YOU");
            if (v.PrivateInbox.Count == 0) sb.AppendLine("  (none)");
            foreach (var m in v.PrivateInbox) sb.AppendLine($"  R{m.Round} from {m.From}: {m.Text}");
            sb.AppendLine("ACTION HISTORY (public after each round; [notes] are the referee's record of what each action actually did)");
            if (v.ActionsHistory.Count == 0) sb.AppendLine("  (no rounds resolved yet)");
            foreach (var g in v.ActionsHistory.GroupBy(a => a.round))
                sb.AppendLine($"  R{g.Key}: " + string.Join(", ", g.Select(a => $"{a.player} {a.action}{(a.note == null ? "" : $" [{a.note}]")}")));
            sb.AppendLine("TRADE HISTORY (every completed trade, plus what became of your own offers and of offers made to you)");
            if (v.TradeHistory == null || v.TradeHistory.Count == 0) sb.AppendLine("  (none yet)");
            else foreach (var o in v.TradeHistory) sb.AppendLine($"  R{o.Round} {o.From} offered {o.To}: {o.From} gives node {o.Give}, {o.To} gives node {o.Get} [{o.Outcome}]");
            sb.AppendLine("YOUR PRIVATE NOTES FROM EARLIER");
            sb.AppendLine(string.IsNullOrWhiteSpace(notes) ? "  (none yet)" : "  " + notes.Trim().Replace("\n", "\n  "));

            if (v.Phase == Phase.Act && v.Offers != null && v.Offers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("TRADE OFFERS THIS ROUND (binding; accepting is free and does not use your action)");
                foreach (var o in v.Offers.Where(o => o.To == v.You))
                    sb.AppendLine($"  {o.From} offers you: you get node {o.Give} (prod {v.Board[o.Give].Production}, fort {v.Board[o.Give].Fortify}), you give node {o.Get} (prod {v.Board[o.Get].Production}, fort {v.Board[o.Get].Fortify}). To accept, add \"accept\": [\"{o.From}\"] to your reply.");
                foreach (var o in v.Offers.Where(o => o.From == v.You))
                    sb.AppendLine(o.Problem != null
                        ? $"  Your offer to {o.To} (give {o.Give}, get {o.Get}) was REJECTED: {o.Problem}. It cannot happen."
                        : $"  You offered {o.To}: you give node {o.Give}, {o.To} gives node {o.Get}. It happens if {o.To} accepts; you need do nothing.");
            }

            if (v.Phase == Phase.Act)
            {
                sb.AppendLine();
                sb.AppendLine("LEGAL ACTIONS (copy exactly one into \"action\")");
                sb.AppendLine(Json.Serialize(d.LegalActions));
            }

            sb.AppendLine();
            sb.AppendLine("YOUR TASK NOW");
            switch (v.Phase)
            {
                case Phase.Draft:
                    sb.AppendLine("Choose the objective you can most plausibly achieve at this table while staying unreadable. Reply: {\"draft\": \"<id>\", \"notes\": \"...\"}");
                    break;
                case Phase.Negotiate:
                    sb.AppendLine($"Send up to {Rules.MaxPublicMessages} public and {Rules.MaxPrivateMessages} private messages (max {Rules.MaxMessageChars} characters each) that advance your objective: propose deals, mislead, probe. To propose a trade, add it to \"offers\": it is binding, and the partner can accept it for free. Then update your beliefs about each opponent. Reply: {{\"messages\": [...], \"offers\": [...], \"beliefs\": {{...}}, \"notes\": \"...\"}}");
                    break;
                case Phase.Act:
                    sb.AppendLine("Choose exactly one legal action, and accept any trade offers to you that you want (free). Reply: {\"action\": {...}, \"accept\": [...], \"notes\": \"...\"}");
                    break;
                case Phase.Guess:
                    sb.AppendLine("Guess every opponent's objective id from the candidates. Each card belongs to one player, so never repeat an id. Reply: {\"guesses\": {\"P2\": \"<id>\", ...}, \"notes\": \"...\"}");
                    break;
            }
            return sb.ToString();
        }

        // "<- hub" or "<- gap between P2 and P3" after a board line, so nobody has to work out which node is which gap.
        static string Marker(PlayerView v, int node)
        {
            if (node == 0) return "   <- hub";
            if (!Objectives.IsGap(node)) return "";
            var between = v.Players.Where(p => Objectives.LeftGap(v.Players, p) == node || Objectives.RightGap(v.Players, p) == node).ToList();
            return between.Count == 2 ? $"   <- gap between {between[0]} and {between[1]}" : "   <- gap";
        }

        static string Counts(Dictionary<string, int> d) =>
            d == null || d.Count == 0 ? "none" : string.Join(", ", d.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"));

        static string Names(HashSet<string> s) =>
            s == null || s.Count == 0 ? "nobody" : string.Join(", ", s.OrderBy(x => x));
    }
}
