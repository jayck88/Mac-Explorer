using MacExplorer.Models;
using MacExplorer.ViewModels;
using AppIcons = MacExplorer.Assets.Icons;

namespace MacExplorer.Services.Impl;

/// <summary>Provides application commands (new folder, batch rename, compress, etc.) for the command palette.</summary>
public class CommandOmniboxProvider : IOmniboxProvider
{
    public string Name => "Commands";
    public int Priority => 5;

    private static readonly (string Title, string Subtitle, string IconData, string Keywords, Func<FileListViewModel, Task> Action)[] Commands =
    [
        ("新建文件夹", "在当前目录创建新文件夹", AppIcons.NewFolder, "新建文件夹 newfolder create",
            async vm => await vm.FileOps.CreateNewFolderAsync(vm.CurrentPath, vm.Entries.ToList(),
                setStatus: msg => vm.StatusText = msg,
                refreshCallback: async name => await vm.RefreshAfterCreateAsync(name))),

        ("批量重命名", "批量重命名选中文件", AppIcons.Rename, "批量重命名 batchrename rename",
            vm => { vm.RaiseRequestBatchRename(); return Task.CompletedTask; }),

        ("压缩", "压缩选中项目", AppIcons.Compress, "压缩 compress archive zip",
            vm => { vm.ShowCompressDialog(); return Task.CompletedTask; }),

        ("显示信息", "打开信息面板", AppIcons.Info, "信息 info inspector panel",
            vm => { vm.IsInfoPanelVisible = true; return Task.CompletedTask; }),

        ("连接远程服务器", "连接 SFTP 远程服务器", AppIcons.ExternalDrive, "远程 sftp connect remote server",
            vm => { vm.RaiseRequestRemoteConnection(); return Task.CompletedTask; }),

        ("打开任务中心", "显示后台任务面板", AppIcons.Settings, "任务 task center panel background",
            vm => { vm.RaiseRequestShowTaskPanel(); return Task.CompletedTask; }),
    ];

    public Task<IReadOnlyList<OmniboxSuggestion>> GetSuggestionsAsync(
        FileListViewModel viewModel,
        string input,
        CancellationToken cancellationToken)
    {
        var value = input?.Trim() ?? string.Empty;

        var results = new List<OmniboxSuggestion>();
        foreach (var cmd in Commands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matches = value.Length == 0
                || cmd.Title.Contains(value, StringComparison.OrdinalIgnoreCase)
                || cmd.Keywords.Contains(value, StringComparison.OrdinalIgnoreCase);

            if (!matches) continue;

            results.Add(new OmniboxSuggestion(
                OmniboxSuggestionKind.Command,
                cmd.Title,
                cmd.Subtitle,
                cmd.Title,
                cmd.IconData,
                "#8B5CF6",
                ExecuteAction: () => cmd.Action(viewModel)));
        }

        return Task.FromResult<IReadOnlyList<OmniboxSuggestion>>(results);
    }
}
