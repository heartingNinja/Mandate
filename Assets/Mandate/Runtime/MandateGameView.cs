// MandateGameView — the visual test harness for the Mandate engine, and the table you sit at to play AI seats.
//
// Put this on any GameObject in a scene and press Play. A table-setup screen opens first: pick who sits in each
// seat (you, a scripted bot, a hosted or local language model, a bot over HTTP), type any API keys, and start.
// Nothing needs to be set in the inspector or the environment, though both still work as defaults. The setup and
// the keys can be remembered on this PC so the next game is one click.
//
// Everything the referee knows is on screen: every objective, every message (public and private), every submitted
// action and whether it was legal, every belief report, every guess, and the final scores. The AI seats' private
// notes are hidden while you play (or shown live, if you flip "AI thinking" in the top bar) and always revealed at
// the end, and every finished game is written to a transcript file with every prompt and reply.
//
// Stepping is non-blocking: Step() begins a phase (every seat gets its view and starts answering), Update() polls
// the runner, and FinishStep() applies the answers once they are all in or the timeout has passed. Scripted seats
// answer instantly so the feel is unchanged; a language model taking twenty seconds never stalls the editor.
//
// This is the only MonoBehaviour. It holds the inspector fields and the game loop, and owns one plain object per
// part of the screen (all IMGUI, no prefabs, no canvas). Each part is its own script in this folder:
//   MandateGameView.cs    this file: inspector fields, new game / step / finish step, and the wiring
//   SeatSetup.cs          one seat's setup (inspector and Table screen), converted to a SeatConfig at game start
//   HumanBot.cs           the IBot that answers with whatever the human panel queued
//   TopBar.cs             top: round and phase, playback, Table / New game, AI thinking, tests, next-game settings
//   PlayersPanel.cs       left: one card per player (colour, kind, objective, met / not met, last action, score)
//   BoardView.cs          centre: the board. Colour = owner, number = node id, +n = production, pips = fortify
//   GameLog.cs            right: the scrolling log of everything that happened
//   HumanPanel.cs         bottom: your controls when you are playing
//   Scoreboard.cs         bottom: the final scores when the game is over
//   TableScreen.cs        overlay: who sits where, keys, options, validation
//   GameNarrator.cs       turns each finished phase into log lines
//   SeatColors.cs         seat colours and coloured name tags
//   TableFile.cs          what the Table screen remembers on disk (table.json)
//   TranscriptWriter.cs   writes a finished game to a text file
//   SendResultsDialog.cs  overlay: opt-in screen to send a finished game to the developer
//   ResultReport.cs       what gets sent: scores, seats, models, the transcript (never the keys)
//   ResultSender.cs       posts it to the results relay (Tools/MandateResultsRelay), retries later if offline
//   ResultsRelaySettings.cs  the relay's URL and token, read from a file kept out of git
//   WebRequests.cs        sends the AI seats' HTTP calls with UnityWebRequest, so the game also runs in a browser
//   WebFiles.cs           browser builds only: saves files for good and downloads transcripts
//   Ui.cs                the shared IMGUI frame: panel sizes, the virtual canvas, the styles
//
// Art hooks: the sprite, colours, line material and board radius are inspector fields read by BoardView. Leave
// them empty for the procedural placeholder look; assign your own assets to restyle the board without code.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using Mandate.Agents;
using Mandate.Core;
using Mandate.Sim;

namespace Mandate.Viewer
{
    public sealed class MandateGameView : MonoBehaviour
    {
        // ------------------------------------------------------------------ inspector: game setup
        [Header("Game setup (defaults; the in-game Table screen edits the same values)")]
        [Tooltip("Seats at the table. The board has 3 ring nodes per player plus the hub; each player starts owning 2 of theirs.")]
        [Range(Rules.MinPlayers, Rules.MaxPlayers)] public int players = 4;

        [Tooltip("Seed for the board layout, objective draft and bot decisions. Same seed = same game, every time (for scripted seats; models are not deterministic).")]
        public int seed = 1;

        [Tooltip("Which scripted bots fill seats that have no kind set: greedy (objective-aware heuristic), random, or mixed (alternating).")]
        public string botKind = "greedy";

