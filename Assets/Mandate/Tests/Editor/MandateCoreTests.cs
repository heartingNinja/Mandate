// MandateCoreTests — NUnit wrapper so the engine's and the agent layer's plain-assert tests show up in Unity's
// Test Runner (Window > General > Test Runner > EditMode). The real assertions live in Mandate.Tests.cs and
// Mandate.Agents.Tests.cs; this file hands each one to NUnit by name, plus a few smoke tests that run whole games.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Mandate.Core;
using Mandate.Sim;

namespace Mandate.EditorTests
{
    public class MandateCoreTests
    {
        // One NUnit test case per engine test, named after the engine test.
        static IEnumerable<TestCaseData> EngineTests() =>
            Mandate.Tests.Tests.All().Select(t => new TestCaseData(t.run).SetName(t.name));

        [TestCaseSource(nameof(EngineTests))]
        public void Engine(Action run)
        {
            // Silence PASS lines; NUnit reports the outcome. A failing Check throws, which NUnit catches.
            Mandate.Tests.Tests.Out = _ => { };
            run();
        }

        // One NUnit test case per agent-layer test. These stand up a local HTTP server, no API key needed.
        static IEnumerable<TestCaseData> AgentTests() =>
            Mandate.AgentTests.Tests.All().Select(t => new TestCaseData(t.run).SetName(t.name));

        [TestCaseSource(nameof(AgentTests))]
        public void Agents(Action run)
        {
            Mandate.AgentTests.Tests.Out = _ => { };
            run();
        }

        [Test]
        public void BotsOnlyEverSubmitLegalActions()
        {
            // Both bot kinds choose from PlayerView.LegalActions, so the referee should never have to downgrade one to Pass.
            foreach (var kind in new[] { "greedy", "random" })
            {
                var names = new List<string> { "P1", "P2", "P3", "P4" };
                var referee = new Referee("t", names, 11);
                var bots = names.Select((p, i) => (p, bot: Bots.Make(kind, i))).ToDictionary(x => x.p, x => x.bot);
                var runner = new GameRunner(referee, bots);
                runner.RunToEnd();
                Assert.IsTrue(runner.Finished, kind + " game finished");
                Assert.IsTrue(referee.State.ActionLog.All(a => a.wasLegal), kind + " bots never submit illegal actions");
                Assert.AreEqual(referee.State.LastRound * names.Count, referee.State.ActionLog.Count, "one logged action per player per round");
            }
        }

        [Test]
        public void SimIsDeterministicForASeed()
        {
            var a = Mandate.Sim.Sim.Run(50, 4, 99, "mixed").ToString();
            var b = Mandate.Sim.Sim.Run(50, 4, 99, "mixed").ToString();
            Assert.AreEqual(a, b, "same seed, same report");
        }

        [Test]
        public void SimSupportsEveryPlayerCount()
        {
            for (int n = Rules.MinPlayers; n <= Rules.MaxPlayers; n++)
            {
                var rep = Mandate.Sim.Sim.Run(5, n, 3, "greedy");
                Assert.AreEqual(5, rep.Games);
                Assert.AreEqual(5 * n, rep.Drafted.Values.Sum(), $"{n} players: every seat drafted an objective");
            }
        }
    }
}
