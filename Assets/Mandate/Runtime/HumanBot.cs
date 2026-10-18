// HumanBot — lets a person sit in one seat of a bot game.
//
// The GameRunner asks every seat for input once per phase through the IBot interface. This bot answers with
// whatever the on-screen panel (MandateGameView) has queued for it. If nothing was queued it behaves like an
// agent that timed out: default draft pick, no messages, Pass, no guesses. That mirrors exactly what the
// referee does to a real AI agent that fails to answer, so a human play-through is a faithful test.

using System.Collections.Generic;
using Mandate.Core;
using Mandate.Sim;

namespace Mandate.Viewer
{
    public sealed class HumanBot : IBot, IOfferMaker
    {
        public string Kind => "human";

        /// <summary>Objective id chosen during the draft. Null = referee falls back to the first option.</summary>
        public string PendingDraft;

        /// <summary>Messages queued this negotiation phase. Item1 null = public. Cleared once submitted.</summary>
        public List<(string to, string text)> PendingMessages = new List<(string to, string text)>();

        /// <summary>Action chosen for this round. Null = Pass. Cleared once submitted.</summary>
        public GameAction PendingAction;

        /// <summary>End-of-game guesses: opponent id -> objective id.</summary>
        public Dictionary<string, string> PendingGuesses = new Dictionary<string, string>();

        public string PickDraft(PlayerView v) => PendingDraft;

        public List<(string to, string text)> Talk(PlayerView v)
        {
            var queued = PendingMessages;
            PendingMessages = new List<(string to, string text)>();
            return queued;
        }

        /// <summary>Trade offers queued this negotiation phase, one per partner. Cleared once submitted.</summary>
        public List<(string partner, int give, int get)> PendingOffers = new List<(string partner, int give, int get)>();

        public List<(string partner, int give, int get)> Offers(PlayerView v)
        {
            var queued = PendingOffers;
            PendingOffers = new List<(string partner, int give, int get)>();
            return queued;
        }

        /// <summary>Players whose trade offers to you this round you accept (act phase). Cleared once submitted.</summary>
        public List<string> PendingAccepts = new List<string>();

        public List<string> Accepts(PlayerView v)
        {
            var queued = PendingAccepts;
            PendingAccepts = new List<string>();
            return queued;
        }

        public BeliefReport Beliefs(PlayerView v) => null;   // humans don't file belief reports in the harness

        public GameAction Act(PlayerView v)
        {
            var a = PendingAction ?? GameAction.Pass();
            PendingAction = null;
            return a;
        }

        public Dictionary<string, string> Guess(PlayerView v) => PendingGuesses;
    }
}
