// Mandate.Wire — the contract between the referee and any seat that lives outside this process.
//
// ViewDto is what a seat receives each phase (a JSON-friendly copy of PlayerView). ResponseDto is what it sends
// back. PhaseResponse is that answer after validation, which is what GameRunner applies. These are plain classes
// with no attributes and no JSON library: Mandate.Agents serialises them with Newtonsoft in camelCase, the
// ASP.NET referee will serialise the same classes the same way, and a bot written in any language reads the same
// shape. Field-level notes for bot authors are in Docs/Agents.md.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Mandate.Core
{
    public static class WireFormat
    {
        // Bump when a field changes meaning; adding optional fields is free.
        // 2: the last round is secret (Rules.MinRounds/MaxRounds, no TotalRounds); trades are offers plus "accept", not an action.
        public const int Version = 2;
    }

    // ---------- What a seat receives ----------
    public sealed class ViewDto
    {
        public int Version = WireFormat.Version;
        public string GameId;
        public int Round;                             // the game ends after round Rules.MinRounds..MaxRounds; which one is secret
        public string Phase;                          // draft | negotiate | act | guess | finished
        public string You;
        public List<string> Players;                  // seat order
        public string Left;                           // player to your left (next in seat order)
        public string Right;                          // player to your right (previous in seat order)
        public string ObjectiveId;                    // null during the draft
        public string ObjectiveText;
        public bool? ObjectiveMetNow;                 // would your objective score if the game ended now? null during the draft
        public List<NodeDto> Board;
        public List<MessageDto> PublicLog;            // every public message so far, including your own
        public List<MessageDto> PrivateInbox;         // private messages sent to you; what you sent is not echoed back
        public List<ActionRecordDto> ActionsHistory;  // every resolved action, every player, every round
        public List<OfferDto> TradeOffers;            // this round: offers you made (Problem set if rejected) and valid offers made to you; accept in the act phase
        public List<OfferDto> TradeHistory;           // earlier rounds: every completed trade, plus your own offers, each with its Outcome
        public List<ActionDto> LegalActions;          // act phase only; copy one back verbatim
        public List<string> MovePreviews;             // negotiate/act: what each of your moves and sabotages would do on this board
        public List<string> Dangers;                  // negotiate/act: how opponents could capture or sabotage your nodes this round
        public List<ObjectiveDto> DealtObjectives;    // every card in play this game (public)
        public List<ObjectiveDto> GuessCandidates;    // DealtObjectives minus the cards you were offered
        public List<ObjectiveDto> DraftOptions;       // draft phase only
        public Dictionary<string, int> TradesCompleted;
        public List<string> LostNodeEver;             // players who have had a node captured or sabotaged to neutral
        public List<string> CapturedOwnedEver;        // players who have captured an owned node
        public RulesDto Rules;
    }

    public sealed class NodeDto { public int Id; public string Owner; public int Production; public int Fortify; public List<int> Adjacent; }
    public sealed class MessageDto { public int Round; public string From; public string To; public string Text; }   // To null = public
    public sealed class ObjectiveDto { public string Id; public string Text; }
    public sealed class ActionRecordDto { public int Round; public string Player; public ActionDto Action; public string Note; }   // Note: what the action actually did, or why it became a pass

    // A trade offer: From gives node Give, Partner gives node Get. Sending one (negotiate phase) only Partner, Give and
    // Get are read. Outcome is set once its round resolves: "done", "not accepted", "rejected: ..." or "void: ...".
    public sealed class OfferDto { public int Round; public string From; public string Partner; public int? Give, Get; public string Problem; public string Outcome; }

    public sealed class RulesDto
    {
        public int MinRounds, MaxRounds, FortifyCap, ProductionCap, MaxPublicMessages, MaxPrivateMessages, MaxMessageChars;
        public int ObjectivePoints, ReadPoints, GotReadPenalty;
    }

    // One action. Only the fields for its type are set:
    //   {type:"pass"} | {type:"move", from, to} | {type:"fortify", node} | {type:"develop", node} | {type:"sabotage", node}
    //   | {type:"release", node}
    public sealed class ActionDto
    {
        public string Type;
        public int? From, To, Node;
    }

    // ---------- What a seat sends back ----------
    // Only the field for the current phase is read; everything else is ignored. Notes are private to the seat.
    public sealed class ResponseDto
    {
        public string Draft;                          // draft: objective id from DraftOptions
        public List<MessageDto> Messages;             // negotiate: Round/From ignored; To null, "" or "public" = public
        public List<OfferDto> Offers;                 // negotiate: optional trade offers, at most one per partner
        public BeliefsDto Beliefs;                    // negotiate: optional, never scored
        public ActionDto Action;                      // act
        public List<string> Accept;                   // act: optional, the players whose trade offers to you this round you accept (free, as well as Action)
        public Dictionary<string, string> Guesses;    // guess: opponent -> objective id
        public string Notes;                          // scratchpad, handed back to the same seat next phase
    }

    public sealed class BeliefsDto
    {
        public Dictionary<string, Dictionary<string, double>> ObjectiveBeliefs;   // target -> (objective id -> probability)
        public Dictionary<string, string> TheyThinkIAm;                           // target -> what you think they have pinned on you
        public string Note;
    }

    // ---------- A seat's answer for one phase, validated ----------
    // Null fields mean "no input", which the referee turns into: default draft pick, no messages, Pass, no guesses.
    public sealed class PhaseResponse
    {
        public string Draft;
        public List<(string to, string text)> Messages;
        public List<(string partner, int give, int get)> Offers;
        public BeliefReport Beliefs;
        public GameAction Action;
        public List<string> Accepts;   // act: offerers whose offers this seat accepts
        public Dictionary<string, string> Guesses;
        public string Notes;
        public string Error;      // why this answer is empty or partial: timeout, HTTP error, unreadable reply. For the log.
    }

    public static class Wire
    {
        public static string PhaseName(Phase p) => p.ToString().ToLowerInvariant();

        public static ViewDto ToDto(PlayerView v)
        {
            int i = v.Players.IndexOf(v.You), n = v.Players.Count;
            return new ViewDto
            {
                GameId = v.GameId,
                Round = v.Round,
                Phase = PhaseName(v.Phase),
                You = v.You,
                Players = v.Players.ToList(),
                Left = v.Players[(i + 1) % n],
                Right = v.Players[(i - 1 + n) % n],
                ObjectiveId = v.ObjectiveId,
                ObjectiveText = v.ObjectiveText,
                ObjectiveMetNow = v.ObjectiveMetNow,
                Board = (v.Board ?? new List<Node>()).Select(nd => new NodeDto { Id = nd.Id, Owner = nd.Owner, Production = nd.Production, Fortify = nd.Fortify, Adjacent = nd.Adjacent.ToList() }).ToList(),
                PublicLog = (v.PublicLog ?? new List<Message>()).Select(ToDto).ToList(),
                PrivateInbox = (v.PrivateInbox ?? new List<Message>()).Select(ToDto).ToList(),
                ActionsHistory = (v.ActionsHistory ?? new List<(int, string, GameAction, string)>()).Select(a => new ActionRecordDto { Round = a.round, Player = a.player, Action = ToDto(a.action), Note = a.note }).ToList(),
                TradeOffers = (v.Offers ?? new List<TradeOffer>()).Select(ToDto).ToList(),
                TradeHistory = (v.TradeHistory ?? new List<TradeOffer>()).Select(ToDto).ToList(),
                LegalActions = (v.LegalActions ?? new List<GameAction>()).Select(ToDto).ToList(),
                MovePreviews = (v.MovePreviews ?? new List<string>()).ToList(),
                Dangers = (v.Dangers ?? new List<string>()).ToList(),
                DealtObjectives = Objectives(v.DealtObjectives),
                GuessCandidates = Objectives(v.GuessCandidates),
                DraftOptions = Objectives(v.DraftOptions),
                TradesCompleted = new Dictionary<string, int>(v.TradesCompleted ?? new Dictionary<string, int>()),
                LostNodeEver = (v.LostNodeEver ?? new HashSet<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                CapturedOwnedEver = (v.CapturedOwnedEver ?? new HashSet<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Rules = new RulesDto
                {
                    MinRounds = Core.Rules.MinRounds, MaxRounds = Core.Rules.MaxRounds, FortifyCap = Core.Rules.FortifyCap, ProductionCap = Core.Rules.ProductionCap,
                    MaxPublicMessages = Core.Rules.MaxPublicMessages, MaxPrivateMessages = Core.Rules.MaxPrivateMessages, MaxMessageChars = Core.Rules.MaxMessageChars,
                    ObjectivePoints = Core.Rules.ObjectivePoints, ReadPoints = Core.Rules.ReadPoints, GotReadPenalty = Core.Rules.GotReadPenalty,
                },
            };
        }

        static List<ObjectiveDto> Objectives(List<(string id, string text)> list) =>
            (list ?? new List<(string, string)>()).Select(o => new ObjectiveDto { Id = o.id, Text = o.text }).ToList();

        public static MessageDto ToDto(Message m) => new MessageDto { Round = m.Round, From = m.From, To = m.To, Text = m.Text };

        public static OfferDto ToDto(TradeOffer o) =>
            new OfferDto { Round = o.Round, From = o.From, Partner = o.To, Give = o.Give, Get = o.Get, Problem = o.Problem, Outcome = o.Outcome };

        public static ActionDto ToDto(GameAction a)
        {
            if (a == null) return null;
            switch (a.Type)
            {
                case ActionType.Move: return new ActionDto { Type = "move", From = a.From, To = a.To };
                case ActionType.Fortify: return new ActionDto { Type = "fortify", Node = a.To };
                case ActionType.Sabotage: return new ActionDto { Type = "sabotage", Node = a.To };
                case ActionType.Develop: return new ActionDto { Type = "develop", Node = a.To };
                case ActionType.Release: return new ActionDto { Type = "release", Node = a.To };
                default: return new ActionDto { Type = "pass" };
            }
        }

        // Lenient on purpose: models paraphrase. Anything not understood is null, which the referee logs as a pass.
        public static GameAction ToAction(ActionDto d)
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Type)) return null;
            switch (d.Type.Trim().ToLowerInvariant())
            {
                case "pass": case "none": case "wait": case "skip":
                    return GameAction.Pass();
                case "move": case "attack": case "capture": case "expand":
                    {
                        int? from = d.From ?? d.Node, to = d.To;
                        return from == null || to == null ? null : GameAction.Move(from.Value, to.Value);
                    }
                case "fortify": case "defend": case "wall":
                    {
                        int? node = d.Node ?? d.To ?? d.From;
                        return node == null ? null : GameAction.Fortify(node.Value);
                    }
                case "sabotage":
                    {
                        int? node = d.Node ?? d.To ?? d.From;
                        return node == null ? null : GameAction.Sabotage(node.Value);
                    }
                case "develop": case "build": case "grow": case "invest":
                    {
                        int? node = d.Node ?? d.To ?? d.From;
                        return node == null ? null : GameAction.Develop(node.Value);
                    }
                case "release": case "abandon": case "give_up": case "giveup":
                    {
                        int? node = d.Node ?? d.From ?? d.To;
                        return node == null ? null : GameAction.Release(node.Value);
                    }
                default:
                    return null;
            }
        }

        public static PhaseResponse ToResponse(ResponseDto d, Phase phase)
        {
            var r = new PhaseResponse();
            if (d == null) { r.Error = "empty reply"; return r; }
            r.Notes = d.Notes;
            r.Draft = string.IsNullOrWhiteSpace(d.Draft) ? null : d.Draft.Trim();
            if (d.Messages != null)
                r.Messages = d.Messages.Where(m => m != null && !string.IsNullOrWhiteSpace(m.Text))
                    .Select(m => (IsPublic(m.To) ? null : SeatId(m.To), m.Text.Trim())).ToList();
            if (d.Offers != null)
                r.Offers = d.Offers.Where(o => o != null && !string.IsNullOrWhiteSpace(o.Partner) && o.Give != null && o.Get != null)
                    .Select(o => (SeatId(o.Partner), o.Give.Value, o.Get.Value)).ToList();
            if (d.Beliefs != null)
                r.Beliefs = new BeliefReport
                {
                    ObjectiveBeliefs = d.Beliefs.ObjectiveBeliefs ?? new Dictionary<string, Dictionary<string, double>>(),
                    TheyThinkIAm = d.Beliefs.TheyThinkIAm ?? new Dictionary<string, string>(),
                    Note = d.Beliefs.Note,
                };
            if (d.Guesses != null)
                r.Guesses = d.Guesses.Where(kv => kv.Key != null && kv.Value != null).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim());
            if (d.Accept != null)
                r.Accepts = d.Accept.Where(a => !string.IsNullOrWhiteSpace(a)).Select(SeatId).ToList();
            if (d.Action != null)
            {
                r.Action = ToAction(d.Action);
                bool trade = d.Action.Type != null && (d.Action.Type.Trim().ToLowerInvariant() == "trade" || d.Action.Type.Trim().ToLowerInvariant() == "swap");
                if (r.Action == null) r.Error = trade ? "a trade is not an action: offer one in negotiation, accept one with \"accept\""
                                                      : "action not understood: " + Describe(d.Action);
            }
            else if (phase == Phase.Act) r.Error = "reply had no action";
            if (phase == Phase.Draft && r.Draft == null) r.Error = "reply had no draft pick";
            if (phase == Phase.Guess && r.Guesses == null) r.Error = "reply had no guesses";
            return r;
        }

        /// <summary>A seat name as models write it ("p2", "Player 2", "player2") turned into the seat id ("P2").
        /// Anything else is only trimmed.</summary>
        public static string SeatId(string name)
        {
            if (name == null) return null;
            var t = name.Trim();
            var lower = t.ToLowerInvariant();
            var digits = lower.StartsWith("player") ? lower.Substring(6).Trim() : lower.StartsWith("p") ? lower.Substring(1).Trim() : null;
            return !string.IsNullOrEmpty(digits) && digits.All(char.IsDigit) ? "P" + digits : t;
        }

        static bool IsPublic(string to)
        {
            if (string.IsNullOrWhiteSpace(to)) return true;
            switch (to.Trim().ToLowerInvariant()) { case "public": case "all": case "everyone": case "*": case "null": return true; }
            return false;
        }

        static string Describe(ActionDto a) => $"type={a.Type} from={a.From} to={a.To} node={a.Node}";
    }
}
