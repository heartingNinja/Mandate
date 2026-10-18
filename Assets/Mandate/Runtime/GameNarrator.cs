// GameNarrator — turns each finished phase into log lines.
//
// Call BeforePhase() with the state just before the runner completes a phase, and AfterPhase() just after. The
// narrator compares the two and writes what happened: draft picks, every public and private message, belief
// reports, every submitted action (and whether it was legal), every node that changed owner, fortification or production,
// the guesses, and the final scores. It remembers how much of the referee's logs it has already printed, so
// each line appears once. It also prints the AI seats' private notes, live (LogCurrentNotes) or at the end
// (RevealNotes).

using System.Collections.Generic;
using System.Linq;
using Mandate.Agents;
using Mandate.Core;
using Mandate.Sim;

namespace Mandate.Viewer
{
    public sealed class GameNarrator
    {
        readonly GameLog log;
        readonly SeatColors colors;

        int seenMessages, seenActions, seenBeliefs;              // how much of the referee's logs we've already printed
        readonly Dictionary<string, string> notesSeen = new Dictionary<string, string>();   // last notes printed per seat

        // Snapshot taken by BeforePhase, compared against the state in AfterPhase.
        Phase phase;
        int round;
        Dictionary<int, string> ownerBefore = new Dictionary<int, string>();
        Dictionary<int, int> fortBefore = new Dictionary<int, int>();
        Dictionary<int, int> prodBefore = new Dictionary<int, int>();

        /// <summary>The seat a person is playing, or null when everyone is an AI and you are only watching. While set,
        /// other seats' draft picks and belief reports stay out of the log, so you cannot read their cards from it.</summary>
        public string HumanSeat;

        public GameNarrator(GameLog log, SeatColors colors)
        {
            this.log = log;
            this.colors = colors;
        }

        /// <summary>Forget everything printed so far. Call when a new game starts.</summary>
        public void Reset()
        {
            seenMessages = seenActions = seenBeliefs = 0;
            notesSeen.Clear();
        }

        /// <summary>Remembers the phase, round and board just before the runner completes the phase.</summary>
        public void BeforePhase(GameState st)
        {
            phase = st.Phase;
            round = st.Round;
            ownerBefore = st.Nodes.ToDictionary(n => n.Id, n => n.Owner);
            fortBefore = st.Nodes.ToDictionary(n => n.Id, n => n.Fortify);
            prodBefore = st.Nodes.ToDictionary(n => n.Id, n => n.Production);
        }

