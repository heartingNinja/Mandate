// Mandate.Core — rules engine. Pure C#, no UnityEngine reference.
// Everything the referee decides lives here. Agents never see this file at runtime,
// but the source is public so anyone can audit it.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Mandate.Core
{
    // ---------- Constants ----------
    public static class Rules
    {
        public const int MinPlayers = 4;
        public const int MaxPlayers = 8;
        public const int NodesPerPlayer = 3;
        public const int StartingNodes = 2;
        public const int StartingProduction = 4; // every player's two starting nodes add up to this
        // The last round is secret: it is drawn from the seed (7, 8 or 9, equally likely) and nobody is told. Players
        // learn the game is over only when a round resolves, so a "last-round" strike may still be answered.
        public const int MinRounds = 7;
        public const int MaxRounds = 9;
        public const int FortifyCap = 3;
        public const int ProductionCap = 3;   // Develop raises a node's production up to this
        public const int MaxPublicMessages = 3;
        public const int MaxPrivateMessages = 3;
        public const int MaxMessageChars = 500;
        public const int ObjectivePoints = 10;
        public const int ReadPoints = 2;      // +2 per correct guess you make
        public const int GotReadPenalty = 2;  // -2 per opponent who guesses you
        public const int DraftSize = 3;       // objectives offered per player; keep 1
    }

    // ---------- Seating ----------
    // Which entrant (a row on the Table screen, an entry in seats.json) sits in which seat. Seats are equal by design,
    // but neighbours are not, so shuffling stops anyone always sitting beside the same opponents. The shuffle comes
    // from the game seed, so the same seed still replays the same game.
    public static class Seating
    {
        /// <summary>seatOf[entrant] = seat index (0 = P1). In order when shuffle is off.</summary>
        public static int[] Assign(int count, int seed, bool shuffle)
        {
            var seatOf = Enumerable.Range(0, count).ToArray();
            if (!shuffle) return seatOf;
            var rng = new Random(unchecked(seed * 7919 + 17));   // its own stream: the board and bots are unchanged
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (seatOf[i], seatOf[j]) = (seatOf[j], seatOf[i]);
            }
            return seatOf;
        }
    }

    public enum Phase { Draft, Negotiate, Act, Guess, Finished }

    // ---------- Board ----------
    public sealed class Node
    {
        public int Id;
        public int Production;          // 1..ProductionCap
        public int Fortify;             // 0..FortifyCap
        public string Owner;            // player id or null
        public List<int> Adjacent = new List<int>();
    }

    // ---------- Actions ----------
    // Trading is not an action: see TradeOffer. Every player's one action per round is one of these.
    public enum ActionType { Pass, Move, Fortify, Sabotage, Develop, Release }

    public sealed class GameAction : IEquatable<GameAction>
    {
        public ActionType Type;
        public int From = -1;           // Move: source node.
        public int To = -1;             // Move: target node. Fortify/Sabotage/Develop/Release: the node.

        public static GameAction Pass() => new GameAction { Type = ActionType.Pass };
        public static GameAction Move(int from, int to) => new GameAction { Type = ActionType.Move, From = from, To = to };
        public static GameAction Fortify(int node) => new GameAction { Type = ActionType.Fortify, To = node };
        public static GameAction Sabotage(int node) => new GameAction { Type = ActionType.Sabotage, To = node };
        public static GameAction Develop(int node) => new GameAction { Type = ActionType.Develop, To = node };
        public static GameAction Release(int node) => new GameAction { Type = ActionType.Release, To = node };

        public bool Equals(GameAction o) => o != null && Type == o.Type && From == o.From && To == o.To;
        public override bool Equals(object o) => Equals(o as GameAction);
        public override int GetHashCode() => HashCode.Combine(Type, From, To);
        public override string ToString() => Type switch
        {
            ActionType.Move => $"move({From}->{To})",
            ActionType.Fortify => $"fortify({To})",
            ActionType.Sabotage => $"sabotage({To})",
            ActionType.Develop => $"develop({To})",
            ActionType.Release => $"release({To})",
            _ => "pass"
        };
    }

    // ---------- Beliefs (logged, never scored in v1) ----------
    // Optional per-round self-report. Referee stores it; nothing in the rules reads it.
    // Used only for the post-game "who reasoned well" reconstruction.
    public sealed class BeliefReport
    {
        public int Round;
        public string Player;
        public Dictionary<string, Dictionary<string, double>> ObjectiveBeliefs = new();   // target -> (objectiveId -> prob)
        public Dictionary<string, string> TheyThinkIAm = new();                         // target -> objectiveId I think they've pinned on me
        public string Note;                                                              // free text, capped
    }

    // ---------- Messages ----------
    public sealed class Message
    {
        public int Round;
        public string From;
        public string To;               // null = public
        public string Text;
    }

    // ---------- Trades ----------
    // The only way to trade. In negotiation From offers To a swap: From gives node Give, To gives node Get. The offer is
    // checked on the spot (Problem says why it can never happen) and it is BINDING: From cannot take it back. In the act
    // phase To may accept it alongside their normal action, so a trade costs neither side their action. Accepted offers
    // swap first, before moves and releases. Offers are private between the two players; a completed trade is public.
    public sealed class TradeOffer
    {
        public int Round;
        public string From;
        public string To;               // the partner
        public int Give;                // node From gives away
        public int Get;                 // node From receives
        public string Problem;          // null = a valid offer. Otherwise why it was rejected (shown to From only)
        public bool Accepted;           // To accepted it in the act phase
        public string Outcome;          // set when the round resolves: "done", "not accepted", "rejected: ..." or "void: ..."
    }

    // ---------- Objectives ----------
    // An objective is a name plus a predicate over final state. All objective logic is here,
    // in one place, so reviewers can check every win condition.
    public sealed class Objective
    {
        public string Id;
        public string Text;             // what the player is told
        public Func<GameState, string, bool> Achieved;   // (state, playerId) => met?
        public Objective(string id, string text, Func<GameState, string, bool> f) { Id = id; Text = text; Achieved = f; }
    }

    public static class Objectives
    {
        static int Prod(GameState s, string p) => s.Nodes.Where(n => n.Owner == p).Sum(n => n.Production);
        static int Count(GameState s, string p) => s.Nodes.Count(n => n.Owner == p);
        // Places by production, with ties: your place is 1 + the number of players with strictly more production.
        // Players on equal production share a place, so two tied for most are both first and nobody is second.
        // No tie-break by node count or seat: a tie stays a tie.
        static int Place(GameState s, string p) { int mine = Prod(s, p); return 1 + s.Players.Count(q => Prod(s, q) > mine); }
        static bool IsLast(GameState s, string p) { int mine = Prod(s, p); return s.Players.All(q => Prod(s, q) >= mine); }
        static int TopProd(GameState s) => s.Players.Max(q => Prod(s, q));
        static string Left(GameState s, string p) => s.Players[(s.Players.IndexOf(p) + 1) % s.Players.Count];
        static string Right(GameState s, string p) => s.Players[(s.Players.IndexOf(p) - 1 + s.Players.Count) % s.Players.Count];

        // Gap nodes: the neutral ring node between two neighbouring seats (every multiple of 3, linked to the hub).
        // Seat i starts on 3i+1 and 3i+2, so its left gap is 3i+3 and its right gap is 3i (the last gap for seat 0).
        // Public so bots can find them from a PlayerView.
        public static int LeftGap(IList<string> players, string p) => 3 * (players.IndexOf(p) + 1);
        public static int RightGap(IList<string> players, string p) { int i = players.IndexOf(p); return i == 0 ? 3 * players.Count : 3 * i; }
        public static bool IsGap(int node) => node != 0 && node % 3 == 0;
        static bool Owns(GameState s, string p, int node) => s.Nodes[node].Owner == p;

        public static List<Objective> Pool() => new List<Objective>
        {
            // Pure
            new Objective("most_production", "Control the most total production at game end (sharing the most counts).",
                (s, p) => Place(s, p) == 1),
            new Objective("most_nodes", "Control the most nodes at game end.",
                (s, p) => s.Players.All(q => q == p || Count(s, q) < Count(s, p))),
            new Objective("hold_center", "Control node 0 at game end.",
                (s, p) => s.Nodes[0].Owner == p),
            new Objective("four_nodes", "Control at least 4 nodes at game end.",
                (s, p) => Count(s, p) >= 4),
            // 7, not 6: walls cap at 3 a node, so this cannot be met by walling just your two starting nodes; you must
            // also take and hold a third (sim 2026-10-17: 6 walls was 82% achieved, 7 walls 40%, near the average).
            new Objective("fortress", "End the game with at least 7 total fortification on nodes you control (at most 3 per node, so at least 3 nodes).",
                (s, p) => s.Nodes.Where(n => n.Owner == p).Sum(n => n.Fortify) >= 7),

            // Relational
            new Objective("left_second", "The player to your left finishes second in production (sharing second counts).",
                (s, p) => Place(s, Left(s, p)) == 2),
            new Objective("right_last", "The player to your right finishes last in production (sharing last counts).",
                (s, p) => IsLast(s, Right(s, p))),
            new Objective("left_wins", "The player to your left finishes first in production (sharing first counts).",
                (s, p) => Place(s, Left(s, p)) == 1),
            new Objective("beat_right", "Finish with at least 3 more production than the player to your right.",
                (s, p) => Prod(s, p) >= Prod(s, Right(s, p)) + 3),

            // Conditional
            new Objective("balance", "No player controls more than 5 nodes at game end, and you control at least 4.",
                (s, p) => s.Players.All(q => Count(s, q) <= 5) && Count(s, p) >= 4),
            new Objective("never_lose", "Never have a node captured from you or sabotaged to neutral during the game (trading or releasing one yourself is fine), and control at least 6 production at game end.",
                (s, p) => !s.LostNodeEver.Contains(p) && Prod(s, p) >= 6),
            new Objective("no_walls", "Total fortification on the board is 3 or less at game end, and you control at least 4 nodes.",
                (s, p) => s.Nodes.Sum(n => n.Fortify) <= 3 && Count(s, p) >= 4),
            // Accepting an offer is free, so trades alone are easy to get by giving nodes away; the production floor
            // makes the trades have to be fair ones (sim 2026-10-17: 99% achieved without it, 23% with it).
            new Objective("trader", "Complete at least 2 trades during the game, and control at least 6 production at game end.",
                (s, p) => s.TradesCompleted.GetValueOrDefault(p, 0) >= 2 && Prod(s, p) >= 6),
            new Objective("top_two", "Finish first or second in production (sharing counts) with at least 6 production, and the leader has at most 1 more than you.",
                (s, p) => Place(s, p) <= 2 && TopProd(s) - Prod(s, p) <= 1 && Prod(s, p) >= 6),

            // More pure
            // Gap cards point at your own neighbourhood, so every seat at every table size gets the same use from them.
            new Objective("hold_left_gap", "Control the gap node between you and the player to your left at game end, with 3 fortification on it.",
                (s, p) => Owns(s, p, LeftGap(s.Players, p)) && s.Nodes[LeftGap(s.Players, p)].Fortify >= 3),
            new Objective("hold_right_gap", "Control the gap node between you and the player to your right at game end, with 3 fortification on it.",
                (s, p) => Owns(s, p, RightGap(s.Players, p)) && s.Nodes[RightGap(s.Players, p)].Fortify >= 3),
            new Objective("hold_both_gaps", "Control both gap nodes beside you (left and right) at game end.",
                (s, p) => Owns(s, p, LeftGap(s.Players, p)) && Owns(s, p, RightGap(s.Players, p))),
            new Objective("two_gaps", "Control at least 2 gap nodes at game end (any gaps, beside you or not).",
                (s, p) => s.Nodes.Count(n => IsGap(n.Id) && n.Owner == p) >= 2),
            new Objective("production_eight", "Control at least 8 total production at game end.", (s, p) => Prod(s, p) >= 8),
            new Objective("contiguous_four", "Control 4 nodes that form a connected chain at game end.",
                (s, p) => LargestConnected(s, p) >= 4),

            // More relational
            new Objective("right_second", "The player to your right finishes second in production (sharing second counts).",
                (s, p) => Place(s, Right(s, p)) == 2),
            new Objective("left_last", "The player to your left finishes last in production (sharing last counts).",
                (s, p) => IsLast(s, Left(s, p))),
            new Objective("neighbours_below", "Finish with more production than both the player to your left and the player to your right.",
                (s, p) => Prod(s, p) > Prod(s, Left(s, p)) && Prod(s, p) > Prod(s, Right(s, p))),

            // More conditional
            new Objective("pacifist", "Never capture an owned node from another player (neutral captures are fine), and control at least 7 production at game end.",
                (s, p) => !s.CapturedOwnedEver.Contains(p) && Prod(s, p) >= 7),
            new Objective("saboteur", "Perform at least 3 sabotage actions during the game.",
                (s, p) => s.ActionLog.Count(a => a.player == p && a.wasLegal && a.action.Type == ActionType.Sabotage) >= 3),
            new Objective("quiet_hub", "Node 0 is neutral at game end, and you control at least 3 nodes.",
                (s, p) => s.Nodes[0].Owner == null && Count(s, p) >= 3),
            new Objective("spread", "Every player controls at least 2 nodes at game end, and you control at least 3 nodes.",
                (s, p) => s.Players.All(q => Count(s, q) >= 2) && Count(s, p) >= 3),
        };

        static int LargestConnected(GameState s, string p)
        {
            var mine = new HashSet<int>(s.Nodes.Where(n => n.Owner == p).Select(n => n.Id));
            int best = 0; var seen = new HashSet<int>();
            foreach (var start in mine)
            {
                if (seen.Contains(start)) continue;
                var stack = new Stack<int>(); stack.Push(start); seen.Add(start); int size = 0;
                while (stack.Count > 0)
                {
                    int n = stack.Pop(); size++;
                    foreach (var a in s.Nodes[n].Adjacent) if (mine.Contains(a) && seen.Add(a)) stack.Push(a);
                }
                best = Math.Max(best, size);
            }
            return best;
        }
    }

    // ---------- State ----------
    public sealed class GameState
    {
        public string GameId;
        public List<string> Players = new List<string>();          // seat order
        public List<Node> Nodes = new List<Node>();
        public Dictionary<string, Objective> Assigned = new Dictionary<string, Objective>();
        public int Round = 1;
        public int LastRound = Rules.MaxRounds;   // SECRET: the round after which the game ends. Never put in a PlayerView.
        public Phase Phase = Phase.Negotiate;
        public List<Message> Messages = new List<Message>();
        // note: the referee's record of what the action actually did ("took neutral node 9", "FAILED: ...",
        // "illegal: ..., counted as pass"). Null for a pass.
        public List<(int round, string player, GameAction action, bool wasLegal, string note)> ActionLog = new();
        public List<TradeOffer> Offers = new List<TradeOffer>();   // every offer made, valid or not, all rounds, with its outcome
        public HashSet<string> LostNodeEver = new HashSet<string>();        // had a node captured, or sabotaged to neutral
        public HashSet<string> CapturedOwnedEver = new HashSet<string>();   // captured a node from another player
        public Dictionary<string, List<Objective>> DraftOptions = new Dictionary<string, List<Objective>>();   // what each player was offered
        public List<Objective> Dealt = new List<Objective>();        // every objective dealt this game, sorted by id so order leaks nothing. Public.
        public Dictionary<string, int> TradesCompleted = new Dictionary<string, int>();
        public Dictionary<string, Dictionary<string, string>> Guesses = new();   // guesser -> (target -> objectiveId)
        public List<BeliefReport> Beliefs = new List<BeliefReport>();

        public bool Owns(string p, int node) => node >= 0 && node < Nodes.Count && Nodes[node].Owner == p;
    }

    // ---------- Player view (what an agent is allowed to see) ----------
    public sealed class PlayerView
    {
        public string GameId;
        public int Round;
        public Phase Phase;
        public string You;
        public List<string> Players;
        public string ObjectiveId;                                  // your own objective's id (null during draft)
        public string ObjectiveText;
        public bool? ObjectiveMetNow;                               // would your objective score if the game ended now? (null during draft)
        public List<Node> Board;                                    // copy
        public List<Message> PublicLog;
        public List<Message> PrivateInbox;
        public List<(int round, string player, GameAction action, string note)> ActionsHistory;   // note: see GameState.ActionLog
        public List<TradeOffer> Offers;                             // this round's offers you made (valid or not) and valid ones made to you
        public List<TradeOffer> TradeHistory;                       // earlier rounds: every completed trade (public), plus your own offers and their outcome
        public List<GameAction> LegalActions;
        public List<string> MovePreviews;                           // negotiate/act: what each of your moves and sabotages would do (Forecast.Previews)
        public List<string> Dangers;                                // negotiate/act: how opponents could capture or sabotage your nodes (Forecast.Dangers)
        public List<(string id, string text)> DealtObjectives;      // every objective in play this game. Public, sorted by id.
        public List<(string id, string text)> GuessCandidates;      // DealtObjectives minus the cards you were offered: what opponents can hold
        public List<(string id, string text)> DraftOptions;         // Draft phase only
        public Dictionary<string, int> TradesCompleted;             // public record; also derivable from TradeHistory
        public HashSet<string> LostNodeEver;                        // players who have had a node captured or sabotaged to neutral (public)
        public HashSet<string> CapturedOwnedEver;                   // players who have captured an owned node (public)
    }

    // ---------- Referee ----------
    public sealed class Referee
    {
        public readonly GameState State;
        readonly Random _rng;

        public Referee(string gameId, List<string> players, int seed)
        {
            if (players.Count < Rules.MinPlayers || players.Count > Rules.MaxPlayers)
                throw new ArgumentException($"Need {Rules.MinPlayers}-{Rules.MaxPlayers} players");
            _rng = new Random(seed);
            State = new GameState { GameId = gameId, Players = players.ToList() };
            // Its own random stream, so the board, the deal and the bots are the same as before for every seed.
            State.LastRound = new Random(unchecked(seed * 104729 + 31)).Next(Rules.MinRounds, Rules.MaxRounds + 1);
            BuildBoard();
            DealObjectives();
        }

        // Ring board: nodes 1..ring sit on a ring, each adjacent to the next and previous. Node 0 is the hub in the
        // centre, off the ring, adjacent to every 3rd ring node (the neutral between each pair of players), so every
        // seat is the same distance from the hub. Deliberately simple; swap for a hex map later without touching the rules.
        void BuildBoard()
        {
            int ring = State.Players.Count * Rules.NodesPerPlayer;
            State.Nodes.Add(new Node { Id = 0, Production = 3 });
            for (int i = 1; i <= ring; i++)
                State.Nodes.Add(new Node { Id = i, Production = 1 + _rng.Next(3) });
            for (int i = 1; i <= ring; i++)
            {
                State.Nodes[i].Adjacent.Add(i % ring + 1);              // next, wrapping ring -> 1
                State.Nodes[i].Adjacent.Add((i + ring - 2) % ring + 1); // previous, wrapping 1 -> ring
                if (i % 3 == 0) { State.Nodes[i].Adjacent.Add(0); State.Nodes[0].Adjacent.Add(i); }
            }
            // Starting nodes: each player gets seat*3+1 and seat*3+2. Node 0 and multiples of 3 start neutral.
            // The first node keeps its roll and the second makes up the rest, so every player starts with
            // the same production (1+3, 2+2 or 3+1). Neutral nodes stay random.
            for (int s = 0; s < State.Players.Count; s++)
            {
                var first = State.Nodes[s * 3 + 1];
                var second = State.Nodes[s * 3 + 2];
                first.Owner = State.Players[s];
                second.Owner = State.Players[s];
                second.Production = Rules.StartingProduction - first.Production;
            }
        }

        void DealObjectives()
        {
            var pool = Objectives.Pool().OrderBy(_ => _rng.Next()).ToList();
            // Draft: each player is offered DraftSize cards and keeps one.
            int per = Math.Min(Rules.DraftSize, pool.Count / State.Players.Count);
            for (int i = 0; i < State.Players.Count; i++)
                State.DraftOptions[State.Players[i]] = pool.Skip(i * per).Take(per).ToList();
            // Everything dealt is public knowledge and is what the end-game guesses are drawn from.
            // Sorted by id so the order says nothing about who was offered what.
            State.Dealt = pool.Take(State.Players.Count * per).OrderBy(o => o.Id, StringComparer.Ordinal).ToList();
            State.Phase = Phase.Draft;
        }

        // ----- Draft -----
        // Invalid or missing pick = first option. Never throws on agent input.
        public void SubmitDraft(string p, string objectiveId)
        {
            if (State.Phase != Phase.Draft || !State.DraftOptions.ContainsKey(p)) return;
            var pick = State.DraftOptions[p].FirstOrDefault(o => o.Id == objectiveId) ?? State.DraftOptions[p][0];
            State.Assigned[p] = pick;
        }

        public void EndDraft()
        {
            if (State.Phase != Phase.Draft) return;
            foreach (var p in State.Players)
                if (!State.Assigned.ContainsKey(p)) State.Assigned[p] = State.DraftOptions[p][0];
            State.Phase = Phase.Negotiate;
        }

        // ----- Views -----
        public PlayerView ViewFor(string p)
        {
            if (!State.Players.Contains(p)) throw new ArgumentException("unknown player");
            State.Assigned.TryGetValue(p, out var obj);
            var offered = new HashSet<string>(State.DraftOptions.TryGetValue(p, out var mine) ? mine.Select(o => o.Id) : Enumerable.Empty<string>());
            // Negotiation never changes the board, so during negotiate this already shows what next act phase allows.
            bool planning = State.Phase == Phase.Negotiate || State.Phase == Phase.Act;
            return new PlayerView
            {
                GameId = State.GameId,
                Round = State.Round,
                Phase = State.Phase,
                You = p,
                Players = State.Players.ToList(),
                ObjectiveId = obj?.Id,
                ObjectiveText = obj?.Text,
                ObjectiveMetNow = obj?.Achieved(State, p),
                Board = State.Nodes.Select(n => new Node { Id = n.Id, Production = n.Production, Fortify = n.Fortify, Owner = n.Owner, Adjacent = n.Adjacent.ToList() }).ToList(),
                PublicLog = State.Messages.Where(m => m.To == null).ToList(),
                PrivateInbox = State.Messages.Where(m => m.To == p).ToList(),
                ActionsHistory = State.ActionLog.Select(a => (a.round, a.player, a.action, a.note)).ToList(),
                Offers = State.Offers.Where(o => o.Round == State.Round && o.Outcome == null && (o.From == p || (o.To == p && o.Problem == null)))
                    .Select(Copy).ToList(),
                TradeHistory = State.Offers.Where(o => o.Outcome != null && (o.Outcome == "done" || o.From == p || (o.To == p && o.Problem == null)))
                    .Select(Copy).ToList(),
                LegalActions = State.Phase == Phase.Act ? LegalActions(p) : new List<GameAction>(),
                MovePreviews = planning ? Forecast.Previews(State, p, LegalActions(p)) : new List<string>(),
                Dangers = planning ? Forecast.Dangers(State, p, LegalActions) : new List<string>(),
                DealtObjectives = State.Dealt.Select(o => (o.Id, o.Text)).ToList(),
                GuessCandidates = State.Dealt.Where(o => !offered.Contains(o.Id)).Select(o => (o.Id, o.Text)).ToList(),
                DraftOptions = State.Phase == Phase.Draft && State.DraftOptions.TryGetValue(p, out var opts)
                    ? opts.Select(o => (o.Id, o.Text)).ToList() : new List<(string, string)>(),
                TradesCompleted = new Dictionary<string, int>(State.TradesCompleted),
                LostNodeEver = new HashSet<string>(State.LostNodeEver),
                CapturedOwnedEver = new HashSet<string>(State.CapturedOwnedEver),
            };
        }

        static TradeOffer Copy(TradeOffer o) =>
            new TradeOffer { Round = o.Round, From = o.From, To = o.To, Give = o.Give, Get = o.Get, Problem = o.Problem, Accepted = o.Accepted, Outcome = o.Outcome };

        // ----- Legality (the single source of truth) -----
        public List<GameAction> LegalActions(string p)
        {
            var list = new List<GameAction> { GameAction.Pass() };
            var mine = State.Nodes.Where(n => n.Owner == p).ToList();
            foreach (var n in mine)
            {
                foreach (var adj in n.Adjacent)
                    if (State.Nodes[adj].Owner != p) list.Add(GameAction.Move(n.Id, adj));
                if (n.Fortify < Rules.FortifyCap) list.Add(GameAction.Fortify(n.Id));
                if (n.Production < Rules.ProductionCap) list.Add(GameAction.Develop(n.Id));
                list.Add(GameAction.Release(n.Id));
            }
            // Cooldown: not the node you sabotaged last round (a different node is fine).
            int lastSabotaged = -1;
            var last = State.ActionLog.LastOrDefault(a => a.player == p && a.round == State.Round - 1);
            if (last.action != null && last.action.Type == ActionType.Sabotage) lastSabotaged = last.action.To;
            // Sabotage reaches enemy nodes next to one of yours. With walls it loses one, with none it goes neutral.
            var reach = new HashSet<int>(mine.SelectMany(n => n.Adjacent));
            foreach (var n in State.Nodes)
                if (n.Owner != null && n.Owner != p && n.Id != lastSabotaged && reach.Contains(n.Id))
                    list.Add(GameAction.Sabotage(n.Id));
            return list;
        }

        public bool IsLegal(string p, GameAction a) => LegalActions(p).Contains(a);

        // ----- Negotiation -----
        // Returns accepted messages; drops anything over caps or malformed. Never throws on agent input.
        public List<Message> SubmitMessages(string p, List<(string to, string text)> msgs)
        {
            if (State.Phase != Phase.Negotiate || msgs == null) return new List<Message>();
            var accepted = new List<Message>();
            int pub = 0, priv = 0;
            foreach (var (to, raw) in msgs)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var text = raw.Length > Rules.MaxMessageChars ? raw.Substring(0, Rules.MaxMessageChars) : raw;
                if (to == null) { if (pub++ >= Rules.MaxPublicMessages) continue; }
                else
                {
                    if (to == p || !State.Players.Contains(to)) continue;
                    if (priv++ >= Rules.MaxPrivateMessages) continue;
                }
                var m = new Message { Round = State.Round, From = p, To = to, Text = text };
                State.Messages.Add(m);
                accepted.Add(m);
            }
            return accepted;
        }

        // Trade offers: checked against the board now, which is the board the act phase resolves on. One offer per
        // partner per round; a second to the same partner replaces the first. Once negotiation closes an offer is
        // binding. Returns every offer recorded, rejected ones included (with Problem set), so the offerer can be told why.
        public List<TradeOffer> SubmitOffers(string p, List<(string partner, int give, int get)> offers)
        {
            if (State.Phase != Phase.Negotiate || offers == null || !State.Players.Contains(p)) return new List<TradeOffer>();
            var recorded = new List<TradeOffer>();
            foreach (var (partner, give, get) in offers.Take(State.Players.Count - 1))
            {
                var o = new TradeOffer { Round = State.Round, From = p, To = partner, Give = give, Get = get, Problem = OfferProblem(p, partner, give, get) };
                State.Offers.RemoveAll(x => x.Round == o.Round && x.From == p && x.To == partner);
                State.Offers.Add(o);
                recorded.Add(o);
            }
            return recorded;
        }

        string OfferProblem(string p, string partner, int give, int get)
        {
            if (partner == p) return "you cannot trade with yourself";
            if (partner == null || !State.Players.Contains(partner)) return $"there is no player called {partner ?? "(none)"}";
            if (!State.Owns(p, give)) return give < 0 || give >= State.Nodes.Count ? $"there is no node {give}" : $"you do not own node {give}";
            if (!State.Owns(partner, get))
                return get < 0 || get >= State.Nodes.Count ? $"there is no node {get}"
                     : State.Nodes[get].Owner == null ? $"node {get} is neutral; only owned nodes can be traded"
                     : $"node {get} belongs to {State.Nodes[get].Owner}, not {partner}";
            return null;
        }

        public void EndNegotiation() { if (State.Phase == Phase.Negotiate) State.Phase = Phase.Act; }

        // Accepting trade offers, in the act phase, as well as the seat's normal action. 'fromPlayers' names the
        // offerers whose offers to p this round are accepted. Returns the offers accepted. Never throws on agent input.
        public List<TradeOffer> AcceptOffers(string p, List<string> fromPlayers)
        {
            if (State.Phase != Phase.Act || fromPlayers == null) return new List<TradeOffer>();
            var accepted = State.Offers.Where(o => o.Round == State.Round && o.To == p && o.Problem == null && fromPlayers.Contains(o.From)).ToList();
            foreach (var o in accepted) o.Accepted = true;
            return accepted;
        }

        // ----- Beliefs -----
        // Accepted during Negotiate or Act. One report per player per round; a second replaces the first.
        // Unknown targets/objective ids are dropped silently. Never affects rules or score.
        public void SubmitBeliefs(string p, BeliefReport b)
        {
            if (b == null || !State.Players.Contains(p)) return;
            if (State.Phase != Phase.Negotiate && State.Phase != Phase.Act) return;
            var ids = new HashSet<string>(Objectives.Pool().Select(o => o.Id));
            var clean = new BeliefReport { Round = State.Round, Player = p, Note = b.Note?.Length > Rules.MaxMessageChars ? b.Note.Substring(0, Rules.MaxMessageChars) : b.Note };
            foreach (var (target, dist) in b.ObjectiveBeliefs ?? new())
                if (target != p && State.Players.Contains(target) && dist != null)
                    clean.ObjectiveBeliefs[target] = dist.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var (target, id) in b.TheyThinkIAm ?? new())
                if (target != p && State.Players.Contains(target) && id != null && ids.Contains(id)) clean.TheyThinkIAm[target] = id;
            State.Beliefs.RemoveAll(x => x.Player == p && x.Round == State.Round);
            State.Beliefs.Add(clean);
        }

        // ----- Resolution -----
        // Takes every player's submitted action (null = no response). Illegal -> Pass, logged.
        public void ResolveRound(Dictionary<string, GameAction> submitted)
        {
            if (State.Phase != Phase.Act) throw new InvalidOperationException("not in Act phase");
            submitted ??= new Dictionary<string, GameAction>();

            var acts = new Dictionary<string, GameAction>();
            var legalOf = new Dictionary<string, bool>();
            foreach (var p in State.Players)
            {
                submitted.TryGetValue(p, out var a);
                legalOf[p] = a != null && IsLegal(p, a);
                acts[p] = legalOf[p] ? a : GameAction.Pass();
            }

            // Every action is logged with a note saying what actually came of it (filled in step by step below), so
            // nobody mistakes an unmatched trade, a failed attack or a cancelled wall for one that worked. Pass: no note.
            var notes = new Dictionary<string, string>();
            foreach (var p in State.Players)
            {
                submitted.TryGetValue(p, out var a);
                notes[p] = legalOf[p] ? null : a == null ? "no usable answer, counted as pass" : $"illegal: {a}, counted as pass";
            }

            // 1. Trades: every offer accepted this round swaps its two nodes, costing neither side their action. Offers
            //    were checked when made and the board has not changed since, so each one can go through; they are taken in
            //    the order made, and a node already swapped this round voids a later offer that uses it. Walls stay put.
            var swapped = new HashSet<int>();
            foreach (var o in State.Offers.Where(o => o.Round == State.Round))
            {
                if (o.Problem != null) { o.Outcome = "rejected: " + o.Problem; continue; }
                if (!o.Accepted) { o.Outcome = "not accepted"; continue; }
                if (swapped.Contains(o.Give) || swapped.Contains(o.Get)) { o.Outcome = "void: one of its nodes was already traded this round"; continue; }
                State.Nodes[o.Give].Owner = o.To;
                State.Nodes[o.Get].Owner = o.From;
                swapped.Add(o.Give); swapped.Add(o.Get);
                State.TradesCompleted[o.From] = State.TradesCompleted.GetValueOrDefault(o.From) + 1;
                State.TradesCompleted[o.To] = State.TradesCompleted.GetValueOrDefault(o.To) + 1;
                o.Outcome = "done";
            }

            // 2. Moves: contested by attackers' source fortify; defender wins ties; equal attackers cancel.
            //    A move whose source was traded away this round is void.
            foreach (var (p, a) in acts.Where(kv => kv.Value.Type == ActionType.Move && State.Nodes[kv.Value.From].Owner != kv.Key))
                notes[p] = $"void: node {a.From} was traded away before moves resolved";
            var moves = acts.Where(kv => kv.Value.Type == ActionType.Move && State.Nodes[kv.Value.From].Owner == kv.Key)
                            .GroupBy(kv => kv.Value.To);
            var newOwner = new Dictionary<int, string>();
            foreach (var g in moves)
            {
                int target = g.Key;
                var defender = State.Nodes[target].Owner;
                int defense = defender == null ? 0 : State.Nodes[target].Fortify;
                var attackers = g.Select(kv => (p: kv.Key, from: kv.Value.From, str: State.Nodes[kv.Value.From].Fortify)).ToList();
                int best = attackers.Max(x => x.str);
                var top = attackers.Where(x => x.str == best).ToList();
                // One strongest attacker wins: a neutral node outright, an owned one only with more fort than its walls.
                string winner = top.Count == 1 && (defender == null || best > defense) ? top[0].p : null;
                if (winner != null) newOwner[target] = winner;
                foreach (var x in attackers)
                    notes[x.p] = x.p == winner ? (defender == null ? $"took neutral node {target}" : $"captured node {target} from {defender}")
                               : MoveFailure(x.p, x.from, x.str, attackers.Select(y => (y.p, y.str)).ToList(), target, defender, defense);
            }
            foreach (var (node, p) in newOwner)
            {
                var prev = State.Nodes[node].Owner;
                if (prev != null) { State.CapturedOwnedEver.Add(p); State.LostNodeEver.Add(prev); }
                State.Nodes[node].Owner = p; State.Nodes[node].Fortify = 0;
            }

            // 3. Releases: the node goes neutral with no walls and keeps its production. Not a loss for never_lose.
            //    Resolves after moves, so nobody can take it the round it is released, and releasing a node that
            //    is captured this round is void (you can't release your way out of an attack).
            foreach (var (p, a) in acts.Where(kv => kv.Value.Type == ActionType.Release))
            {
                if (State.Nodes[a.To].Owner == p) { State.Nodes[a.To].Owner = null; State.Nodes[a.To].Fortify = 0; notes[p] = $"node {a.To} is now neutral"; }
                else notes[p] = $"void: you no longer owned node {a.To}";
            }

            // 4. Fortify and develop. Both need you to still own the node, so one captured this round is void.
            var fortified = new Dictionary<string, (int node, int before)>();
            foreach (var (p, a) in acts.Where(kv => kv.Value.Type == ActionType.Fortify))
            {
                if (State.Nodes[a.To].Owner == p && State.Nodes[a.To].Fortify < Rules.FortifyCap) { fortified[p] = (a.To, State.Nodes[a.To].Fortify); State.Nodes[a.To].Fortify++; }
                else notes[p] = State.Nodes[a.To].Owner == p ? $"void: node {a.To} is already at fort {Rules.FortifyCap}"
                              : swapped.Contains(a.To) ? $"void: you traded node {a.To} away before the walls went up"
                              : $"void: you lost node {a.To} before the walls went up";
            }
            foreach (var (p, a) in acts.Where(kv => kv.Value.Type == ActionType.Develop))
            {
                var n = State.Nodes[a.To];
                if (n.Owner == p && n.Production < Rules.ProductionCap) { notes[p] = $"node {a.To} production {n.Production} -> {n.Production + 1}"; n.Production++; }
                else notes[p] = n.Owner == p ? $"void: node {a.To} is already at production {Rules.ProductionCap}"
                              : swapped.Contains(a.To) ? $"void: you traded node {a.To} away before it was developed"
                              : $"void: you lost node {a.To} before it was developed";
            }

            // 5. Sabotage: -1 wall, or a node with no walls goes neutral (a loss for its owner, not a capture for the saboteur).
            //    Fortify resolved first, so walling a bare node this round saves it.
            foreach (var (p, a) in acts.Where(kv => kv.Value.Type == ActionType.Sabotage))
            {
                var n = State.Nodes[a.To];
                if (n.Fortify > 0) { notes[p] = $"node {a.To} fort {n.Fortify} -> {n.Fortify - 1}"; n.Fortify--; }
                else if (n.Owner != null && n.Owner != p) { notes[p] = $"node {a.To} had no walls: {n.Owner} lost it, now neutral"; State.LostNodeEver.Add(n.Owner); n.Owner = null; }
                else notes[p] = n.Owner == p ? $"no effect: node {a.To} became yours this round" : $"no effect: node {a.To} was already neutral";
            }

            // A wall knocked straight back down by a sabotage the same round is the easiest thing to misread, so say it.
            foreach (var (p, (node, before)) in fortified)
            {
                var n = State.Nodes[node];
                notes[p] = $"node {node} fort {before} -> {before + 1}"
                         + (n.Owner != p ? ", then sabotaged away: you lost it, now neutral"
                          : n.Fortify != before + 1 ? $", then sabotage took it back to fort {n.Fortify}" : "");
            }

            foreach (var p in State.Players) State.ActionLog.Add((State.Round, p, acts[p], legalOf[p], notes[p]));

            if (State.Round >= State.LastRound) State.Phase = Phase.Guess;
            else
            {
                State.Round++;
                State.Phase = Phase.Negotiate;
            }
        }

        // Why a move into 'target' did not take it: beaten or matched by another mover, or not enough walls to beat the defender.
        static string MoveFailure(string p, int from, int str, List<(string p, int str)> attackers, int target, string defender, int defense)
        {
            var stronger = attackers.Where(x => x.p != p && x.str > str).Select(x => x.p).ToList();
            if (stronger.Count > 0) return $"FAILED: {string.Join(" and ", stronger)} also moved into node {target} with more fort than your {str}";
            var equal = attackers.Where(x => x.p != p && x.str == str).Select(x => x.p).ToList();
            if (equal.Count > 0 && (defender == null || str > defense))
                return $"FAILED: {string.Join(" and ", equal)} also moved into node {target} with the same fort ({str}), so the moves cancelled";
            return $"FAILED: your node {from} had fort {str} and {defender}'s node {target} had fort {defense}; you need MORE fort than the defender (a tie goes to the defender)";
        }

        /// <summary>True when a logged action's note says it came to nothing (failed, cancelled, void, illegal or missing).</summary>
        public static bool CameToNothing(string note) =>
            note != null && (note.StartsWith("FAILED") || note.StartsWith("void") || note.StartsWith("no effect")
                             || note.StartsWith("illegal") || note.StartsWith("no usable"));

        // ----- Guessing & scoring -----
        public void SubmitGuesses(string p, Dictionary<string, string> guesses)
        {
            if (State.Phase != Phase.Guess || guesses == null) return;
            State.Guesses[p] = guesses.Where(kv => kv.Key != p && State.Players.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public Dictionary<string, int> Score()
        {
            State.Phase = Phase.Finished;
            var score = new Dictionary<string, int>();
            foreach (var p in State.Players)
            {
                int s = State.Assigned[p].Achieved(State, p) ? Rules.ObjectivePoints : 0;
                if (State.Guesses.TryGetValue(p, out var mine))
                    s += Rules.ReadPoints * mine.Count(kv => State.Assigned[kv.Key].Id == kv.Value);
                s -= Rules.GotReadPenalty * State.Guesses.Count(kv => kv.Key != p && kv.Value.TryGetValue(p, out var g) && g == State.Assigned[p].Id);
                score[p] = s;
            }
            return score;
        }
    }
}
