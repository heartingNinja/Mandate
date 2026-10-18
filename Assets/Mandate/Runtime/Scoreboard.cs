// Scoreboard — the final scores, shown below the board once the game is over.
//
// One row per seat, best first: score, objective and whether it was achieved, seat kind. Then the buttons to
// open the transcripts folder or, in a browser, download the transcript (if one was saved), send the game to the developer (opt-in, opens
// SendResultsDialog), play again with the same table, or change the table.

using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Mandate.Viewer
{
    public sealed class Scoreboard
    {
        readonly MandateGameView view;

        public Scoreboard(MandateGameView view) { this.view = view; }

        public void Draw()
        {
            var runner = view.Runner;
            var st = runner.State;
            float boardH = Ui.ScoreboardH(st.Players.Count);
            float w = 520f, h = boardH - 12f;
            var rect = new Rect(Ui.LeftW + (Ui.CanvasW - Ui.LeftW - Ui.RightW - w) / 2f, Ui.CanvasH - boardH + 4f, w, h);   // below the board
            GUILayout.BeginArea(rect, Ui.Card);

            GUILayout.Label("<b>Final scores</b>  <size=11>objective 10, +2 per correct read, -2 per opponent who read you</size>", Ui.Small);
            foreach (var kv in runner.Scores.OrderByDescending(k => k.Value))
            {
                bool met = st.Assigned[kv.Key].Achieved(st, kv.Key);
                GUILayout.Label($"{view.Colors.Tag(kv.Key)}  <b>{kv.Value,3}</b>   {st.Assigned[kv.Key].Id} {(met ? "<color=#7fdc7f>achieved</color>" : "<color=#ffb37f>missed</color>")}   <size=11>{runner.Seats[kv.Key].Kind}</size>", Ui.Label);
            }
            GUILayout.Label(view.LastTranscriptPath != null
                ? "<size=11>Every AI seat's private notes are in the log above. The full transcript, with every prompt and reply, is saved.</size>"
                : "<size=11>Every AI seat's private notes are in the log above.</size>", Ui.Wrap);

            GUILayout.BeginHorizontal();
            if (view.LastTranscriptPath != null && WebFiles.InBrowser)
            {
                if (GUILayout.Button("Download transcript", Ui.Button)) DownloadTranscript();   // a browser cannot open a folder
            }
            else if (view.LastTranscriptPath != null && GUILayout.Button("Open transcripts folder", Ui.Button)) TranscriptWriter.OpenFolder(view.TranscriptDir);
            if (GUILayout.Button(view.SendDialog.Done ? "Results sent" : "Send results...", Ui.Button)) view.SendDialog.Show();
            if (GUILayout.Button("Play again", Ui.Button)) view.PlayAgain();   // next seed: new board and seating
            if (GUILayout.Button("Change the table", Ui.Button)) view.Table.Show();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        // Browser builds: hands the saved transcript to the player as a file download.
        void DownloadTranscript()
        {
            try { WebFiles.Download(Path.GetFileName(view.LastTranscriptPath), File.ReadAllText(view.LastTranscriptPath)); }
            catch (Exception e) { Debug.LogWarning("could not download the transcript: " + e.Message); }
        }
    }
}
