# How Mandate Works

Mandate is a table game for AI players. Four to eight players sit around a small map, each with a secret goal. They talk, make promises, break them, and every round each player secretly picks one move. The moves all happen at once. After seven, eight or nine rounds (nobody knows which until it happens) everyone tries to guess what everyone else was really after.

It is built so that any AI model can sit at the table on equal terms, and so that a person can watch the whole thing and understand exactly what happened and why.

## The map

The map is a ring of circles called **nodes**. Every node produces something: a number from 1 to 3 that we call **production**. Production is the main way players are compared at the end. Each node's production is rolled when the game starts and everyone can see it. The rolls are evened out for starting nodes: every player's two starting nodes add up to 4 (a 1 and a 3, or two 2s), so nobody is dealt a weaker start. Neutral nodes are fully random. A player can **develop** a node they own to raise its production by one, up to 3.

Lines connect the nodes. Each ring node is joined to its two ring neighbours. Node 0 is the **hub**: it sits in the centre, off the ring, always produces 3, and is joined to every third node (3, 6, 9 and so on), so it is a shortcut across the map and touches more nodes than anything else.

There are three ring nodes for every player at the table, plus the hub. Each player starts owning two of them, side by side. Between each pair of neighbouring players sits one neutral node, called a **gap node**, including between the last seat and the first (node 12 in a four-player game, at the top of the ring). The gap nodes are every third node (3, 6, 9 and so on) and are exactly the ones joined to the hub, so every seat is the same distance from it: two steps, through a neutral node.

A node can also be **fortified**, up to three levels. Fortification is a wall. It makes a node harder to take, and it is also the strength you attack with from that node.

## Table size

The board is built from the player count and never changes size during a game. No new nodes appear, and total production only goes up, through Develop. Otherwise what changes is who owns each node and how high its walls are. Neutral nodes are free to grab; an owned node changes hands by capture or by trade, and goes back to neutral when its owner releases it or it is sabotaged with no walls left.

| Players | Nodes | Neutral at start | Nodes the hub touches | Cards dealt | Cards you guess from |
|---|---|---|---|---|---|
| 4 | 13 | 5 | 4 | 12 | 9 |
| 5 | 16 | 6 | 5 | 15 | 12 |
| 6 | 19 | 7 | 6 | 18 | 15 |
| 7 | 22 | 8 | 7 | 21 | 18 |
| 8 | 25 | 9 | 8 | 24 | 21 |

Seat order matters, because several cards talk about your neighbours. Seat 1 owns nodes 1 and 2, seat 2 owns nodes 4 and 5, and so on round the ring. **The player to your left** is the next seat, whose nodes are just past the neutral node beside yours. **The player to your right** is the previous seat. In a four-player game seat 1's left is seat 2 and its right is seat 4.

Every seat has a gap node on its left and one on its right; your left gap is your left neighbour's right gap. Four cards are about gap nodes, and they are worded relative to you rather than naming a node number, so they work the same for every seat at any table size.

## Secret goals

Before the game starts, every player is dealt three goal cards and keeps one. Nobody else sees which one. The full list of cards dealt this game is public, so everyone knows what goals are at the table; they just don't know who holds which. Each card exists once, so no two players can hold the same goal.

There are 27 different goals in the deck, and they come in a few flavours:

- **Own or build something.** "Control the hub at the end." "Have the most production."
- **Shape the ranking.** "The player on your left finishes second." "Finish ahead of both your neighbours."
- **Keep the board calm, while still growing.** "Nobody controls more than five nodes, and you have at least four." "Never lose a node, and reach six production."
- **Do something a number of times.** "Complete two trades." "Sabotage three times."

The goals are deliberately mixed. Some want a big empire, some want a quiet map, some want a particular rival to do well. That is where the negotiation comes from: two players may need opposite things, or the same thing, and neither knows which.

## The 27 cards

Some words that every card uses:

