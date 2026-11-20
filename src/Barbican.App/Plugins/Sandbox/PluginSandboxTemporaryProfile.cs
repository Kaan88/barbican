#if WINDOWS
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using FileAttributes = System.IO.FileAttributes;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Owns one browser profile folder from reservation through browser shutdown.</summary>
internal sealed partial class PluginSandboxTemporaryProfile(string folder) : IAsyncDisposable
{
    private readonly TaskCompletionSource _processExited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _reserved;
    private bool _browserAttempted;
    private bool _browserMayBeOpen;
    public string Folder { get; } = Path.IsPathFullyQualified(folder)
        ? Path.GetFullPath(folder) : throw new ArgumentException("A plugin browser profile needs an absolute path.", nameof(folder));

    public void Reserve()
    {
        var parent = Path.GetDirectoryName(Folder)!;
        Directory.CreateDirectory(parent);
        RequireProfileParent(parent);
        if (!CreateDirectory(Folder, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not reserve a new plugin browser data folder.");
        RequireOrdinaryDirectory(Folder);
        _reserved = true;
    }

    public void MarkBrowserStarting(CoreWebView2Environment environment)
    {
        environment.BrowserProcessExited += OnProcessExited;
        _browserAttempted = true;
        _browserMayBeOpen = true;
    }

    public void MarkBrowserClosed() => _browserMayBeOpen = false;

    private void OnProcessExited(CoreWebView2Environment sender, CoreWebView2BrowserProcessExitedEventArgs args)
    {
        sender.BrowserProcessExited -= OnProcessExited;
        _processExited.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_reserved || _browserMayBeOpen) return;
        if (_browserAttempted)
        {
            try { await _processExited.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { return; } // Retain the folder if WebView2 does not confirm exit.
        }
        await Task.Run(DeleteAsync).ConfigureAwait(false);
    }

    private async Task DeleteAsync()
    {
        for (var retry = 0; ; retry++)
        {
            try
            {
                RequireProfileParent(Path.GetDirectoryName(Folder)!);
                RequireOrdinaryDirectory(Folder);
                // Delete only the reserved instance directory. Directory.Delete unlinks nested junctions.
                Directory.Delete(Folder, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception error) when (retry < 20 && error is IOException or UnauthorizedAccessException)
            {
                // WebView2 can retain browser files until its process finishes closing.
                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            }
        }
    }

    private static void RequireProfileParent(string parent)
    {
        RequireOrdinaryDirectory(Path.GetDirectoryName(parent)!);
        RequireOrdinaryDirectory(parent);
    }

    private static void RequireOrdinaryDirectory(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new InvalidOperationException("The plugin browser data path must be an ordinary directory.");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectory(string name, IntPtr security);
}
#endif
