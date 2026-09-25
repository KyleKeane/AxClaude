using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using AxClaude.Core.Updates;

namespace AxClaude;

/// <summary>
/// The app's side of FR-1.10: asks GitHub for the latest release, downloads its zip into
/// <c>%LOCALAPPDATA%\AxClaude\updates\&lt;version&gt;</c>, and hands over to that version's own install.ps1, which waits
/// for this process to end, installs over the current installation and starts the new AxClaude on the same folder.
/// The running executable is never overwritten while it runs (D25).
/// </summary>
internal static class Updater
{
    private static readonly HttpClient Http = CreateClient();

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AxClaude", "updates");

    /// <summary>What the installer did after this process ended, for a bug report.</summary>
    public static string LogPath { get; } = Path.Combine(Log.Directory, "update.log");

    /// <summary>The latest release, or null when none is published. Throws when GitHub cannot be reached.</summary>
    public static Task<ReleaseInfo?> CheckAsync(CancellationToken cancellation) => UpdateCheck.FetchLatestAsync(Http, cancellation);

    /// <summary>
    /// Downloads and extracts the release's zip and returns the folder that holds the new AxClaude.exe and install.ps1.
    /// A complete earlier download of the same version is reused.
    /// </summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken cancellation)
    {
        if (release.ZipUrl is null)
        {
            throw new InvalidOperationException("The release has no Windows zip attached.");
        }

        var version = release.Version.ToString(3);
        var folder = Path.Combine(Folder, version);
        if (IsComplete(folder))
        {
            return folder;
        }

        Directory.CreateDirectory(Folder);
        // Only the version being installed is kept: earlier downloads are about 45 MB each.
        foreach (var old in Directory.GetDirectories(Folder).Where(path => !string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (IOException)
            {
                // Still in use; the next update tries again.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var zip = Path.Combine(Folder, $"AxClaude-{version}{UpdateCheck.ZipSuffix}");
        using (var response = await Http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(zip);
            await response.Content.CopyToAsync(file, cancellation).ConfigureAwait(false);
        }

        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        ZipFile.ExtractToDirectory(zip, folder);
        File.Delete(zip);
        if (!IsComplete(folder))
        {
            throw new InvalidOperationException("The downloaded zip does not contain AxClaude.exe and install.ps1.");
        }

        return folder;
    }

    /// <summary>
    /// Starts the downloaded version's install.ps1 without a window. It waits for this process to end, installs, and
    /// starts the new AxClaude on <paramref name="projectFolder"/>, with <c>-- --continue</c> when asked, so that the
    /// conversation is picked up again. Its output goes to <see cref="LogPath"/>.
    /// </summary>
    public static void LaunchInstaller(string folder, string? projectFolder, bool continueConversation)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = folder,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(folder, "install.ps1"));
        start.ArgumentList.Add("-WaitForProcess");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("-LogFile");
        start.ArgumentList.Add(LogPath);
        if (projectFolder is not null)
        {
            start.ArgumentList.Add("-Start");
            start.ArgumentList.Add(projectFolder);
            if (continueConversation)
            {
                start.ArgumentList.Add("-ContinueConversation");
            }
        }

        Process.Start(start)?.Dispose();
        Log.Info($"Update installer started from {folder}; it installs when this process ends and logs to {LogPath}");
    }

    private static bool IsComplete(string folder) =>
        File.Exists(Path.Combine(folder, "AxClaude.exe")) && File.Exists(Path.Combine(folder, "install.ps1"));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AxClaude", Program.Version));
        return client;
    }
}
