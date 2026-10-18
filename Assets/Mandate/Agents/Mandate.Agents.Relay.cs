// Mandate.Agents.Relay — a seat played by whoever answers files in a folder.
//
// Each phase the seat writes its prompt to <folder>/prompt-NN.txt (the same system + user text an API seat is sent)
// and waits for <folder>/reply-NN.txt. Whatever writes the reply — a coding agent, a script, a person in a text
// editor — is the player. The engine stays the referee: it builds the prompt, checks the reply, applies the rules.
//
// Used for "Claude plays itself": one Claude Code subagent per seat, each reading only its own folder.
// Needs a file system, so it works in the editor and the console but not in a WebGL build.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mandate.Agents
{
    public sealed class RelayClient : IChatClient
    {
        readonly string folder;
        int turn;

        /// <summary>folder = this seat's own directory. Created if missing; old prompt and reply files are cleared.</summary>
        public RelayClient(string folder)
        {
            this.folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(this.folder);
            foreach (var f in Directory.GetFiles(this.folder, "prompt-*.txt")) File.Delete(f);
            foreach (var f in Directory.GetFiles(this.folder, "reply-*.txt")) File.Delete(f);
        }

        public async Task<ChatReply> Complete(string system, string user, CancellationToken ct)
        {
            turn++;
            string n = turn.ToString("00");
            string prompt = Path.Combine(folder, "prompt-" + n + ".txt");
            string reply = Path.Combine(folder, "reply-" + n + ".txt");

            // Write to a temp name and rename, so a reader never sees half a prompt.
            File.WriteAllText(prompt + ".tmp", "=== SYSTEM ===\n" + system + "\n=== USER ===\n" + user + "\n=== REPLY ===\nWrite your JSON reply to " + reply + "\n");
            File.Move(prompt + ".tmp", prompt);

            // Wait for the reply. It counts once two reads a moment apart agree, so a half-written file is never taken.
            string last = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                string text = null;
                try { if (File.Exists(reply)) text = File.ReadAllText(reply); } catch (IOException) { }
                if (!string.IsNullOrWhiteSpace(text) && text == last) return new ChatReply(text);
                last = text;
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
        }
    }
}
