namespace FiveCardDraw;

class PokerGame
{
    private List<Player> players;
    private Deck deck;
    public int pot = 0;
    public bool CheatMode = false;

    // ── Events the UI subscribes to ─────────────────────────────────────────
    public event Action<string>? OnLog;
    public event Action? OnStateChanged;
    public event Action<int, int>? OnHumanBetRequested;   // (toCall, currentBet)
    public event Action? OnHumanDrawRequested;
    public event Action<List<(Player player, HandEvaluation eval)>>? OnShowdown;
    public event Action? OnRoundOver;
    public event Action<string>? OnGameOver;

    // ── TaskCompletionSources for suspending on human input ──────────────────
    private TaskCompletionSource<int>? _pendingBet;
    private TaskCompletionSource<int[]>? _pendingDraw;
    private TaskCompletionSource<bool>? _pendingContinue;

    public PokerGame(List<Player> playerList)
    {
        players = playerList;
        deck = new Deck();
        foreach (var p in players)
        {
            p.Game = this;
            p.Log = msg => OnLog?.Invoke(msg);
        }
    }

    // ── Called by UI to supply human decisions ───────────────────────────────
    public void SubmitBet(int amount) =>
        _pendingBet?.TrySetResult(amount);

    public void SubmitFold() =>
        _pendingBet?.TrySetResult(-1);

    public void SubmitDraw(int[] indices) =>
        _pendingDraw?.TrySetResult(indices);

    public void ContinueToNextRound() =>
        _pendingContinue?.TrySetResult(true);

    public List<Player> GetPlayers() => players;

    // ── Main game loop ───────────────────────────────────────────────────────
    public async Task RunAsync()
    {
        var human = players.FirstOrDefault(p => !p.IsComputer);
        string greeting = human != null
            ? $"Welcome to Five Card Draw Poker, {human.Name}! Let's deal the cards."
            : "Welcome to Five Card Draw Poker! Let's deal the cards.";
        EmitLog(greeting);
        SpeechHelper.Say(greeting);
        await SpeechHelper.WaitForSpeechAsync();

        while (true)
        {
            // Remove players who busted
            int originalCount = players.Count;
            foreach (var p in players.ToList())
            {
                if (p.Chips <= 0)
                {
                    if (originalCount > 2)
                        EmitLog($"{p.Name} is out of chips and is eliminated!");
                    p.Hand.Clear();
                    players.Remove(p);
                }
            }

            if (players.Count <= 1)
            {
                string msg = players.Count == 1
                    ? $"Game over! {players[0].Name} wins with {players[0].Chips} chips!"
                    : "Game over!";
                EmitLog(msg);
                SpeechHelper.PlayWinnerTune();
                OnGameOver?.Invoke(msg);
                OnStateChanged?.Invoke();
                return;
            }

            // Reset for new round
            foreach (var p in players) p.Folded = false;
            deck = new Deck();
            pot = 0;

            EmitLog("─── New Round ───");
            LogChipCounts();
            SpeechHelper.Say("New round.");

            DealHands();
            OnStateChanged?.Invoke();
            ExplainHumanHand("You were dealt");

            await BettingRoundAsync("First Betting Round");

            if (players.Count(p => !p.Folded) > 1)
            {
                await DrawPhaseAsync();
                ExplainHumanHand("After the draw, you hold");
                await BettingRoundAsync("Final Betting Round");
            }

            await ShowdownAsync();
            OnStateChanged?.Invoke();

            // If only one player has chips left after this hand, game over.
            var alive = players.Where(p => p.Chips > 0).ToList();
            if (alive.Count <= 1)
            {
                foreach (var p in players.ToList())
                    if (p.Chips <= 0) players.Remove(p);

                // Let the showdown explanations finish speaking before anything else.
                await SpeechHelper.WaitForSpeechAsync();

                string winMsg = players.Count == 1
                    ? $"★★★ {players[0].Name} wins the game with {players[0].Chips} chips! ★★★"
                    : "Game over!";
                EmitLog(winMsg);
                OnGameOver?.Invoke(winMsg);
                OnStateChanged?.Invoke();

                SpeechHelper.Say(winMsg);
                await SpeechHelper.WaitForSpeechAsync();

                if (players.Count == 1)
                {
                    SpeechHelper.SayFemale($"Congratulations {players[0].Name}, you are the champion!");
                    await SpeechHelper.WaitForSpeechAsync();
                }

                SpeechHelper.PlayWinnerTune();
                return;
            }

            OnRoundOver?.Invoke();

            _pendingContinue = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await _pendingContinue.Task;
        }
    }