        [Tooltip("Play yourself, as the first entry in Seats. Same as choosing 'you' for the first row on the Table screen.")]
        [FormerlySerializedAs("humanControlsSeat1")]
        public bool humanPlays = false;

        [Tooltip("Shuffle who sits in which seat at the start of each game, so nobody always has the same neighbours. The shuffle comes from the seed: same seed, same seating.")]
        public bool shuffleSeats = true;

        [Tooltip("Who is at the table (element 0 can be you). Which seat each one gets is shuffled when Shuffle Seats is on; otherwise element 0 = P1. The Table screen edits this list at run time. Kinds: you, greedy, random, openrouter, openai, gemini, anthropic, groq, together, deepseek, xai, mistral, ollama, lmstudio, vllm, compatible, http. Details: Docs/Agents.md.")]
        public SeatSetup[] seats = new SeatSetup[0];

        [Tooltip("Open the Table screen when Play starts, instead of starting a bot game straight away.")]
        public bool openTableOnStart = true;

        [Tooltip("Save the table (seats and options) to persistentDataPath/Mandate/table.json when a game starts, and load it next time.")]
        public bool rememberTable = true;

        [Tooltip("Also save API keys typed on the Table screen into that file. It is plain text on this PC only; untick to keep keys for this session. Always starts off in a browser (see LoadTable).")]
        public bool rememberKeys = true;

        [Tooltip("Seconds an AI seat gets to answer each phase. A seat that is late counts as a pass for that phase. Cheap 'flash' models on shared servers often need 1-2 minutes, so leave room.")]
        [Range(10, 600)] public int phaseTimeoutSeconds = 240;

        [Tooltip("Show each AI seat's private notes in the log as the game goes. Can be flipped any time from the top bar. Notes are always revealed at the end.")]
        public bool revealAgentNotes = false;

        [Tooltip("Write a plain-text transcript of every finished game (log, notes, every prompt and reply) to persistentDataPath/Mandate/transcripts.")]
        public bool saveTranscripts = true;

        // The results relay's URL and token are not inspector fields: the scene is in git, and they must not be.
        // They are read from Assets/Resources/MandateResultsRelay.json (ResultsRelaySettings.cs), which git ignores.

        // ------------------------------------------------------------------ inspector: playback
        [Header("Playback")]
        [Tooltip("Advance a phase automatically every 'Seconds Per Phase'. Pauses whenever the human seat needs input. Switched on by the Table screen's Start button when nobody is playing.")]
        public bool autoPlay = false;

        [Tooltip("Delay between automatic phase steps.")]
        [Range(0.05f, 3f)] public float secondsPerPhase = 0.7f;

        // ------------------------------------------------------------------ inspector: art hooks
        [Header("Art (optional - leave empty for the procedural placeholder)")]
        [Tooltip("Sprite used for every node fill. Empty = a generated anti-aliased circle.")]
        public Sprite nodeSprite;

        [Tooltip("Sprite drawn behind each node as its outline ring. Empty = the same circle as the fill, scaled up.")]
        public Sprite ringSprite;

        [Tooltip("Material for the node sprites. Empty = a generated unlit sprite material. Assign an asset for builds so the shader is guaranteed to ship.")]
        public Material spriteMaterial;

        [Tooltip("Material for the edge lines between nodes. Empty = a generated unlit sprite material so vertex colours show.")]
        public Material lineMaterial;

        [Tooltip("Colour per seat, in seat order. Needs at least 8 entries.")]
        public Color[] playerColors =
        {
            new Color(0.90f, 0.30f, 0.25f), new Color(0.25f, 0.55f, 0.95f), new Color(0.30f, 0.75f, 0.35f), new Color(0.95f, 0.65f, 0.20f),
            new Color(0.65f, 0.40f, 0.85f), new Color(0.20f, 0.80f, 0.80f), new Color(0.90f, 0.85f, 0.25f), new Color(0.95f, 0.45f, 0.70f),
        };

        [Tooltip("Colour of nodes nobody owns.")]
        public Color neutralColor = new Color(0.42f, 0.42f, 0.46f);

        [Tooltip("Camera clear colour behind the board.")]
        public Color backgroundColor = new Color(0.10f, 0.11f, 0.13f);

