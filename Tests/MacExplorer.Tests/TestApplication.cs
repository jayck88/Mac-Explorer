using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace MacExplorer.Tests;

public sealed class TestApplication : Application
{
    public TestApplication()
    {
        Styles.Add((Styles)AvaloniaXamlLoader.Load(
            new Uri("avares://MacExplorer/Assets/ThemeTokens.axaml")));
        Styles.Add((Styles)AvaloniaXamlLoader.Load(
            new Uri("avares://MacExplorer/Controls/AppWindow.axaml")));
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