    // ── Dealing ──────────────────────────────────────────────────────────────
    private void DealHands()
    {
        foreach (var player in players)
        {
            if (player.Folded) continue;
            player.Hand.Clear();
            for (int i = 0; i < 5; i++)
            {
                Card? drawn = deck.DrawCard();
                if (drawn != null) player.Hand.Add(drawn);
            }
        }
        EmitLog("Cards dealt.");
    }

    // ── Betting ──────────────────────────────────────────────────────────────
    private async Task BettingRoundAsync(string roundName)
    {
        EmitLog($"─── {roundName} ───");
        SpeechHelper.Say(roundName);

        var activePlayers = players.Where(p => !p.Folded).ToList();
        var betsThisRound = activePlayers.ToDictionary(p => p, _ => 0);
        var acted = activePlayers.ToDictionary(p => p, p => p.Chips == 0);
        int currentBet = 0;
        bool done = false;

        while (!done && activePlayers.Count > 1)
        {
            foreach (var player in activePlayers.ToList())
            {
                if (player.Folded || acted[player]) continue;

                int toCall = currentBet - betsThisRound[player];

                if (player.Chips == 0)
                {
                    acted[player] = true;
                    continue;
                }

                EmitLog($"{player.Name} — {player.Chips} chips. To call: {toCall}.");

                int playerBet;
                bool shouldFold;

                if (player.IsComputer)
                {
                    await Task.Delay(900);
                    playerBet = player.ComputerBetLogic(toCall);
                    shouldFold = playerBet < 0;
                }
                else
                {
                    _pendingBet = new TaskCompletionSource<int>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    OnHumanBetRequested?.Invoke(toCall, currentBet);
                    OnStateChanged?.Invoke();
                    SpeechHelper.Say("Enter your bet amount.");
                    playerBet = await _pendingBet.Task;
                    shouldFold = playerBet == -1;
                }

                if (shouldFold)
                {
                    player.Folded = true;
                    EmitLog($"{player.Name} folds.");
                    SpeechHelper.Say($"{player.Name} folds.");
                    activePlayers.Remove(player);
                    betsThisRound.Remove(player);
                    acted.Remove(player);
                    OnStateChanged?.Invoke();
                    if (activePlayers.Count == 1) { done = true; break; }
                    continue;
                }

                int actualBet = Math.Max(0, Math.Min(playerBet, player.Chips));
                int amountBet = player.Bet(actualBet, toCall);
                betsThisRound[player] += amountBet;
                pot += amountBet;

                // A raise reopens action for all others
                if (amountBet > toCall)
                {
                    currentBet = betsThisRound[player];
                    foreach (var p in activePlayers) acted[p] = false;
                }

                acted[player] = true;
                OnStateChanged?.Invoke();
            }

            if (activePlayers.Count <= 1) break;

            done = activePlayers.All(p =>
                (acted[p] || p.Chips == 0) &&
                (betsThisRound[p] == currentBet || p.Chips == 0));
        }

        EmitLog($"Pot: {pot} chips.");
    }

    // ── Draw phase ───────────────────────────────────────────────────────────
    private async Task DrawPhaseAsync()
    {
        EmitLog("─── Draw Phase ───");
        SpeechHelper.Say("Draw phase.");

        foreach (var player in players)
        {
            if (player.Folded) continue;

            if (player.IsComputer)
            {
                await Task.Delay(900);
                int numToReplace = ComputerDrawLogic(player.Hand);
                List<int> indexesToReplace = numToReplace > 0
                    ? ComputerCardIndexes(player.Hand, numToReplace)
                    : new List<int>();

                // Announce first, wait for speech, then reveal the new cards.
                string msg = numToReplace == 0
                    ? $"{player.Name} stands pat."
                    : $"{player.Name} draws {numToReplace} card{(numToReplace == 1 ? "" : "s")}.";
                EmitLog(msg);
                SpeechHelper.Say(msg);
                await SpeechHelper.WaitForSpeechAsync();

                if (numToReplace > 0)
                {
                    indexesToReplace = indexesToReplace
                        .Distinct()
                        .OrderByDescending(i => i)
                        .ToList();

                    foreach (int idx in indexesToReplace)
                    {
                        if (idx >= 0 && idx < player.Hand.Count)
                        {
                            Card? replacement = deck.DrawCard();
                            if (replacement != null) player.Hand[idx] = replacement;
                        }
                    }
                }
                OnStateChanged?.Invoke();
            }
            else
            {
                _pendingDraw = new TaskCompletionSource<int[]>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                OnHumanDrawRequested?.Invoke();
                OnStateChanged?.Invoke();
                SpeechHelper.Say("Select the cards you want to discard.");
                int[] indices = await _pendingDraw.Task;
                int numToReplace = indices.Length;
                List<int> indexesToReplace = indices.ToList();

                if (numToReplace > 0)
                {
                    indexesToReplace = indexesToReplace
                        .Distinct()
                        .OrderByDescending(i => i)
                        .ToList();

                    foreach (int idx in indexesToReplace)
                    {
                        if (idx >= 0 && idx < player.Hand.Count)
                        {
                            Card? replacement = deck.DrawCard();
                            if (replacement != null) player.Hand[idx] = replacement;
                        }
                    }
                }

                string msg = numToReplace == 0
                    ? $"{player.Name} stands pat."
                    : $"{player.Name} draws {numToReplace} card{(numToReplace == 1 ? "" : "s")}.";
                EmitLog(msg);
                SpeechHelper.Say(msg);
                OnStateChanged?.Invoke();
            }
        }
    }

