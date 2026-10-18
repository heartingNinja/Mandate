// MandateConsole — headless harness. Three jobs:
//   sim    run the engine + agent tests, then a batch sim with scripted bots (the balancing tool)
//   test   run the tests only
//   play   seat real agents from a JSON file (LLM APIs, local models, http bots, scripted bots) and play whole games,
//          narrating every phase to stdout and saving a transcript + every prompt/reply under ./transcripts/
// This is the "have them play themselves" path. The Unity scene is the "play against them" path.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Mandate.Agents;
using Mandate.Core;
using Mandate.Sim;

static class Program
{
    const string Usage =
        "usage:\n" +
        "  MandateConsole [games] [players] [seed] [greedy|random|mixed]   tests, then a batch sim (default 1000 4 42 greedy)\n" +
        "  MandateConsole test                                                    engine + agent tests only\n" +
        "  MandateConsole prompt [seat] [seed]                                    print what an AI seat is told (round 1 negotiate)\n" +
        "  MandateConsole play <seats.json> [seed] [games] [fixed]                seats from JSON play full games; transcripts to ./transcripts/\n" +
        "                                                                         seating is shuffled every game unless 'fixed' (entry 1 = P1, ...)\n" +
        "seats.json is a list of seat objects, e.g. [{\"kind\":\"openrouter\"},{\"kind\":\"gemini\"},{\"kind\":\"claude\"},{\"kind\":\"greedy\"}]\n" +
        "See Docs/Agents.md for every kind, its default model, endpoint and API-key variable.";

    static int Main(string[] args)
    {
        string cmd = args.Length > 0 && !int.TryParse(args[0], out _) ? args[0].ToLowerInvariant() : "sim";
        try
        {
            switch (cmd)
            {
                case "sim": return Sim(args);
                case "test": return RunTests() ? 0 : 1;
                case "play": return Play(args);
                case "prompt": return ShowPrompt(args);
                default: Console.WriteLine(Usage); return 2;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("ERROR: " + e.Message);
            return 1;
        }
    }

    // Prints exactly what an AI seat is told in round 1 negotiate (system prompt, then user prompt), to read the wording.
    static int ShowPrompt(string[] args)
    {
        string seat = args.Length > 1 ? args[1].ToUpperInvariant() : "P1";
        int seed = args.Length > 2 ? int.Parse(args[2]) : 1;
        var r = new Referee("prompt", new List<string> { "P1", "P2", "P3", "P4" }, seed);
        r.EndDraft();
        var v = r.ViewFor(seat);
        Console.WriteLine("===== SYSTEM PROMPT =====");
        Console.WriteLine(Prompting.SystemPrompt(v, new SeatConfig { Kind = "openrouter" }));
        Console.WriteLine("===== USER PROMPT =====");
        Console.WriteLine(Prompting.UserPrompt(v, ""));
        return 0;
    }

    static bool RunTests()
    {
        try
        {
            Mandate.Tests.Tests.RunAll();
            Mandate.AgentTests.Tests.RunAll();
            return true;
        }
        catch (Exception e) { Console.WriteLine("TESTS FAILED: " + e); return false; }
    }

    static int Sim(string[] args)
    {
        if (!RunTests()) return 1;
        int games = args.Length > 0 ? int.Parse(args[0]) : 1000;
        int players = args.Length > 1 ? int.Parse(args[1]) : 4;
        int seed = args.Length > 2 ? int.Parse(args[2]) : 42;
        string kind = args.Length > 3 ? args[3] : "greedy";

        var sw = Stopwatch.StartNew();
        var report = Mandate.Sim.Sim.Run(games, players, seed, kind);
        sw.Stop();
        Console.WriteLine(report.ToString());
        Console.WriteLine("sim time: " + sw.ElapsedMilliseconds + " ms");
        return 0;
    }

    static int Play(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine(Usage); return 2; }
        var configs = Json.Deserialize<List<SeatConfig>>(File.ReadAllText(args[1]));
        int seed = args.Length > 2 ? int.Parse(args[2]) : 1;
        int games = args.Length > 3 ? int.Parse(args[3]) : 1;
        bool shuffle = !(args.Length > 4 && args[4].ToLowerInvariant() == "fixed");
        if (configs == null || configs.Count < Rules.MinPlayers || configs.Count > Rules.MaxPlayers)
            throw new ArgumentException($"{args[1]} must list between {Rules.MinPlayers} and {Rules.MaxPlayers} seats");
        Directory.CreateDirectory("transcripts");

        var totals = new Dictionary<string, (int score, int wins, int games)>();
        for (int g = 0; g < games; g++)
        {
            int gameSeed = seed + g;
            var names = Enumerable.Range(1, configs.Count).Select(i => "P" + i).ToList();
            var rng = new Random(gameSeed);
            var seatOf = Seating.Assign(configs.Count, gameSeed, shuffle);   // entry i sits in seat seatOf[i]
            var seats = new Dictionary<string, IAgent>();
            for (int i = 0; i < names.Count; i++) seats[names[seatOf[i]]] = Seats.Make(configs[i], rng.Next());   // throws with a clear message if a key is missing
            var runner = new GameRunner(new Referee("play-" + gameSeed, names, gameSeed), seats) { PhaseTimeout = TimeSpan.FromSeconds(configs.Any(c => c.Kind == "relay") ? 3600 : 180) };   // relay seats are agents that take their time

            var text = new StringBuilder();
            void Say(string s) { Console.WriteLine(s); text.AppendLine(s); }

            Say($"=== Mandate game {g + 1} of {games}, seed {gameSeed}{(shuffle ? ", seats shuffled" : "")} ===");
            foreach (var p in names) Say($"  {p}: {seats[p].Kind}");
            Say("  in play: " + string.Join(", ", runner.State.Dealt.Select(o => o.Id)));
            Narrate(runner, Say);

            Say("--- Private notes, revealed after the game");
            foreach (var p in names)
                if (seats[p] is LlmAgent llm)
                    Say($"  {p} [{llm.Kind}]: {(string.IsNullOrWhiteSpace(llm.Notes) ? "(none)" : llm.Notes.Replace("\n", "\n      "))}");

            string basePath = Path.Combine("transcripts", $"game-{gameSeed}-{DateTime.Now:yyyyMMdd-HHmmss}");
            File.WriteAllText(basePath + ".txt", text.ToString());
            var exchanges = names.Where(p => seats[p] is LlmAgent).ToDictionary(p => p, p => ((LlmAgent)seats[p]).Transcript);
            File.WriteAllText(basePath + ".exchanges.json", Json.Serialize(exchanges, indented: true));
            Console.WriteLine($"saved {basePath}.txt and {basePath}.exchanges.json");

            int top = runner.Scores.Values.Max();
            foreach (var p in names)
            {
                totals.TryGetValue(seats[p].Kind, out var t);
                totals[seats[p].Kind] = (t.score + runner.Scores[p], t.wins + (runner.Scores[p] == top ? 1 : 0), t.games + 1);
            }
        }

        if (games > 1)
        {
            Console.WriteLine("=== Totals (mean score, wins)");
            foreach (var kv in totals.OrderByDescending(k => (double)k.Value.score / k.Value.games))
                Console.WriteLine($"  {kv.Key,-32} {(double)kv.Value.score / kv.Value.games,6:F2}   {kv.Value.wins}/{kv.Value.games}");
        }
        return 0;
    }

