// Mandate.Tests — plain asserts, no test framework dependency so it runs anywhere.
// Console: Tests.RunAll(). Unity: the Mandate menu and the NUnit wrapper in Assets/Mandate/Tests call All().

using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Mandate.Core;
using Mandate.Sim;

namespace Mandate.Tests
{
    public static class Tests
    {
        // Where PASS/FAIL lines go. Console by default; Unity swaps in Debug.Log.
        public static Action<string> Out = Console.WriteLine;

        static void Check(bool cond, string name) { Out((cond ? "PASS " : "FAIL ") + name); if (!cond) throw new Exception(name); }
        static Referee NewGame(int players = 4, int seed = 1)
        {
            var r = new Referee("t", Enumerable.Range(1, players).Select(i => "P" + i).ToList(), seed);
            if (r.State.Phase == Phase.Draft) r.EndDraft();   // default picks, tests don't care which
            return r;
        }

        // Every test, by name, so a test runner can report them individually.
        public static IEnumerable<(string name, Action run)> All()
        {
            yield return (nameof(ViewNeverLeaks), ViewNeverLeaks);
            yield return (nameof(IllegalActionBecomesPass), IllegalActionBecomesPass);
            yield return (nameof(AcceptedOfferSwapsNodes), AcceptedOfferSwapsNodes);
            yield return (nameof(UnacceptedOfferDoesNothing), UnacceptedOfferDoesNothing);
            yield return (nameof(LastRoundIsSecretAndVaries), LastRoundIsSecretAndVaries);
            yield return (nameof(TradeOutcomeIsInTheHistory), TradeOutcomeIsInTheHistory);
            yield return (nameof(TradeOffersAreCheckedAndShownToThePartner), TradeOffersAreCheckedAndShownToThePartner);
            yield return (nameof(EveryActionOutcomeIsInTheHistory), EveryActionOutcomeIsInTheHistory);
            yield return (nameof(ForecastMatchesWhatHappens), ForecastMatchesWhatHappens);
            yield return (nameof(SeatNamesAreNormalised), SeatNamesAreNormalised);
            yield return (nameof(NeutralNodeCaptured), NeutralNodeCaptured);
            yield return (nameof(FortifiedDefenderHolds), FortifiedDefenderHolds);
            yield return (nameof(EqualAttackersCancel), EqualAttackersCancel);
            yield return (nameof(SabotageThenMove), SabotageThenMove);
            yield return (nameof(MessageCapsEnforced), MessageCapsEnforced);
            yield return (nameof(ScoringMath), ScoringMath);
            yield return (nameof(DraftDealsUniqueOptions), DraftDealsUniqueOptions);
            yield return (nameof(DraftPickRespected), DraftPickRespected);
            yield return (nameof(SabotageCooldown), SabotageCooldown);
            yield return (nameof(AdjacentSabotageOnly), AdjacentSabotageOnly);
            yield return (nameof(StartingProductionIsEqual), StartingProductionIsEqual);
            yield return (nameof(EverySeatSameDistanceFromHub), EverySeatSameDistanceFromHub);
            yield return (nameof(GapCardsPointBesideYou), GapCardsPointBesideYou);
            yield return (nameof(TiesAreShared), TiesAreShared);
            yield return (nameof(SeatingShufflesFromTheSeed), SeatingShufflesFromTheSeed);
            yield return (nameof(BeliefsLoggedNotScored), BeliefsLoggedNotScored);
            yield return (nameof(TradeDoesNotCountAsLoss), TradeDoesNotCountAsLoss);
            yield return (nameof(MoveFromTradedNodeIsVoid), MoveFromTradedNodeIsVoid);
            yield return (nameof(DevelopRaisesProductionToCap), DevelopRaisesProductionToCap);
            yield return (nameof(CapturedNodeKeepsProduction), CapturedNodeKeepsProduction);
            yield return (nameof(ReleaseMakesNodeNeutral), ReleaseMakesNodeNeutral);
            yield return (nameof(SabotageOnBareNodeMakesItNeutral), SabotageOnBareNodeMakesItNeutral);
            yield return (nameof(GuessCandidatesAreDealtMinusYourOffer), GuessCandidatesAreDealtMinusYourOffer);
            yield return (nameof(WireActionsRoundTrip), WireActionsRoundTrip);
            yield return (nameof(WireResponseValidation), WireResponseValidation);
            yield return (nameof(RunnerWaitsForSlowSeats), RunnerWaitsForSlowSeats);
            yield return (nameof(HungSeatBecomesPassAfterTimeout), HungSeatBecomesPassAfterTimeout);
            yield return (nameof(CrashingSeatBecomesPass), CrashingSeatBecomesPass);
            yield return (nameof(WrappedBotsMatchDirectBots), WrappedBotsMatchDirectBots);
        }

        public static void RunAll()
        {
            int n = 0;
            foreach (var (name, run) in All()) { run(); n++; }
            Out($"all {n} tests passed");
        }

        static void ViewNeverLeaks()
        {
            var r = NewGame();
            foreach (var p in r.State.Players)
            {
                var v = r.ViewFor(p);
                Check(v.ObjectiveText == r.State.Assigned[p].Text, "view has own objective");
                Check(v.ObjectiveId == r.State.Assigned[p].Id, "view has own objective id");
                Check(v.ObjectiveMetNow == r.State.Assigned[p].Achieved(r.State, p), "view says whether own objective is met right now");
                foreach (var q in r.State.Players.Where(q => q != p))
                    Check(v.ObjectiveText != r.State.Assigned[q].Text, "view does not contain other objective");
                Check(v.PrivateInbox.All(m => m.To == p), "inbox only contains own private messages");
            }
        }

