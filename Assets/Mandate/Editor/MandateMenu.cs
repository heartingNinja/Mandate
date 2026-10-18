// MandateMenu — editor-only shortcuts under the "Mandate" menu bar entry.
//
//   Mandate / Run Core Tests        runs the plain-assert engine tests and the agent-layer tests, prints PASS/FAIL to the Console
//   Mandate / Run Sim (...)         plays 1000 bot games and prints per-objective achievement rates
//   Mandate / Open Game Scene       opens Assets/Scenes/Mandate.unity, the visual test harness
//   Mandate / Build Web (WebGL)     builds the browser version and an itch.io zip (WebBuild.cs)
//
// Nothing here touches the rules. It is a convenience layer over Mandate.Core / Mandate.Sim / Mandate.Agents.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Mandate.EditorTools
{
    public static class MandateMenu
    {
        const string ScenePath = "Assets/Scenes/Mandate.unity";

        [MenuItem("Mandate/Run Core Tests", priority = 0)]
        public static void RunCoreTests()
        {
            var detail = new StringBuilder();
            int pass = 0, fail = 0;

            // Route the PASS/FAIL lines into a buffer so the Console gets one tidy entry per run.
            Mandate.Tests.Tests.Out = line => detail.AppendLine("    " + line);
            Mandate.AgentTests.Tests.Out = line => detail.AppendLine("    " + line);

            var all = Mandate.Tests.Tests.All().Concat(Mandate.AgentTests.Tests.All());
            foreach (var (name, run) in all)
            {
                try { run(); pass++; detail.AppendLine($"  ok   {name}"); }
                catch (Exception e) { fail++; detail.AppendLine($"  FAIL {name}: {e.Message}"); }
            }

            string summary = $"Mandate tests: {pass} passed, {fail} failed";
            if (fail == 0) Debug.Log(summary + "\n" + detail);
            else Debug.LogError(summary + "\n" + detail);
        }

        [MenuItem("Mandate/Run Sim (1000 greedy)", priority = 20)] public static void SimGreedy() => RunSim("greedy");
        [MenuItem("Mandate/Run Sim (1000 random)", priority = 21)] public static void SimRandom() => RunSim("random");
        [MenuItem("Mandate/Run Sim (1000 mixed)", priority = 22)]  public static void SimMixed()  => RunSim("mixed");

        static void RunSim(string kind)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var report = Mandate.Sim.Sim.Run(1000, 4, 42, kind);
            sw.Stop();
            Debug.Log(report + $"sim time: {sw.ElapsedMilliseconds} ms");
        }

        [MenuItem("Mandate/Open Game Scene", priority = 40)]
        public static void OpenGameScene()
        {
            if (!System.IO.File.Exists(ScenePath)) { Debug.LogWarning("Scene not found: " + ScenePath); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
