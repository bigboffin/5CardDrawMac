using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace FiveCardDraw;

public partial class MainWindow : Window
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private static readonly Color TableColor = Color.Parse("#19592A");
    private static readonly Color SelectedColor = Colors.Gold;
    private const int OppCardW = 52, OppCardH = 73;
    private const int HumanCardW = 90, HumanCardH = 126;
    private const int SelectBorder = 4;

    // ── Game state ────────────────────────────────────────────────────────────
    private PokerGame? _game;
    private bool _inShowdown;
    private int _toCall;
    private readonly bool[] _selectedCards = new bool[5];
    private List<(Player player, HandEvaluation eval)> _showdownData = new();

    // ── Human card controls ───────────────────────────────────────────────────
    private readonly Image[] _humanCardImages = new Image[5];
    private readonly Border[] _humanCardBorders = new Border[5];

    // ── Opponent panel data ───────────────────────────────────────────────────
    private readonly Dictionary<Player, (Border box, Image[] pics, TextBlock chipLbl)> _oppPanels = new();

    public MainWindow()
    {
        InitializeComponent();
        CardImageProvider.Log = AppendLog;

        BtnFold.Click += (_, _) => _game?.SubmitFold();
        BtnCall.Click += BtnCall_Click;
        BtnRaise.Click += BtnRaise_Click;
        BtnDraw.Click += BtnDraw_Click;
        BtnNextHand.Click += BtnNextHand_Click;

        ChkMute.IsCheckedChanged += (_, _) =>
            SpeechHelper.Enabled = ChkMute.IsChecked != true;

        ChkCheat.IsCheckedChanged += (_, _) =>
        {
            if (_game != null) { _game.CheatMode = ChkCheat.IsChecked == true; RefreshUI(); }
        };

        ChkAutoAdvance.IsCheckedChanged += (_, _) =>
        {
            if (ChkAutoAdvance.IsChecked == true && BtnNextHand.IsEnabled
                && (string?)BtnNextHand.Content == "Next Hand ▶")
                TriggerAutoAdvance();
        };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await StartNewGame();
    }

    // ── Game startup ──────────────────────────────────────────────────────────
    private async Task StartNewGame()
    {
        SetupResult? result = await SetupDialog.ShowAsync(this);
        if (result == null) { Close(); return; }

        TbLog.Text = "";
        PnlOpponents.Children.Clear();
        _oppPanels.Clear();
        _showdownData.Clear();
        _inShowdown = false;
        LblPhase.Text = "";
        LblPhase.Foreground = Brushes.White;
        LblPhase.FontSize = 13;
        LblPhase.FontWeight = FontWeight.Normal;

        var players = new List<Player>();
        var human = new Player(result.PlayerName, result.StartingChips, isComputer: false);
        players.Add(human);
        for (int i = 2; i <= result.PlayerCount; i++)
            players.Add(new Player($"Computer {i - 1}", result.StartingChips, isComputer: true));

        _game = new PokerGame(players);
        _game.CheatMode = result.CheatMode;
        ChkCheat.IsChecked = result.CheatMode;

        _game.OnLog += AppendLog;
        _game.OnStateChanged += RefreshUI;
        _game.OnHumanBetRequested += HandleBetRequested;
        _game.OnHumanDrawRequested += HandleDrawRequested;
        _game.OnShowdown += HandleShowdown;
        _game.OnRoundOver += HandleRoundOver;
        _game.OnGameOver += HandleGameOver;

        BuildPlayerPanels(players);
        DisableAllActions();

        _ = _game.RunAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                Dispatcher.UIThread.InvokeAsync(() =>
                    AppendLog($"[GAME ERROR] {t.Exception?.InnerException?.Message ?? t.Exception?.Message}"));
        }, TaskScheduler.Default);
    }

    // ── Build per-player panels ───────────────────────────────────────────────
    private void BuildPlayerPanels(List<Player> players)
    {
        PnlHumanCards.Children.Clear();
        for (int i = 0; i < 5; i++)
        {
            var cardImg = new Image
            {
                Width = HumanCardW,
                Height = HumanCardH,
                Stretch = Stretch.Uniform
            };
            int idx = i;
            var border = new Border
            {
                Background = new SolidColorBrush(TableColor),
                BorderBrush = new SolidColorBrush(TableColor),
                BorderThickness = new Thickness(SelectBorder),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = cardImg,
                Margin = new Thickness(4),
                Width = HumanCardW + SelectBorder * 2,
                Height = HumanCardH + SelectBorder * 2,
                VerticalAlignment = VerticalAlignment.Top
            };
            border.PointerPressed += (_, _) => HumanCard_Click(idx);
            PnlHumanCards.Children.Add(border);
            _humanCardBorders[i] = border;
            _humanCardImages[i] = cardImg;
        }

        var humanPlayer = players.First(p => !p.IsComputer);
        LblHumanInfo.Text = $"  ★ {humanPlayer.Name}  —  {humanPlayer.Chips} chips";

        foreach (var opp in players.Where(p => p.IsComputer))
        {
            var chipLbl = new TextBlock
            {
                Text = $"{opp.Chips} chips",
                Foreground = Brushes.Silver,
                FontSize = 8,
                Margin = new Thickness(0, 0, 0, 2)
            };

            var cardRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            var pics = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                pics[i] = new Image
                {
                    Width = OppCardW,
                    Height = OppCardH,
                    Stretch = Stretch.Uniform
                };
                cardRow.Children.Add(pics[i]);
            }

            var inner = new StackPanel { Spacing = 2, Children = { chipLbl, cardRow } };

            var header = new TextBlock
            {
                Text = opp.Name,
                Foreground = Brushes.LightGray,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var content = new StackPanel { Children = { header, inner } };

            var box = new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(TableColor),
                Padding = new Thickness(6),
                Child = content
            };

            PnlOpponents.Children.Add(box);
            _oppPanels[opp] = (box, pics, chipLbl);
        }
    }

    // ── Event handlers from PokerGame ─────────────────────────────────────────
    private void AppendLog(string msg)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            TbLog.Text += msg + "\n";
            LogScroller.ScrollToEnd();
        });
    }

    private void RefreshUI()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_game == null) return;
            try
            {
                var players = _game.GetPlayers();
                var human = players.FirstOrDefault(p => !p.IsComputer);

                if (human != null)
                {
                    LblHumanInfo.Text = $"  ★ {human.Name}  —  {human.Chips} chips";
                    for (int i = 0; i < 5; i++)
                    {
                        _humanCardImages[i].Source = i < human.Hand.Count
                            ? CardImageProvider.GetCardImage(human.Hand[i], HumanCardW, HumanCardH)
                            : null;
                        _humanCardBorders[i].BorderBrush = new SolidColorBrush(TableColor);
                    }
                }
                else
                {
                    LblHumanInfo.Text = "  ★ Eliminated";
                    for (int i = 0; i < 5; i++)
                    {
                        _humanCardImages[i].Source = null;
                        _humanCardBorders[i].BorderBrush = new SolidColorBrush(TableColor);
                    }
                }

                foreach (var (opp, (_, pics, chipLbl)) in _oppPanels)
                {
                    if (!players.Contains(opp))
                    {
                        chipLbl.Text = "Eliminated";
                        foreach (var pic in pics) pic.Source = null;
                        continue;
                    }
                    chipLbl.Text = $"{opp.Chips} chips" + (opp.Folded ? "  [folded]" : "");

                    bool reveal = _game.CheatMode
                        || (_inShowdown && _showdownData.Any(d => d.player == opp));
                    for (int i = 0; i < 5; i++)
                    {
                        if (i < opp.Hand.Count)
                            pics[i].Source = reveal
                                ? CardImageProvider.GetCardImage(opp.Hand[i], OppCardW, OppCardH)
                                : CardImageProvider.GetCardBack();
                        else
                            pics[i].Source = null;
                    }
                }

                LblPot.Text = $"Pot: {_game.pot}";
            }
            catch (Exception ex)
            {
                AppendLog($"[UI ERROR] {ex.Message}");
            }
        });
    }

    private void HandleBetRequested(int toCall, int currentBet)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _toCall = toCall;
            LblPhase.Text = toCall == 0 ? "Your action — check or bet" : $"Your action — call {toCall} or raise";
            LblToCall.Text = toCall == 0 ? "Check / Bet" : $"To call: {toCall}";
            BtnCall.Content = toCall == 0 ? "Check" : $"Call ({toCall})";
            BtnRaise.Content = toCall == 0 ? "Bet" : "Raise";

            if (_game != null)
            {
                var human = _game.GetPlayers().FirstOrDefault(p => !p.IsComputer);
                if (human != null)
                {
                    int minBet = toCall == 0 ? 1 : toCall + 1;
                    NudRaise.Minimum = minBet;
                    NudRaise.Maximum = Math.Max(minBet, human.Chips);
                    NudRaise.Value = Math.Min((decimal)Math.Max(minBet, human.Chips), Math.Max(minBet, toCall + 5));
                }
            }

            BtnFold.IsEnabled = toCall > 0;
            BtnCall.IsEnabled = true;
            NudRaise.IsEnabled = true;
            BtnRaise.IsEnabled = true;
            BtnDraw.IsEnabled = false;
            BtnNextHand.IsEnabled = false;
        });
    }

    private void HandleDrawRequested()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            for (int i = 0; i < 5; i++) _selectedCards[i] = false;
            UpdateCardSelectionUI();
            LblPhase.Text = "Click cards to discard, then Draw";
            LblToCall.Text = "Select 0–3 cards to replace";
            BtnFold.IsEnabled = false;
            BtnCall.IsEnabled = false;
            NudRaise.IsEnabled = false;
            BtnRaise.IsEnabled = false;
            BtnDraw.IsEnabled = true;
            BtnNextHand.IsEnabled = false;
        });
    }

    private void HandleShowdown(List<(Player player, HandEvaluation eval)> data)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _inShowdown = true;
            _showdownData = data;
            LblPhase.Text = "Showdown";
            RefreshUI();
        });
    }

    private void HandleRoundOver()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DisableAllActions();
            BtnNextHand.IsEnabled = true;
            LblPhase.Text = "Round over — click Next Hand to continue";
            if (ChkAutoAdvance.IsChecked == true) TriggerAutoAdvance();
        });
    }

    private void HandleGameOver(string msg)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DisableAllActions();
            LblPhase.Text = msg;
            LblPhase.Foreground = new SolidColorBrush(Colors.Gold);
            LblPhase.FontSize = 14;
            LblPhase.FontWeight = FontWeight.Bold;
            BtnNextHand.Content = "New Game";
            BtnNextHand.Background = new SolidColorBrush(Color.Parse("#22783C"));
            BtnNextHand.IsEnabled = true;
        });
    }

    // ── Button handlers ───────────────────────────────────────────────────────
    private void BtnCall_Click(object? sender, RoutedEventArgs e)
    {
        DisableAllActions();
        _game?.SubmitBet(_toCall);
    }

    private void BtnRaise_Click(object? sender, RoutedEventArgs e)
    {
        DisableAllActions();
        _game?.SubmitBet((int)(NudRaise.Value ?? 10));
    }

    private void BtnDraw_Click(object? sender, RoutedEventArgs e)
    {
        DisableAllActions();
        int[] indices = Enumerable.Range(0, 5).Where(i => _selectedCards[i]).ToArray();
        for (int i = 0; i < 5; i++) _selectedCards[i] = false;
        UpdateCardSelectionUI();
        _game?.SubmitDraw(indices);
    }

    private void BtnNextHand_Click(object? sender, RoutedEventArgs e)
    {
        if ((string?)BtnNextHand.Content == "New Game")
        {
            BtnNextHand.Content = "Next Hand ▶";
            BtnNextHand.Background = new SolidColorBrush(Color.Parse("#505050"));
            BtnNextHand.IsEnabled = false;
            LblPhase.Text = "";
            LblPhase.Foreground = Brushes.White;
            LblPhase.FontSize = 13;
            LblPhase.FontWeight = FontWeight.Normal;
            _ = StartNewGame();
        }
        else
        {
            _inShowdown = false;
            _game?.ContinueToNextRound();
            BtnNextHand.IsEnabled = false;
        }
    }

    private void HumanCard_Click(int index)
    {
        if (!BtnDraw.IsEnabled) return;
        int selected = _selectedCards.Count(s => s);
        if (!_selectedCards[index] && selected >= 3) return;
        _selectedCards[index] = !_selectedCards[index];
        UpdateCardSelectionUI();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void UpdateCardSelectionUI()
    {
        int count = _selectedCards.Count(s => s);
        BtnDraw.Content = $"Draw Selected ({count})";
        for (int i = 0; i < 5; i++)
            _humanCardBorders[i].BorderBrush = _selectedCards[i]
                ? new SolidColorBrush(SelectedColor)
                : new SolidColorBrush(TableColor);
    }

    private void DisableAllActions()
    {
        BtnFold.IsEnabled = false;
        BtnCall.IsEnabled = false;
        NudRaise.IsEnabled = false;
        BtnRaise.IsEnabled = false;
        BtnDraw.IsEnabled = false;
        BtnNextHand.IsEnabled = false;
        LblToCall.Text = "";
    }

    private async void TriggerAutoAdvance()
    {
        await SpeechHelper.WaitForSpeechAsync();
        if (ChkAutoAdvance.IsChecked == true && BtnNextHand.IsEnabled
            && (string?)BtnNextHand.Content == "Next Hand ▶")
        {
            _inShowdown = false;
            _game?.ContinueToNextRound();
            BtnNextHand.IsEnabled = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        SpeechHelper.Cleanup();
        base.OnClosed(e);
    }
}