        static void IllegalActionBecomesPass()
        {
            var r = NewGame(); r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(4, 5) }); // P1 doesn't own 4
            var log = r.State.ActionLog.First(a => a.player == "P1");
            Check(log.action.Type == ActionType.Pass && !log.wasLegal, "illegal move logged as pass");
        }

        // Offer in negotiation, accept in the act phase: the nodes swap and both sides still take their action.
        static Referee Traded(string from, int give, string to, int get, bool accept)
        {
            var r = NewGame();
            r.SubmitOffers(from, new List<(string, int, int)> { (to, give, get) });
            r.EndNegotiation();
            if (accept) r.AcceptOffers(to, new List<string> { from });
            return r;
        }

        static void AcceptedOfferSwapsNodes()
        {
            var r = Traded("P1", 1, "P2", 4, accept: true);
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(2, 3), ["P2"] = GameAction.Fortify(5) });
            Check(r.State.Nodes[1].Owner == "P2" && r.State.Nodes[4].Owner == "P1", "accepted offer swaps the nodes");
            Check(r.State.TradesCompleted["P1"] == 1 && r.State.TradesCompleted["P2"] == 1, "trade counted for both");
            Check(r.State.Nodes[3].Owner == "P1" && r.State.Nodes[5].Fortify == 1, "a trade costs neither side their action");
            Check(Enum.GetNames(typeof(ActionType)).All(n => n != "Trade"), "trading is not an action");
        }

        static void UnacceptedOfferDoesNothing()
        {
            var r = Traded("P1", 1, "P2", 4, accept: false);
            Check(r.AcceptOffers("P3", new List<string> { "P1" }).Count == 0, "only the partner can accept");
            r.ResolveRound(new Dictionary<string, GameAction>());
            Check(r.State.Nodes[1].Owner == "P1" && r.State.Nodes[4].Owner == "P2", "an offer nobody accepts changes nothing");
            Check(r.ViewFor("P1").TradeHistory.Single().Outcome == "not accepted", "the offerer learns it was not accepted");
            Check(r.ViewFor("P3").TradeHistory.Count == 0, "an offer that came to nothing stays private");
        }

        static void LastRoundIsSecretAndVaries()
        {
            var seen = new HashSet<int>();
            for (int seed = 1; seed <= 60; seed++)
            {
                var r = NewGame(seed: seed);
                int last = r.State.LastRound;
                seen.Add(last);
                Check(last >= Rules.MinRounds && last <= Rules.MaxRounds, "last round is between 7 and 9");
                Check(new Referee("t", new List<string> { "P1", "P2", "P3", "P4" }, seed).State.LastRound == last, "same seed, same last round");
                while (r.State.Phase != Phase.Guess) { r.EndNegotiation(); r.ResolveRound(new Dictionary<string, GameAction>()); }
                Check(r.State.Round == last && r.State.ActionLog.Max(a => a.round) == last, "the game ends exactly after the secret last round");
            }
            Check(seen.SetEquals(new[] { 7, 8, 9 }), "every length happens");
            Check(typeof(PlayerView).GetFields().All(f => !f.Name.Contains("Last")), "the view has no field that could carry the last round");
        }

        static void TradeOutcomeIsInTheHistory()
        {
            var r = NewGame();
            r.SubmitOffers("P1", new List<(string, int, int)> { ("P2", 1, 4), ("P3", 1, 7) });   // node 1 offered twice
            r.EndNegotiation();
            r.AcceptOffers("P2", new List<string> { "P1" }); r.AcceptOffers("P3", new List<string> { "P1" });
            r.ResolveRound(new Dictionary<string, GameAction> { ["P4"] = GameAction.Fortify(10), ["P1"] = GameAction.Fortify(1) });
            Check(r.State.ActionLog.Last(a => a.player == "P1").note == "void: you traded node 1 away before the walls went up", "walls on a node you traded away say so, not 'lost'");
            var seen = r.ViewFor("P4").TradeHistory;
            Check(seen.Count == 1 && seen[0].From == "P1" && seen[0].To == "P2" && seen[0].Outcome == "done", "a completed trade is public");
            Check(r.ViewFor("P3").TradeHistory.Single(o => o.To == "P3").Outcome.StartsWith("void: one of its nodes"), "a node can only be traded once a round; the later offer is void");
            Check(r.State.Nodes[1].Owner == "P2" && r.State.Nodes[7].Owner == "P3", "the void offer swapped nothing");
            Check(r.State.ActionLog.Last(a => a.player == "P4").note == "node 10 fort 0 -> 1", "actions keep their own notes");
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(5, 6) });   // P1 doesn't own 5
            var last = r.State.ActionLog.Where(a => a.round == 2).ToList();
            Check(last.Single(a => a.player == "P1").note.StartsWith("illegal: move(5->6)"), "illegal action names what was tried");
            Check(last.Single(a => a.player == "P2").note == "no usable answer, counted as pass", "missing answer noted");
        }

        static void EveryActionOutcomeIsInTheHistory()
        {
            var r = NewGame(); r.EndNegotiation();   // P1 owns 1,2; P2 4,5; P3 7,8; P4 10,11; 3, 6, 9, 12 neutral
            r.ResolveRound(new Dictionary<string, GameAction>
            {
                ["P1"] = GameAction.Move(2, 3), ["P2"] = GameAction.Move(4, 3),   // same fort into the same neutral
                ["P3"] = GameAction.Move(8, 9), ["P4"] = GameAction.Fortify(10),
            });
            string Note(string p) => r.State.ActionLog.Last(a => a.player == p).note;
            Check(Note("P1").StartsWith("FAILED: P2 also moved into node 3 with the same fort") && Note("P2").StartsWith("FAILED: P1 also moved"), "equal movers cancel and both are told why");
            Check(Note("P3") == "took neutral node 9" && Note("P4") == "node 10 fort 0 -> 1", "a move and a wall say what they did");

            r.State.Nodes[3].Owner = "P1";   // P1's node 3 (fort 0) now borders P2's node 4 (fort 0)
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction>
            {
                ["P1"] = GameAction.Move(3, 4),                                     // tie: defender holds
                ["P3"] = GameAction.Fortify(9), ["P4"] = GameAction.Sabotage(9),     // the wall goes up and straight back down
            });
            Check(Note("P1").Contains("you need MORE fort") && r.State.Nodes[4].Owner == "P2", "an attack on a tie fails and says why");
            Check(Note("P3") == "node 9 fort 0 -> 1, then sabotage took it back to fort 0" && Note("P4") == "node 9 fort 1 -> 0", "a fortify cancelled by sabotage is spelled out");
            Check(Referee.CameToNothing(Note("P1")) && !Referee.CameToNothing(Note("P3")) && Referee.CameToNothing(Note("P2")), "failures are recognisable");
        }

        static void ForecastMatchesWhatHappens()
        {
            var r = NewGame();
            r.State.Nodes[3].Owner = "P1"; r.State.Nodes[3].Fortify = 1;   // P1's node 3 (fort 1) borders P2's node 4 (fort 0)
            var p1 = r.ViewFor("P1");
            Check(p1.MovePreviews.Any(l => l.StartsWith("move(3->4): CAPTURES P2's node 4")) && p1.MovePreviews.Any(l => l.StartsWith("move(1->12): takes neutral node 12")), "previews say which moves work");
            var p2 = r.ViewFor("P2");
            Check(p2.MovePreviews.Any(l => l.StartsWith("move(4->3): FAILS")), "an attack without more fort is called a failure");
            Check(p2.Dangers.Any(l => l.StartsWith("P1 can CAPTURE your node 4 from their node 3")) && p2.Dangers.Any(l => l.StartsWith("P1 can sabotage your node 4, which has no walls")), "dangers list the threats to your nodes");
            Check(r.ViewFor("P4").Dangers.Count == 0, "no false alarms");
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(3, 4), ["P2"] = GameAction.Fortify(4) });
            Check(r.State.Nodes[4].Owner == "P1", "the previewed capture happens, and walls built the same round come too late");
        }

        static void TradeOffersAreCheckedAndShownToThePartner()
        {
            var r = NewGame();   // P1 owns 1,2; P2 owns 4,5; node 3 is neutral
            var made = r.SubmitOffers("P1", new List<(string, int, int)> { ("P2", 1, 4), ("P3", 1, 3), ("P4", 4, 10) });
            Check(made.Count == 3 && made[0].Problem == null, "valid offer accepted");
            Check(made[1].Problem.Contains("neutral") && made[2].Problem.Contains("do not own"), "impossible offers rejected with a reason");
            r.SubmitOffers("P1", new List<(string, int, int)> { ("P2", 2, 5) });
            Check(r.State.Offers.Count(o => o.From == "P1" && o.To == "P2") == 1, "a second offer to the same partner replaces the first");
            r.EndNegotiation();
            var p2 = r.ViewFor("P2").Offers;
            Check(p2.Count == 1 && p2[0].From == "P1" && p2[0].Give == 2 && p2[0].Get == 5, "partner sees the offer");
            Check(r.ViewFor("P3").Offers.Count == 0, "a rejected offer is not shown to its partner");
            Check(r.ViewFor("P1").Offers.Count == 3, "the offerer sees all their offers, rejected ones included");
            Check(r.SubmitOffers("P2", new List<(string, int, int)> { ("P1", 4, 1) }).Count == 0, "no offers outside negotiation");
            Check(r.AcceptOffers("P4", new List<string> { "P1" }).Count == 0, "a rejected offer cannot be accepted");
            r.AcceptOffers("P2", new List<string> { "P1" });
            r.ResolveRound(new Dictionary<string, GameAction>());
            Check(r.State.Nodes[2].Owner == "P2" && r.State.Nodes[5].Owner == "P1", "accepting the offer swaps the nodes");
        }

        static void SeatNamesAreNormalised()
        {
            Check(Wire.SeatId("p2") == "P2" && Wire.SeatId(" Player 3 ") == "P3" && Wire.SeatId("player4") == "P4", "seat names normalised");
            Check(Wire.SeatId("public") == "public" && Wire.SeatId("P2") == "P2", "other names only trimmed");
            var resp = Wire.ToResponse(new ResponseDto { Action = new ActionDto { Type = "pass" }, Accept = new List<string> { "p2" } }, Phase.Act);
            Check(resp.Accepts.Single() == "P2", "accepted offerer normalised");
            var trade = Wire.ToResponse(new ResponseDto { Action = new ActionDto { Type = "trade", From = 1, To = 4 } }, Phase.Act);
            Check(trade.Action == null && trade.Error.StartsWith("a trade is not an action"), "an old-style trade action is explained, not just refused");
        }

        static void NeutralNodeCaptured()
        {
            var r = NewGame(); r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(2, 3) }); // 3 is neutral
            Check(r.State.Nodes[3].Owner == "P1", "single attacker takes neutral node");
        }

        static void FortifiedDefenderHolds()
        {
            var r = NewGame(); r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P2"] = GameAction.Fortify(4) });
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(2, 3) }); // take 3 first
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(3, 4) }); // attack fortified 4 from unfortified 3
            Check(r.State.Nodes[4].Owner == "P2", "fortified defender holds against weaker attacker");
        }

        static void EqualAttackersCancel()
        {
            var r = NewGame(); r.EndNegotiation();
            // node 0 is neutral and adjacent to 3, 6, 9; get P1 onto 3 and P2 onto 6 first
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(2, 3), ["P2"] = GameAction.Move(5, 6) });
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(3, 0), ["P2"] = GameAction.Move(6, 0) });
            Check(r.State.Nodes[0].Owner == null, "two equal attackers on same node cancel");
        }

        static void SabotageThenMove()
        {
            // P1 takes gap node 3 and walls it; P3 walks to the hub (next to 3); then P2 (next door on 4) sabotages 3
            // while P3 attacks it from the hub.
            var r = NewGame(); r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(2, 3), ["P3"] = GameAction.Move(7, 6) });
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Fortify(3), ["P3"] = GameAction.Move(6, 0) });
            r.EndNegotiation();
            Check(r.State.Nodes[3].Owner == "P1" && r.State.Nodes[3].Fortify == 1 && r.State.Nodes[0].Owner == "P3", "setup: P1 walled 3, P3 holds the hub");
            r.ResolveRound(new Dictionary<string, GameAction> { ["P2"] = GameAction.Sabotage(3), ["P3"] = GameAction.Move(0, 3) });
            // Order is moves then sabotage within a round, so the move this round still faces fortify 1 and fails.
            Check(r.State.Nodes[3].Owner == "P1" && r.State.Nodes[3].Fortify == 0, "sabotage resolves after moves in the same round");
        }

        static void MessageCapsEnforced()
        {
            var r = NewGame();
            var acc = r.SubmitMessages("P1", new List<(string, string)>
            {
                (null, "a"), (null, "b"), (null, "c"), (null, "d"),           // 4 public, cap 3
                ("P2", "x"), ("P3", "y"), ("P4", "z"), ("P2", "w"),           // 4 private, cap 3
                ("P1", "self"), ("P9", "ghost"),                              // invalid targets
                (null, new string('!', 1000)),                                // over length, but also over public cap
            });
            Check(acc.Count(m => m.To == null) == 3, "public cap 3");
            Check(acc.Count(m => m.To != null) == 3, "private cap 3");
            Check(acc.All(m => m.To != "P1" && m.To != "P9"), "invalid targets dropped");
        }

        static void DraftDealsUniqueOptions()
        {
            var r = new Referee("t", Enumerable.Range(1, 8).Select(i => "P" + i).ToList(), 3);
            Check(r.State.Phase == Phase.Draft, "starts in draft");
            var all = r.State.DraftOptions.Values.SelectMany(o => o).Select(o => o.Id).ToList();
            Check(all.Count == 24 && all.Distinct().Count() == 24, "8 players x 3 unique options");
            var v = r.ViewFor("P1");
            Check(v.DraftOptions.Count == 3 && v.ObjectiveText == null && v.ObjectiveMetNow == null, "draft view shows options, no objective yet");
        }

        static void DraftPickRespected()
        {
            var r = new Referee("t", new List<string> { "P1", "P2", "P3", "P4" }, 3);
            var want = r.State.DraftOptions["P1"][2].Id;
            r.SubmitDraft("P1", want);
            r.SubmitDraft("P2", "not_an_option");
            r.EndDraft();
            Check(r.State.Assigned["P1"].Id == want, "valid pick kept");
            Check(r.State.Assigned["P2"].Id == r.State.DraftOptions["P2"][0].Id, "invalid pick falls back to first");
            Check(r.State.Phase == Phase.Negotiate, "negotiate after draft");
        }

        static void SabotageCooldown()
        {
            // P1 takes gap nodes 3 and 12, so it can reach P2's node 4 and P4's node 11.
            var r = NewGame(); r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(2, 3), ["P2"] = GameAction.Fortify(4), ["P4"] = GameAction.Fortify(11) });
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(1, 12) });
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Sabotage(4), ["P2"] = GameAction.Fortify(4) });
            r.EndNegotiation();
            Check(!r.IsLegal("P1", GameAction.Sabotage(4)), "same node next round is illegal");
            Check(r.IsLegal("P1", GameAction.Sabotage(11)), "different node is fine");
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Sabotage(11) });
            r.EndNegotiation();
            Check(r.IsLegal("P1", GameAction.Sabotage(4)), "cooldown clears after one round");
        }

        static void AdjacentSabotageOnly()
        {
            var r = NewGame(); r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P2"] = GameAction.Fortify(4), ["P3"] = GameAction.Fortify(7) });
            r.EndNegotiation();
            // P1 owns 1,2. Node 4 is not adjacent (2-3-4). Node 7 is far. Neither reachable.
            Check(!r.IsLegal("P1", GameAction.Sabotage(4)), "non-adjacent sabotage illegal");
            Check(!r.IsLegal("P1", GameAction.Sabotage(7)), "distant sabotage illegal");
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(2, 3) });
            r.EndNegotiation();
            Check(r.IsLegal("P1", GameAction.Sabotage(4)), "adjacent after taking node 3");
        }

        static void StartingProductionIsEqual()
        {
            // Every table size, many seeds: each player's two starting nodes always add up to the same total.
            for (int players = Rules.MinPlayers; players <= Rules.MaxPlayers; players++)
                for (int seed = 1; seed <= 50; seed++)
                {
                    var r = NewGame(players, seed);
                    foreach (var p in r.State.Players)
                    {
                        var mine = r.State.Nodes.Where(n => n.Owner == p).ToList();
                        if (mine.Sum(n => n.Production) != Rules.StartingProduction || mine.Any(n => n.Production < 1 || n.Production > 3))
                            Check(false, $"{p} starts with {mine.Sum(n => n.Production)} production ({players} players, seed {seed})");
                    }
                }
            Check(true, "every player starts with equal production");
        }

        static void EverySeatSameDistanceFromHub()
        {
            // The hub is off the ring and touches only the neutral node between each pair of seats,
            // so nobody starts next to it and every seat is two steps away.
            for (int players = Rules.MinPlayers; players <= Rules.MaxPlayers; players++)
            {
                var st = NewGame(players).State;
                var hubAdj = st.Nodes[0].Adjacent;
                bool ok = st.Nodes.Count == players * Rules.NodesPerPlayer + 1
                    && hubAdj.Count == players
                    && hubAdj.All(i => st.Nodes[i].Owner == null)
                    && st.Players.All(p => st.Nodes.Where(n => n.Owner == p).SelectMany(n => n.Adjacent).Count(i => hubAdj.Contains(i)) == 2);
                if (!ok) Check(false, $"hub is not the same distance from every seat ({players} players)");
            }
            Check(true, "every seat is two steps from the hub");
        }

        static void GapCardsPointBesideYou()
        {
            // Your left gap touches one of your starting nodes, is linked to the hub, and is your left neighbour's right gap.
            for (int players = Rules.MinPlayers; players <= Rules.MaxPlayers; players++)
            {
                var st = NewGame(players).State;
                for (int i = 0; i < players; i++)
                {
                    string p = st.Players[i], left = st.Players[(i + 1) % players];
                    int lg = Objectives.LeftGap(st.Players, p), rg = Objectives.RightGap(st.Players, p);
                    var mine = st.Nodes.Where(n => n.Owner == p).ToList();
                    bool ok = lg != rg
                        && new[] { lg, rg }.All(g => Objectives.IsGap(g) && st.Nodes[0].Adjacent.Contains(g) && mine.Any(n => n.Adjacent.Contains(g)))
                        && Objectives.RightGap(st.Players, left) == lg;
                    if (!ok) Check(false, $"{p}'s gaps are wrong ({players} players): left {lg}, right {rg}");
                }
            }
            Check(true, "left and right gaps sit beside every seat");
        }

        static void TiesAreShared()
        {
            // P1 and P2 tie on 6 production at the top, P3 and P4 tie on 3 at the bottom. No tie-break by seat or node count.
            var st = NewGame().State;
            foreach (var n in st.Nodes) { n.Owner = null; n.Production = 1; }
            void Give(string p, int node, int prod) { st.Nodes[node].Owner = p; st.Nodes[node].Production = prod; }
            Give("P1", 1, 3); Give("P1", 2, 3);
            Give("P2", 4, 3); Give("P2", 5, 3);
            Give("P3", 7, 3);
            Give("P4", 10, 3);
            bool Met(string id, string p) => Objectives.Pool().Single(o => o.Id == id).Achieved(st, p);
            Check(Met("most_production", "P1") && Met("most_production", "P2") && !Met("most_production", "P3"), "a tie for most is shared first");
            Check(Met("left_wins", "P4") && Met("left_wins", "P1"), "left_wins counts a shared first");
            Check(!st.Players.Any(p => Met("left_second", p) || Met("right_second", p)), "two tied for first leave nobody second");
            Check(Met("left_last", "P2") && Met("right_last", "P1"), "a tie for last is shared last");
            Check(Met("top_two", "P1") && Met("top_two", "P2") && !Met("top_two", "P3"), "top_two counts a shared first");
            Check(!Met("beat_right", "P2") && !Met("neighbours_below", "P1"), "a tie is not finishing ahead");
        }

        static void SeatingShufflesFromTheSeed()
        {
            // Every seat used exactly once, same seed gives the same seating, off means in order, and it does move people.
            bool allPermutations = true, repeatable = true; var firstSeats = new HashSet<int>();
            for (int seed = 1; seed <= 200; seed++)
            {
                var a = Seating.Assign(6, seed, true);
                allPermutations &= a.OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 6));
                repeatable &= a.SequenceEqual(Seating.Assign(6, seed, true));
                firstSeats.Add(a[0]);
            }
            Check(allPermutations, "seating is a permutation");
            Check(repeatable, "same seed, same seating");
            Check(Seating.Assign(5, 9, false).SequenceEqual(Enumerable.Range(0, 5)), "no shuffle keeps entry order");
            Check(firstSeats.Count == 6, "the first entry lands in every seat over many seeds");
        }

        static void BeliefsLoggedNotScored()
        {
            var r = NewGame();
            var p2Obj = r.State.Assigned["P2"].Id;
            r.SubmitBeliefs("P1", new BeliefReport
            {
                ObjectiveBeliefs = { ["P2"] = new() { [p2Obj] = 0.7, ["not_real"] = 0.3 }, ["P1"] = new() { [p2Obj] = 1.0 }, ["P9"] = new() },
                TheyThinkIAm = { ["P2"] = "hold_center", ["P3"] = "bogus" },
                Note = "test",
            });
            var b = r.State.Beliefs.Single();
            Check(b.ObjectiveBeliefs.Keys.SequenceEqual(new[] { "P2" }), "self and unknown targets dropped");
            Check(b.ObjectiveBeliefs["P2"].Keys.SequenceEqual(new[] { p2Obj }), "unknown objective ids dropped");
            Check(b.TheyThinkIAm.Count == 1 && b.TheyThinkIAm["P2"] == "hold_center", "bogus id dropped");
            r.SubmitBeliefs("P1", new BeliefReport { Note = "second" });
            Check(r.State.Beliefs.Count == 1 && r.State.Beliefs[0].Note == "second", "second report in same round replaces first");
            while (r.State.Phase != Phase.Guess) { r.EndNegotiation(); r.ResolveRound(new Dictionary<string, GameAction>()); }
            var s = r.Score();
            Check(s["P1"] == (r.State.Assigned["P1"].Achieved(r.State, "P1") ? 10 : 0), "beliefs contribute nothing to score");
        }

        static void ScoringMath()
        {
            var r = NewGame();
            while (r.State.Phase != Phase.Guess) { r.EndNegotiation(); r.ResolveRound(new Dictionary<string, GameAction>()); }
            Check(r.State.Phase == Phase.Guess, "guess phase after the secret last round");
            var p2Obj = r.State.Assigned["P2"].Id;
            r.SubmitGuesses("P1", new Dictionary<string, string> { ["P2"] = p2Obj, ["P3"] = "wrong", ["P4"] = "wrong" });
            r.SubmitGuesses("P3", new Dictionary<string, string> { ["P2"] = p2Obj });
            var s = r.Score();
            bool p1Obj = r.State.Assigned["P1"].Achieved(r.State, "P1");
            bool p2Met = r.State.Assigned["P2"].Achieved(r.State, "P2");
            Check(s["P1"] == (p1Obj ? 10 : 0) + 2, "P1: objective + one correct read");
            Check(s["P2"] == (p2Met ? 10 : 0) - 4, "P2: read by two players = -4");
        }

        static void DevelopRaisesProductionToCap()
        {
            var r = NewGame(); r.EndNegotiation();
            r.State.Nodes[2].Production = 1;
            Check(!r.IsLegal("P1", GameAction.Develop(4)), "cannot develop someone else's node");
            for (int i = 1; i < Rules.ProductionCap; i++)
            {
                r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Develop(2) });
                r.EndNegotiation();
            }
            Check(r.State.Nodes[2].Production == Rules.ProductionCap, "develop adds 1 production per round");
            Check(!r.IsLegal("P1", GameAction.Develop(2)), "develop stops at the cap");
        }

        static void CapturedNodeKeepsProduction()
        {
            var r = NewGame(); r.EndNegotiation();
            r.State.Nodes[2].Production = 1;
            r.State.Nodes[3].Owner = "P2"; r.State.Nodes[3].Fortify = 1;   // P2 next to P1's bare node 2
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Develop(2), ["P2"] = GameAction.Move(3, 2) });
            Check(r.State.Nodes[2].Owner == "P2" && r.State.Nodes[2].Production == 1, "develop on a node captured that round is void");
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P2"] = GameAction.Develop(2) });
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Fortify(1) });
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(1, 2) });
            Check(r.State.Nodes[2].Owner == "P1" && r.State.Nodes[2].Production == 2, "a captured node keeps its developed production");
        }

        static void ReleaseMakesNodeNeutral()
        {
            var r = NewGame(); r.EndNegotiation();
            Check(!r.IsLegal("P1", GameAction.Release(4)), "cannot release someone else's node");
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Fortify(2) });
            r.EndNegotiation();
            int prod = r.State.Nodes[2].Production;
            r.State.Nodes[3].Owner = "P2";
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Release(2), ["P2"] = GameAction.Move(3, 2) });
            Check(r.State.Nodes[2].Owner == null && r.State.Nodes[2].Fortify == 0 && r.State.Nodes[2].Production == prod,
                "release resolves after moves: the walls stop the move, then the node goes neutral with no walls");
            Check(!r.State.LostNodeEver.Contains("P1"), "releasing your own node is not a loss");
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P2"] = GameAction.Move(3, 2) });
            Check(r.State.Nodes[2].Owner == "P2", "a released node can be taken from the next round on");
            Check(!r.State.CapturedOwnedEver.Contains("P2"), "taking a released node is not a capture");
            r.EndNegotiation();
            r.State.Nodes[2].Fortify = 1;
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Release(1), ["P2"] = GameAction.Move(2, 1) });
            Check(r.State.Nodes[1].Owner == "P2" && r.State.ActionLog.Last(x => x.player == "P1").note.StartsWith("void"),
                "releasing a node that is captured the same round is void: you can't release your way out of an attack");
        }

        static void SabotageOnBareNodeMakesItNeutral()
        {
            var r = NewGame(); r.EndNegotiation();
            Check(!r.IsLegal("P1", GameAction.Sabotage(3)), "cannot sabotage a neutral node");
            r.State.Nodes[3].Owner = "P2";   // bare node next to P1's node 2
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Sabotage(3), ["P2"] = GameAction.Fortify(3) });
            Check(r.State.Nodes[3].Owner == "P2" && r.State.Nodes[3].Fortify == 0, "fortify resolves first, so walling a bare node saves it");
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P3"] = GameAction.Pass() });   // cooldown: not the same node two rounds running
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Sabotage(3) });
            Check(r.State.Nodes[3].Owner == null, "sabotage on a node with no walls makes it neutral");
            Check(r.State.LostNodeEver.Contains("P2"), "losing a node to sabotage counts as a loss");
            Check(!r.State.CapturedOwnedEver.Contains("P1"), "the saboteur has not captured anything");
        }

        static void TradeDoesNotCountAsLoss()
        {
            var r = Traded("P1", 1, "P2", 4, accept: true);
            r.ResolveRound(new Dictionary<string, GameAction>());
            Check(r.State.Nodes[1].Owner == "P2" && !r.State.LostNodeEver.Contains("P1") && !r.State.LostNodeEver.Contains("P2"), "trading a node away is not a loss");
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P3"] = GameAction.Move(8, 9) });   // P3 takes neutral 9
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P3"] = GameAction.Fortify(9) });
            r.EndNegotiation();
            r.ResolveRound(new Dictionary<string, GameAction> { ["P3"] = GameAction.Move(9, 10) });  // P3 captures P4's node 10 with strength 1
            Check(r.State.Nodes[10].Owner == "P3", "fortified attacker captures unfortified node");
            Check(r.State.LostNodeEver.Contains("P4") && r.State.CapturedOwnedEver.Contains("P3"), "capture records loss and capture");
        }

        static void MoveFromTradedNodeIsVoid()
        {
            // P1's offer of node 2 is accepted, and in the same round P1 also tries to move out of node 2. Trades resolve
            // first, so by the time moves resolve node 2 is P2's and the move is void.
            var r = Traded("P1", 2, "P2", 4, accept: true);
            r.ResolveRound(new Dictionary<string, GameAction> { ["P1"] = GameAction.Move(2, 3) });
            Check(r.State.Nodes[2].Owner == "P2" && r.State.Nodes[4].Owner == "P1", "trade executed");
            Check(r.State.Nodes[3].Owner == null && r.State.ActionLog.Last(a => a.player == "P1").note.StartsWith("void: node 2 was traded away"), "a move from a node traded away the same round is void, and says so");
        }

        static void GuessCandidatesAreDealtMinusYourOffer()
        {
            var r = new Referee("t", new List<string> { "P1", "P2", "P3", "P4" }, 5);
            r.EndDraft();
            Check(r.State.Dealt.Count == 12, "4 players x 3 offers = 12 dealt");
            Check(r.State.Dealt.Select(o => o.Id).SequenceEqual(r.State.Dealt.Select(o => o.Id).OrderBy(x => x, StringComparer.Ordinal)), "dealt list is sorted by id, so order leaks no seat");
            foreach (var p in r.State.Players)
            {
                var v = r.ViewFor(p);
                Check(v.DealtObjectives.Count == 12, "view lists everything dealt");
                Check(v.GuessCandidates.Count == 9, "candidates exclude the 3 you were offered");
                Check(v.GuessCandidates.All(c => c.id != v.ObjectiveId), "your own card is never a candidate");
                foreach (var q in r.State.Players.Where(q => q != p))
                    Check(v.GuessCandidates.Any(c => c.id == r.State.Assigned[q].Id), "every opponent's card is a candidate");
            }
        }

        // ----- wire format -----
        static void WireActionsRoundTrip()
        {
            var r = NewGame(); r.EndNegotiation();
            var v = r.ViewFor("P1");
            var dto = Wire.ToDto(v);
            Check(dto.Version == WireFormat.Version && dto.Phase == "act" && dto.You == "P1", "view dto basics");
            Check(dto.Left == "P2" && dto.Right == "P4", "neighbours in dto");
            Check(dto.LegalActions.Count == v.LegalActions.Count && dto.Board.Count == v.Board.Count, "dto carries legal actions and board");
            foreach (var a in v.LegalActions)
                Check(a.Equals(Wire.ToAction(Wire.ToDto(a))), "action survives dto round trip: " + a);
            Check(GameAction.Fortify(2).Equals(Wire.ToAction(new ActionDto { Type = "Fortify", To = 2 })), "fortify accepts 'to' as the node");
            Check(v.LegalActions.Any(a => a.Type == ActionType.Develop) && v.LegalActions.Any(a => a.Type == ActionType.Release), "develop and release are offered");
            Check(GameAction.Develop(2).Equals(Wire.ToAction(new ActionDto { Type = "build", Node = 2 })), "develop accepts 'build'");
            Check(GameAction.Release(1).Equals(Wire.ToAction(new ActionDto { Type = "abandon", From = 1 })), "release accepts 'abandon' and 'from'");
            Check(GameAction.Move(2, 3).Equals(Wire.ToAction(new ActionDto { Type = "MOVE", From = 2, To = 3 })), "type is case-insensitive");
            Check(Wire.ToAction(new ActionDto { Type = "teleport", From = 1, To = 4 }) == null, "unknown type is null");
            Check(Wire.ToAction(new ActionDto { Type = "move", From = 1 }) == null, "move without target is null");
        }

        static void WireResponseValidation()
        {
            var act = Wire.ToResponse(new ResponseDto { Action = new ActionDto { Type = "pass" }, Notes = "n" }, Phase.Act);
            Check(act.Action.Type == ActionType.Pass && act.Notes == "n" && act.Error == null, "act response parsed");
            var noAct = Wire.ToResponse(new ResponseDto { Notes = "n" }, Phase.Act);
            Check(noAct.Action == null && noAct.Error != null, "missing action in act phase is flagged");
            var msgs = Wire.ToResponse(new ResponseDto
            {
                Messages = new List<MessageDto>
                {
                    new MessageDto { Text = "hi" }, new MessageDto { To = "public", Text = "all" },
                    new MessageDto { To = "P2", Text = "psst" }, new MessageDto { To = "P3", Text = "  " },
                }
            }, Phase.Negotiate);
            Check(msgs.Messages.Count == 3 && msgs.Messages[0].to == null && msgs.Messages[1].to == null && msgs.Messages[2].to == "P2",
                "messages: blank or 'public' target is public, empty text dropped");
            var empty = Wire.ToResponse(null, Phase.Guess);
            Check(empty.Error != null && empty.Guesses == null, "null reply is an error, not a crash");
        }

        // ----- async seats -----
        sealed class DelayedSeat : IAgent
        {
            readonly IAgent inner; readonly int ms;
            public DelayedSeat(IBot bot, int ms) { inner = new BotAgent(bot); this.ms = ms; }
            public string Kind => "delayed";
            public async Task<PhaseResponse> Respond(PlayerView v, CancellationToken ct)
            {
                await Task.Delay(ms, ct).ConfigureAwait(false);
                return await inner.Respond(v, ct).ConfigureAwait(false);
            }
        }

        sealed class HungSeat : IAgent   // never answers and ignores cancellation, like a stuck HTTP call
        {
            public string Kind => "hung";
            public Task<PhaseResponse> Respond(PlayerView v, CancellationToken ct) => new TaskCompletionSource<PhaseResponse>().Task;
        }

        sealed class CrashingSeat : IAgent
        {
            public string Kind => "crash";
            public Task<PhaseResponse> Respond(PlayerView v, CancellationToken ct) => throw new InvalidOperationException("boom");
        }

        static GameRunner RunnerWith(Func<int, IAgent> seat, int seed = 7)
        {
            var names = new List<string> { "P1", "P2", "P3", "P4" };
            var seats = new Dictionary<string, IAgent>();
            for (int i = 0; i < names.Count; i++) seats[names[i]] = seat(i);
            return new GameRunner(new Referee("t", names, seed), seats);
        }

        static void RunnerWaitsForSlowSeats()
        {
            var run = RunnerWith(i => i == 0 ? new DelayedSeat(new GreedyBot(i), 150) : (IAgent)new BotAgent(new GreedyBot(i)));
            Check(run.BeginPhase(), "begin starts a phase");
            Check(run.PhasePending && !run.PhaseReady, "phase pending while the slow seat thinks");
            Check(run.Waiting.SequenceEqual(new[] { "P1" }), "waiting lists the slow seat");
            Check(!run.BeginPhase() && run.CompletePhase() == null, "cannot begin twice or complete early");
            var sw = Stopwatch.StartNew();
            while (!run.PhaseReady && sw.ElapsedMilliseconds < 5000) Thread.Sleep(10);
            Check(run.PhaseReady, "ready once the slow seat answers");
            Check(run.CompletePhase() == "draft complete" && run.State.Phase == Phase.Negotiate, "complete applies the phase");
            Check(run.LastResponses["P1"].Error == null && run.LastResponses["P1"].Draft != null, "slow seat's draft pick was used");
            run.RunToEnd();
            Check(run.Finished && run.State.ActionLog.All(a => a.wasLegal), "blocking StepPhase also waits for the slow seat");
        }

        static void HungSeatBecomesPassAfterTimeout()
        {
            var run = RunnerWith(i => i == 1 ? new HungSeat() : (IAgent)new BotAgent(new GreedyBot(i)));
            run.PhaseTimeout = TimeSpan.FromMilliseconds(100);
            run.Grace = TimeSpan.FromMilliseconds(50);
            run.StepPhase(); run.StepPhase(); run.StepPhase();   // draft, negotiate, act
            Check(run.State.Round == 2, "game moved on without the hung seat");
            Check(run.LastResponses["P2"].Error != null && run.LastResponses["P2"].Error.Contains("timed out"), "hung seat reported as timed out");
            Check(run.State.ActionLog.Single(a => a.player == "P2").action.Type == ActionType.Pass, "hung seat's action is a pass");
            Check(run.State.Assigned["P2"].Id == run.State.DraftOptions["P2"][0].Id, "hung seat got the default draft pick");
        }

        static void CrashingSeatBecomesPass()
        {
            var run = RunnerWith(i => i == 2 ? new CrashingSeat() : (IAgent)new BotAgent(new GreedyBot(i)));
            run.RunToEnd();
            Check(run.Finished, "game finishes despite a crashing seat");
            Check(run.LastResponses["P3"].Error == "boom", "exception message is reported");
            Check(run.State.ActionLog.Where(a => a.player == "P3").All(a => a.action.Type == ActionType.Pass), "crashing seat always passes");
        }

        static void WrappedBotsMatchDirectBots()
        {
            // The IBot constructor and the IAgent constructor must produce the same game for the same seed.
            var names = new List<string> { "P1", "P2", "P3", "P4" };
            var a = new GameRunner(new Referee("t", names, 21), names.Select((p, i) => (p, bot: (IBot)new GreedyBot(i))).ToDictionary(x => x.p, x => x.bot));
            var b = new GameRunner(new Referee("t", names, 21), names.Select((p, i) => (p, seat: (IAgent)new BotAgent(new GreedyBot(i)))).ToDictionary(x => x.p, x => x.seat));
            a.RunToEnd(); b.RunToEnd();
            Check(a.Scores.SequenceEqual(b.Scores) && a.State.ActionLog.Count == b.State.ActionLog.Count, "same seats, same game");
            for (int i = 0; i < a.State.ActionLog.Count; i++)
                Check(a.State.ActionLog[i].action.Equals(b.State.ActionLog[i].action), "identical action log");
        }
    }
}
