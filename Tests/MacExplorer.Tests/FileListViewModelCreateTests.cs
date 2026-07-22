using MacExplorer.Indexing;
using MacExplorer.Models;
using MacExplorer.Services;
using MacExplorer.ViewModels;
using Xunit;

namespace MacExplorer.Tests;

public sealed class FileListViewModelCreateTests
{
    [Fact]
    public async Task CreateNewFileAsync_SelectsCreatedEntryAndRequestsRenameWithoutDirectoryReload()
    {
        var fileService = new FakeFileService("/tmp/FKFinderTests");
        var viewModel = CreateViewModel(fileService);
        var renamedEntries = new List<FileSystemEntry>();
        viewModel.RenameRequested += renamedEntries.Add;
        viewModel.Entries.Add(new FileSystemEntry
        {
            FullPath = "/tmp/FKFinderTests/Existing.txt",
            Name = "Existing.txt",
            Extension = ".txt",
            IsDirectory = false,
            IconKey = "file-text"
        });

        await viewModel.CreateNewFileAsync(".txt");

        var created = Assert.Single(viewModel.Entries, e => e.Name == "未命名.txt");
        Assert.Same(created, Assert.Single(viewModel.SelectedEntries));
        Assert.Same(created, Assert.Single(renamedEntries));
        Assert.Equal(0, fileService.EnumerateDirectoryCallCount);
    }

    [Fact]
    public async Task ConfirmDeleteSelectedAsync_RemovesDeletedEntryWithoutDirectoryReload()
    {
        var fileService = new FakeFileService("/tmp/FKFinderTests");
        var viewModel = CreateViewModel(fileService);
        var deleted = new FileSystemEntry
        {
            FullPath = "/tmp/FKFinderTests/DeleteMe.txt",
            Name = "DeleteMe.txt",
            Extension = ".txt",
            IsDirectory = false,
            IconKey = "file-text"
        };
        var survivor = new FileSystemEntry
        {
            FullPath = "/tmp/FKFinderTests/KeepMe.txt",
            Name = "KeepMe.txt",
            Extension = ".txt",
            IsDirectory = false,
            IconKey = "file-text"
        };
        viewModel.Entries.Add(deleted);
        viewModel.Entries.Add(survivor);
        viewModel.SelectedEntries.Add(deleted);

        await viewModel.ConfirmDeleteSelectedAsync();

        Assert.DoesNotContain(viewModel.Entries, e => e.FullPath == deleted.FullPath);
        Assert.Same(survivor, Assert.Single(viewModel.Entries));
        Assert.Empty(viewModel.SelectedEntries);
        Assert.Equal(0, fileService.EnumerateDirectoryCallCount);
    }

    [Fact]
    public async Task RenameEntryAsync_ReconcilesInPlaceAndSuppressesDuplicateRefresh()
    {
        var fileService = new FakeFileService("/tmp/FKFinderTests");
        var notifier = new FakeDirectoryChangeNotifier();
        var viewModel = CreateViewModel(fileService, notifier);
        var original = new FileSystemEntry
        {
            FullPath = "/tmp/FKFinderTests/Before.txt",
            Name = "Before.txt",
            Extension = ".txt",
            IsDirectory = false,
            IconKey = "file-text"
        };
        fileService.Seed(original);
        viewModel.Entries.Add(original);
        viewModel.SelectedEntries.Add(original);
        var entries = viewModel.Entries;

        var renamed = await viewModel.RenameEntryAsync(original, "After.txt");

        Assert.True(renamed);
        Assert.Same(entries, viewModel.Entries);
        var visible = Assert.Single(viewModel.Entries);
        Assert.Equal("After.txt", visible.Name);
        Assert.Equal("/tmp/FKFinderTests/After.txt", visible.FullPath);
        Assert.Same(visible, Assert.Single(viewModel.SelectedEntries));
        Assert.Equal(0, fileService.EnumerateDirectoryCallCount);
        Assert.Same(viewModel, notifier.SuppressedViewModel);
        Assert.Same(viewModel, notifier.ExcludedViewModel);
    }

    [Fact]
    public async Task RefreshAsync_KeepsCollectionAndUnchangedEntriesStable()
    {
        var fileService = new FakeFileService("/tmp/FKFinderTests");
        var viewModel = CreateViewModel(fileService);
        for (var i = 0; i < 300; i++)
        {
            var entry = new FileSystemEntry
            {
                FullPath = $"/tmp/FKFinderTests/file-{i:D3}.txt",
                Name = $"file-{i:D3}.txt",
                Extension = ".txt",
                IsDirectory = false,
                IconKey = "file-text"
            };
            fileService.Seed(entry);
            viewModel.Entries.Add(entry);
        }

        var entries = viewModel.Entries;
        var collectionChanges = 0;
        entries.CollectionChanged += (_, _) => collectionChanges++;

        await viewModel.RefreshAsync();

        Assert.Same(entries, viewModel.Entries);
        Assert.Equal(300, viewModel.Entries.Count);
        Assert.Equal(0, collectionChanges);
        Assert.Equal(1, fileService.EnumerateDirectoryCallCount);
    }

