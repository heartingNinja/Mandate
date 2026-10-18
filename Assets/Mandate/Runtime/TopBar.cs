// TopBar — the strip along the top of the screen.
//
// Row 1: status (round and phase, or who is still thinking), the Table and New game buttons, Step / Run to end,
// auto-play with a speed slider, the live "AI thinking" switch, and the Run tests / Sim buttons.
// Row 2: the player count and seed of the current game, read only. Both are set on the Table screen before a game
// starts.

using System.Linq;
using UnityEngine;
using Mandate.Agents;
using Mandate.Core;

namespace Mandate.Viewer
{
    public sealed class TopBar
    {
        readonly MandateGameView view;

        public TopBar(MandateGameView view) { this.view = view; }

        public void Draw()
        {
            var runner = view.Runner;
            var st = runner?.State;
            bool pending = runner != null && runner.PhasePending;

            GUILayout.BeginArea(new Rect(0, 0, Ui.CanvasW, Ui.TopH), Ui.Panel);

            // ---- row 1: status and playback
            GUILayout.BeginHorizontal();
            GUILayout.Label("MANDATE", Ui.Title, GUILayout.Width(120));
            string status = st == null ? "<i>set up the table to start</i>" :
                pending ? $"<color=#ffd27f>Thinking: {string.Join(", ", runner.Waiting)}</color>" :
                st.Phase == Phase.Finished ? "Finished" :
                st.Phase == Phase.Draft ? "Objective draft" :
                st.Phase == Phase.Guess ? "Guessing phase" :
                $"Round {st.Round} (ends {Rules.MinRounds}-{Rules.MaxRounds})  -  {(st.Phase == Phase.Negotiate ? "negotiate" : "act")}";   // the real last round is secret
            GUILayout.Label($"<b>{status}</b>", Ui.Label, GUILayout.Width(250));

            if (GUILayout.Button(new GUIContent("Table", "Choose who plays each seat (you, a scripted bot or an AI model), paste API keys, and set options. The game pauses while it is open."), Ui.Button, GUILayout.Width(64))) { if (view.Table.Open) view.Table.Hide(); else view.Table.Show(); }
            if (GUILayout.Button(new GUIContent("New game", "Throw away this game and start a new one with the Table screen's seats and rules (same seed, so the same board)."), Ui.Button, GUILayout.Width(90))) { view.Table.Hide(); view.NewGame(); }
            GUI.enabled = runner != null && !runner.Finished && !pending && !view.Table.Open;
            if (GUILayout.Button(new GUIContent("Step", "Play one phase (negotiate or act). Greyed out while AI seats are still thinking."), Ui.Button, GUILayout.Width(64))) view.Step();
            GUI.enabled = runner != null && !runner.Finished && !view.Table.Open;
            if (GUILayout.Button(new GUIContent(view.RunToEnd ? "Stop" : "Run to end", "Play phase after phase, as fast as the seats can answer, until the game ends. Ignores the speed slider. Click Stop to pause."), Ui.Button, GUILayout.Width(90))) view.RunToEnd = !view.RunToEnd;
            GUI.enabled = true;
            view.autoPlay = GUILayout.Toggle(view.autoPlay, new GUIContent(" auto", "Play the next phase by itself after the speed slider's delay. Waits whenever you have a move to make."), Ui.Toggle, GUILayout.Width(58));
            string speedTip = $"How long auto waits between phases: left is slow (3 seconds), right is fast (0.05 seconds). Now {view.secondsPerPhase:0.00} s. Only used while auto is on; AI seats still take as long as they need to answer.";
            GUILayout.Label(new GUIContent("speed", speedTip), Ui.Small, GUILayout.Width(42));
            view.secondsPerPhase = 3.05f - GUILayout.HorizontalSlider(3.05f - view.secondsPerPhase, 0.05f, 3f, GUILayout.Width(80));   // slider right = faster
            Ui.Tip(speedTip);

            // Gap and divider so "AI thinking" doesn't read as part of the auto / speed group.
            GUILayout.Space(14);
            GUILayout.Label("|", Ui.Small, GUILayout.Width(8));
            GUILayout.Space(6);

            // Live switch: watch the AI seats' private notes as they go, or play it straight and read them at the end.
            bool wasRevealing = view.revealAgentNotes;
            view.revealAgentNotes = GUILayout.Toggle(view.revealAgentNotes, new GUIContent(" AI thinking", "Show each AI seat's private notes in the log as the game goes. Off = play it straight; the notes are always shown at the end."), Ui.Toggle, GUILayout.Width(100));
            if (view.revealAgentNotes && !wasRevealing) view.Narrator.LogCurrentNotes(runner);

            GUILayout.FlexibleSpace();
            // The agent tests start a local web server, which a browser cannot do, so a web build has no tests button.
            if (!WebFiles.InBrowser && GUILayout.Button(new GUIContent("Run tests", "Run the built-in rules and agent tests. Failures and the totals go to the log."), Ui.Button, GUILayout.Width(84))) view.RunTests();
            if (GUILayout.Button(new GUIContent("Sim 200 games", "Play 200 quick games between scripted bots (this player count, seed and bot kind, standard rules) and put the results in the log."), Ui.Button, GUILayout.Width(114))) view.RunSim();
            GUILayout.EndHorizontal();

            // ---- row 2: the game being played. Read only: players and seed are set on the Table screen before a game.
            GUILayout.BeginHorizontal();
            string gameTip = "The player count and seed this game was started with. Change them on the Table screen; they apply from the next game.";
            if (st == null) GUILayout.Label(new GUIContent("<i>no game yet - press Table to choose the seats</i>", gameTip), Ui.Small);
            else GUILayout.Label(new GUIContent($"<i>this game:</i>  {st.Players.Count} players  -  seed {view.GameSeed}", gameTip), Ui.Small);
            GUILayout.FlexibleSpace();
            int aiSeats = view.seats.Take(view.players).Count(s => s != null && Seats.Kinds.TryGetValue(s.NormalizedKind(), out var k) && k.Family != "bot" && k.Family != "human");
            GUILayout.Label($"<size=11>{(aiSeats == 0 ? "no AI seats" : aiSeats + " AI seat" + (aiSeats == 1 ? "" : "s"))} at the table - press Table to change</size>", Ui.Small);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}
