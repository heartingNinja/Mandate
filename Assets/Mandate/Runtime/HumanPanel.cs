// HumanPanel — your controls at the bottom of the screen when you are playing (your seat is view.HumanSeat).
//
// Each phase shows what the referee is waiting for from you: pick one of three objectives in the draft, queue
// public and private messages and (binding) trade offers in negotiation, click one legal action in the act phase and
// tick any trade offers made to you that you accept (free), and cycle through the candidate objectives per opponent
// in the guess phase. Nothing is sent until Step: the
// panel only fills the HumanBot's pending fields, and the runner reads them when the phase begins. Leaving the
// panel empty gives exactly what a timed-out agent gets (default pick, no messages, pass, no guesses).

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mandate.Core;

namespace Mandate.Viewer
{
    public sealed class HumanPanel
    {
        readonly MandateGameView view;

        Vector2 actionScroll;
        string pubDraft = "", privDraft = "";   // the message being typed, public and private
        int privTarget;                          // index into the other players for the private message
        int offerTarget, offerGive, offerGet;    // trade offer being set up: partner index, and indexes into your nodes and theirs
        readonly Dictionary<string, int> guessIdx = new Dictionary<string, int>();   // opponent -> candidate index shown

        public HumanPanel(MandateGameView view) { this.view = view; }

        /// <summary>Forget the guess positions. Call when a new game starts.</summary>
        public void Reset() => guessIdx.Clear();

        public void Draw()
        {
            var runner = view.Runner;
            var human = view.Human;
            var st = runner.State;
            string me = view.HumanSeat;
            var rect = new Rect(Ui.LeftW, Ui.CanvasH - Ui.HumanH, Ui.CanvasW - Ui.LeftW - Ui.RightW, Ui.HumanH);
            GUILayout.BeginArea(rect, Ui.Panel);
            if (runner.PhasePending)
            {
                // Your answer is in; the other seats are still thinking.
                GUILayout.Label($"<b>Submitted.</b> Waiting for {string.Join(", ", runner.Waiting)}...", Ui.Label);
                GUILayout.EndArea();
                return;
            }
            switch (st.Phase)
            {
                case Phase.Draft:
                    GUILayout.Label($"<b>You are {view.Colors.Tag(me)} - keep one objective, then press Step</b>", Ui.Label);
                    foreach (var o in st.DraftOptions[me])
                    {
                        GUILayout.BeginHorizontal();
                        CardIcons.Badge(o.Id);
                        if (Ui.TipToggle(human.PendingDraft == o.Id, $"  <b>{o.Id}</b>  {o.Text}", CardIcons.Tooltip(o.Id))) human.PendingDraft = o.Id;
                        GUILayout.EndHorizontal();
                    }
                    break;

                case Phase.Negotiate:
                    GUILayout.Label($"<b>Negotiate</b> - queue up to {Rules.MaxPublicMessages} public and {Rules.MaxPrivateMessages} private messages, then Step", Ui.Label);
                    GUILayout.BeginHorizontal();
                    pubDraft = GUILayout.TextField(pubDraft, GUILayout.Width(rect.width - 150));
                    if (GUILayout.Button("Send public", Ui.Button, GUILayout.Width(110)) && pubDraft.Trim().Length > 0)
                    { human.PendingMessages.Add((null, pubDraft.Trim())); pubDraft = ""; }
                    GUILayout.EndHorizontal();
                    var others = st.Players.Where(q => q != me).ToList();
                    privTarget = Mathf.Clamp(privTarget, 0, others.Count - 1);
                    GUILayout.BeginHorizontal();
                    privDraft = GUILayout.TextField(privDraft, GUILayout.Width(rect.width - 250));
                    if (GUILayout.Button("to " + others[privTarget], Ui.Button, GUILayout.Width(80))) privTarget = (privTarget + 1) % others.Count;
                    if (GUILayout.Button("Send private", Ui.Button, GUILayout.Width(110)) && privDraft.Trim().Length > 0)
                    { human.PendingMessages.Add((others[privTarget], privDraft.Trim())); privDraft = ""; }
                    GUILayout.EndHorizontal();
                    DrawOfferRow(st, me, others);
                    foreach (var m in human.PendingMessages)
                        GUILayout.Label($"<size=11>queued {(m.to == null ? "public" : "to " + m.to)}: {m.text}</size>", Ui.Wrap);
                    foreach (var o in human.PendingOffers)
                        GUILayout.Label($"<size=11>queued trade offer to {o.partner}: you give {o.give}, you get {o.get} (binding once sent)</size>", Ui.Wrap);
                    break;

                case Phase.Act:
                    GUILayout.Label($"<b>Act</b> - pick one action, then Step.  Selected: <b>{human.PendingAction ?? GameAction.Pass()}</b>", Ui.Label);
                    var legal = runner.Referee.LegalActions(me);
                    actionScroll = GUILayout.BeginScrollView(actionScroll);
                    var roundOffers = st.Offers.Where(o => o.Round == st.Round && o.Problem == null).ToList();
                    var toMe = roundOffers.Where(o => o.To == me).ToList();
                    if (toMe.Count > 0)
                    {
                        GUILayout.Label("<size=11>Trade offers made to you. Accepting is free: tick any you want, and still pick your action below.</size>", Ui.Wrap);
                        foreach (var o in toMe)
                        {
                            bool on = human.PendingAccepts.Contains(o.From);
                            bool now = GUILayout.Toggle(on, $"accept {o.From}: you get node {o.Give}, you give node {o.Get}", Ui.Toggle);
                            if (now && !on) human.PendingAccepts.Add(o.From);
                            if (!now && on) human.PendingAccepts.Remove(o.From);
                        }
                    }
                    foreach (var o in roundOffers.Where(o => o.From == me))
                        GUILayout.Label($"<size=11>You offered {o.To}: give {o.Give}, get {o.Get}. It happens if {o.To} accepts.</size>", Ui.Wrap);
                    DrawActionGrid(legal, 6, 118f);
                    GUILayout.EndScrollView();
                    break;

                case Phase.Guess:
                    GUILayout.Label("<b>Guess</b> each opponent's objective (+2 each if right), then Step", Ui.Label);
                    GUILayout.Label("<size=11>Candidates are every objective dealt this game except the ones you were offered. Each card is held by exactly one player.</size>", Ui.Wrap);
                    var ids = runner.Referee.ViewFor(me).GuessCandidates;
                    if (ids.Count == 0) break;
                    foreach (var q in st.Players.Where(q => q != me))
                    {
                        int idx = (guessIdx.TryGetValue(q, out int g) ? g : 0) % ids.Count;
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(view.Colors.Tag(q), Ui.Label, GUILayout.Width(40));
                        if (GUILayout.Button("<", Ui.Button, GUILayout.Width(30))) idx = (idx - 1 + ids.Count) % ids.Count;
                        CardIcons.Badge(ids[idx].id);
                        Ui.TipLabel($"<b>{ids[idx].id}</b>", CardIcons.Tooltip(ids[idx].id), Ui.Label, GUILayout.Width(146));
                        if (GUILayout.Button(">", Ui.Button, GUILayout.Width(30))) idx = (idx + 1) % ids.Count;
                        GUILayout.Label($"<size=11>{ids[idx].text}</size>", Ui.Wrap);
                        GUILayout.EndHorizontal();
                        guessIdx[q] = idx;
                        human.PendingGuesses[q] = ids[idx].id;
                    }
                    break;
            }
            GUILayout.EndArea();
        }

