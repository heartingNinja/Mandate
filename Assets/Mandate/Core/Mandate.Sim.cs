// Mandate.Sim — scripted bots, a phase-by-phase game runner, and a batch simulator for balancing.
// Pure C#. Bots only ever see a PlayerView (the same thing an LLM agent gets), never GameState.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mandate.Core;

namespace Mandate.Sim
{
    // ---------- Bot contract ----------
    // One call per phase. Every method may return null; the runner treats null as "no input".
    public interface IBot
    {
        string Kind { get; }
        string PickDraft(PlayerView v);                       // objective id from v.DraftOptions
        List<(string to, string text)> Talk(PlayerView v);   // messages this round (to == null is public)
        BeliefReport Beliefs(PlayerView v);                   // optional self-report
        GameAction Act(PlayerView v);                         // one action from v.LegalActions
        Dictionary<string, string> Guess(PlayerView v);      // opponent -> objective id
    }

    // Optional extra for a bot that trades: it makes formal offers in negotiation and accepts offers in the act phase.
    // The greedy bot and the human seat do.
    public interface IOfferMaker
    {
        List<(string partner, int give, int get)> Offers(PlayerView v);   // negotiate: at most one per partner
        List<string> Accepts(PlayerView v);                                // act: offerers whose offers in v.Offers to accept
    }

    public static class Bots
    {
        public static IBot Make(string kind, int seed)
        {
            switch ((kind ?? "greedy").ToLowerInvariant())
            {
                case "random": return new RandomBot(seed);
                case "greedy": return new GreedyBot(seed);
                default: throw new ArgumentException("unknown bot kind: " + kind);
            }
        }

        // "mixed" alternates greedy/random by seat; anything else is used for every seat.
        public static string KindForSeat(string kind, int seat) =>
            kind == "mixed" ? (seat % 2 == 0 ? "greedy" : "random") : kind;
    }

    // ---------- What a bot can work out from its view ----------
    public sealed class Sight
    {
        public readonly PlayerView V;
        public readonly string Me;
        public readonly List<Node> B;
        public readonly Dictionary<string, int> Prod = new(), Cnt = new();
        public readonly string Left, Right;

        public Sight(PlayerView v)
        {
            V = v; Me = v.You; B = v.Board;
            foreach (var p in v.Players)
            {
                Prod[p] = B.Where(n => n.Owner == p).Sum(n => n.Production);
                Cnt[p] = B.Count(n => n.Owner == p);
            }
            int i = v.Players.IndexOf(Me), n = v.Players.Count;
            Left = v.Players[(i + 1) % n];
            Right = v.Players[(i - 1 + n) % n];
        }

        public int Dist(int from, int to)
        {
            if (from == to) return 0;
            var dist = new Dictionary<int, int> { [from] = 0 };
            var q = new Queue<int>(); q.Enqueue(from);
            while (q.Count > 0)
            {
                int n = q.Dequeue();
                foreach (var a in B[n].Adjacent)
                {
                    if (dist.ContainsKey(a)) continue;
                    dist[a] = dist[n] + 1;
                    if (a == to) return dist[a];
                    q.Enqueue(a);
                }
            }
            return 99;
        }

        // An adjacent enemy node could beat this node's defence next round.
        public bool Threatened(Node n) =>
            n.Adjacent.Any(a => B[a].Owner != null && B[a].Owner != Me && B[a].Fortify > n.Fortify);

        public int MyFortify => B.Where(n => n.Owner == Me).Sum(n => n.Fortify);

