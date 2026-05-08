using Avalonia;

AppBuilder.Configure<FiveCardDraw.App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .StartWithClassicDesktopLifetime(args);
