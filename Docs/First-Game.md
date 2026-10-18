# Your first game of Mandate

Mandate is a negotiation game. Four to eight players sit around a small map, each with a secret goal. Every round
you talk, then everyone secretly picks one move, and all the moves happen at once. After seven, eight or nine rounds (a secret) you guess
what everyone else was really after. The other players can be AI models, and this guide is how to sit down and
play against them for the first time.

Time to first game: about five minutes, most of it getting an API key.

## 1. Get a key (three minutes)

The AI seats need an account with a model provider. The cheapest start is OpenRouter, which has free models:
go to openrouter.ai, sign in, create an API key, and copy it. You will paste it into the game in a moment.

Other providers work the same way: Google Gemini (Google has a free tier too), Anthropic for Claude, OpenAI for
ChatGPT. You can mix them at one table. The full list is in `Docs/Agents.md`.

If you would rather not paste keys into the game, set them as environment variables instead
(`OPENROUTER_API_KEY`, `GEMINI_API_KEY`, `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`) and restart Unity and Unity Hub.
The Table screen shows "found in the environment" next to a provider whose key it can already see.

## 2. Set up the table (two minutes)

1. In Unity, open **Mandate > Open Game Scene** from the menu bar and press **Play**.
2. The **Table** screen opens over the board. It has one row per player.
3. The first row is you: click its kind button and choose **you**. Seats are shuffled each game, so you will not
   always be P1; the log says which colour you are and your nodes are marked **you** on the board.
4. For rows 2, 3 and 4 click the kind button and choose a provider, for example **openrouter**. Type a **name**
   so the log reads "Nova says" instead of "P2 says". Leave **model** blank for the free default, or type an id
   from openrouter.ai/models such as `google/gemini-3.8-flash` or `openai/gpt-6-astra`.
5. A **Keys** section appears with one field per provider you picked. Paste your key. It shows **set**.
6. Decide whether you want **show AI thinking** on. Off means you play it straight and read their private notes
   at the end. On means their notes appear in the log as the game goes. You can flip this any time from the top
   bar.
7. Leave **remember this table on this PC** on so the next game is one click. Keys are saved in plain text in a
   file on this computer only; untick **remember the keys too** if you would rather retype them next time.
   In a browser, **remember the keys too** starts off: keys are forgotten when you close the tab, because other
   games on the same website (itch.io, for one) could read anything saved there.
8. Press **Start game**. If a seat cannot be created (no key, an unknown model), a red line says which and why;
   fix it and press Start again, or **Start anyway** to let that seat be a scripted bot this game.

You can also set all of this in the Inspector on the `Mandate` object before pressing Play; the Table screen
edits the same values.

## 3. The rules in one minute

- **The map.** A ring of nodes plus a hub (node 0) in the middle. Each node produces 1 to 3 points of
  production. You start with two nodes. Colour shows the owner; grey is nobody.
- **Your goal.** At the start you are offered three goal cards and keep one. Nobody sees which. Goals are things
  like "control the hub at the end", "have the most production", "the player on your left finishes second", or
  "never lose a node". The list of cards dealt to the whole table is public; who holds which is not.
- **A round** has two halves. **Negotiate**: send up to three public and three private messages. Nothing said is
  binding. **Act**: everyone secretly picks one move: pass, move into a neighbouring node, fortify or develop one of yours,
  sabotage an enemy node next to you (a bare one goes neutral), or release one of yours. All moves resolve together.
- **Fighting.** Moving into an empty node takes it. Moving into an owned node takes it only if the wall on the node
  you attack from is higher than the wall on the target. Ties go to the defender. Walls go up to 3.
- **Trades** are free and binding. In negotiation you offer a swap (one of your nodes for one of theirs); in the
  act phase the other player can tick **accept** on top of their move. You cannot take an offer back.
- **The end** is secret: the game stops after round 7, 8 or 9, and you only find out when it happens. Round 9 is
  always the last.
- **Scoring** after the last round: 10 points for achieving your goal, +2 for every opponent whose goal you guess
  correctly, and -2 for every opponent who guesses yours. Being obvious costs you.

The long version with an example is `Docs/How-Mandate-Works.md`.

## 4. Play

The screen has four parts: the top bar (round, phase, buttons), the player cards on the left, the map in the
middle, and the log on the right. Your controls appear at the bottom whenever it is your turn.

