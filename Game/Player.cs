namespace FiveCardDraw;

class Player
{
    public string Name { get; set; }
    public List<Card> Hand { get; set; }
    public int Chips { get; set; }
    public bool Folded { get; set; }
    public bool IsComputer { get; set; }

    internal Action<string>? Log { get; set; }
    internal PokerGame? Game { get; set; }

    private static readonly Random _random = new();

    public Player(string name, int startingChips, bool isComputer = false)
    {
        Name = name;
        Chips = startingChips;
        Hand = new List<Card>();
        Folded = false;
        IsComputer = isComputer;
    }

    public int Bet(int amount, int toCall)
    {
        if (amount < 0)
        {
            Fold();
            return 0;
        }

        if (Chips == 0)
        {
            Log?.Invoke($"{Name} is already all-in.");
            return 0;
        }

        int actualBet = Math.Min(amount, Chips);
        bool isAllIn = toCall > 0 && actualBet < toCall;

        if (isAllIn)
        {
            actualBet = Chips;
            Chips = 0;
            Log?.Invoke($"{Name} goes all-in with {actualBet}!");
            SpeechHelper.Say($"{Name} goes all in!");
            return actualBet;
        }

        Chips -= actualBet;

        if (toCall > 0)
        {
            if (actualBet == toCall)
            {
                Log?.Invoke($"{Name} calls {actualBet}.");
                SpeechHelper.Say($"{Name} calls.");
            }
            else
            {
                Log?.Invoke($"{Name} raises to {actualBet}.");
                SpeechHelper.Say($"{Name} raises.");
            }
        }
        else
        {
            Log?.Invoke($"{Name} bets {actualBet}.");
            SpeechHelper.Say($"{Name} bets {actualBet}.");
        }

        return actualBet;
    }

    internal int ComputerBetLogic(int toCall)
    {
        if (Game == null) throw new InvalidOperationException("Game not set.");
        HandEvaluation eval = Game.EvaluateHand(Hand);
        return toCall > 0 ? ComputerCallLogic(eval, toCall) : ComputerOpenLogic(eval);
    }

    private int ComputerCallLogic(HandEvaluation eval, int toCall)
    {
        bool anyPairOrBetter = eval.Category >= HandCategory.OnePair;
        bool decentHighCard = eval.Tiebreakers.Count > 0 && eval.Tiebreakers[0] >= 8;
        bool secondaryHigh = eval.Tiebreakers.Count > 1 && eval.Tiebreakers[1] >= 10;
        bool smallBet = toCall < (Game!.pot * 0.25);
        bool randomStay = _random.Next(100) < 80;

        if (anyPairOrBetter || decentHighCard || secondaryHigh || smallBet || randomStay)
            return Math.Min(toCall, Chips);

        return -1;
    }

    private int ComputerOpenLogic(HandEvaluation eval)
    {
        int max = Chips;
        var betTable = new Dictionary<HandCategory, int>
        {
            { HandCategory.RoyalFlush,    Math.Min(100, max) },
            { HandCategory.StraightFlush, Math.Min(80,  max) },
            { HandCategory.FourOfAKind,   Math.Min(60,  max) },
            { HandCategory.FullHouse,     Math.Min(50,  max) },
            { HandCategory.Flush,         Math.Min(40,  max) },
            { HandCategory.Straight,      Math.Min(30,  max) },
            { HandCategory.ThreeOfAKind,  Math.Min(20,  max) },
            { HandCategory.TwoPair,       Math.Min(15,  max) },
            { HandCategory.OnePair,       Math.Min(10,  max) },
            { HandCategory.HighCard,      _random.Next(0, 5) },
        };

        int bet = betTable[eval.Category] + _random.Next(-3, 4);
        return Math.Max(0, Math.Min(bet, max));
    }

    public void Fold()
    {
        Folded = true;
        Log?.Invoke($"{Name} folds.");
        SpeechHelper.Say($"{Name} folds.");
    }
}
