using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace FiveCardDraw;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        string path = Path.Combine(AppContext.BaseDirectory, "Dogs Playing Cards.png");
        if (File.Exists(path))
        {
            var bmp = new Bitmap(path);
            var screen = Screens.Primary;
            if (screen != null)
            {
                int maxW = (int)(screen.Bounds.Width * 0.80);
                int maxH = (int)(screen.Bounds.Height * 0.80);
                double scale = Math.Min((double)maxW / bmp.PixelSize.Width,
                                        (double)maxH / bmp.PixelSize.Height);
                double w = bmp.PixelSize.Width * scale;
                double h = bmp.PixelSize.Height * scale;
                Width = w;
                Height = h;
                Position = new PixelPoint(
                    (int)((screen.Bounds.Width - w) / 2),
                    (int)((screen.Bounds.Height - h) / 2));
            }
            SplashImage.Source = bmp;
        }
        else
        {
            Width = 600;
            Height = 400;
        }

        await Task.Delay(4000);

        new MainWindow().Show();
        Close();
    }
}
