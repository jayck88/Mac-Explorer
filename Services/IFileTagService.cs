using MacExplorer.Models;

namespace MacExplorer.Services;

public interface IFileTagService
{
    event EventHandler? TagsChanged;

    Task<IReadOnlyList<FileTag>> GetSidebarTagsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> FindFilePathsAsync(FileTag tag, CancellationToken cancellationToken = default);
    Task ReplaceFileTagsAsync(string filePath, IReadOnlyList<string> tags, CancellationToken cancellationToken = default);
    Task UpdatePathAsync(string oldPath, string newPath, CancellationToken cancellationToken = default);
    Task CopyPathAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    Task DeletePathAsync(string path, CancellationToken cancellationToken = default);
}

public interface IFinderTagQueryService
{
    Task<IReadOnlyList<string>> FindFilePathsAsync(FileTag tag, CancellationToken cancellationToken = default);
}