        [Tooltip("World-space radius of the ring the outer nodes sit on. The hub (node 0) sits in the centre.")]
        public float boardRadius = 4.2f;

        // ------------------------------------------------------------------ the game, and the parts of the screen
        /// <summary>Drives the referee with the seats. Null until the first game starts.</summary>
        public GameRunner Runner { get; private set; }

        /// <summary>The seed the current game was started with (the seed field may already be set for the next one).</summary>
        public int GameSeed { get; private set; }

        /// <summary>The bot behind your seat when a person is playing; null otherwise.</summary>
        public HumanBot Human { get; private set; }

        /// <summary>The seat you sit in this game ("P3"), or null when nobody is playing.</summary>
        public string HumanSeat { get; private set; }

        public GameLog Log { get; private set; }
        public SeatColors Colors { get; private set; }
        public GameNarrator Narrator { get; private set; }
        public BoardView Board { get; private set; }
        public TableScreen Table { get; private set; }
        public SendResultsDialog SendDialog { get; private set; }

        /// <summary>Sends finished games to the results relay. Created in Start, from ResultsRelaySettings.</summary>
        public ResultSender Results { get; private set; }

        /// <summary>Sends every AI seat's HTTP calls while this view runs. Ticked from Update.</summary>
        WebRequests web;
        TopBar topBar;
        PlayersPanel playersPanel;
        HumanPanel humanPanel;
        Scoreboard scoreboard;

        /// <summary>Keep stepping as soon as each phase completes. The top bar's "Run to end" button.</summary>
        public bool RunToEnd { get; set; }

        /// <summary>Where the finished game's transcript was written, or null.</summary>
        public string LastTranscriptPath { get; private set; }

        HashSet<int> changed = new HashSet<int>();   // nodes whose owner changed in the last resolution
        float timer;                                 // seconds since the last auto-play step

        public string MandateDir => Path.Combine(Application.persistentDataPath, "Mandate");
        public string TablePath => Path.Combine(MandateDir, "table.json");
        public string TranscriptDir => Path.Combine(MandateDir, "transcripts");
        public string ResultsPendingDir => Path.Combine(MandateDir, "results-pending");

        // Height of whatever is drawn along the bottom, so the board can stay clear of it.
        float BottomH =>
            Runner == null ? 8f :
            Human != null && !Runner.Finished ? Ui.HumanH :                       // your controls
            Runner.Finished ? Ui.ScoreboardH(Runner.State.Players.Count) :         // final scoreboard
            8f;

        // ================================================================== Unity lifecycle
        void Awake()
        {
            Log = new GameLog();
            Colors = new SeatColors(playerColors, neutralColor);
            Narrator = new GameNarrator(Log, Colors);
            Board = new BoardView(this);
            Table = new TableScreen(this);
            topBar = new TopBar(this);
            playersPanel = new PlayersPanel(this);
            humanPanel = new HumanPanel(this);
            scoreboard = new Scoreboard(this);
            SendDialog = new SendResultsDialog(this);
            // Every seat's calls go through UnityWebRequest while the game runs: the only way out of a browser, and
            // the editor and desktop builds use it too so the web path is the one that gets played.
            web = new WebRequests();
            Http.Transport = web;
        }

        void OnDestroy()
        {
            if (web == null) return;
            web.CancelAll();
            if (Http.Transport == web) Http.Transport = new HttpClientTransport();   // back to the default for edit-mode tests
        }

        void Start()
        {
            // AI seats take minutes per phase, so keep playing while you look at another window or browser tab.
            // Without this, Unity stops updating when unfocused and an AI game sits frozen until you click back.
            Application.runInBackground = true;
            var relay = ResultsRelaySettings.Load();
            Results = new ResultSender(relay.url, relay.token, ResultsPendingDir);
            StartCoroutine(Results.RetryPending());   // games the player chose to send earlier while offline
            LoadTable();
            if (humanPlays) { EnsureSeats(1); if (!seats[0].IsHuman() && seats[0].NormalizedKind() == "") seats[0].kind = "you"; }
            if (openTableOnStart) Table.Show();
            else NewGame();
        }

        // Scripts recompiled during Play mode wipe everything Awake built (and the game in progress) without
        // calling Awake again. Rather than throw every frame, wait for the user to stop and press Play again.
        bool Reloaded => Board == null;

