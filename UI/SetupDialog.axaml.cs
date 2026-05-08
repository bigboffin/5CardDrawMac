using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FiveCardDraw;

public record SetupResult(string PlayerName, int PlayerCount, int StartingChips, bool CheatMode);

public partial class SetupDialog : Window
{
    private SetupResult? _result;

    public SetupDialog()
    {
        InitializeComponent();
    }

    private void BtnOK_Click(object? sender, RoutedEventArgs e)
    {
        string name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Player" : TxtName.Text.Trim();
        _result = new SetupResult(
            name,
            (int)(NudPlayers.Value ?? 3),
            (int)(NudChips.Value ?? 100),
            ChkCheat.IsChecked == true);
        Close(_result);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    public static async Task<SetupResult?> ShowAsync(Window owner)
    {
        var dlg = new SetupDialog();
        return await dlg.ShowDialog<SetupResult?>(owner);
    }
}
