namespace FiveCardDraw;

public class HandEvaluation
{
    public HandCategory Category { get; set; }
    public List<int> Tiebreakers { get; set; } = new List<int>();
    public string Explanation { get; set; } = string.Empty;
}