        void Update()
        {
            if (Reloaded) return;
            web.Tick();                                   // AI seats' requests: start, finish, cancel
            Board.FitCamera(BottomH);
            if (Runner == null) return;
            if (Runner.PhasePending)
            {
                if (Runner.PhaseReady) FinishStep();      // every seat answered (or the timeout passed)
                return;
            }
            if (Runner.Finished) { RunToEnd = false; return; }
            if (Table.Open) return;                       // paused while the table is being edited
            if (RunToEnd) { Step(); return; }
            if (autoPlay && Human == null)
            {
                timer += Time.deltaTime;
                if (timer >= secondsPerPhase) { timer = 0f; Step(); }
            }
        }

        void OnGUI()
        {
            if (Reloaded)
            {
                GUI.Label(new Rect(20, 20, 600, 30), "Scripts were recompiled during Play. Stop and press Play again.");
                return;
            }
            Ui.EnsureStyles();
            Ui.BeginTips();   // only the control under the mouse right now sets this repaint's tooltip
            GUI.matrix = Matrix4x4.Scale(new Vector3(Ui.Scale, Ui.Scale, 1f));   // draw on the virtual canvas
            topBar.Draw();
            if (Runner != null)
            {
                Board.DrawLabels(Runner.State.Nodes, HumanSeat);
                playersPanel.Draw();
                Log.Draw();
                if (Human != null && !Runner.Finished) humanPanel.Draw();
                if (Runner.Finished) scoreboard.Draw();
            }
            if (Table.Open) Table.Draw();
            if (SendDialog.Open) SendDialog.Draw();
            CardIcons.DrawTooltip();   // last, so it sits on top of every panel
        }

        // ================================================================== game control
        /// <summary>Starts a fresh game from the current table (inspector fields, as edited by the Table screen).</summary>
        public void NewGame()
        {
            Log.Clear(); Narrator.Reset(); humanPanel.Reset(); SendDialog.Reset(); changed.Clear();
            timer = 0f; RunToEnd = false; LastTranscriptPath = null;
            EnsureSeats(players);

            var names = Enumerable.Range(1, players).Select(i => "P" + i).ToList();
            var referee = new Referee("local-" + seed, names, seed);
            GameSeed = seed;

            // One agent per entry in 'seats', placed in the seat Seating gives it (entry i sits in seat seatOf[i]).
            // Scripted bots get RNGs derived from the game seed so replays are exact.
            var seatOf = Seating.Assign(players, seed, shuffleSeats);
            var rng = new System.Random(seed);
            Human = humanPlays || seats[0].IsHuman() ? new HumanBot() : null;
            HumanSeat = Human != null ? names[seatOf[0]] : null;
            Narrator.HumanSeat = HumanSeat;   // the log keeps the AI seats' cards secret from you until the end
            var table = new Dictionary<string, IAgent>();
            for (int i = 0; i < names.Count; i++)
            {
                int botSeed = rng.Next();
                string p = names[seatOf[i]];
                if (i == 0 && Human != null) { table[p] = new BotAgent(Human); continue; }
                string kind = seats[i].NormalizedKind();
                if (seats[i].IsHuman())
                {
                    Log.Add($"<color=#ffb37f>row {i + 1}: only the first row can be you. Using {botKind} instead.</color>");
                    kind = "";
                }
                if (kind == "") { table[p] = new BotAgent(Bots.Make(Bots.KindForSeat(botKind, i), botSeed)); continue; }
                try { table[p] = Seats.Make(ConfigFor(i), botSeed); }
                catch (Exception e)
                {
                    Log.Add($"<color=#ff6b6b>seat {p} ({kind}) could not be created: {e.Message} Using a greedy bot instead.</color>");
                    table[p] = new BotAgent(Bots.Make("greedy", botSeed));
                }
            }
            Runner = new GameRunner(referee, table) { PhaseTimeout = TimeSpan.FromSeconds(phaseTimeoutSeconds) };

            Log.Add($"<b>New game</b> - {players} players, seed {seed}{(shuffleSeats ? ", seats shuffled" : "")}{(Human != null ? $", <b>you are {Colors.Tag(HumanSeat)}</b>" : "")}");
            Log.Add($"seats: {string.Join(", ", names.Select(p => $"{Colors.Tag(p)} {table[p].Kind}"))}");
            Log.Add($"in play this game (public): {string.Join(", ", referee.State.Dealt.Select(o => o.Id))}");
            if (referee.State.Phase == Phase.Draft)
                foreach (var p in names.Where(p => HumanSeat == null || p == HumanSeat))
                    Log.Add($"{Colors.Tag(p)} offered: {string.Join(" | ", referee.State.DraftOptions[p].Select(o => o.Id))}");
            if (table.Values.Any(s => s is LlmAgent))
                Log.Add(revealAgentNotes
                    ? "<size=11>AI thinking is shown in the log as it happens. Flip 'AI thinking' in the top bar to hide it.</size>"
                    : "<size=11>AI notes are hidden while you play and revealed at the end. Flip 'AI thinking' in the top bar to watch them live.</size>");

            Board.Build(Runner.State.Nodes);
            Board.Refresh(Runner.State.Nodes, changed, Colors);
        }

