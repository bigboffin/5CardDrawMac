namespace FiveCardDraw;

class Deck
{
    private List<Card> cards;
    private static readonly Random random = new();

    public Deck()
    {
        cards = new List<Card>();
        string[] suits = { "Hearts", "Diamonds", "Clubs", "Spades" };
        string[] ranks = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "Jack", "Queen", "King", "Ace" };
        int[] values = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };

        foreach (var suit in suits)
            for (int i = 0; i < ranks.Length; i++)
                cards.Add(new Card(suit, ranks[i], values[i]));

        Shuffle();
    }

    public void Shuffle()
    {
        int n = cards.Count;
        for (int i = n - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }

    public Card? DrawCard()
    {
        if (cards.Count == 0) return null;
        Card drawn = cards[0];
        cards.RemoveAt(0);
        return drawn;
    }
}