- **Production** means the sum of the production numbers on the nodes you own when the last round ends.
- **The ranking** is every player sorted by production, highest first. Ties are not broken: players on equal production share a place. If two players tie for the most, both are first and nobody is second (the next player is third). If several tie for the least, all of them are last. "Above" or "ahead of" someone means strictly more production, so a tie is not ahead.
- **Left** and **right** are your seat neighbours, as above.
- Every card is checked once, after the last round. What happened earlier only matters when the card says "during the game".

**Own or build something**

| Card | You win if | How it plays |
|---|---|---|
| Most production | You are first in the ranking. Sharing first counts. | Grab neutral nodes early while they are free, take the hub if you can, then wall up. Everyone starts with 4 production, so the race is decided by what you take. |
| Most nodes | You own strictly more nodes than every other player. | A tie fails. Everyone starts on two, so it is a race for the neutrals and then captures. |
| Hold the hub | You own node 0 at the end. | Worth 3 production and touching more nodes than anything else, so everyone can reach it. Hard to keep without walls. |
| Four nodes | You own at least four nodes at the end. | Two more than you start with, and kept to the end: a bare node can be sabotaged back to neutral, so wall the ones you take. |
| Fortress | The walls on nodes you still own add up to seven or more at the end. | Each Fortify adds one level to one node, three per node at most, so your two starting nodes alone top out at six: you must also take and hold a third node, and spend at least seven Fortify actions, in a game of seven to nine rounds. Sabotage knocks levels off, and losing a node zeroes its wall. |
| Hold your left gap | You own the gap node between you and the player to your left at the end, with three walls on it. | You can reach it in round one, but so can your left neighbour, and it leads to the hub. Three walls takes three Fortify actions and cannot be captured by a move, only sabotaged down first. |
| Hold your right gap | You own the gap node between you and the player to your right at the end, with three walls on it. | The same on the other side. |
| Hold both gaps | You own both gap nodes beside you at the end. | Two grabs and two walls, against both neighbours at once. Much harder than one. |
| Two gaps | You own at least two gap nodes at the end, any two. | The two beside you are the obvious pair, but a gap further round the ring through the hub counts too, which gives you a way round a neighbour who walls up. |
| Production eight | Your production is eight or more. | No ranking involved, so others can be higher. The hub plus a couple of good nodes gets there. |
| Chain of four | You own four nodes that connect to each other. | Your two starting nodes already touch, so you need two more in a line, or the hub as a bridge across the map. |
| Top two | You are first or second in the ranking (sharing counts) with at least six production, and the leader is at most one production ahead of you. | If you are the leader it counts. Otherwise stay close and, if needed, hold the leader down rather than chasing them. |

**Shape the ranking**

| Card | You win if | How it plays |
|---|---|---|
| Left finishes first | The player to your left is first in the ranking, alone or shared. | Feed them and defend them without saying why. |
| Left finishes second | The player to your left is second: exactly one player has more production than them. | Someone must be above them, alone. A tie for first leaves nobody second. You may have to help them and then hold them back. |
| Left finishes last | The player to your left is last in the ranking, alone or shared. | Take their nodes, or get others to. |
| Right finishes second | The player to your right is exactly second. | As above, other side. |
| Right finishes last | The player to your right is last in the ranking, alone or shared. | As above, other side. |
| Beat right | You have at least three more production than the player to your right. | Only one rival matters; everyone else is a possible ally. |
| Both neighbours below | You have more production than both the player to your left and the player to your right. A tie with either does not count. | Your two closest rivals on the map are the two you must beat. |

**Keep the board calm**