    // ── Showdown ─────────────────────────────────────────────────────────────
    private async Task ShowdownAsync()
    {
        EmitLog("─── Showdown ───");
        SpeechHelper.Say("Showdown.");
        await SpeechHelper.WaitForSpeechAsync();

        var remaining = players.Where(p => !p.Folded).ToList();

        if (remaining.Count == 0)
        {
            EmitLog("All players folded. Pot returned.");
            pot = 0;
            OnShowdown?.Invoke(new List<(Player, HandEvaluation)>());
            return;
        }

        var evaluations = new Dictionary<Player, HandEvaluation>();
        var showdownData = new List<(Player player, HandEvaluation eval)>();

        List<Player> winners;

        if (remaining.Count == 1)
        {
            var solo = remaining[0];
            evaluations[solo] = EvaluateHand(solo.Hand);
            showdownData.Add((solo, evaluations[solo]));
            winners = remaining;
            EmitLog($"  {solo.Name}: {evaluations[solo].Explanation}");
            SpeechHelper.Say($"{solo.Name} has {evaluations[solo].Explanation}.");
        }
        else
        {
            foreach (var p in remaining)
            {
                evaluations[p] = EvaluateHand(p.Hand);
                showdownData.Add((p, evaluations[p]));
            }

            HandEvaluation best = evaluations[remaining[0]];
            foreach (var p in remaining)
                if (CompareHands(evaluations[p], best) > 0)
                    best = evaluations[p];

            winners = remaining
                .Where(p => CompareHands(evaluations[p], best) == 0)
                .ToList();

            foreach (var p in remaining)
            {
                bool isWinner = winners.Contains(p);
                string line = isWinner
                    ? $"★ {p.Name}: {evaluations[p].Explanation} ★"
                    : $"  {p.Name}: {evaluations[p].Explanation}";
                EmitLog(line);
                SpeechHelper.Say($"{p.Name} has {evaluations[p].Explanation}.");
            }
        }

        OnShowdown?.Invoke(showdownData);

        int share = pot / winners.Count;
        int remainder = pot % winners.Count;

        if (winners.Count == 1)
        {
            string winMsg = $"{winners[0].Name} wins the pot of {pot} chips!";
            EmitLog(winMsg);
            SpeechHelper.Say(winMsg);
        }
        else
        {
            string names = string.Join(" and ", winners.Select(w => w.Name));
            EmitLog($"Split pot! {names} each win {share} chips.");
            SpeechHelper.Say($"Split pot! {names} tie.");
        }

        // Wait for all showdown speech (hand explanations + winner announcement)
        // before updating chip counts and pot so the UI reflects the change only
        // after the full declaration is spoken.
        await SpeechHelper.WaitForSpeechAsync();

        foreach (var w in winners) w.Chips += share;
        winners[0].Chips += remainder;
        pot = 0;
    }

    // ── Hand evaluation (ported verbatim) ─────────────────────────────────────
    internal HandEvaluation EvaluateHand(List<Card> hand)
    {
        List<Card> sortedHand = hand.OrderByDescending(c => c.Value).ToList();
        var groups = sortedHand
            .GroupBy(c => c.Value)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Value)
            .ToList();

        bool isFlush = hand.All(c => c.Suit == hand[0].Suit);

        var sortedAsc = hand.OrderBy(c => c.Value).ToList();
        bool isStraight = true;
        for (int i = 0; i < sortedAsc.Count - 1; i++)
        {
            if (sortedAsc[i + 1].Value - sortedAsc[i].Value != 1)
            {
                isStraight = false;
                break;
            }
        }

