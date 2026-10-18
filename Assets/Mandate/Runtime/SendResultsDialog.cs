// SendResultsDialog — the opt-in screen for sending a finished game to the developer.
//
// Opens by itself once when a game ends, and again from the scoreboard's "Send results..." button. Nothing is sent until the player reads what is included
// and presses Send. They can add a comment (a bug, a model that misbehaved, a card that felt unfair) and,
// optionally, an email address for a reply. One send per game: after it goes, the button turns into "Sent".
// The report is built by ResultReport and delivered by ResultSender.

using UnityEngine;

namespace Mandate.Viewer
{
    public sealed class SendResultsDialog
    {
        readonly MandateGameView view;

        /// <summary>True while the dialog is showing.</summary>
        public bool Open { get; private set; }

        /// <summary>True once this game has been sent (or saved to send later), so it is not sent twice.</summary>
        public bool Done { get; private set; }

        string comment = "", contact = "", status = "";
        bool sending;

        public SendResultsDialog(MandateGameView view) { this.view = view; }

        public void Show() { Open = true; }
        public void Hide() { if (!sending) Open = false; }

        /// <summary>Forgets the last game. Called when a new game starts.</summary>
        public void Reset() { Open = false; Done = false; sending = false; comment = ""; status = ""; }

        public void Draw()
        {
            float w = Mathf.Min(Ui.CanvasW - 40f, 560f), h = 430f;
            GUILayout.BeginArea(new Rect((Ui.CanvasW - w) / 2f, Mathf.Max(Ui.TopH + 12f, (Ui.CanvasH - h) / 2f), w, h), Ui.Panel);

            GUILayout.Label("<b>Send this game to the developer</b>", Ui.Title);
            GUILayout.Label("This helps balance the cards and pick which AI models to recommend. It sends:", Ui.Wrap);
            GUILayout.Label("-  the final scores, each seat's card, and which model played it\n"
                + "-  the full transcript: the game log, every chat message (yours too), every AI prompt and reply, "
                + "the AIs' private notes, and their thinking where the model shares it\n"
                + "-  the game version and your platform, plus anything you write below", Ui.Wrap);
            GUILayout.Label("<color=#7fdc7f>API keys are never sent.</color> It goes to heartingNinja by email.", Ui.Wrap);

            GUILayout.Space(6);
            GUILayout.Label("Comment (optional): a bug, a strange move, a card that felt unfair...", Ui.Small);
            comment = GUILayout.TextArea(comment, 2000, Ui.Field, GUILayout.Height(70));
            GUILayout.Label("Your email (optional), only if you'd like a reply", Ui.Small);
            contact = GUILayout.TextField(contact, 200, Ui.Field);

            GUILayout.FlexibleSpace();
            if (status.Length > 0) GUILayout.Label(status, Ui.Wrap);

            GUILayout.BeginHorizontal();
            GUI.enabled = !sending && !Done;
            if (GUILayout.Button(sending ? "Sending..." : Done ? "Sent" : "<b>Send</b>", Ui.Button, GUILayout.Height(28))) Send();
            GUI.enabled = !sending;
            if (GUILayout.Button(Done ? "Close" : "Not now", Ui.Button, GUILayout.Height(28))) Hide();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        void Send()
        {
            if (view.Runner == null || !view.Runner.Finished) return;
            sending = true;
            status = "Sending... (a long AI game can take a few seconds)";
            var report = ResultReport.Build(view, comment, contact);
            view.StartCoroutine(view.Results.Send(report, (delivered, message) =>
            {
                sending = false;
                Done = true;   // sent, or saved to go next launch; either way, don't queue it twice
                status = delivered ? $"<color=#7fdc7f>{message}</color>" : $"<color=#ffb37f>{message}</color>";
            }));
        }
    }
}
