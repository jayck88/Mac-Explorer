using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacExplorer.Models;
using MacExplorer.Views;
using System.Reflection;
using Xunit;

namespace MacExplorer.Tests;

public sealed class FileListViewLayoutTests
{
    [AvaloniaFact]
    public void GridEntryDisplaysTheMiddleAbbreviatedFileName()
    {
        var entry = File("a-very-long-file-name.csproj");
        var view = new FileListView();
        var template = Assert.IsAssignableFrom<IDataTemplate>(view.Resources["GridEntryTemplate"]);
        var card = Assert.IsAssignableFrom<Control>(template.Build(entry));
        card.DataContext = entry;
        var window = new Window { Width = 300, Height = 240, Content = card };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var fileName = card.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Classes.Contains("entry-name-text"));
        Assert.Equal("a-very…csproj", fileName.Text);

        window.Close();
    }

    [AvaloniaFact]
    public void RealizedAndNewRowsUseTheSameEffectiveWidthsAsHeader()
    {
        var view = new FileListView();
        var surface = view.FindControl<Grid>("InteractionSurface")!;
        surface.Children.Add(BuildRow(view, File("first.txt")));
        var window = new Window { Width = 760, Height = 480, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        AssertRowsMatchHeader(view, expectedRowCount: 1);

        surface.Children.Add(BuildRow(view, File("second.txt")));
        Dispatcher.UIThread.RunJobs();

        AssertRowsMatchHeader(view, expectedRowCount: 2);
        window.Close();
    }

    [AvaloniaFact]
    public void InlineRenameEditorIsCompactAndDoesNotStretchAcrossTheNameColumn()
    {
        var entry = File("short.txt");
        var view = new FileListView();
        view.Styles.Add((Styles)AvaloniaXamlLoader.Load(
            new Uri("avares://MacExplorer/Assets/Styles.axaml")));
        view.Styles.Add((Styles)AvaloniaXamlLoader.Load(
            new Uri("avares://MacExplorer/Assets/ComponentStyles.axaml")));
        var surface = view.FindControl<Grid>("InteractionSurface")!;
        surface.Children.Add(BuildRow(view, entry));
        var window = new Window { Width = 760, Height = 480, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var renameMethod = typeof(FileListView).GetMethod(
            "OnRenameRequested",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(renameMethod);
        renameMethod.Invoke(view, [entry]);
        Dispatcher.UIThread.RunJobs();

        var editor = Assert.Single(
            view.GetVisualDescendants().OfType<TextBox>(),
            textBox => textBox.Classes.Contains("inline-rename-editor"));
        Assert.Equal(HorizontalAlignment.Left, editor.HorizontalAlignment);
        Assert.Equal(22, editor.Height);
        Assert.InRange(editor.Width, 72, 140);
        Assert.True(editor.Width < 420);
        Assert.Equal(new Thickness(1), editor.BorderThickness);
        Assert.Equal(new CornerRadius(4), editor.CornerRadius);
        Assert.Null(editor.FocusAdorner);

        window.Close();
    }

    private static void AssertRowsMatchHeader(FileListView view, int expectedRowCount)
    {
        var header = view.FindControl<Grid>("ListHeaderGrid")!;
        var expected = GetDataColumnWidths(header);
        var rows = view.GetVisualDescendants()
            .OfType<Grid>()
            .Where(grid => grid.Classes.Contains("file-list-row-grid"))
            .ToArray();

        Assert.Equal(expectedRowCount, rows.Length);
        Assert.All(rows, row => Assert.Equal(expected, GetDataColumnWidths(row)));
    }

    private static double[] GetDataColumnWidths(Grid grid) => grid.ColumnDefinitions
        .Skip(1)
        .Take(4)
        .Select(column => column.Width.Value)
        .ToArray();

    private static Control BuildRow(FileListView view, FileSystemEntry entry)
    {
        var template = Assert.IsAssignableFrom<IDataTemplate>(view.Resources["ListEntryTemplate"]);
        var row = Assert.IsAssignableFrom<Control>(template.Build(entry));
        row.DataContext = entry;
        return row;
    }

    private static FileSystemEntry File(string name) => new()
    {
        FullPath = Path.Combine("/tmp", name),
        Name = name,
        Extension = ".txt"
    };

}
