using MacExplorer.Models;
using MacExplorer.ViewModels;
using AppIcons = MacExplorer.Assets.Icons;

namespace MacExplorer.Services.Impl;

/// <summary>Provides recent/frequent directories for the command palette.</summary>
public class RecentDirectoryOmniboxProvider : IOmniboxProvider
{
    private const int MaxResults = 5;
    private readonly IFrequentFolderService? _frequentFolderService;

    public RecentDirectoryOmniboxProvider(IFrequentFolderService? frequentFolderService = null)
    {
        _frequentFolderService = frequentFolderService;
    }

    public string Name => "Recent";
    public int Priority => 15;

    public async Task<IReadOnlyList<OmniboxSuggestion>> GetSuggestionsAsync(
        FileListViewModel viewModel,
        string input,
        CancellationToken cancellationToken)
    {
        if (_frequentFolderService == null)
            return [];

        var value = input?.Trim() ?? string.Empty;
        var recent = await _frequentFolderService.GetTopFoldersAsync(MaxResults * 2);

        var results = new List<OmniboxSuggestion>();
        foreach (var folder in recent)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = folder.Name;
            if (string.IsNullOrEmpty(name)) name = folder.Path;
            var path = folder.Path;

            var matches = value.Length == 0
                || name.Contains(value, StringComparison.OrdinalIgnoreCase)
                || path.Contains(value, StringComparison.OrdinalIgnoreCase);

            if (!matches) continue;

            results.Add(new OmniboxSuggestion(
                OmniboxSuggestionKind.RecentDirectory,
                name,
                path,
                path,
                AppIcons.FrequentFolder,
                "#F59E0B"));

            if (results.Count >= MaxResults) break;
        }

        return results;
    }
}
