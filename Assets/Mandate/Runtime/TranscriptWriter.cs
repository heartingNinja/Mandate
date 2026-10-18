// TranscriptWriter — writes a finished game to a plain-text file.
//
// The file holds the whole log without rich text, a line per AI seat with its model, tokens and cost, then for
// every AI seat the system prompt once and every prompt and reply it exchanged, phase by phase, with timings,
// tokens and errors. When the model's provider returns its
// thinking (OpenRouter, DeepSeek, Claude, Gemini; not OpenAI), it is written just above the reply it led to.
// Files go to persistentDataPath/Mandate/transcripts/game-<seed>-<date>.txt. OpenFolder() shows that folder in the OS;
// a browser build has no folders to show, so the scoreboard downloads the file instead (WebFiles.Download).
// Build() returns the same text without saving it; the results sender attaches it to a report.

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Mandate.Agents;
using Mandate.Sim;

namespace Mandate.Viewer
{
    public static class TranscriptWriter
    {
        /// <summary>Writes the transcript and returns the path. Throws if the file cannot be written.</summary>
        public static string Save(string dir, int seed, GameLog log, GameRunner runner)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"game-{seed}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, Build(log, runner));
            WebFiles.Save();   // in a browser, keep it for the next visit
            return path;
        }

        /// <summary>The whole transcript as text: the log, then every AI seat's prompts, thinking and replies.</summary>
        public static string Build(GameLog log, GameRunner runner)
        {
            var sb = new StringBuilder();
            sb.Append(log.PlainText());

            // One line per AI seat: the model asked for, the model that answered, and what the game used.
            var ais = runner.State.Players.Where(p => runner.Seats[p] is LlmAgent).ToList();
            if (ais.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("===== AI seats: model, tokens, cost =====");
                foreach (var p in ais)
                {
                    var llm = (LlmAgent)runner.Seats[p];
                    lock (llm.Transcript)
                    {
                        var ex = llm.Transcript;
                        string used = string.Join(", ", ex.Select(e => e.Model).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());
                        bool priced = ex.Any(e => e.Cost >= 0);
                        sb.AppendLine($"{p}  {llm.Kind}  model asked: {llm.Config.Model ?? "-"}  answered by: {(used == "" ? "not said" : used)}  "
                            + $"tokens in {ex.Sum(e => e.InputTokens):N0} / out {ex.Sum(e => e.OutputTokens):N0}  "
                            + $"cost {(priced ? "$" + ex.Where(e => e.Cost >= 0).Sum(e => e.Cost).ToString("0.0000") : "not reported")}");
                    }
                }
            }

            foreach (var p in ais)
            {
                var llm = (LlmAgent)runner.Seats[p];
                sb.AppendLine();
                sb.AppendLine($"===== {p} ({llm.Kind}): every prompt and reply =====");
                lock (llm.Transcript)   // the agent may still be appending from its worker thread
                {
                    if (llm.Transcript.Count > 0) { sb.AppendLine("--- system prompt (same every phase)"); sb.AppendLine(llm.Transcript[0].System); }
                    foreach (var ex in llm.Transcript)
                    {
                        string tokens = ex.InputTokens + ex.OutputTokens > 0 ? $", {ex.InputTokens:N0} in / {ex.OutputTokens:N0} out" : "";
                        sb.AppendLine($"--- round {ex.Round} {ex.Phase} ({ex.Seconds:0.0}s{tokens}){(ex.Error != null ? "  error: " + ex.Error : "")}");
                        sb.AppendLine(ex.User);
                        if (!string.IsNullOrWhiteSpace(ex.Thinking))
                        {
                            sb.AppendLine(">>> thinking:");
                            sb.AppendLine(ex.Thinking.Trim());
                        }
                        sb.AppendLine(">>> reply:");
                        sb.AppendLine(ex.Reply ?? "(none)");
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>Opens a folder in the OS file browser, creating it first so the link always resolves.</summary>
        public static void OpenFolder(string dir)
        {
            try { Directory.CreateDirectory(dir); Application.OpenURL("file:///" + dir.Replace('\\', '/')); }
            catch (Exception e) { Debug.LogWarning("could not open " + dir + ": " + e.Message); }
        }
    }
}