**Draft.** The bottom panel shows your three goal cards. Click one, then press **Step** in the top bar.
Read your card carefully: it is the only thing you are playing for.

**Negotiate.** Type a message and press **Send public** (everyone sees it) or pick a player with the
**to P2** button and press **Send private**. Queue up to three of each. Press **Step** when you are done. Your
messages go out at the same time as everyone else's, so you cannot reply within a round.

**Waiting.** The top bar shows *Thinking: P2, P3* while the models answer, and the player cards show who is still
thinking. This takes a few seconds on a small model and up to a minute on a big one. The editor stays responsive.
If a model takes longer than the timeout (four minutes by default) it simply passes.

**Read the log.** After each phase the log shows every message, then every move, then what changed on the map:
which nodes changed hands, which walls went up or down. A white ring marks nodes that changed owner this round.
Illegal moves show as "illegal ... -> pass". With **AI thinking** on, each model's private notes appear here too,
marked "thinking".

**Act.** The bottom panel lists every legal move as a button. Click one and press **Step**. Trade offers made to you
appear above the moves with an **accept** tick box; accepting is free. If you press Step without choosing, you pass.

**Guess.** After the last round the bottom panel shows one row per opponent with left and right arrows. Cycle each to the
goal you think they hold. Each card belongs to exactly one player, so a confident read on one opponent narrows the
rest. Press **Step**.

**The reveal.** The final scores appear under the map, and the log prints every AI seat's private notes: what it
was planning, who it trusted, and what it thought you were up to. This is the best part, and it happens whether or
not you watched the thinking live. A full transcript with every prompt, every reply, and the models' own thinking
(where the model shares it) is saved automatically; the scoreboard has **Open transcripts folder**, **Send
results...** (optional: send the game to the developer, with a comment if you like), **Play again**, and **Change the table**.

## 5. Tips for a first game

- Talk in round one. Silent tables are dull, and the models respond to what you say.
- Never say what your goal is. Say what you want people to think it is.
- The relational goals ("my left neighbour finishes second") are the ones nobody expects. If you hold one, you
  need to help someone without them noticing.
- Watch the action history, not just the chat. A player who fortifies the same node every round is telling you
  something.
- Trades are the only cooperative move. Propose exact nodes: "I give 4, you give 7, this round."
- At the guess phase, use elimination. Anyone who attacked cannot hold "never capture a node".

## 6. If something goes wrong

| What you see | What it means | What to do |
|---|---|---|
| Red line on the Table screen: "needs an API key" | no key for that provider | Paste one in the Keys section |
| "no usable answer: HTTP 401" in the log | wrong or expired key | Press **Table**, paste a new key, Start game |
| "no usable answer: HTTP 404" or "model not found" | the model id is wrong or retired | Put a current id in that seat's model field |
| "no usable answer: HTTP 429" | the free model is rate-limited | Wait, or pick another free model, or a paid one |
| "no usable answer: reply was not JSON" | the model rambled instead of answering | It passes that phase. Persistent: try a stronger model |
| "timed out after 120s" | a slow model | Raise **Phase Timeout Seconds** on the Mandate object in the Inspector |
| A seat shows "greedy" when you set a model | it fell back to a bot; the first log lines say why | Fix the seat and press **Start game** again |

Nothing a seat does can crash or stall the game. The worst case is that it passes.

## 7. Watching the models play each other

On the Table screen set every row, including the first, to a provider. Turn **show AI thinking** on if you want to
watch them reason. Press **Start game**, then **Run to end** in the top bar (or tick **auto** with the speed
slider). The notes, scores and transcript are there at the end exactly as when you play.

For many games at once, without Unity: open a terminal in `Tools/MandateConsole`, edit `seats.example.json`, and run
`dotnet run -- play seats.example.json 1 10` for ten games with totals per seat at the end. Transcripts land in
`Tools/MandateConsole/transcripts/`. The console reads keys from environment variables.

## 8. After your first game

- Try a table with different providers in each seat and see who reads whom.
- Give the seats personas (tick **advanced fields** on the Table screen). A table of four ruthless liars plays
  very differently from four diplomats.
- The rules are meant to change. To test a tweak to the goal cards, run the simulator
  (`dotnet run -- 1000 4 42 greedy` in `Tools/MandateConsole`) and compare how often each card is met.
