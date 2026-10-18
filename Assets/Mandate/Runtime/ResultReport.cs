// ResultReport — one finished game, packed up to send to the developer.
//
// Built only when the player presses Send in the "Send results" dialog. It holds the scores, each seat's model,
// card, timings and token use, the player's optional comment, and the full transcript (every prompt, the models' thinking,
// every reply). It never reads table.json, and every API key the game knows about is blanked out of the text
// before it goes, in case a model ever repeats one.
//
// Field names are read by the results relay (not in this repository). Rename one here and rename it there.
// Plain public fields so JsonUtility can write it and read it back from the pending folder.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mandate.Agents;
using Mandate.Core;

namespace Mandate.Viewer
{
    [Serializable]
    public sealed class ResultReport
    {
        public string Token;            // shared secret the relay checks; filled in when sending
        public string ReportId;         // 32 hex characters; the relay ignores an id it has seen, so retries never email twice
        public string InstallId;        // random id of this copy of the game (PlayerPrefs); not derived from the PC or the player
        public string GameVersion;      // Application.version
        public int WireVersion;         // Mandate.Core.WireFormat.Version: which rules the game was played under
        public string Platform;
        public string UnityVersion;
        public string FinishedUtc;
        public int Seed;
        public int Players;
        public int Rounds;              // rounds played (the secret last round, revealed at the end)
        public bool Draft = true;       // objective draft; always on now, kept so the results sheet's Draft column stays filled
        public string HumanSeat;        // the seat a person played, or "" for an all-AI game
        public int TradesCompleted;     // across all seats
        public string Comment;          // what the player typed, optional
        public string Contact;          // an email address to reply to, optional
        public SeatResult[] Seats;
        public string Transcript;       // the full transcript text, as saved to the transcripts folder

        [Serializable]
        public sealed class SeatResult
        {
            public string Seat;          // P1, P2, ...
            public string Who;           // the label shown in game: you, greedy, openrouter:qwen/..., a custom name
            public string Provider;      // seat kind: you, bot, openrouter, anthropic, gemini, http, ...
            public string Model;         // model id for AI seats, "" otherwise
            public string Card;          // objective id
            public bool Met;
            public int Score;
            public int Place;            // 1 = best; ties share a place
            public int Trades;
            public int Replies;          // AI seats: calls made
            public int Errors;           // AI seats: replies that failed or could not be read
            public int Timeouts;         // AI seats: replies that came too late (counted as a pass)
            public float AvgSeconds;
            public float MaxSeconds;
            public int ThinkingReplies;  // replies that came back with the model's thinking
            public string ModelUsed;     // AI seats: the model(s) the provider says answered, which can differ from Model when Model is an alias
            public int InputTokens;      // AI seats: prompt tokens over the whole game, 0 when the provider never said
            public int OutputTokens;     // AI seats: reply + thinking tokens over the whole game
            public double Cost = -1;     // AI seats: US dollars, only when the provider reports it (OpenRouter); -1 = unknown
        }

        /// <summary>Packs up the finished game on the view. The comment and contact are what the player typed.</summary>
        public static ResultReport Build(MandateGameView view, string comment, string contact)
        {
            var runner = view.Runner;
            var st = runner.State;
            var r = new ResultReport
            {
                ReportId = Guid.NewGuid().ToString("N"),
                InstallId = InstallIdentity(),
                GameVersion = Application.version,
                WireVersion = WireFormat.Version,
                Platform = Application.platform.ToString(),
                UnityVersion = Application.unityVersion,
                FinishedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                Seed = view.seed,
                Players = st.Players.Count,
                Rounds = st.LastRound,
                HumanSeat = view.HumanSeat ?? "",
                TradesCompleted = st.TradesCompleted.Values.Sum(),
                Comment = (comment ?? "").Trim(),
                Contact = (contact ?? "").Trim(),
            };

            var seats = new List<SeatResult>();
            foreach (var p in st.Players)
            {
                var agent = runner.Seats[p];
                var llm = agent as LlmAgent;
                int score = runner.Scores[p];
                var s = new SeatResult
                {
                    Seat = p,
                    Who = agent.Kind,
                    Provider = p == view.HumanSeat ? "you" : llm != null ? llm.Config.Kind : agent is HttpBotAgent http ? http.Config.Kind : "bot",
                    Model = llm?.Config.Model ?? "",
                    Card = st.Assigned[p].Id,
                    Met = st.Assigned[p].Achieved(st, p),
                    Score = score,
                    Place = 1 + runner.Scores.Values.Count(v => v > score),
                    Trades = st.TradesCompleted.TryGetValue(p, out var t) ? t : 0,
                };
                if (llm != null)
                    lock (llm.Transcript)
                    {
                        var ex = llm.Transcript;
                        s.Replies = ex.Count;
                        s.Errors = ex.Count(e => e.Error != null);
                        s.Timeouts = ex.Count(e => e.Error == "timed out");
                        s.AvgSeconds = ex.Count > 0 ? (float)ex.Average(e => e.Seconds) : 0f;
                        s.MaxSeconds = ex.Count > 0 ? (float)ex.Max(e => e.Seconds) : 0f;
                        s.ThinkingReplies = ex.Count(e => !string.IsNullOrWhiteSpace(e.Thinking));
                        s.ModelUsed = string.Join(", ", ex.Select(e => e.Model).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());
                        s.InputTokens = ex.Sum(e => e.InputTokens);
                        s.OutputTokens = ex.Sum(e => e.OutputTokens);
                        s.Cost = ex.Any(e => e.Cost >= 0) ? ex.Where(e => e.Cost >= 0).Sum(e => e.Cost) : -1;
                    }
                seats.Add(s);
            }
            r.Seats = seats.ToArray();

            r.Transcript = RemoveKeys(TranscriptWriter.Build(view.Log, runner), KnownKeys(view));
            r.Comment = RemoveKeys(r.Comment, KnownKeys(view));
            return r;
        }

        /// <summary>A random id for this copy of the game, made on first use and kept in PlayerPrefs.</summary>
        public static string InstallIdentity()
        {
            const string pref = "Mandate.InstallId";
            string id = PlayerPrefs.GetString(pref, "");
            if (id.Length != 32)
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(pref, id);
                PlayerPrefs.Save();
            }
            return id;
        }

        // Every API key the game can see: typed on the Table screen, set on a seat, or in a provider's environment variable.
        static List<string> KnownKeys(MandateGameView view)
        {
            var keys = new List<string>(view.Table.Keys.Values);
            foreach (var seat in view.seats) if (seat != null) keys.Add(seat.ToConfig().ApiKey);
            foreach (var info in Mandate.Agents.Seats.Kinds.Values)
                if (info.KeyEnv != null) keys.Add(Environment.GetEnvironmentVariable(info.KeyEnv));
            return keys.Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length >= 8).Select(k => k.Trim()).Distinct().ToList();
        }

        // Replaces every occurrence of each key with a marker.
        static string RemoveKeys(string text, List<string> keys)
        {
            foreach (var key in keys) text = text.Replace(key, "[key removed]");
            return text;
        }
    }
}
