# Mandate — AI seats and the wire format

Any model with an HTTP API, any local model, and any bot in any language can sit at a Mandate table. This guide is
how to seat them: in Unity to play against them, in the console to have them play each other, and over HTTP to bring
your own. Nothing about a provider lives in the engine; a seat is anything that answers five questions about a
`PlayerView`, and the code that turns those questions into HTTP calls is `Assets/Mandate/Agents/`.

## Seat kinds

A seat is a `SeatConfig`: `kind`, and optionally `name`, `model`, `endpoint`, `apiKeyEnv`, `apiKey`, `persona`,
`effort`, `temperature`, `maxTokens`, `jsonMode`. Everything except `kind` has a default per kind.

| kind | family | default endpoint | key variable | default model | notes |
|---|---|---|---|---|---|
| `greedy`, `random` | scripted | | | | the sim bots |
| `human` | person | | | | Unity only, first row on the Table screen (tick **I play**) |
| `openrouter` | OpenAI chat | `https://openrouter.ai/api/v1` | `OPENROUTER_API_KEY` | `nvidia/nemotron-3.5-lightning:free` | one key, hundreds of models. Ids ending `:free` cost nothing (rate-limited). `effort` defaults to `low` (sent as OpenRouter's `reasoning.effort`) so cheap thinking models answer inside the timeout; set `medium` or `high` per seat for deeper play. Any vendor's model works here: `openai/gpt-6-astra`, `google/gemini-3.8-flash`, `anthropic/claude-opus-5`, `deepseek/deepseek-v4.1-flash`, `x-ai/grok-4.6` |
| `openai`, `chatgpt` | OpenAI chat | `https://api.openai.com/v1` | `OPENAI_API_KEY` | `gpt-5.6-luna` | `gpt-6-astra` is the flagship |
| `gemini`, `google` | Gemini | `https://generativelanguage.googleapis.com` | `GEMINI_API_KEY` or `GOOGLE_API_KEY` | `gemini-3.8-flash` | Google's own free tier applies |
| `anthropic`, `claude` | Anthropic Messages | `https://api.anthropic.com` | `ANTHROPIC_API_KEY` | `claude-opus-5` | `effort` defaults to `medium` |
| `groq` | OpenAI chat | `https://api.groq.com/openai/v1` | `GROQ_API_KEY` | `llama-3.3-70b-versatile` | |
| `together` | OpenAI chat | `https://api.together.xyz/v1` | `TOGETHER_API_KEY` | `meta-llama/Llama-3.3-70B-Instruct-Turbo` | |
| `deepseek` | OpenAI chat | `https://api.deepseek.com/v1` | `DEEPSEEK_API_KEY` | `deepseek-chat` | |
| `xai`, `grok` | OpenAI chat | `https://api.x.ai/v1` | `XAI_API_KEY` | `grok-4.6` | |
| `mistral` | OpenAI chat | `https://api.mistral.ai/v1` | `MISTRAL_API_KEY` | `mistral-small-latest` | |
| `ollama` | OpenAI chat | `http://localhost:11434/v1` | none | `llama3.1` | set `model` to one you have pulled |
| `lmstudio` | OpenAI chat | `http://localhost:1234/v1` | none | `local-model` | |
| `vllm` | OpenAI chat | `http://localhost:8000/v1` | none | (required) | |
| `compatible` | OpenAI chat | (required) | | (required) | anything else that speaks `/chat/completions` |
| `http`, `bot` | your code | (required) | optional bearer | | see *Bring your own bot* |

The default model ids were checked against the providers' lists on 2026-09-13 and will go stale. Override `model`
per seat. A wrong id comes back as an HTTP error in the game log, and that seat passes for the phase.

Timeouts: a seat gets 240 seconds per phase in Unity (inspector field) and 180 in the console. A late seat passes,
exactly like a scripted bot returning nothing. A failed call (server error, dropped connection, empty reply) is tried
once more while the phase still has time. Any other error (bad key, unreadable reply, refusal) is logged next to the
seat and treated the same way, so a broken seat can never stall or crash a game. Beliefs are optional: a belief the
game cannot read is dropped and the rest of the answer still counts.

When requests go through .NET's `HttpClient`, Unity's runtime only allows 2 at once to the same website unless told
otherwise, so `HttpClientTransport` raises that to 32. The Unity view now sends with `UnityWebRequest` instead (see
*Playing in a browser*), which has no such cap.

## API keys

Three ways, in order of convenience:

1. **Type them in the game.** Press Play; the Table screen has a Keys section with one field per provider in use.
   With "remember this table on this PC" ticked the table is saved to `persistentDataPath/Mandate/table.json`
   (`%USERPROFILE%\AppData\LocalLow\<company>\<product>\Mandate\table.json`), and with "remember the keys too" the
   keys go in that file as plain text. It is local to this PC and never enters the scene or the project folder.
2. **Environment variables.** `OPENROUTER_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY` (or `GOOGLE_API_KEY`),
   `ANTHROPIC_API_KEY`, and so on. Unity reads the environment at launch, so restart Unity and Unity Hub after
   setting one. The Table screen shows "found in the environment" next to a provider it can already reach. The
   console harness uses this route.
3. **`apiKey` on a seat** in the Inspector or in a `seats.json` file. Saved into the scene or the file, so keep it
   out of anything you share.

A key typed on the Table screen wins over the environment; a seat's own `apiKey` wins over both.

## Playing in a browser (WebGL)

A WebGL build runs the whole game in the player's browser, AI seats included. Each player brings their own key:

- **Where the key lives.** Typed on the Table screen, as on desktop. With "remember the keys too" it is saved in
  that browser's own storage for that page (IndexedDB, under `persistentDataPath`), as plain text. It never
  reaches us or anyone else. Calls go from the browser straight to the provider. A browser has no environment
  variables, so route 2 above does not exist there.
- **Never put your own key in a web build** (seat `apiKey`, the scene, a file in the build). Anyone can read the
  build files and spend your credit.
- **Which providers work.** A browser only lets a page call a server that allows it (CORS). Checked 2026-10-18:
  OpenRouter, OpenAI, Anthropic, Gemini, Together, DeepSeek, xAI and Mistral allow it. Anthropic also needs the
  `anthropic-dangerous-direct-browser-access` header, which the Claude seat always sends. Groq could not be checked.
  A blocked call shows in the log as "could not reach ...".
- **Local models and your own bots** (Ollama, LM Studio, vLLM, `http` seats) work from a browser only when they run
  on the player's own computer (`localhost`): an https page may call plain `http://localhost`, but not plain http
  on another machine. The server must also allow the page's address: for Ollama set `OLLAMA_ORIGINS=*` and restart
  it; in LM Studio turn on "Enable CORS" in the server settings. `Tools/ExampleBots/random_bot.py` already sends the
  headers. Newer browsers may ask the player to allow local network access. In a browser the Table screen shows
  these hints under the seat. Not yet tried with a real local model in a browser; the desktop build is the easy
  route for local models.
- **Remember and Delete.** With "remember the keys too" ticked, keys come back on the next visit (same browser,
  same address). **Delete** next to a key removes it from the browser at once. Unticking "remember this table" and
  starting a game deletes the saved table, keys included. A private window forgets everything when closed, and
  some browsers (Safari) clear a site's storage after a few weeks without a visit.
- **Transcripts** are saved in the browser too, and the scoreboard has **Download transcript** in place of the
  folder button. **Send results...** works the same.
- Re-uploading the build to a new address (itch.io gives every upload its own) starts with an empty table, because
  browser storage belongs to the address.

How it works: every request goes through `Http.Transport`. The Unity view installs `WebRequests`
(`Assets/Mandate/Runtime/WebRequests.cs`), which sends with `UnityWebRequest` on the main thread. It does this in
the editor and desktop builds too, so the browser's path is the one you play every time. The console and the
tests use .NET's `HttpClient`.

## Playing against them in Unity

1. Open `Assets/Scenes/Mandate.unity` (or **Mandate > Open Game Scene**) and press Play. The **Table** screen opens.
2. One row per seat. Click a seat's kind button to pick **you** (first row only), a scripted bot, or a provider. With **shuffle seats** on (the default) the rows are who plays, not where: each game deals the seats out from the seed, and the log says which colour you are.
   Give AI seats a **name** (up to 18 characters) so the log reads "Claude says" rather than "P3 says"; leave **model**
   blank for the provider's default or type any id. Picking a model from the ▾ list names the seat after it in
   short form (`google/gemma-4-31b-it` becomes `gemma-4-31b-it`) unless you typed a name of your own. Tick **advanced fields** for endpoint, effort, JSON mode and a **persona**
   ("You are a paranoid warlord who speaks in short sentences").
3. Paste keys in the **Keys** section, choose whether to **show AI thinking** live, and press **Start game**.
   A seat that cannot be built (no key, unknown kind) is listed in red before anything starts; **Start anyway**
   lets it fall back to a scripted bot.
4. Play with **Step** as usual. The top bar shows *Thinking: P2, P3* while models answer and the seat cards show
   *thinking…*; the editor stays responsive. Each phase takes as long as the slowest seat, from a few seconds for
   a small model to a minute for a big one at high effort.
5. The log shows every message and action exactly as before, and why any seat failed to answer. The **AI
   thinking** toggle in the top bar shows or hides the models' private notes at any moment. Notes are always
   revealed at the end, which is where the bluffs show, and a transcript with every prompt and reply is saved to
   `%USERPROFILE%\AppData\LocalLow\<company>\<product>\Mandate\transcripts\`. The scoreboard has a button that
   opens the folder. Where the provider returns the model's own thinking, it is written above each reply under
   `>>> thinking:`: OpenRouter and DeepSeek send their models' reasoning, Claude and Gemini are asked for a thinking
   summary, and OpenAI's own API sends none. The thinking is never shown to the other seats.
6. **Send results...** on the scoreboard lets the player send the finished game (scores, seats, models and the full
   transcript, never the keys) to the developer. It is opt-in, game by game. Setup: `Tools/MandateResultsRelay/README.md`.

Everything the Table screen sets can also be set on the `Mandate` object in the Inspector before pressing Play.

Rough cost: a phase sends the whole game state, two to four thousand tokens, and a four-player game is eighteen
phases per seat. On a free OpenRouter model it costs nothing. On a paid flagship budget well under a dollar per
seat per game; the Anthropic seat marks its rules prompt cacheable so repeated phases are cheaper.

## Having them play each other (console)

```
cd Tools/MandateConsole
dotnet run -- play seats.example.json 1          # one game, seed 1, narrated to stdout
dotnet run -- play seats.json 1 10               # ten games, seeds 1-10, mean score and wins per seat name at the end
dotnet run -- play seats.json 1 10 fixed         # the same, but entry 1 always sits in P1, entry 2 in P2, ...
dotnet run -- test                               # engine + agent tests, no key needed
dotnet run -- prompt P3 1                        # print exactly what an AI in seat P3 is told (seed 1, round 1)
```

`seats.json` is a JSON list of seat objects. Seating is shuffled from each game's seed, so over several games every entry gets different neighbours; add `fixed` to keep entry 1 in P1 and so on; `seats.example.json` shows the shape. Four to eight
seats. Every game writes `transcripts/game-<seed>-<time>.txt` (the narration plus each seat's private notes) and
`.exchanges.json` (every prompt and reply per seat). Blocking calls are fine here, so the console uses the
runner's `StepPhase()` directly.

## Bring your own bot (any language)

Run an HTTP server. Each phase the referee POSTs the view as JSON and expects a JSON reply. Seat it with kind `http`
and your URL as the endpoint. `Tools/ExampleBots/random_bot.py` is a complete bot in 25 lines:

```
python Tools/ExampleBots/random_bot.py 8765
```

then `{ "kind": "http", "endpoint": "http://localhost:8765/", "name": "Python bot" }`.

### The view you receive

All names are camelCase. `version` is 2 and will change only if a field changes meaning. (Version 2: the last round is secret, so `totalRounds` is gone and `rules` has `minRounds`/`maxRounds`; trades are offers plus `accept` instead of a `trade` action.)

| field | meaning |
|---|---|
| `gameId`, `round`, `phase` | `phase` is `draft`, `negotiate`, `act` or `guess`. The game ends after round `rules.minRounds`..`rules.maxRounds` (7-9); which one is secret, and you see `phase: guess` when it has happened |
| `you`, `players`, `left`, `right` | your seat id, seat order, and your two neighbours (the relational objectives refer to them) |
| `objectiveId`, `objectiveText` | your secret objective (absent during the draft) |
| `board[]` | `{id, owner, production, fortify, adjacent[]}`; `owner` absent = neutral; node 0 is the hub |
| `publicLog[]`, `privateInbox[]` | `{round, from, to, text}`; `to` absent = public. What you sent privately is not echoed back |
| `actionsHistory[]` | `{round, player, action, note}` for every resolved action, all rounds. `note` is the referee's record of what the action actually did: `"took neutral node 9"`, `"FAILED: ... (a tie goes to the defender)"`, `"node 9 fort 0 -> 1, then sabotage took it back to fort 0"`, `"illegal: ..., counted as pass"`, `"no usable answer, counted as pass"`; absent for a pass |
| `tradeOffers[]` | this round's trade offers: the ones you made (`problem` set if rejected) and valid ones made to you. `{round, from, partner, give, get, problem}`: `from` gives node `give`, `partner` gives node `get`. Offers are binding; accept one with `"accept"` in the act phase |
| `legalActions[]` | act phase only. Copy one back verbatim |
| `dealtObjectives[]`, `guessCandidates[]`, `draftOptions[]` | `{id, text}`. Candidates are the dealt cards minus the ones you were offered |
| `tradesCompleted`, `lostNodeEver[]`, `capturedOwnedEver[]` | the public record that objectives are checked against |
| `rules` | `minRounds`, `maxRounds`, `fortifyCap`, `productionCap`, message caps, and the three scoring constants |

Actions look like `{"type":"pass"}`, `{"type":"move","from":2,"to":3}`, `{"type":"fortify","node":2}`,
`{"type":"develop","node":2}`, `{"type":"sabotage","node":4}`, `{"type":"release","node":2}`. Trading is not an
action: see `offers` and `accept` below.

### The reply you send

Only the field for the current phase is read.

| phase | field |
|---|---|
| draft | `"draft": "<objective id from draftOptions>"` |
| negotiate | `"messages": [{"to": "P2", "text": "..."}, {"text": "..."}]` (no `to` = public), optionally `"offers": [{"partner": "P2", "give": 12, "get": 5}]` (trade offers, one per partner, checked at once against the board and BINDING: if the partner accepts, the swap happens), and optionally `"beliefs": {"objectiveBeliefs": {"P2": {"trader": 0.6}}, "theyThinkIAm": {"P2": "fortress"}, "note": "..."}` |
| act | `"action": {...}` one of `legalActions`, and optionally `"accept": ["P2"]`: the players whose offers to you this round you accept. Accepting is free (you still take your action); accepted trades resolve before everything else |
| guess | `"guesses": {"P2": "<id>", "P3": "<id>"}` |
| any | `"notes": "..."` free text, logged with the game; language-model seats get it back next phase |

Missing or malformed fields count as: default draft pick, no messages, pass, no guesses. The parser is lenient
about case and a few synonyms (`"attack"` for move, `"to"` for `node`), seat names like `"p2"` or `"Player 2"`,
and a message given as a bare string is taken as public text. For language-model seats, a reply that fails to
parse is repaired once and retried: a stray quote after a number (`"to":6"`) is dropped, and a reply cut off
part-way is closed. If a thinking model sends no text at all but left its answer in its reasoning (`reasoning` on OpenRouter, `reasoning_content` on DeepSeek's API and vLLM), the last reply object in the reasoning is used and the transcript marks it "[answer taken from the model's reasoning]". A failed or empty call is retried once with a line telling the model to reply with only the JSON; if that fails too, the error gives `finish_reason` and the start of the reasoning.

## What a language model is told

Every model seat gets the same text, in `Assets/Mandate/Agents/Mandate.Agents.Prompting.cs`:

- A **system prompt** that is identical for the whole game: the rules, how resolution works, scoring, the words
  the objective cards use (that ties in the production ranking are shared, not broken, who left and right are, that cards are checked
  once at the end, that a move keeps its source node, when nodes return to neutral (release, or sabotage on a node with no walls), that three walls cannot
  be captured by a move, that a trade is never a loss), a short
  section on how to play well (make deals, read the table, never state your objective, write down promises), the
  exact JSON to reply with per phase, and the seat's persona if one is set. Being constant, it is cacheable.
- A **user prompt** per phase: the board as a table, standings, the public record, every objective in play, the
  public chat, private messages received, the full action history by round, the seat's own notes from earlier
  phases, the legal actions as JSON (act phase), and a one-line task for the phase.

Nothing in the prompt goes beyond what `PlayerView` exposes, so a model seat and a human seat see the same game.

## Adding a provider

- Speaks the OpenAI chat format: add one line to `Seats.Kinds` in `Mandate.Agents.Seats.cs`.
- A new API family: implement `IChatClient` (system + user in, text out) in `Mandate.Agents.Clients.cs` and add a
  case to `Seats.Make`. The Anthropic and Gemini clients are about forty lines each and are the pattern.
- A new transport entirely (a socket, a queue): implement `IAgent` directly, as `HttpBotAgent` does.

## Limits to know about

- **Web builds:** see *Playing in a browser* above. Groq could not be checked (it blocked the test connection), and
  local models need to allow the page's address.
- OpenAI: `maxTokens` is only sent when set (some current models want `max_completion_tokens` instead); leave it 0.
- Anthropic: `temperature` is never sent (current models reject it); thinking is left at the model default; the
  `effort` field maps to `output_config.effort`.
- Gemini: the reply is requested as `application/json`, which the current models honour.
- Tests: `Mandate.Agents.Tests.cs` runs every adapter and a full game against a local fake server, so
  `dotnet run -- test` and the Unity Test Runner cover the wire format without any key.
