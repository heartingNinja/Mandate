# Mandate: The Rules

**Mandate is a negotiation game built for AI players, but people can play too.** Four to eight players share a small map. Each one has a secret goal. They talk, make deals and break them. Every round, all players pick one secret action and the actions happen at the same time. When the game ends, everyone tries to guess what everyone else was after.

You score points two ways: by reaching your own goal, and by working out other players' goals while hiding yours.

## At a glance

- **Players:** 4 to 8: AI models, scripted bots, people, or a mix.
- **Length:** 7, 8 or 9 rounds. The number is drawn in secret, so nobody knows which round is the last.
- **Each round:** talk, then every player picks one secret action, and they all happen at once.
- **Scoring:** 10 points for reaching your goal, +2 for each opponent you read correctly, −2 for each opponent who reads you.

## The map

- The map is a **ring of nodes**, with three nodes for each player, plus the **hub** (node 0) in the middle. A 4-player game has 13 nodes and an 8-player game has 25.
- Every node has a **production** value from 1 to 3, and everyone can see it. Production is how players are ranked at the end. The hub is always worth 3.
- You **start with two nodes next to each other**, worth 4 production between them (a 1 and a 3, or two 2s). Everyone starts equal.
- Between each pair of neighbouring players is one neutral **gap node**. Gap nodes are the only ring nodes linked to the hub, so every player is the same distance from the centre.
- A node can have **walls** (fortification) from 0 to 3. Walls defend the node, and they are also your attack strength when you push out from it.
- **Left** is the next seat round the ring, and **right** is the previous seat. Several goals depend on your neighbours.

## Setup

Each player is dealt **three goal cards and secretly keeps one**. The full list of dealt cards is public, so everyone knows which goals are in play but not who holds which. Each card is in the deck only once, so no two players can have the same goal.

## A round

### 1. Talk

Each player may send **up to 3 public messages** and **up to 3 private messages** (500 characters each). Nothing said is binding. Promises, threats and lies are all allowed.

The one binding thing is a **trade offer**: one of your nodes for one of theirs. The referee checks the offer as soon as you make it, and you cannot take it back.

### 2. Act

Each player secretly chooses **one action**:

- **Move:** push from a node you own into a neighbouring node. You keep the node you pushed from.
  - An empty node is yours.
  - To take an owned node, your attack strength (the walls on the node you push from) must be **strictly higher** than its walls. Ties go to the defender, so a node with no walls can never take an owned node.
  - A captured node loses all its walls.
  - If two players push into the same node with equal strength, they cancel out and neither takes it.
- **Fortify:** +1 wall on one of your nodes (max 3).
- **Develop:** +1 production on one of your nodes (max 3). The extra production stays with the node if it changes owner.
- **Sabotage:** knock one wall off an enemy node next to one of yours. If it has no walls, it goes **back to neutral**: the owner loses it, but you don't gain it. You can't sabotage the same node two rounds in a row.
- **Release:** give up one of your nodes. It becomes neutral with no walls and keeps its production.
- **Pass:** do nothing.

**Accepting a trade is free.** If someone offered you a trade, you can accept it as well as taking your normal action. Walls go with the node. A completed trade is public, but an offer that nobody accepts stays private between the two players.

### How actions resolve

All actions resolve at once, always in this order:

1. Trades
2. Moves
3. Releases
4. Fortify and Develop
5. Sabotage

This order has a few effects:

- A wall you build this round protects you next round, not against this round's moves.
- Sabotage can't soften a node for an attack in the same round.
- A fortify does land before sabotage, so walling a bare node saves it from being sabotaged to neutral that round.
- Releases happen after moves, so nobody can take a node the round it is released; from the next round it is an ordinary neutral node. If your node is captured the same round you release it, the release does nothing.
- A node with 3 walls **can't be captured by a move at all**. It has to be sabotaged down first.

An illegal action counts as a pass, and so does an AI that doesn't answer in time. Nothing is random after the deal, so the same choices always give the same result.

## The end

The game ends after round 7, 8 or 9, with an equal chance of each. Nobody is told in advance, so a "last-round betrayal" in round 7 or 8 can still be punished.

Then two things happen:

1. **Guessing.** Each player guesses every opponent's goal. You choose from the dealt cards, minus the three you were offered. In a 4-player game that is 9 cards for 3 opponents, and each card belongs to exactly one player.
2. **Scoring.**
   - **+10** if you achieved your goal
   - **+2** for each opponent whose goal you guessed right
   - **−2** for each opponent who guessed yours

So there are two contests going on at once: one on the map and one in the conversation. A player who reaches their goal but makes it obvious can lose to a quieter player who was never read.

## The 27 goal cards

Every card is checked once, after the last round, unless it says "during the game".

- **Production** is the total production of the nodes you own.
- **The ranking** orders players by production. **Ties are shared**: two players level on the most are both first and nobody is second.
- **"Ahead of"** means strictly more, so a tie doesn't count.

### Own or build something

- **Most production:** you are first in the ranking (sharing counts).
- **Most nodes:** you own strictly more nodes than anyone else.
- **Hold the hub:** you own node 0.
- **Four nodes:** you own at least 4 nodes.
- **Fortress:** your nodes have at least 7 walls in total. Walls cap at 3 per node, so you need at least three nodes.
- **Hold your left gap:** you own the gap node between you and your left neighbour, with 3 walls on it.
- **Hold your right gap:** the same on your right.
- **Hold both gaps:** you own both gap nodes beside you.
- **Two gaps:** you own any 2 gap nodes.
- **Production eight:** your production is 8 or more.
- **Chain of four:** you own 4 nodes that connect to each other.
- **Top two:** you are first or second with at least 6 production, and the leader is at most 1 ahead of you.

### Shape the ranking

- **Left finishes first:** your left neighbour is first (sharing counts).
- **Left finishes second:** your left neighbour is second: exactly one player is ahead of them.
- **Right finishes second:** the same for your right neighbour.
- **Left finishes last:** your left neighbour is last (sharing counts).
- **Right finishes last:** the same for your right neighbour.
- **Beat right:** you have at least 3 more production than your right neighbour.
- **Both neighbours below:** you are ahead of both your neighbours.

### Keep the board calm, but still grow

- **Balance:** nobody owns more than 5 nodes, and you own at least 4.
- **Never lose:** none of your nodes was captured or sabotaged to neutral during the game, and you end with at least 6 production. Trading or releasing a node yourself is fine.
- **No walls:** the whole board has 3 walls or fewer, and you own at least 4 nodes.
- **Pacifist:** you never captured a node from another player during the game (taking neutral nodes is fine), and you end with at least 7 production.
- **Quiet hub:** node 0 is neutral, and you own at least 3 nodes.
- **Spread:** every player owns at least 2 nodes, and you own at least 3.

### Do something a number of times

- **Trader:** you completed at least 2 trades during the game, and you end with at least 6 production.
- **Saboteur:** you made at least 3 legal sabotage actions during the game.

## Why it's fair

- Every player gets the same rules, the same view of the map and the same amount of talk. The only hidden things are the goal cards and the actions chosen this round.
- Starting positions are equal: the same production, and the same distance to the hub.
- The referee is open-source code, so anyone can check every rule and every goal.
- Every message, action and guess is logged, so any game can be replayed and audited from start to finish.

---

*The full rules, with a worked example and notes on how each goal tends to play, are in [How Mandate Works](How-Mandate-Works.md). The rules are still being tuned from simulations and live AI games, so details may change.*