    [Fact]
    public async Task NavigateToAsync_KeepsOverlayHiddenUntilNewDirectoryIsReady()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fkfinder-navigation-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(root, "source");
        var targetPath = Path.Combine(root, "target");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        try
        {
            var fileService = new FakeFileService(sourcePath);
            var viewModel = CreateViewModel(fileService);
            var oldEntry = new FileSystemEntry
            {
                FullPath = Path.Combine(sourcePath, "old.txt"),
                Name = "old.txt",
                Extension = ".txt"
            };
            var newEntry = new FileSystemEntry
            {
                FullPath = Path.Combine(targetPath, "new.txt"),
                Name = "new.txt",
                Extension = ".txt"
            };
            viewModel.Entries.Add(oldEntry);
            fileService.Seed(newEntry);
            var overlayBecameVisible = false;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(FileListViewModel.IsLoading) && viewModel.IsLoading)
                    overlayBecameVisible = true;
            };

            await viewModel.NavigateToAsync(targetPath);

            Assert.False(overlayBecameVisible);
            Assert.False(viewModel.IsLoading);
            Assert.Equal(targetPath, viewModel.CurrentPath);
            Assert.Equal(newEntry.FullPath, Assert.Single(viewModel.Entries).FullPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FileListViewModel CreateViewModel(
        FakeFileService fileService,
        IDirectoryChangeNotifier? directoryChangeNotifier = null)
    {
        var navigation = new NavigationViewModel(fileService)
        {
            CurrentPath = fileService.HomeDirectory,
            IsHomePage = false
        };
        var index = new FakeFileIndex();
        var writer = new FakeFileIndexWriter();
        var fileOps = new FileOpsViewModel(
            fileService: fileService,
            directoryChangeNotifier: directoryChangeNotifier);

        return new FileListViewModel(
            navigation,
            fileOps,
            new SearchViewModel(),
            new ArchiveViewModel(fileService: fileService),
            new AiViewModel(fileIndex: index),
            new CollectionViewModel(fileIndex: index, fileService: fileService),
            new SortFilterViewModel(),
            fileService,
            index,
            writer,
            new IndexConfiguration(),
            directoryChangeNotifier: directoryChangeNotifier);
    }

    private sealed class FakeFileService(string homeDirectory) : IFileService
    {
        private readonly Dictionary<string, FileSystemEntry> _entries = new(StringComparer.Ordinal);

        public int EnumerateDirectoryCallCount { get; private set; }
        public string HomeDirectory { get; } = homeDirectory;
        public string RootDirectory => "/";
        public string TrashDirectory => Path.Combine(HomeDirectory, ".Trash");

        public void Seed(FileSystemEntry entry) => _entries[entry.FullPath] = entry;

        public Task<IReadOnlyList<FileSystemEntry>> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileSystemEntry>>(_entries.Values.ToList());

        public async IAsyncEnumerable<IReadOnlyList<FileSystemEntry>> EnumerateDirectoryBatchesAsync(
            string path,
            int batchSize = 256,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnumerateDirectoryCallCount++;
            await Task.Yield();
            var entries = _entries.Values
                .Where(entry => string.Equals(Path.GetDirectoryName(entry.FullPath), path, StringComparison.Ordinal))
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .Select(Clone)
                .ToArray();
            for (var i = 0; i < entries.Length; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return entries.Skip(i).Take(batchSize).ToArray();
            }
        }

        public Task<FileSystemEntry?> GetEntryAsync(string path)
            => Task.FromResult(_entries.GetValueOrDefault(path));

        public Task<bool> ExistsAsync(string path)
            => Task.FromResult(_entries.ContainsKey(path));

        public Task<string> CreateFolderAsync(string parentPath, string name)
        {
            var fullPath = Path.Combine(parentPath, name);
            _entries[fullPath] = new FileSystemEntry
            {
                FullPath = fullPath,
                Name = name,
                IsDirectory = true,
                IconKey = "folder"
            };
            return Task.FromResult(fullPath);
        }

        public Task<string> CreateFileAsync(string parentPath, string name)
            => CreateFileWithContentAsync(parentPath, name, []);

        public Task<string> CreateFileWithContentAsync(string parentPath, string name, byte[] content)
        {
            var fullPath = Path.Combine(parentPath, name);
            _entries[fullPath] = new FileSystemEntry
            {
                FullPath = fullPath,
                Name = name,
                Extension = Path.GetExtension(name),
                IsDirectory = false,
                IconKey = "file-generic"
            };
            return Task.FromResult(fullPath);
        }

        public Task DeleteAsync(string path, bool moveToTrash = true) => Task.CompletedTask;
        public Task RenameAsync(string path, string newName)
        {
            if (_entries.Remove(path, out var entry))
            {
                var newPath = Path.Combine(Path.GetDirectoryName(path) ?? "", newName);
                _entries[newPath] = new FileSystemEntry
                {
                    FullPath = newPath,
                    Name = newName,
                    Extension = Path.GetExtension(newName),
                    IsDirectory = entry.IsDirectory,
                    Size = entry.Size,
                    LastModified = entry.LastModified,
                    Created = entry.Created,
                    IconKey = entry.IconKey
                };
            }
            return Task.CompletedTask;
        }
        public Task MoveAsync(string sourcePath, string destinationPath, bool overwrite = false) => Task.CompletedTask;
        public Task CopyAsync(string sourcePath, string destinationDirectory) => Task.CompletedTask;
        public string GetParentPath(string path) => Path.GetDirectoryName(path) ?? "";
        public string CombinePath(string directory, string name) => Path.Combine(directory, name);
        public IReadOnlyList<string> GetVolumes() => [];
        public Task DeletePermanentlyAsync(string path) => Task.CompletedTask;
        public Task EmptyTrashAsync() => Task.CompletedTask;
        public Task ResolveAppIconsAsync(IEnumerable<FileSystemEntry> entries, Action? onBatchResolved = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsCrossVolume(string sourcePath, string destinationPath) => false;
        public Task MoveWithProgressAsync(IReadOnlyList<string> sourcePaths, string destinationDirectory, IProgress<FileOperationProgress>? progress = null, CancellationToken ct = default) => Task.CompletedTask;

        private static FileSystemEntry Clone(FileSystemEntry entry) => new()
        {
            FullPath = entry.FullPath,
            Name = entry.Name,
            IsDirectory = entry.IsDirectory,
            Size = entry.Size,
            LastModified = entry.LastModified,
            Created = entry.Created,
            Extension = entry.Extension,
            IsHidden = entry.IsHidden,
            IsSymbolicLink = entry.IsSymbolicLink,
            IsReadable = entry.IsReadable,
            IsWritable = entry.IsWritable,
            IconKey = entry.IconKey,
            IsVirtual = entry.IsVirtual,
            VirtualFolderType = entry.VirtualFolderType,
            VirtualFolderKey = entry.VirtualFolderKey,
            VirtualItemCount = entry.VirtualItemCount
        };
    }

    private sealed class FakeFileIndex : IFileIndex
    {
        public Task<IReadOnlyList<FileSystemEntry>> GetDirectoryContentsAsync(string parentPath)
            => Task.FromResult<IReadOnlyList<FileSystemEntry>>([]);

        public Task<FileSystemEntry?> GetEntryAsync(string path)
            => Task.FromResult<FileSystemEntry?>(null);

        public Task<IReadOnlyList<FileSystemEntry>> SearchByNameAsync(string pattern, int limit = 100)
            => Task.FromResult<IReadOnlyList<FileSystemEntry>>([]);

        public Task<bool> IsDirectoryFreshAsync(string path, TimeSpan freshnessThreshold)
            => Task.FromResult(false);
    }

    private sealed class FakeFileIndexWriter : IFileIndexWriter
    {
        public Task UpdateDirectoryAsync(string directoryPath, IReadOnlyList<FileSystemEntry> entries, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateDirectoriesAsync(IEnumerable<string> directoryPaths) => Task.CompletedTask;
        public Task RenameEntryAsync(string oldPath, string newPath, string newName) => Task.CompletedTask;
        public Task RemoveEntryAsync(string path) => Task.CompletedTask;
        public Task AddEntryAsync(FileSystemEntry entry) => Task.CompletedTask;
    }

    private sealed class FakeDirectoryChangeNotifier : IDirectoryChangeNotifier
    {
        public FileListViewModel? SuppressedViewModel { get; private set; }
        public FileListViewModel? ExcludedViewModel { get; private set; }

        public void NotifyChanged(string[] directoryPaths, FileListViewModel? excludeVm = null)
            => ExcludedViewModel = excludeVm;

        public void SuppressRefresh(string[] directoryPaths, TimeSpan duration)
        {
        }

        public void SuppressRefreshFor(FileListViewModel vm, string[] directoryPaths, TimeSpan duration)
            => SuppressedViewModel = vm;

        public void Subscribe(FileListViewModel vm)
        {
        }

        public void Unsubscribe(FileListViewModel vm)
        {
        }
    }
}
