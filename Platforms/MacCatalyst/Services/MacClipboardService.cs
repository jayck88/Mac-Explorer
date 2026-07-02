using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;
using MacExplorer.Models;
using MacExplorer.Services;

namespace MacExplorer.Platforms.MacCatalyst.Services;

public class MacClipboardService : IClipboardService
{
    private ClipboardEntry? _entry;

    public void CopyFiles(string[] paths)
    {
        SetClipboardEntry(paths, ClipboardOperation.Copy);
    }

    public void CutFiles(string[] paths)
    {
        SetClipboardEntry(paths, ClipboardOperation.Cut);
    }

    public async Task CopyTextAsync(string text)
    {
        if (OperatingSystem.IsMacOS())
        {
            var copied = await Dispatcher.UIThread.InvokeAsync(() => TryWriteTextToPasteboard(text));
            if (copied)
                return;
        }

        await CopyTextWithPbcopyAsync(text).ConfigureAwait(false);
    }

    private static bool TryWriteTextToPasteboard(string text)
    {
        try
        {
            dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 1);

            var pasteboardClass = objc_getClass("NSPasteboard");
            if (pasteboardClass == IntPtr.Zero)
                return false;

            var pasteboard = Send(pasteboardClass, "generalPasteboard");
            if (pasteboard == IntPtr.Zero)
                return false;

            Send(pasteboard, "clearContents");
            var value = CreateString(text);
            var textTypes = new[]
            {
                "public.utf8-plain-text",
                "public.plain-text",
                "public.text",
                "NSStringPboardType"
            };

            var copied = false;
            foreach (var type in textTypes)
                copied |= SendBool(pasteboard, "setString:forType:", value, CreateString(type));

            return copied;
        }
        catch
        {
            return false;
        }
    }

    private static async Task CopyTextWithPbcopyAsync(string text)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/pbcopy")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法访问系统剪贴板");

        await process.StandardInput.WriteAsync(text);
        process.StandardInput.Close();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error)
                ? "复制到系统剪贴板失败"
                : $"复制到系统剪贴板失败：{error}";
            throw new InvalidOperationException(message);
        }
    }

    public Task PasteFilesAsync(string targetDirectory) => Task.CompletedTask;

    public bool HasClipboardFiles => _entry is { IsEmpty: false };

    public ClipboardEntry? GetClipboardEntry() => _entry;

    public void Clear()
    {
        _entry = null;
    }

    private void SetClipboardEntry(string[] paths, ClipboardOperation operation)
    {
        var existingPaths = paths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        _entry = new ClipboardEntry
        {
            SourcePaths = existingPaths.ToList(),
            Operation = operation
        };

        if (existingPaths.Length > 0)
            _ = WriteToSystemPasteboardAsync(existingPaths);
    }

    private async Task WriteToSystemPasteboardAsync(IReadOnlyList<string> paths)
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            var items = paths
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .Select(path => new
                {
                    Path = path,
                    IsDirectory = Directory.Exists(path)
                })
                .ToArray();

            if (items.Length == 0) return;

            var script = $$"""
ObjC.import('AppKit');
ObjC.import('Foundation');

const items = {{JsonSerializer.Serialize(items)}};
const urls = $.NSMutableArray.array;
const filenames = $.NSMutableArray.array;
let firstUrlString = null;
for (const item of items) {
  const url = $.NSURL.fileURLWithPathIsDirectory(item.Path, item.IsDirectory);
  urls.addObject(url);
  filenames.addObject(item.Path);
  if (firstUrlString === null) {
    firstUrlString = ObjC.unwrap(url.absoluteString);
  }
}

const pasteboard = $.NSPasteboard.generalPasteboard;
pasteboard.clearContents;
const ok = pasteboard.writeObjects(urls);
if (!ok) {
  throw new Error('Failed to write file URLs to NSPasteboard');
}
pasteboard.setPropertyListForType(filenames, 'NSFilenamesPboardType');
const plainText = items.map(item => item.Path).join('\n');
pasteboard.setStringForType(plainText, 'public.utf8-plain-text');
pasteboard.setStringForType(plainText, 'public.plain-text');
pasteboard.setStringForType(plainText, 'public.text');
pasteboard.setStringForType(plainText, 'NSStringPboardType');
if (firstUrlString !== null) {
  pasteboard.setStringForType(firstUrlString, 'NSURLPboardType');
  pasteboard.setStringForType(firstUrlString, 'Apple URL pasteboard type');
}
""";
            var startInfo = new ProcessStartInfo("/usr/bin/osascript")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add("JavaScript");
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(script);

            using var process = Process.Start(startInfo);
            if (process != null)
                await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch
        {
            // The in-app clipboard remains valid even if macOS rejects pasteboard sync.
        }
    }

    private static IntPtr CreateString(string value)
    {
        var stringClass = objc_getClass("NSString");
        return stringClass == IntPtr.Zero
            ? IntPtr.Zero
            : objc_msgSend_string(stringClass, sel_registerName("stringWithUTF8String:"), value);
    }

    private static IntPtr Send(IntPtr receiver, string selector)
        => receiver == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(receiver, sel_registerName(selector));

    private static bool SendBool(IntPtr receiver, string selector, IntPtr value, IntPtr type)
        => receiver != IntPtr.Zero
            && value != IntPtr.Zero
            && type != IntPtr.Zero
            && objc_msgSend_bool_intptr_intptr(receiver, sel_registerName(selector), value, type) != 0;

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_string(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte objc_msgSend_bool_intptr_intptr(
        IntPtr receiver,
        IntPtr selector,
        IntPtr value,
        IntPtr type);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlopen(string path, int mode);
}