        // A GameState rebuilt from public information only, so any objective predicate can be evaluated for any player.
        // Everything copied here is visible to every seat: the board, the action history, trades, captures.
        public GameState Replica()
        {
            var s = new GameState { GameId = V.GameId, Players = V.Players.ToList(), Round = V.Round, Phase = V.Phase };
            s.Nodes = B.Select(n => new Node { Id = n.Id, Production = n.Production, Fortify = n.Fortify, Owner = n.Owner, Adjacent = n.Adjacent.ToList() }).ToList();
            foreach (var a in V.ActionsHistory) s.ActionLog.Add((a.round, a.player, a.action, true, a.note));
            foreach (var kv in V.TradesCompleted ?? new Dictionary<string, int>()) s.TradesCompleted[kv.Key] = kv.Value;
            s.LostNodeEver.UnionWith(V.LostNodeEver ?? new HashSet<string>());
            s.CapturedOwnedEver.UnionWith(V.CapturedOwnedEver ?? new HashSet<string>());
            return s;
        }

        // How well each guess candidate fits each opponent, from public information alone.
        // "True for them right now" is the signal, discounted by how many players it is also true for:
        // a goal the whole table currently meets says nothing about who holds it.
        public Dictionary<string, Dictionary<string, double>> Plausibility()
        {
            var rep = Replica();
            var pool = Objectives.Pool().ToDictionary(o => o.Id);
            var metBy = new Dictionary<string, HashSet<string>>();
            foreach (var (id, _) in V.GuessCandidates ?? new List<(string, string)>())
                if (pool.TryGetValue(id, out var obj))
                    metBy[id] = new HashSet<string>(V.Players.Where(q => Safe(obj, rep, q)));
            var result = new Dictionary<string, Dictionary<string, double>>();
            foreach (var q in V.Players.Where(q => q != Me))
                result[q] = metBy.ToDictionary(kv => kv.Key, kv => kv.Value.Contains(q) ? 1.0 / kv.Value.Count : 0.05);
            return result;
        }

        static bool Safe(Objective o, GameState s, string p) { try { return o.Achieved(s, p); } catch { return false; } }
    }

    // ---------- Random bot: picks an action type, then a random action of that type ----------
    public sealed class RandomBot : IBot
    {
        readonly Random _rng;
        public RandomBot(int seed) { _rng = new Random(seed); }
        public string Kind => "random";

        public string PickDraft(PlayerView v) => v.DraftOptions.Count == 0 ? null : v.DraftOptions[_rng.Next(v.DraftOptions.Count)].id;
        public List<(string to, string text)> Talk(PlayerView v) => null;
        public BeliefReport Beliefs(PlayerView v) => null;

        public GameAction Act(PlayerView v)
        {
            var types = v.LegalActions.Select(a => a.Type).Distinct().ToList();
            var t = types[_rng.Next(types.Count)];
            var ofType = v.LegalActions.Where(a => a.Type == t).ToList();
            return ofType[_rng.Next(ofType.Count)];
        }

        public Dictionary<string, string> Guess(PlayerView v)
        {
            var c = v.GuessCandidates;
            if (c == null || c.Count == 0) return new Dictionary<string, string>();
            return v.Players.Where(q => q != v.You).ToDictionary(q => q, q => c[_rng.Next(c.Count)].id);
        }
    }

    // ---------- Greedy bot: one-round heuristic keyed on its own objective ----------
    // Deliberately simple. Its job is to give the sim a baseline that is better than random,
    // and to exercise every code path (messages, beliefs, trades, sabotage) so the viewer has something to show.
    public sealed class GreedyBot : IBot, IOfferMaker
    {
        readonly Random _rng;
        int _partnerCursor;

        public GreedyBot(int seed) { _rng = new Random(seed); }
        public string Kind => "greedy";

        public string PickDraft(PlayerView v) => v.DraftOptions.Count == 0 ? null : v.DraftOptions[0].id;   // first = uniformly random, keeps sim sampling fair

        public List<(string to, string text)> Talk(PlayerView v)
        {
            var msgs = new List<(string, string)>();
            if (v.ObjectiveId == "trader") msgs.Add((null, "Open to trades this round. Ask me."));
            else if (v.Round == 1) msgs.Add((null, "Let's keep the centre neutral and grow outward."));   // cheap talk; gives the viewer some chat
            return msgs;
        }