        // Ace-low straight: A-2-3-4-5
        if (!isStraight &&
            sortedAsc[0].Value == 2 && sortedAsc[1].Value == 3 &&
            sortedAsc[2].Value == 4 && sortedAsc[3].Value == 5 &&
            sortedAsc[4].Value == 14)
        {
            isStraight = true;
            sortedHand.RemoveAll(c => c.Value == 14);
            sortedHand.Add(new Card(sortedHand[0].Suit, "Ace", 1));
            sortedHand = sortedHand.OrderByDescending(c => c.Value).ToList();
        }

        if (isFlush && isStraight)
        {
            if (sortedHand[0].Value == 14 && sortedHand[1].Value == 13)
                return new HandEvaluation
                {
                    Category = HandCategory.RoyalFlush,
                    Tiebreakers = sortedHand.Select(c => c.Value).ToList(),
                    Explanation = $"Royal Flush: 10, Jack, Queen, King, Ace of {hand[0].Suit}"
                };
            return new HandEvaluation
            {
                Category = HandCategory.StraightFlush,
                Tiebreakers = sortedHand.Select(c => c.Value).ToList(),
                Explanation = $"Straight Flush: {string.Join(", ", sortedHand)}"
            };
        }

        if (groups[0].Count == 4)
        {
            int fourVal = groups[0].Value;
            int kicker = groups.Count > 1 ? groups[1].Value : 0;
            return new HandEvaluation
            {
                Category = HandCategory.FourOfAKind,
                Tiebreakers = new List<int> { fourVal, kicker },
                Explanation = $"Four of a Kind: Four {ValueToRank(fourVal)}s"
            };
        }

        if (groups[0].Count == 3 && groups.Count > 1 && groups[1].Count == 2)
            return new HandEvaluation
            {
                Category = HandCategory.FullHouse,
                Tiebreakers = new List<int> { groups[0].Value, groups[1].Value },
                Explanation = $"Full House: {ValueToRank(groups[0].Value)}s over {ValueToRank(groups[1].Value)}s"
            };

        if (isFlush)
            return new HandEvaluation
            {
                Category = HandCategory.Flush,
                Tiebreakers = sortedHand.Select(c => c.Value).ToList(),
                Explanation = $"Flush: All {hand[0].Suit}"
            };

        if (isStraight)
            return new HandEvaluation
            {
                Category = HandCategory.Straight,
                Tiebreakers = sortedHand.Select(c => c.Value).ToList(),
                Explanation = $"Straight: {string.Join(", ", sortedHand)}"
            };

        if (groups[0].Count == 3)
        {
            int threeVal = groups[0].Value;
            var kickers = sortedHand.Where(c => c.Value != threeVal).Select(c => c.Value).ToList();
            return new HandEvaluation
            {
                Category = HandCategory.ThreeOfAKind,
                Tiebreakers = new List<int> { threeVal }.Concat(kickers).ToList(),
                Explanation = $"Three of a Kind: Three {ValueToRank(threeVal)}s"
            };
        }

        if (groups[0].Count == 2 && groups.Count(g => g.Count == 2) == 2)
        {
            var pairs = groups.Where(g => g.Count == 2)
                              .Select(g => g.Value)
                              .OrderByDescending(v => v)
                              .ToList();
            int kicker = sortedHand.First(c => !pairs.Contains(c.Value)).Value;
            return new HandEvaluation
            {
                Category = HandCategory.TwoPair,
                Tiebreakers = pairs.Concat(new[] { kicker }).ToList(),
                Explanation = $"Two Pair: {ValueToRank(pairs[0])}s and {ValueToRank(pairs[1])}s"
            };
        }

        if (groups[0].Count == 2)
        {
            int pairVal = groups[0].Value;
            var kickers = sortedHand.Where(c => c.Value != pairVal).Select(c => c.Value).ToList();
            return new HandEvaluation
            {
                Category = HandCategory.OnePair,
                Tiebreakers = new List<int> { pairVal }.Concat(kickers).ToList(),
                Explanation = $"One Pair: A pair of {ValueToRank(pairVal)}s"
            };
        }

