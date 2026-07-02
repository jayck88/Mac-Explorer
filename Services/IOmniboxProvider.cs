using MacExplorer.Models;
using MacExplorer.ViewModels;

namespace MacExplorer.Services;

/// <summary>
/// Extension point for the omnibox command palette.
/// Each provider returns suggestions for a given input and can execute them.
/// </summary>
public interface IOmniboxProvider
{
    /// <summary>Display name shown in the palette header (if needed).</summary>
    string Name { get; }

    /// <summary>Sort priority — lower = higher in results.</summary>
    int Priority { get; }

    /// <summary>Return suggestions for the current input, or empty list if not applicable.</summary>
    Task<IReadOnlyList<OmniboxSuggestion>> GetSuggestionsAsync(
        FileListViewModel viewModel,
        string input,
        CancellationToken cancellationToken);
}