| Card | You win if | How it plays |
|---|---|---|
| Balance | Nobody, including you, owns more than five nodes at the end, and you own at least four. | Grow to four or five and stop. An expander can be cut back down, so this is recoverable late. |
| Never lose | No node was ever captured from you or sabotaged to neutral during the game, and your production is six or more at the end. | Trading or releasing one yourself is fine. Develop helps you reach six without fighting. Grab neutrals and wall them; turtling on your start (4 production) is not enough. |
| No walls | All walls on the whole board add up to three or less at the end, and you own at least four nodes. | Sabotage is your tool, and do not build, but you still have to grow. Fortress and Never Lose holders are your enemies. |
| Pacifist | You never captured a node from another player, and your production is seven or more at the end. | Taking neutral nodes is fine, and so are trades. You have to grow without attacking, so the neutrals and a good trade matter. |
| Quiet hub | Node 0 is still neutral at the end, and you own at least three nodes. | Once someone takes the hub you have to get it back to neutral: sabotage it when it has no walls, or talk its owner into releasing it. A release on the last round sticks, because nobody can move into a node the round it is released. Talk early. |
| Spread | Every player owns at least two nodes at the end, and you own at least three. | Grow, but do not crush anyone. If someone is captured down to one node you may have to trade them one back. |

**Do something a number of times**

| Card | You win if | How it plays |
|---|---|---|
| Trader | You completed at least two trades during the game, and you control at least 6 production at the end. | You offer, a partner accepts, and both sides get credit. Accepting is free, so anyone will take a gift; the production floor means your trades have to be fair ones, not giveaways. |
| Saboteur | You made at least three legal sabotage actions during the game. | You can sabotage any enemy node next to yours (walled or not), but not the same node two rounds running. |

## A round

The game lasts seven, eight or nine rounds. Which one is drawn in secret before the game starts (each is equally likely) and nobody is told: you only find out the game is over when a round ends and no new one begins. Round nine is always the last. This is on purpose: if everyone knew the last round, the best move would always be a strike nobody can answer. With a secret end, a "last-round" betrayal in round seven or eight may well be answered.

Each round has two halves.

**1. Talk.** Every player can send up to three public messages that everyone reads, and up to three private messages to individual players. Messages are free text: "I'll leave node 3 alone if you stay off the hub." Nothing said here is binding. The referee just records it. The one thing that is binding is a **trade offer** (see below).

**2. Act.** Every player secretly chooses one action. The referee collects them all, then plays them out together. Nobody sees anyone else's choice until it has already happened.

The actions are:

| Action | What it does |
|---|---|
| **Pass** | Nothing. |
| **Move** | Push out from a node you own into a node next to it. You keep the node you push from; a move never empties it. If the target is empty, you take it. If someone owns it, you take it only if your attack is stronger than its wall. Your attack strength is the fortification of the node you push from. Ties go to the defender, so an unwalled node can never take an owned node. Taking a node knocks its wall down to zero. |
| **Fortify** | Raise the wall on one of your nodes by one, up to three. |
| **Develop** | Raise the production of one of your nodes by one, up to three. The production stays with the node, so whoever owns it later gets it too. |
| **Sabotage** | Knock one level off the wall of an enemy node that touches one of yours. If it has no wall left, it goes back to neutral instead: its owner loses it (that counts against Never Lose) but you do not gain it (it is not a capture for Pacifist). You cannot hit the same node two rounds running. |
| **Release** | Give up one of your nodes. It goes back to neutral with no walls and keeps its production. Not a loss for Never Lose. |


**Trades** are not an action, so they cost nothing. In the talk half you may offer any player a swap: one of your nodes for one of theirs. The referee checks the offer straight away (you must own the one, they the other) and the offer is binding: you cannot take it back. In the act half the other player sees it and may accept it on top of their normal action. Accepted trades happen first, before anything else in the round. Walls stay with the node. A trade never counts as losing or capturing a node, and a completed trade is public; an offer nobody accepts stays between the two of you.

Because you keep the node you push from, your node count only goes down when someone captures from you, sabotages a bare node of yours to neutral, or you trade or release one.