        return new HandEvaluation
        {
            Category = HandCategory.HighCard,
            Tiebreakers = sortedHand.Select(c => c.Value).ToList(),
            Explanation = $"High Card: {ValueToRank(sortedHand[0].Value)}"
        };
    }

    private static string ValueToRank(int value) => value switch
    {
        11 => "Jack",
        12 => "Queen",
        13 => "King",
        14 or 1 => "Ace",
        _ => value.ToString()
    };

    private static int CompareHands(HandEvaluation h1, HandEvaluation h2)
    {
        if (h1.Category != h2.Category)
            return h1.Category > h2.Category ? 1 : -1;

        for (int i = 0; i < Math.Min(h1.Tiebreakers.Count, h2.Tiebreakers.Count); i++)
            if (h1.Tiebreakers[i] != h2.Tiebreakers[i])
                return h1.Tiebreakers[i] > h2.Tiebreakers[i] ? 1 : -1;

        return 0;
    }

    // ── Computer AI (ported verbatim) ─────────────────────────────────────────
    private static bool HasFourToFlush(List<Card> hand) =>
        hand.GroupBy(c => c.Suit).Any(g => g.Count() == 4);

    private static bool HasFourToStraight(List<Card> hand)
    {
        for (int skip = 0; skip < hand.Count; skip++)
        {
            var subset = hand.Where((_, i) => i != skip)
                             .Select(c => c.Value)
                             .OrderBy(v => v)
                             .ToList();
            bool consecutive = true;
            for (int i = 0; i < subset.Count - 1; i++)
                if (subset[i + 1] - subset[i] != 1) { consecutive = false; break; }
            if (consecutive) return true;
        }
        return false;
    }

    private int ComputerDrawLogic(List<Card> hand)
    {
        HandEvaluation eval = EvaluateHand(hand);

        if (eval.Category == HandCategory.HighCard &&
            (HasFourToFlush(hand) || HasFourToStraight(hand)))
            return 1;

        return eval.Category switch
        {
            HandCategory.HighCard => 3,
            HandCategory.OnePair => 2,
            HandCategory.TwoPair => 1,
            _ => 0
        };
    }

    private List<int> ComputerCardIndexes(List<Card> hand, int numToReplace)
    {
        var groups = hand.GroupBy(c => c.Value)
                         .Select(g => new { Value = g.Key, Count = g.Count() })
                         .OrderByDescending(g => g.Count)
                         .ThenByDescending(g => g.Value)
                         .ToList();

        HashSet<int> keepValues = new();
        switch (EvaluateHand(hand).Category)
        {
            case HandCategory.TwoPair:
                foreach (var g in groups.Where(g => g.Count >= 2))
                    keepValues.Add(g.Value);
                break;

            case HandCategory.OnePair:
            case HandCategory.ThreeOfAKind:
                keepValues.Add(groups[0].Value);
                break;

            case HandCategory.HighCard:
                if (numToReplace == 1 && HasFourToFlush(hand))
                {
                    string flushSuit = hand.GroupBy(c => c.Suit)
                                          .OrderByDescending(g => g.Count())
                                          .First().Key;
                    foreach (var c in hand.Where(c => c.Suit == flushSuit))
                        keepValues.Add(c.Value);
                }
                else if (numToReplace == 1 && HasFourToStraight(hand))
                {
                    for (int skip = 0; skip < hand.Count; skip++)
                    {
                        var subset = hand.Where((_, i) => i != skip)
                                         .Select(c => c.Value)
                                         .OrderBy(v => v)
                                         .ToList();
                        bool consecutive = true;
                        for (int i = 0; i < subset.Count - 1; i++)
                            if (subset[i + 1] - subset[i] != 1) { consecutive = false; break; }
                        if (consecutive)
                        {
                            foreach (var c in hand.Where((_, i) => i != skip))
                                keepValues.Add(c.Value);
                            break;
                        }
                    }
                }
                else
                {
                    foreach (var c in hand.OrderByDescending(c => c.Value)
                                          .Take(hand.Count - numToReplace))
                        keepValues.Add(c.Value);
                }
                break;

            default:
                return new List<int>();
        }

        return hand.Select((c, i) => (c, i))
                   .Where(x => !keepValues.Contains(x.c.Value))
                   .OrderBy(x => x.c.Value)
                   .Take(numToReplace)
                   .Select(x => x.i)
                   .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void EmitLog(string msg) => OnLog?.Invoke(msg);

    private void ExplainHumanHand(string prefix)
    {
        var human = players.FirstOrDefault(p => !p.IsComputer && !p.Folded);
        if (human == null || human.Hand.Count < 5) return;

        HandEvaluation eval = EvaluateHand(human.Hand);
        string msg = $"{prefix} {eval.Explanation}.";
        EmitLog(msg);
        SpeechHelper.Say(msg);
    }

    private void LogChipCounts()
    {
        foreach (var p in players.OrderByDescending(p => p.Chips))
            EmitLog($"  {p.Name}: {p.Chips} chips");
    }
}
