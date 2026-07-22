using System.Text.RegularExpressions;
using MacExplorer.Indexing;
using MacExplorer.Models;
using Microsoft.Extensions.Logging;

namespace MacExplorer.Services.Impl;

public class BatchRenameService : IBatchRenameService
{
    private readonly IFileService _fileService;
    private readonly IFileIndexWriter? _fileIndexWriter;
    private readonly IAiTagService? _aiTagService;
    private readonly IPinnedFolderService? _pinnedFolderService;
    private readonly IDirectoryChangeNotifier? _directoryChangeNotifier;
    private readonly IFileTagService? _fileTagService;
    private readonly ILogger<BatchRenameService>? _logger;

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    public BatchRenameService(
        IFileService fileService,
        IFileIndexWriter? fileIndexWriter = null,
        IAiTagService? aiTagService = null,
        IPinnedFolderService? pinnedFolderService = null,
        IDirectoryChangeNotifier? directoryChangeNotifier = null,
        IFileTagService? fileTagService = null,
        ILogger<BatchRenameService>? logger = null)
    {
        _fileService = fileService;
        _fileIndexWriter = fileIndexWriter;
        _aiTagService = aiTagService;
        _pinnedFolderService = pinnedFolderService;
        _directoryChangeNotifier = directoryChangeNotifier;
        _fileTagService = fileTagService;
        _logger = logger;
    }

    public List<BatchRenamePreviewItem> GeneratePreview(
        IReadOnlyList<FileSystemEntry> entries,
        IReadOnlyList<BatchRenameRule> rules)
    {
        var enabledRules = rules.Where(r => r.IsEnabled).ToList();
        var items = new List<BatchRenamePreviewItem>(entries.Count);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect existing names (entries not being renamed)
        foreach (var entry in entries)
            usedNames.Add(entry.Name);

        int sequenceCounter = enabledRules.FirstOrDefault(r => r.Type == BatchRenameRuleType.Sequence)?.SequenceStart ?? 1;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var originalName = entry.Name;
            var newName = originalName;

            // Split name and extension
            string namePart, extPart;
            if (entry.IsDirectory)
            {
                namePart = originalName;
                extPart = "";
            }
            else
            {
                extPart = Path.GetExtension(originalName);
                namePart = string.IsNullOrEmpty(extPart) ? originalName : originalName[..^extPart.Length];
            }

            foreach (var rule in enabledRules)
            {
                newName = ApplyRule(rule, newName, namePart, extPart, i, ref sequenceCounter, out var newNamePart, out var newExtPart);
                if (rule.ApplyToExtension)
                    (namePart, extPart) = (newNamePart, newExtPart);
                else
                    namePart = newNamePart;
            }

            // Reconstruct full name
            newName = string.IsNullOrEmpty(extPart) ? namePart : namePart + extPart;

            var item = new BatchRenamePreviewItem
            {
                OriginalPath = entry.FullPath,
                OriginalName = originalName,
                NewName = newName,
                NewPath = Path.Combine(Path.GetDirectoryName(entry.FullPath) ?? "", newName)
            };

            // Validate
            if (string.IsNullOrWhiteSpace(namePart))
            {
                item.HasError = true;
                item.ErrorReason = "文件名不能为空";
            }
            else if (newName.IndexOfAny(InvalidChars) >= 0)
            {
                item.HasError = true;
                item.ErrorReason = "包含非法字符";
            }
            else if (!string.Equals(originalName, newName, StringComparison.Ordinal))
            {
                // Check for conflicts (another entry in the batch or existing file)
                if (usedNames.Contains(newName) && !string.Equals(originalName, newName, StringComparison.OrdinalIgnoreCase))
                {
                    item.HasConflict = true;
                }
                else if (File.Exists(item.NewPath) && !string.Equals(entry.FullPath, item.NewPath, StringComparison.Ordinal))
                {
                    item.HasConflict = true;
                }
            }

            // Track the new name to prevent duplicates within the batch
            usedNames.Remove(originalName);
            usedNames.Add(newName);

            items.Add(item);
        }

