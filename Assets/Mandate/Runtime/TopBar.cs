// TopBar — the strip along the top of the screen.
//
// Row 1: status (round and phase, or who is still thinking), the Table and New game buttons, Step / Run to end,
// the live "AI thinking" switch, and Run tests (not in a browser).
// Row 2: the player count and seed of the current game, read only. Both are set on the Table screen before a game
// starts.

using UnityEngine;
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

            if (Ui.TipButton("Table", "Choose who plays each seat (you, a scripted bot or an AI model), paste API keys, and set options. The game pauses while it is open.", GUILayout.Width(64))) { if (view.Table.Open) view.Table.Hide(); else view.Table.Show(); }
            if (Ui.TipButton("New game", "Throw away this game and start a new one with the Table screen's seats and rules (same seed, so the same board).", GUILayout.Width(90))) { view.Table.Hide(); view.NewGame(); }
            GUI.enabled = runner != null && !runner.Finished && !pending && !view.Table.Open;
            if (Ui.TipButton("Step", "Play one phase (negotiate or act). Greyed out while AI seats are still thinking.", GUILayout.Width(64))) view.Step();
            GUI.enabled = runner != null && !runner.Finished && !view.Table.Open;
            if (Ui.TipButton(view.RunToEnd ? "Stop" : "Run to end", "Play phase after phase, as fast as the seats can answer, until the game ends. Click Stop to pause.", GUILayout.Width(90))) view.RunToEnd = !view.RunToEnd;
            GUI.enabled = true;
            GUILayout.Space(14);

            // Live switch: watch the AI seats' private notes as they go, or play it straight and read them at the end.
            bool wasRevealing = view.revealAgentNotes;
            view.revealAgentNotes = Ui.TipToggle(view.revealAgentNotes, " AI thinking", "Show each AI seat's private notes in the log as the game goes. Off = play it straight; the notes are always shown at the end.", GUILayout.Width(100));
            if (view.revealAgentNotes && !wasRevealing) view.Narrator.LogCurrentNotes(runner);

            GUILayout.FlexibleSpace();
            // The agent tests start a local web server, which a browser cannot do, so a web build has no tests button.
            if (!WebFiles.InBrowser && Ui.TipButton("Run tests", "Run the built-in rules and agent tests. Failures and the totals go to the log.", GUILayout.Width(84))) view.RunTests();
            GUILayout.EndHorizontal();

            // ---- row 2: the game being played. Read only: players and seed are set on the Table screen before a game.
            GUILayout.BeginHorizontal();
            string gameTip = "The player count and seed this game was started with. Change them on the Table screen; they apply from the next game.";
            if (st == null) Ui.TipLabel("<i>no game yet - press Table to choose the seats</i>", gameTip, Ui.Small);
            else Ui.TipLabel($"<i>this game:</i>  {st.Players.Count} players  -  seed {view.GameSeed}", gameTip, Ui.Small);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}
