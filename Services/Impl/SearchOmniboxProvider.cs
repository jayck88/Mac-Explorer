using MacExplorer.Models;
using MacExplorer.ViewModels;
using AppIcons = MacExplorer.Assets.Icons;

namespace MacExplorer.Services.Impl;

/// <summary>Provides file search results from the FTS5 index.</summary>
public class SearchOmniboxProvider : IOmniboxProvider
{
    private const int MaxSearchSuggestions = 10;

    public string Name => "Search";
    public int Priority => 10;

    public async Task<IReadOnlyList<OmniboxSuggestion>> GetSuggestionsAsync(
        FileListViewModel viewModel,
        string input,
        CancellationToken cancellationToken)
    {
        var value = input?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return [];

        var results = await viewModel.GetSearchSuggestionsAsync(
            value, MaxSearchSuggestions, cancellationToken);

        var suggestions = new List<OmniboxSuggestion>();
        foreach (var entry in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            suggestions.Add(new OmniboxSuggestion(
                OmniboxSuggestionKind.Result,
                entry.Name,
                entry.FullPath,
                entry.FullPath,
                entry.IsDirectory ? AppIcons.Folder : AppIcons.File,
                entry.IsDirectory ? "#54A3F7" : "#7C8798",
                entry));
        }

        return suggestions;
    }
}