        return items;
    }

    private static string ApplyRule(
        BatchRenameRule rule,
        string currentFull,
        string namePart,
        string extPart,
        int index,
        ref int sequenceCounter,
        out string newNamePart,
        out string newExtPart)
    {
        newNamePart = namePart;
        newExtPart = extPart;

        switch (rule.Type)
        {
            case BatchRenameRuleType.FindReplace:
                if (!string.IsNullOrEmpty(rule.FindText))
                {
                    newNamePart = namePart.Replace(rule.FindText, rule.ReplaceText, StringComparison.OrdinalIgnoreCase);
                    if (rule.ApplyToExtension)
                        newExtPart = extPart.Replace(rule.FindText, rule.ReplaceText, StringComparison.OrdinalIgnoreCase);
                }
                break;

            case BatchRenameRuleType.AddPrefix:
                newNamePart = rule.PrefixText + namePart;
                break;

            case BatchRenameRuleType.AddSuffix:
                newNamePart = namePart + rule.SuffixText;
                break;

            case BatchRenameRuleType.Sequence:
                var seq = sequenceCounter.ToString().PadLeft(rule.SequencePadding, '0');
                newNamePart = $"{namePart}_{seq}";
                sequenceCounter += rule.SequenceStep;
                break;

            case BatchRenameRuleType.Date:
                var dateStr = DateTime.Now.ToString(rule.DateFormat);
                newNamePart = $"{namePart}_{dateStr}";
                break;

            case BatchRenameRuleType.CaseConversion:
                newNamePart = rule.CaseMode switch
                {
                    CaseConversionMode.Uppercase => namePart.ToUpper(),
                    CaseConversionMode.Lowercase => namePart.ToLower(),
                    CaseConversionMode.TitleCase => ToTitleCase(namePart),
                    _ => namePart
                };
                break;
        }

        return string.IsNullOrEmpty(newExtPart) ? newNamePart : newNamePart + newExtPart;
    }

    private static string ToTitleCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var chars = input.ToCharArray();
        bool newWord = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i]) || chars[i] == '_' || chars[i] == '-')
            {
                newWord = true;
            }
            else if (newWord)
            {
                chars[i] = char.ToUpper(chars[i]);
                newWord = false;
            }
            else
            {
                chars[i] = char.ToLower(chars[i]);
            }
        }
        return new string(chars);
    }

    public async Task<BatchRenameResult> ExecuteAsync(
        List<BatchRenamePreviewItem> previewItems,
        IProgress<BatchRenameProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchRenameResult { TotalCount = previewItems.Count };
        var affectedDirs = new HashSet<string>(StringComparer.Ordinal);
        var completed = 0;

        foreach (var item in previewItems)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!item.IsChanged || item.HasError || item.HasConflict)
            {
                result.SkippedCount++;
                completed++;
                progress?.Report(new BatchRenameProgress
                {
                    CompletedCount = completed,
                    TotalCount = previewItems.Count,
                    CurrentPath = item.OriginalPath
                });
                continue;
            }

            try
            {
                var oldPath = item.OriginalPath;
                var newPath = item.NewPath;

                await _fileService.RenameAsync(oldPath, item.NewName);

                // Update file index
                if (_fileIndexWriter != null)
                    await _fileIndexWriter.RenameEntryAsync(oldPath, newPath, item.NewName);

                // Update AI tags
                if (_aiTagService != null)
                    await _aiTagService.UpdateFilePathAsync(oldPath, newPath);

                if (_fileTagService != null)
                    await _fileTagService.UpdatePathAsync(oldPath, newPath, cancellationToken);

                // Update pinned folders
                if (_pinnedFolderService != null)
                {
                    var isDir = Directory.Exists(newPath);
                    if (isDir)
                        await _pinnedFolderService.UpdateFolderPathAsync(oldPath, newPath, item.NewName);
                }

                var dir = Path.GetDirectoryName(oldPath) ?? "";
                if (!string.IsNullOrEmpty(dir))
                    affectedDirs.Add(dir);

                result.SuccessCount++;
                result.SuccessfulItems.Add(item);
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                result.Errors.Add($"{item.OriginalName}: {ex.Message}");
                _logger?.LogError(ex, "Batch rename failed for {File}", item.OriginalName);
            }
            finally
            {
                completed++;
                progress?.Report(new BatchRenameProgress
                {
                    CompletedCount = completed,
                    TotalCount = previewItems.Count,
                    CurrentPath = item.OriginalPath
                });
            }
        }

        if (affectedDirs.Count > 0)
            _directoryChangeNotifier?.NotifyChanged(affectedDirs.ToArray(), null);

        return result;
    }
}