        /// <summary>The SeatConfig for seat i, with any key typed on the Table screen filled in for its provider.</summary>
        public SeatConfig ConfigFor(int i)
        {
            var cfg = seats[i].ToConfig();
            if (string.IsNullOrWhiteSpace(cfg.ApiKey) && Seats.Kinds.TryGetValue(cfg.Kind, out var info))
            {
                string env = !string.IsNullOrWhiteSpace(cfg.ApiKeyEnv) ? cfg.ApiKeyEnv : info.KeyEnv;
                if (env != null && Table.Keys.TryGetValue(env, out var typed) && !string.IsNullOrWhiteSpace(typed)) cfg.ApiKey = typed.Trim();
            }
            return cfg;
        }

        /// <summary>Grows the seats array so every seat up to 'count' has an entry.</summary>
        public void EnsureSeats(int count)
        {
            if (seats == null) seats = new SeatSetup[0];
            if (seats.Length >= count) { for (int i = 0; i < seats.Length; i++) if (seats[i] == null) seats[i] = new SeatSetup(); return; }
            var grown = new SeatSetup[count];
            for (int i = 0; i < count; i++) grown[i] = i < seats.Length && seats[i] != null ? seats[i] : new SeatSetup();
            seats = grown;
        }

        /// <summary>Begins one phase: every seat gets its view and starts answering. Scripted seats answer at once, so the
        /// phase completes immediately; AI seats think in the background and Update finishes the step when they are all in.</summary>
        public void Step()
        {
            if (Runner == null || Runner.Finished || Runner.PhasePending) return;
            Runner.BeginPhase();
            if (Runner.PhaseReady) FinishStep();
        }

        // Applies the answers of the pending phase, narrates what changed, and recolours the board.
        void FinishStep()
        {
            Narrator.BeforePhase(Runner.State);
            Runner.CompletePhase();
            changed = Narrator.AfterPhase(Runner);
            if (revealAgentNotes) Narrator.LogCurrentNotes(Runner);

            if (Runner.Finished)
            {
                Narrator.RevealNotes(Runner);
                if (saveTranscripts) SaveTranscript();
                SendDialog.Show();   // ask once in the middle of the screen; "Not now" closes it, the scoreboard button reopens it
            }
            Board.Refresh(Runner.State.Nodes, changed, Colors);
        }

        /// <summary>The scoreboard's Play again: the next seed, so a fresh board and seating. "New game" in the top bar
        /// keeps the seed and replays the same game.</summary>
        public void PlayAgain()
        {
            seed++;
            NewGame();
        }

        /// <summary>The Table screen's Start button: remember the table, close the screen, and start.
        /// With nobody at the table there is nothing to wait for, so auto-play goes on; untick it to step by hand.</summary>
        public void StartFromTable()
        {
            humanPlays = seats[0].IsHuman();
            if (!humanPlays) autoPlay = true;
            SaveTable();
            Table.Hide();
            NewGame();
        }

        // ================================================================== files
        void SaveTranscript()
        {
            try
            {
                LastTranscriptPath = TranscriptWriter.Save(TranscriptDir, seed, Log, Runner);
                Log.Add($"<size=11>transcript saved: {LastTranscriptPath}</size>");
            }
            catch (Exception e) { Log.Add($"<color=#ff6b6b>could not save transcript: {e.Message}</color>"); }
        }