        /// <summary>Writes what the completed phase did. Returns the nodes whose owner changed, for the board's white rings.</summary>
        public HashSet<int> AfterPhase(GameRunner runner)
        {
            var st = runner.State;
            var changed = new HashSet<int>();

            switch (phase)
            {
                case Phase.Draft:
                    log.Add("<b>Draft</b>");
                    foreach (var p in st.Players)
                        log.Add(HumanSeat == null || p == HumanSeat
                            ? $"{Tag(p)} keeps <i>{st.Assigned[p].Id}</i> - {st.Assigned[p].Text}"
                            : $"{Tag(p)} keeps a card <i>(secret until the end)</i>");
                    break;

                case Phase.Negotiate:
                    log.Add($"<b>Round {round} - negotiation</b>");
                    for (; seenMessages < st.Messages.Count; seenMessages++)
                    {
                        var m = st.Messages[seenMessages];
                        log.Add(m.To == null ? $"{Tag(m.From)} says: {m.Text}" : $"{Tag(m.From)} -> {Tag(m.To)} (private): {m.Text}");
                    }
                    // Offers are private between the two players, so while you play you only see your own.
                    foreach (var o in st.Offers.Where(o => o.Round == round && (HumanSeat == null || o.From == HumanSeat || o.To == HumanSeat)))
                        log.Add($"{Tag(o.From)} offers {Tag(o.To)} a trade (binding): gives {o.Give}, gets {o.Get}"
                            + (o.Problem == null ? "" : $" <color=#ff6b6b>(rejected: {o.Problem})</color>"));
                    for (; seenBeliefs < st.Beliefs.Count; seenBeliefs++)
                    {
                        var b = st.Beliefs[seenBeliefs];
                        if (b.ObjectiveBeliefs.Count == 0 || HumanSeat != null) continue;   // an AI's reads are its own business while you play
                        var reads = b.ObjectiveBeliefs.Where(kv => kv.Value.Count > 0)
                            .Select(kv => $"{kv.Key}={kv.Value.OrderByDescending(x => x.Value).First().Key}");
                        log.Add($"<size=11>{Tag(b.Player)} believes: {string.Join(", ", reads)}</size>");
                    }
                    break;

                case Phase.Act:
                    // Trades resolve first and cost no action, so they get their own heading above the actions. A
                    // completed one is public; one that came to nothing only matters to its two players.
                    var tradeLines = new List<string>();
                    foreach (var o in st.Offers.Where(o => o.Round == round && o.Problem == null))
                    {
                        if (o.Outcome == "done")
                            tradeLines.Add($"{Tag(o.From)} and {Tag(o.To)} trade: {o.From} gives node {o.Give}, {o.To} gives node {o.Get} <color=#7fdc7f>(done)</color>");
                        else if (HumanSeat == null || o.From == HumanSeat || o.To == HumanSeat)
                            tradeLines.Add($"<size=11>{Tag(o.From)}'s offer to {Tag(o.To)} (gives {o.Give}, gets {o.Get}): <color=#ffb37f>{o.Outcome}</color></size>");
                    }
                    log.Add($"<b>Round {round} - trades</b>" + (tradeLines.Count == 0 ? " - none" : ""));
                    foreach (var line in tradeLines) log.Add(line);

                    log.Add($"<b>Round {round} - actions</b>");
                    for (; seenActions < st.ActionLog.Count; seenActions++)
                    {
                        var a = st.ActionLog[seenActions];
                        runner.LastSubmitted.TryGetValue(a.player, out var submitted);
                        log.Add(a.wasLegal ? $"{Tag(a.player)} {a.action}" + OutcomeNote(a.note)
                            : submitted == null ? $"{Tag(a.player)} <color=#ff6b6b>no action</color> -> pass"
                            : $"{Tag(a.player)} <color=#ff6b6b>illegal</color> {submitted} -> pass");
                    }
                    // What the board looks like after everything above resolved, in node order. These come from all the
                    // actions and trades together, so they get their own heading rather than sitting under the last line.
                    var boardChanges = new List<string>();
                    foreach (var n in st.Nodes)
                    {
                        if (ownerBefore[n.Id] != n.Owner)
                        {
                            changed.Add(n.Id);
                            boardChanges.Add($"   node {n.Id}: {Name(ownerBefore[n.Id])} -> {Name(n.Owner)}");
                        }
                        else if (fortBefore[n.Id] != n.Fortify)
                            boardChanges.Add($"   node {n.Id}: fortify {fortBefore[n.Id]} -> {n.Fortify}");
                        if (prodBefore[n.Id] != n.Production)
                            boardChanges.Add($"   node {n.Id}: production {prodBefore[n.Id]} -> {n.Production}");
                    }
                    log.Add("<b>Board changes</b>" + (boardChanges.Count == 0 ? " - none" : ""));
                    foreach (var line in boardChanges) log.Add(line);
                    if (st.Phase == Phase.Guess) log.Add("<b>Last round played - guessing phase</b>");
                    break;

                case Phase.Guess:
                    log.Add("<b>Guesses</b>");
                    foreach (var kv in st.Guesses)
                        log.Add($"{Tag(kv.Key)}: " + string.Join(", ", kv.Value.Select(g =>
                            $"{g.Key}={g.Value}{(st.Assigned[g.Key].Id == g.Value ? " <color=#7fdc7f>(correct)</color>" : "")}")));
                    log.Add("<b>Final scores</b>");
                    foreach (var kv in runner.Scores.OrderByDescending(k => k.Value))
                        log.Add($"{Tag(kv.Key)} <b>{kv.Value}</b> - {st.Assigned[kv.Key].Id} {(st.Assigned[kv.Key].Achieved(st, kv.Key) ? "<color=#7fdc7f>achieved</color>" : "<color=#ffb37f>missed</color>")}");
                    break;
            }

            // Seats that gave no usable answer this phase.
            foreach (var kv in runner.LastResponses)
                if (kv.Value.Error != null)
                    log.Add($"<size=11>{Tag(kv.Key)} <color=#ffb37f>no usable answer: {kv.Value.Error}</color></size>");

            return changed;
        }

        /// <summary>Prints each AI seat's current notes, once per change. Used live when "AI thinking" is on.</summary>
        public void LogCurrentNotes(GameRunner runner)
        {
            if (runner == null) return;
            foreach (var p in runner.State.Players)
            {
                if (!(runner.Seats[p] is LlmAgent llm) || string.IsNullOrWhiteSpace(llm.Notes)) continue;
                if (notesSeen.TryGetValue(p, out var prev) && prev == llm.Notes) continue;
                notesSeen[p] = llm.Notes;
                log.Add($"<size=11><i>{Tag(p)} thinking: {llm.Notes}</i></size>");
            }
        }

        /// <summary>Once the game is over, every AI seat's scratchpad becomes public. This is where the bluffs show.</summary>
        public void RevealNotes(GameRunner runner)
        {
            var llms = runner.State.Players.Where(p => runner.Seats[p] is LlmAgent).ToList();
            if (llms.Count == 0) return;
            log.Add("<b>Private notes, revealed now the game is over</b>");
            foreach (var p in llms)
            {
                var notes = ((LlmAgent)runner.Seats[p]).Notes;
                log.Add($"<size=11>{Tag(p)}: {(string.IsNullOrWhiteSpace(notes) ? "<i>(none)</i>" : notes)}</size>");
            }
        }

        // The referee's note on a legal action: anything that failed, cancelled or did nothing in red with the reason.
        // Successful moves, walls and sabotage need no note: the node lines below show them.
        static string OutcomeNote(string note) => Referee.CameToNothing(note) ? $" <color=#ff6b6b>({note})</color>" : "";

        string Tag(string player) => colors.Tag(player);
        static string Name(string owner) => owner ?? "neutral";
    }
}