        // One row for a formal trade offer: click to cycle the partner, the node you give and the node you want.
        void DrawOfferRow(GameState st, string me, List<string> others)
        {
            offerTarget = Mathf.Clamp(offerTarget, 0, others.Count - 1);
            string partner = others[offerTarget];
            var mine = st.Nodes.Where(n => n.Owner == me).Select(n => n.Id).ToList();
            var theirs = st.Nodes.Where(n => n.Owner == partner).Select(n => n.Id).ToList();
            GUILayout.BeginHorizontal();
            GUILayout.Label("<size=11>Trade offer:</size>", Ui.Small, GUILayout.Width(80));
            if (GUILayout.Button("to " + partner, Ui.Button, GUILayout.Width(80))) offerTarget = (offerTarget + 1) % others.Count;
            if (mine.Count > 0 && theirs.Count > 0)
            {
                offerGive %= mine.Count; offerGet %= theirs.Count;
                if (GUILayout.Button("give " + mine[offerGive], Ui.Button, GUILayout.Width(80))) offerGive = (offerGive + 1) % mine.Count;
                if (GUILayout.Button("get " + theirs[offerGet], Ui.Button, GUILayout.Width(80))) offerGet = (offerGet + 1) % theirs.Count;
                if (GUILayout.Button("Queue offer", Ui.Button, GUILayout.Width(110)))
                {
                    var queue = view.Human.PendingOffers;
                    queue.RemoveAll(o => o.partner == partner);   // one offer per partner
                    queue.Add((partner, mine[offerGive], theirs[offerGet]));
                }
            }
            else GUILayout.Label($"<size=11>{(mine.Count == 0 ? "you own no nodes" : partner + " owns no nodes")}</size>", Ui.Small);
            GUILayout.EndHorizontal();
        }

        // Toggle buttons in rows of 'perRow'; the selected one is the human's pending action.
        void DrawActionGrid(List<GameAction> actions, int perRow, float width)
        {
            var human = view.Human;
            for (int i = 0; i < actions.Count; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Math.Min(i + perRow, actions.Count); j++)
                {
                    var a = actions[j];
                    bool selected = a.Equals(human.PendingAction) || (human.PendingAction == null && a.Type == ActionType.Pass);
                    if (GUILayout.Toggle(selected, a.ToString(), Ui.Button, GUILayout.Width(width)) && !selected) human.PendingAction = a;
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