A node with three walls cannot be captured by a move at all: attack strength tops out at three and ties go to the defender. It has to be sabotaged down first. Since one player can only hit it every other round, two players working together bring it down twice as fast.

When the referee plays the round out, it always goes in the same order: trades first, then moves, then releases, then fortify and develop, then sabotage. So a wall you build this round protects you next round, not this one, and sabotage cannot soften a node for an attack in the same round. Fortify does land before sabotage, though, so walling a bare node saves it from being sabotaged to neutral that round. Releases come after moves, so nobody can take a node the round it is released (it is an ordinary neutral from the next round on), and you can't release a node to dodge an attack: if it is captured that round, the release does nothing.

If two players push into the same node with equal strength, they cancel out and nobody takes it. If a player submits something that is not allowed, it simply counts as a pass, and the log says so.

## The end: guess and score

After the last round, each player guesses every other player's secret goal. You pick from the cards that were dealt this game, minus the three you were offered yourself. In a four-player game that is nine cards for three opponents, and each card belongs to exactly one player, so a good read on one opponent narrows the others. A blind guess is right about one time in nine; a player who has been watching the board and the chat does much better than that.

Then the referee scores:

- **10 points** if you achieved your own goal.
- **+2 points** for every opponent whose goal you guessed correctly.
- **−2 points** for every opponent who guessed yours.

So there are two games happening at once. One is on the map. The other is in the conversation: reach your goal without giving it away, and read the table better than the table reads you. A player who wins the map but was obvious about it can lose to a quieter player who was never read.

Players can also file an optional note each round saying what they currently believe about everyone else. It is never scored. It exists so that afterwards you can see who was reasoning well and who was guessing.

## Why it is fair

- Every player gets the same rules, the same map view, and the same amount of talk. Nothing is hidden except the goal cards and the current round's actions.
- Nothing is random after the deal. The same choices always produce the same result, so a game can be replayed and checked.
- The referee's code is public. Anyone can read every goal and every rule.
- AI players all receive the same instructions and the same time limit. A model that fails to answer just passes, like anyone else.
- Everything is logged: every message, every action, every guess. A human can audit any game from start to finish.

## A short example

Four players: Red, Blue, Green, Orange. Red drew "Control the hub at the end." Blue drew "Never have a node captured from you."

Round 1, talk. Red says publicly: "Let's keep the hub neutral, it just causes fights." Privately to Green: "If you take node 3, I won't contest it." Red is lying about the hub and buying goodwill with the node.

Round 1, act. Red pushes into node 3 anyway, which is empty, and takes it. Blue fortifies. Green pushes into node 6. Orange passes.

Round 4. Red now owns node 3, which touches the hub, and has fortified it twice. Red pushes from node 3 into the hub with strength 2. The hub is empty, so Red takes it. Blue, watching, now suspects Red's goal.

The game ends after round 8 this time. Guesses: Blue guesses Red wants the hub: correct, Red loses 2. Red guesses Blue wants "never lose a node": correct, Blue loses 2 and Red gains 2. Red kept the hub: 10 points. Blue was never attacked: 10 points. Red finishes on 10 + 2 − 2 = 10. Blue finishes on 10 + 2 − 2 = 10. Green and Orange each missed their goals and guessed nothing right, so Red and Blue tie for the win. Red got there by lying; Blue got there by noticing.

## Watching a game in Unity

Open the Mandate scene and press Play. Bots fill the seats by default; real AI models, local models, or a bot you wrote in any language can take any seat (see `Docs/Agents.md`), and the game waits for them without freezing. Press **Step** to advance one phase at a time, or **auto** to let it run. The left panel shows every player's secret goal and whether they are currently on track. The middle shows the map: colour is the owner, the number is the node, the small `+n` is production, white pips are wall levels, and a white ring means the node changed hands this round. The right panel is the full log. Pick **you** for a seat on the Table screen to play yourself; with **shuffle seats** on, which seat you get changes from game to game.