        // Only the trader makes offers: my best node for their worst, so any greedy partner gains by accepting.
        public List<(string partner, int give, int get)> Offers(PlayerView v)
        {
            var list = new List<(string, int, int)>();
            if (v.ObjectiveId != "trader") return list;
            var s = new Sight(v);
            var others = v.Players.Where(q => q != v.You && s.Cnt[q] > 0).ToList();
            if (others.Count == 0 || s.Cnt[v.You] == 0) return list;
            var partner = others[_partnerCursor++ % others.Count];
            var give = s.B.Where(n => n.Owner == v.You).OrderByDescending(n => n.Production).First();
            var get = s.B.Where(n => n.Owner == partner).OrderBy(n => n.Production).First();
            list.Add((partner, give.Id, get.Id));
            return list;
        }

        // Accepting costs nothing, so take any offer that does not lose production (the trader takes every offer).
        // An offer's Give is the node I would receive; its Get is the node I would hand over.
        public List<string> Accepts(PlayerView v) =>
            (v.Offers ?? new List<TradeOffer>()).Where(o => o.To == v.You && o.Problem == null
                && (v.ObjectiveId == "trader" || v.Board[o.Give].Production >= v.Board[o.Get].Production))
                .Select(o => o.From).ToList();

        public BeliefReport Beliefs(PlayerView v)
        {
            var s = new Sight(v);
            var b = new BeliefReport { Note = "fit to public information" };
            foreach (var (q, dist) in s.Plausibility())
            {
                double sum = dist.Values.Sum();
                if (sum <= 0) continue;
                b.ObjectiveBeliefs[q] = dist.OrderByDescending(kv => kv.Value).Take(3)
                    .ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / sum, 2));
            }
            return b;
        }

        // Each card was dealt exactly once, so never pin the same objective on two opponents:
        // settle the most confident read first and remove its card from the running.
        public Dictionary<string, string> Guess(PlayerView v)
        {
            var s = new Sight(v);
            var fit = s.Plausibility();
            var used = new HashSet<string>();
            var guess = new Dictionary<string, string>();
            foreach (var q in fit.Keys.OrderByDescending(q => fit[q].Count == 0 ? 0 : fit[q].Values.Max()).ToList())
            {
                var best = fit[q].Where(kv => !used.Contains(kv.Key))
                    .OrderByDescending(kv => kv.Value).ThenBy(kv => _rng.Next()).FirstOrDefault();
                if (best.Key == null) continue;
                guess[q] = best.Key;
                used.Add(best.Key);
            }
            return guess;
        }

        // The one node this objective wants held, or -1. For two-node cards, aim at whichever is still missing.
        static int GapTarget(PlayerView v, Sight s, string obj)
        {
            int left = Objectives.LeftGap(v.Players, v.You), right = Objectives.RightGap(v.Players, v.You);
            switch (obj)
            {
                case "hold_center": return 0;
                case "hold_left_gap": return left;
                case "hold_right_gap": return right;
                case "hold_both_gaps":
                case "two_gaps":   // any two will do, and the two beside you are the nearest
                    return s.B[left].Owner != v.You ? left : right;
                default: return -1;
            }
        }

        public GameAction Act(PlayerView v)
        {
            var s = new Sight(v);
            string obj = v.ObjectiveId ?? "";
            int target = GapTarget(v, s, obj);
            string friend = obj == "left_wins" || obj == "left_second" ? s.Left : obj == "right_second" ? s.Right : null;
            var rivals = new HashSet<string>(v.Players.Where(q => q != v.You && q != friend));
            if (obj == "beat_right" || obj == "right_last") rivals = new HashSet<string> { s.Right };
            if (obj == "left_last") rivals = new HashSet<string> { s.Left };
            if (obj == "neighbours_below") rivals = new HashSet<string> { s.Left, s.Right };
            int sabotagesSoFar = v.ActionsHistory.Count(a => a.player == v.You && a.action.Type == ActionType.Sabotage);

            GameAction best = GameAction.Pass(); double bestScore = double.NegativeInfinity;
            foreach (var a in v.LegalActions)
            {
                double val = 0;
                switch (a.Type)
                {
                    case ActionType.Pass: val = 0; break;

                    case ActionType.Move:
                        {
                            var t = s.B[a.To]; var src = s.B[a.From];
                            if (t.Owner == null) val = 3 + t.Production;
                            else
                            {
                                val = src.Fortify > t.Fortify ? 2 + t.Production : -50;
                                if (rivals.Contains(t.Owner)) val += 2;
                                if (t.Owner == friend) val = -100;
                                if (obj == "pacifist") val = -100;
                                if (obj == "spread" && s.Cnt[t.Owner] <= 2) val = -100;
                                if (obj == "balance" && s.Cnt[t.Owner] > 5 && val > 0) val += 6;
                            }
                            if (target >= 0)
                            {
                                int d0 = s.Dist(a.From, target), d1 = s.Dist(a.To, target);
                                if (a.To == target && val > 0) val += 8; else if (d1 < d0 && val > 0) val += 3 * (d0 - d1);
                            }
                            if (obj == "never_lose" && t.Owner != null) val = -100;   // grab neutrals, never provoke
                            if (obj == "quiet_hub" && a.To == 0) val = -100;
                            if (obj == "balance" && s.Cnt[s.Me] >= 5 && !(t.Owner != null && s.Cnt[t.Owner] > 5)) val = -100;
                            if (friend != null && s.Prod[s.Me] >= s.Prod[friend]) val -= 4;
                            if (obj == "no_walls") val -= 1;
                            break;
                        }

                    case ActionType.Fortify:
                        {
                            var n = s.B[a.To];
                            val = 1 + 0.5 * n.Production + (s.Threatened(n) ? 2 : 0);
                            if (obj == "fortress" && s.MyFortify < 7) val += 5;
                            if (obj == "never_lose") val += 4;
                            if (target == n.Id) val += 6;
                            if (obj == "no_walls") val = -100;
                            break;
                        }

                    case ActionType.Develop:
                        {
                            var n = s.B[a.To];
                            val = 1.5 - (s.Threatened(n) ? 1 : 0);
                            if (obj == "most_production" || obj == "production_eight" || obj == "pacifist" || obj == "never_lose"
                                || obj == "top_two" || obj == "beat_right" || obj == "neighbours_below") val += 2.5;
                            if (friend != null && s.Prod[s.Me] >= s.Prod[friend]) val -= 3;
                            break;
                        }

                    case ActionType.Release:
                        {
                            var n = s.B[a.To];
                            val = -20;
                            if (obj == "quiet_hub" && n.Id == 0) val = 10;
                            if (obj == "balance" && s.Cnt[s.Me] > 5) val = 6 - n.Production;   // shed the weakest
                            break;
                        }

                    case ActionType.Sabotage:
                        {
                            var n = s.B[a.To];
                            val = 0.5 + (rivals.Contains(n.Owner) ? 1.5 : 0);
                            if (n.Fortify == 0)
                            {
                                // Bare node: this knocks it back to neutral.
                                val += 0.5 * n.Production;   // worth less than taking a neutral yourself
                                if (obj == "quiet_hub" && n.Id == 0) val += 10;
                                if (obj == "spread" && s.Cnt[n.Owner] <= 2) val = -100;
                                if (obj == "balance" && s.Cnt[n.Owner] > 5) val += 6;
                            }
                            if (obj == "saboteur") val += sabotagesSoFar < 3 ? 8 : 1;
                            if (obj == "no_walls") val += 6;
                            if (n.Owner == friend) val -= 2;
                            break;
                        }
                }
                val += _rng.NextDouble() * 0.01;   // tie-break
                if (val > bestScore) { bestScore = val; best = a; }
            }
            return best;
        }
    }

    // ---------- Seats: anything that can answer a phase, possibly slowly ----------
    // IBot answers instantly. IAgent may take time (an LLM over HTTP, a remote bot, a person at a panel), so it
    // returns a Task and the runner polls. Every IBot becomes an IAgent through BotAgent, so one table can seat
    // scripted bots, hosted models and a human together. Returning null, throwing, or missing the timeout all
    // mean "no input" for that phase: default draft pick, no messages, Pass, no guesses.
    public interface IAgent
    {
        string Kind { get; }
        Task<PhaseResponse> Respond(PlayerView v, CancellationToken ct);   // one call per phase
    }

    public sealed class BotAgent : IAgent
    {
        public readonly IBot Bot;
        public BotAgent(IBot bot) { Bot = bot ?? throw new ArgumentNullException(nameof(bot)); }
        public string Kind => Bot.Kind;

        public Task<PhaseResponse> Respond(PlayerView v, CancellationToken ct)
        {
            var r = new PhaseResponse();
            switch (v.Phase)
            {
                case Phase.Draft: r.Draft = Bot.PickDraft(v); break;
                case Phase.Negotiate:
                    r.Messages = Bot.Talk(v); r.Beliefs = Bot.Beliefs(v);
                    if (Bot is IOfferMaker offerer) r.Offers = offerer.Offers(v);
                    break;
                case Phase.Act:
                    r.Action = Bot.Act(v);
                    if (Bot is IOfferMaker trader) r.Accepts = trader.Accepts(v);
                    break;
                case Phase.Guess: r.Guesses = Bot.Guess(v); break;
            }
            return Task.FromResult(r);
        }
    }

    // ---------- Runner: drives one game a phase at a time ----------
    // Two ways to drive it:
    //   StepPhase()                                   blocking. Scripted bots, the console harness, the batch sim.
    //   BeginPhase() / PhaseReady / CompletePhase()   non-blocking. The Unity view begins a phase, polls PhaseReady
    //                                                 from Update, then completes it, so a slow seat never stalls a frame.
    // Views are taken for everyone before anyone's input is applied, so a seat never sees a same-phase peer's answer.
    public sealed class GameRunner
    {
        public readonly Referee Referee;
        public readonly Dictionary<string, IAgent> Seats;
        public Dictionary<string, GameAction> LastSubmitted = new();       // Act: what each seat asked for (null = nothing usable)
        public Dictionary<string, PhaseResponse> LastResponses = new();    // every seat's answer to the last completed phase
        public Dictionary<string, int> Scores;                             // null until Finished
        public TimeSpan PhaseTimeout = TimeSpan.FromSeconds(120);          // per seat, per phase; a late seat is a pass
        public TimeSpan Grace = TimeSpan.FromSeconds(5);                   // extra wait for seats that ignore cancellation

        public GameState State => Referee.State;
        public bool Finished => State.Phase == Phase.Finished;
        public bool PhasePending => pending != null;

        sealed class PendingPhase
        {
            public Phase Phase;
            public Dictionary<string, Task<PhaseResponse>> Tasks;
            public Dictionary<string, CancellationTokenSource> Cancels;
            public Stopwatch Clock;
        }
        PendingPhase pending;

        public GameRunner(Referee referee, Dictionary<string, IBot> bots)
            : this(referee, bots.ToDictionary(kv => kv.Key, kv => (IAgent)new BotAgent(kv.Value))) { }

        public GameRunner(Referee referee, Dictionary<string, IAgent> seats)
        {
            Referee = referee; Seats = seats;
            foreach (var p in referee.State.Players)
                if (!seats.TryGetValue(p, out var s) || s == null) throw new ArgumentException("no seat for " + p);
        }

        /// <summary>Seats that have not answered the pending phase yet. Empty when nothing is pending.</summary>
        public IEnumerable<string> Waiting =>
            pending == null ? Enumerable.Empty<string>() : pending.Tasks.Where(kv => !kv.Value.IsCompleted).Select(kv => kv.Key);

        /// <summary>Takes every seat's view and asks every seat to answer. False if a phase is already pending or the game is over.</summary>
        public bool BeginPhase()
        {
            if (pending != null || Finished) return false;
            var views = State.Players.ToDictionary(p => p, p => Referee.ViewFor(p));
            var ph = new PendingPhase { Phase = State.Phase, Tasks = new(), Cancels = new(), Clock = Stopwatch.StartNew() };
            foreach (var p in State.Players)
            {
                var cts = new CancellationTokenSource(PhaseTimeout);
                ph.Cancels[p] = cts;
                try { ph.Tasks[p] = Seats[p].Respond(views[p], cts.Token) ?? Task.FromResult<PhaseResponse>(null); }
                catch (Exception e) { ph.Tasks[p] = Task.FromException<PhaseResponse>(e); }
            }
            pending = ph;
            return true;
        }

        /// <summary>True once every seat has answered, or the timeout plus grace has passed.</summary>
        public bool PhaseReady =>
            pending != null && (pending.Tasks.Values.All(t => t.IsCompleted) || pending.Clock.Elapsed > PhaseTimeout + Grace);

        /// <summary>Applies the pending phase's answers. Returns a one-line summary, or null if seats are still answering.</summary>
        public string CompletePhase()
        {
            if (pending == null || !PhaseReady) return null;
            var answers = new Dictionary<string, PhaseResponse>();
            foreach (var (p, task) in pending.Tasks)
            {
                PhaseResponse r;
                if (task.Status == TaskStatus.RanToCompletion) r = task.Result ?? new PhaseResponse { Error = "no answer" };
                else if (!task.IsCompleted || task.IsCanceled) r = new PhaseResponse { Error = $"timed out after {PhaseTimeout.TotalSeconds:0}s" };
                else r = new PhaseResponse { Error = Innermost(task.Exception).Message };
                answers[p] = r;
                if (task.IsCompleted) pending.Cancels[p].Dispose(); else pending.Cancels[p].Cancel();   // tell a straggler to stop
            }
            var phase = pending.Phase;
            pending = null;
            LastResponses = answers;
            return Apply(phase, answers);
        }

        /// <summary>Advances exactly one phase, waiting for slow seats. The blocking form of Begin/Complete.</summary>
        public string StepPhase()
        {
            if (Finished) return "finished";
            BeginPhase();
            if (!PhaseReady)
            {
                try { Task.WaitAll(pending.Tasks.Values.ToArray(), PhaseTimeout + Grace); }
                catch (AggregateException) { }   // faulted seats are reported per seat in CompletePhase
            }
            return CompletePhase();
        }

        public void RunToEnd() { int guard = 0; while (!Finished && guard++ < 100) StepPhase(); }

        string Apply(Phase phase, Dictionary<string, PhaseResponse> answers)
        {
            var players = State.Players;
            switch (phase)
            {
                case Phase.Draft:
                    foreach (var p in players) Referee.SubmitDraft(p, answers[p].Draft);
                    Referee.EndDraft();
                    return "draft complete";
                case Phase.Negotiate:
                    foreach (var p in players)
                    {
                        Referee.SubmitMessages(p, answers[p].Messages);
                        Referee.SubmitOffers(p, answers[p].Offers);
                        Referee.SubmitBeliefs(p, answers[p].Beliefs);
                    }
                    Referee.EndNegotiation();
                    return $"round {State.Round}: negotiation closed";
                case Phase.Act:
                    {
                        LastSubmitted = players.ToDictionary(p => p, p => answers[p].Action);
                        foreach (var p in players) Referee.AcceptOffers(p, answers[p].Accepts);
                        int r = State.Round;
                        Referee.ResolveRound(LastSubmitted);
                        return $"round {r}: resolved";
                    }
                case Phase.Guess:
                    foreach (var p in players) Referee.SubmitGuesses(p, answers[p].Guesses);
                    Scores = Referee.Score();
                    return "scored";
                default:
                    return "finished";
            }
        }

        static Exception Innermost(Exception e)
        {
            while (e is AggregateException a && a.InnerException != null) e = a.InnerException;
            return e;
        }
    }

    // ---------- Batch sim ----------
    public sealed class SimReport
    {
        public int Games, Players; public string BotKind; public int Seed;
        public readonly Dictionary<string, int> Drafted = new(), Achieved = new();
        public readonly Dictionary<string, int> ObjWins = new(), ObjScore = new();   // by objective held: games won, total score
        public readonly Dictionary<string, int> SeatScore = new(), SeatWins = new();
        public readonly Dictionary<string, int> KindScore = new(), KindGames = new(), KindWins = new();
        public readonly Dictionary<int, int> StartScore = new(), StartPlayers = new(), StartWins = new(); // keyed by starting production
        public int TotalReads, TotalGuesses;
        public double MeanTrades, MeanCaptures;
        public readonly Dictionary<ActionType, int> Actions = new();   // legal actions taken, all games, by type

        public double Rate(string id) => Drafted.GetValueOrDefault(id) == 0 ? 0 : (double)Achieved.GetValueOrDefault(id) / Drafted[id];

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Mandate sim: {Games} games, {Players} players, bots={BotKind}, seed={Seed}");
            sb.AppendLine($"  reads: {TotalReads}/{TotalGuesses} correct ({100.0 * TotalReads / Math.Max(1, TotalGuesses):F1}%), trades/game {MeanTrades:F2}, captures/game {MeanCaptures:F2}");
            sb.AppendLine("  seat   mean score   wins");
            foreach (var kv in SeatScore.OrderBy(k => k.Key))
                sb.AppendLine($"  {kv.Key,-6} {(double)kv.Value / Games,10:F2} {SeatWins.GetValueOrDefault(kv.Key),6}");
            sb.AppendLine("  actions/game: " + string.Join("  ", Actions.OrderBy(k => k.Key).Select(kv => $"{kv.Key.ToString().ToLowerInvariant()} {(double)kv.Value / Games:F1}")));
            if (KindGames.Count > 1)
            {
                sb.AppendLine("  bot      mean score   win rate");
                foreach (var kv in KindGames)
                    sb.AppendLine($"  {kv.Key,-8} {(double)KindScore[kv.Key] / kv.Value,10:F2} {100.0 * KindWins.GetValueOrDefault(kv.Key) / kv.Value,8:F1}%");
            }
            // Does a stronger start win more? With equal starts there is a single row.
            sb.AppendLine("  start prod  players  mean score  win rate");
            foreach (var kv in StartPlayers.OrderBy(k => k.Key))
                sb.AppendLine($"  {kv.Key,10} {kv.Value,8} {(double)StartScore[kv.Key] / kv.Value,11:F2} {100.0 * StartWins.GetValueOrDefault(kv.Key) / kv.Value,8:F1}%");
            // Shared ties mean a game can have several winners, so compare against the average, not 1/players.
            double avgWin = 100.0 * ObjWins.Values.Sum() / Math.Max(1, Drafted.Values.Sum());
            sb.AppendLine($"  objective          drafted  achieved   rate   holder wins  holder score   flag   (average holder win rate {avgWin:F1}%)");
            foreach (var id in Objectives.Pool().Select(o => o.Id).OrderByDescending(Rate))
            {
                double r = Rate(id);
                string flag = Drafted.GetValueOrDefault(id) == 0 ? "never drafted" : r < 0.10 ? "TOO HARD" : r > 0.80 ? "TOO EASY" : "";
                int held = Math.Max(1, Drafted.GetValueOrDefault(id));
                sb.AppendLine($"  {id,-18} {Drafted.GetValueOrDefault(id),7} {Achieved.GetValueOrDefault(id),9} {100 * r,6:F1}%  {100.0 * ObjWins.GetValueOrDefault(id) / held,10:F1}%  {(double)ObjScore.GetValueOrDefault(id) / held,12:F2}   {flag}");
            }
            return sb.ToString();
        }
    }

    public static class Sim
    {
        // Runs N full games with scripted bots and tallies objective achievement, seat balance, and bot strength.
        public static SimReport Run(int games, int players, int seed, string botKind = "greedy")
        {
            var report = new SimReport { Games = games, Players = players, BotKind = botKind, Seed = seed };
            var master = new Random(seed);
            long trades = 0, captures = 0;
            for (int g = 0; g < games; g++)
            {
                var names = Enumerable.Range(1, players).Select(i => "P" + i).ToList();
                var referee = new Referee("sim" + g, names, master.Next());
                var bots = new Dictionary<string, IBot>();
                for (int i = 0; i < names.Count; i++) bots[names[i]] = Bots.Make(Bots.KindForSeat(botKind, i), master.Next());
                var start = names.ToDictionary(p => p, p => referee.State.Nodes.Where(n => n.Owner == p).Sum(n => n.Production));
                var runner = new GameRunner(referee, bots);
                runner.RunToEnd();
                Tally(report, runner, start);
                trades += referee.State.TradesCompleted.Values.Sum() / 2;
                captures += referee.State.CapturedOwnedEver.Count;
                foreach (var a in referee.State.ActionLog.Where(a => a.wasLegal))
                    report.Actions[a.action.Type] = report.Actions.GetValueOrDefault(a.action.Type) + 1;
            }
            report.MeanTrades = (double)trades / Math.Max(1, games);
            report.MeanCaptures = (double)captures / Math.Max(1, games);
            return report;
        }

        static void Tally(SimReport rep, GameRunner run, Dictionary<string, int> start)
        {
            var st = run.State;
            int top = run.Scores.Values.Max();
            foreach (var p in st.Players)
            {
                var obj = st.Assigned[p];
                rep.Drafted[obj.Id] = rep.Drafted.GetValueOrDefault(obj.Id) + 1;
                if (obj.Achieved(st, p)) rep.Achieved[obj.Id] = rep.Achieved.GetValueOrDefault(obj.Id) + 1;
                rep.SeatScore[p] = rep.SeatScore.GetValueOrDefault(p) + run.Scores[p];
                bool won = run.Scores[p] == top;
                rep.ObjScore[obj.Id] = rep.ObjScore.GetValueOrDefault(obj.Id) + run.Scores[p];
                if (won) rep.ObjWins[obj.Id] = rep.ObjWins.GetValueOrDefault(obj.Id) + 1;
                if (won) rep.SeatWins[p] = rep.SeatWins.GetValueOrDefault(p) + 1;
                var kind = run.Seats[p].Kind;
                rep.KindGames[kind] = rep.KindGames.GetValueOrDefault(kind) + 1;
                rep.KindScore[kind] = rep.KindScore.GetValueOrDefault(kind) + run.Scores[p];
                if (won) rep.KindWins[kind] = rep.KindWins.GetValueOrDefault(kind) + 1;
                int sp = start[p];
                rep.StartPlayers[sp] = rep.StartPlayers.GetValueOrDefault(sp) + 1;
                rep.StartScore[sp] = rep.StartScore.GetValueOrDefault(sp) + run.Scores[p];
                if (won) rep.StartWins[sp] = rep.StartWins.GetValueOrDefault(sp) + 1;
                if (st.Guesses.TryGetValue(p, out var guesses))
                    foreach (var kv in guesses) { rep.TotalGuesses++; if (st.Assigned[kv.Key].Id == kv.Value) rep.TotalReads++; }
            }
        }
    }
}