    // Steps the game to the end, printing what happened after every phase (same narration as the Unity log).
    static void Narrate(GameRunner runner, Action<string> say)
    {
        var st = runner.State;
        int seenMsg = 0, seenAct = 0, seenBel = 0;
        while (!runner.Finished)
        {
            var phase = st.Phase; int round = st.Round;
            var owners = st.Nodes.ToDictionary(n => n.Id, n => n.Owner);
            var sw = Stopwatch.StartNew();
            runner.StepPhase();
            string took = $"({sw.Elapsed.TotalSeconds:0.0}s)";

            switch (phase)
            {
                case Phase.Draft:
                    say($"--- Draft {took}");
                    foreach (var p in st.Players) say($"  {p} keeps {st.Assigned[p].Id}: {st.Assigned[p].Text}");
                    break;
                case Phase.Negotiate:
                    say($"--- Round {round} negotiation {took}");
                    for (; seenMsg < st.Messages.Count; seenMsg++)
                    {
                        var m = st.Messages[seenMsg];
                        say(m.To == null ? $"  {m.From}: {m.Text}" : $"  {m.From} -> {m.To} (private): {m.Text}");
                    }
                    for (; seenBel < st.Beliefs.Count; seenBel++)
                    {
                        var b = st.Beliefs[seenBel];
                        var reads = b.ObjectiveBeliefs.Where(kv => kv.Value.Count > 0).Select(kv => $"{kv.Key}={kv.Value.OrderByDescending(x => x.Value).First().Key}").ToList();
                        if (reads.Count > 0) say($"  {b.Player} believes: {string.Join(", ", reads)}");
                    }
                    break;
                case Phase.Act:
                    say($"--- Round {round} actions {took}");
                    for (; seenAct < st.ActionLog.Count; seenAct++)
                    {
                        var a = st.ActionLog[seenAct];
                        runner.LastSubmitted.TryGetValue(a.player, out var asked);
                        say(a.wasLegal ? $"  {a.player} {a.action}" : $"  {a.player} illegal {(asked == null ? "(no action)" : asked.ToString())} -> pass");
                    }
                    // Trades are offers accepted alongside an action, not actions, so they get their own lines.
                    foreach (var o in st.Offers.Where(o => o.Round == round && o.Problem == null))
                        say($"  trade: {o.From} gives node {o.Give} to {o.To}, {o.To} gives node {o.Get} to {o.From} ({o.Outcome})");
                    foreach (var n in st.Nodes)
                        if (owners[n.Id] != n.Owner) say($"    node {n.Id}: {owners[n.Id] ?? "neutral"} -> {n.Owner ?? "neutral"}");
                    say("    standings: " + string.Join("  ", st.Players.Select(p => $"{p} prod {st.Nodes.Where(n => n.Owner == p).Sum(n => n.Production)} nodes {st.Nodes.Count(n => n.Owner == p)}")));
                    break;
                case Phase.Guess:
                    say($"--- Guesses {took}");
                    foreach (var kv in st.Guesses)
                        say($"  {kv.Key}: " + string.Join(", ", kv.Value.Select(x => $"{x.Key}={x.Value}{(st.Assigned[x.Key].Id == x.Value ? " (correct)" : "")}")));
                    say("--- Final scores");
                    foreach (var kv in runner.Scores.OrderByDescending(k => k.Value))
                        say($"  {kv.Key} {kv.Value,3}  {st.Assigned[kv.Key].Id} {(st.Assigned[kv.Key].Achieved(st, kv.Key) ? "achieved" : "missed")}  [{runner.Seats[kv.Key].Kind}]");
                    break;
            }
            foreach (var kv in runner.LastResponses)
                if (kv.Value.Error != null) say($"  ! {kv.Key} gave no usable answer: {kv.Value.Error}");
        }
    }
}
