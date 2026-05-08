namespace FiveCardDraw;

public class Card
{
    public string Suit { get; set; }
    public string Rank { get; set; }
    public int Value { get; set; }

    public Card(string suit, string rank, int value)
    {
        Suit = suit;
        Rank = rank;
        Value = value;
    }

    public override string ToString() => $"{Rank} of {Suit}";
}
