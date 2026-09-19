using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AxClaude.Core.Updates;

/// <summary>A published release of the app on GitHub (FR-1.10): its version, its notes and where its zip is.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Notes, string PageUrl, string? ZipUrl, long ZipSize);

/// <summary>
/// Finds out whether a newer AxClaude has been released (FR-1.10, D25). A release is a GitHub release of the
/// repository with the zip that publish.ps1 builds attached. The parsing and the version comparison are pure and unit
/// tested; the app makes the one network call through <see cref="FetchLatestAsync"/>.
/// </summary>
public static class UpdateCheck
{
    public const string Repository = "KyleKeane/AxClaude";

    /// <summary>The end of the zip's file name, as publish.ps1 names it: <c>AxClaude-1.0.1-win-x64.zip</c>.</summary>
    public const string ZipSuffix = "-win-x64.zip";

    public static string ReleasesPage => $"https://github.com/{Repository}/releases";
    public static string LatestReleaseApi => $"https://api.github.com/repos/{Repository}/releases/latest";

    /// <summary>
    /// The version in a tag or version string: <c>v1.2.3</c>, <c>1.2.3</c> and <c>1.2.3.0</c> all give 1.2.3, <c>1.2</c>
    /// gives 1.2.0; anything else null.
    /// </summary>
    public static Version? ParseVersion(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        return Version.TryParse(trimmed, out var version) ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0)) : null;
    }

    /// <summary>True when the release is above the running version (major, minor, build).</summary>
    public static bool IsNewer(ReleaseInfo release, string currentVersion) =>
        ParseVersion(currentVersion) is { } current && release.Version > current;

    /// <summary>
    /// Reads GitHub's "latest release" JSON. Null when it is not a release with a version tag, or not JSON at all.
    /// The zip is the asset whose name ends with <see cref="ZipSuffix"/>; a release without one has no
    /// <see cref="ReleaseInfo.ZipUrl"/>.
    /// </summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var tag = GetString(root, "tag_name");
            if (tag is null || ParseVersion(tag) is not { } version)
            {
                return null;
            }

            string? zipUrl = null;
            long zipSize = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (GetString(asset, "name") is { } name && name.EndsWith(ZipSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        zipUrl = GetString(asset, "browser_download_url");
                        zipSize = asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0;
                        break;
                    }
                }
            }

            var notes = (GetString(root, "body") ?? string.Empty).Replace("\r\n", "\n").Trim();
            return new ReleaseInfo(version, tag, notes, GetString(root, "html_url") ?? ReleasesPage, zipUrl, zipSize);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks GitHub for the latest release. Null when the repository has none yet; throws when GitHub cannot be reached
    /// or answers with an error, so the caller can say why.
    /// </summary>
    public static async Task<ReleaseInfo?> FetchLatestAsync(HttpClient http, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