        // Applies the remembered table (if any) to the inspector fields and the Table screen's keys.
        void LoadTable()
        {
            // In a browser, keys are kept only until the tab closes unless the player ticks "remember the keys".
            // Sites like itch.io serve every game from one address, so another game there could read saved files.
            if (WebFiles.InBrowser) rememberKeys = false;
            var file = TableFile.Load(TablePath);
            if (file == null) return;
            if (file.players >= Rules.MinPlayers && file.players <= Rules.MaxPlayers) players = file.players;
            revealAgentNotes = file.revealAgentNotes;
            if (file.rulesSaved)   // files from before these were saved keep the inspector's values
            {
                seed = file.seed;
                if (!string.IsNullOrWhiteSpace(file.botKind)) botKind = file.botKind;
                shuffleSeats = file.shuffleSeats;
            }
            if (file.seats != null && file.seats.Count > 0) seats = file.seats.ToArray();
            Table.Keys.Clear();
            foreach (var k in file.keys ?? new List<KeyEntry>())
                if (!string.IsNullOrWhiteSpace(k.env) && !string.IsNullOrWhiteSpace(k.key)) Table.Keys[k.env] = k.key;
            // Saved keys mean the player chose to remember them: keep the box ticked so the next Start doesn't erase them.
            if (Table.Keys.Count > 0) rememberKeys = true;
            humanPlays = seats.Length > 0 && seats[0] != null && seats[0].IsHuman();
        }

        // Writes the current table (and the keys, if allowed) so the next Play starts from it. With "remember" off,
        // anything saved earlier is deleted, so unticking it on a shared computer really forgets the keys.
        void SaveTable()
        {
            if (!rememberTable) { TableFile.Delete(TablePath); return; }
            var file = new TableFile
            {
                players = players, revealAgentNotes = revealAgentNotes, seats = seats.Take(players).ToList(),
                rulesSaved = true, seed = seed, botKind = botKind, shuffleSeats = shuffleSeats,
            };
            if (rememberKeys)
                foreach (var kv in Table.Keys)
                    if (!string.IsNullOrWhiteSpace(kv.Value)) file.keys.Add(new KeyEntry { env = kv.Key, key = kv.Value });
            file.Save(TablePath);
        }

        // ================================================================== tests and sim (top bar buttons)
        /// <summary>Runs the engine's and the agent layer's self-tests inside play mode and prints the result to the log.</summary>
        public void RunTests()
        {
            int pass = 0, fail = 0;
            Action<string> sink = s => { if (s.StartsWith("FAIL")) Log.Add($"<color=#ff6b6b>{s}</color>"); };
            Mandate.Tests.Tests.Out = sink;
            Mandate.AgentTests.Tests.Out = sink;
            // The agent tests wait for a local fake server while blocking this frame, so UnityWebRequest (which only
            // moves between frames) would never answer. They get .NET's HttpClient for the duration.
            var transport = Http.Transport;
            Http.Transport = new HttpClientTransport();
            try
            {
                foreach (var (name, run) in Mandate.Tests.Tests.All().Concat(Mandate.AgentTests.Tests.All()))
                {
                    try { run(); pass++; }
                    catch (Exception e) { fail++; Log.Add($"<color=#ff6b6b>FAIL {name}: {e.Message}</color>"); }
                }
            }
            finally { Http.Transport = transport; }
            string msg = fail == 0 ? $"Tests: {pass} passed" : $"Tests: {pass} passed, {fail} FAILED";
            Log.Add(fail == 0 ? $"<color=#7fdc7f><b>{msg}</b></color>" : $"<color=#ff6b6b><b>{msg}</b></color>");
            Debug.Log(msg);
        }

        /// <summary>Plays 200 bot games with the current settings and prints objective achievement rates.</summary>
        public void RunSim()
        {
            var report = Mandate.Sim.Sim.Run(200, players, seed, botKind);
            foreach (var line in report.ToString().Split('\n'))
                if (line.Trim().Length > 0) Log.Add($"<size=11>{line.TrimEnd()}</size>");
            Debug.Log(report.ToString());
        }
    }
}
