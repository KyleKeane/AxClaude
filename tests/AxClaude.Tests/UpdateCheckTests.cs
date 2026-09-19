using AxClaude.Core.Updates;

namespace AxClaude.Tests;

public class UpdateCheckTests
{
    // The shape GitHub returns for /repos/{owner}/{repo}/releases/latest, cut down to the fields the app reads.
    private const string Release = """
        {
          "tag_name": "v1.2.3",
          "name": "AxClaude 1.2.3",
          "draft": false,
          "prerelease": false,
          "html_url": "https://github.com/KyleKeane/AxClaude/releases/tag/v1.2.3",
          "body": "## 1.2.3 - 2026-10-01\r\n\r\n- Reads faster.\r\n",
          "assets": [
            { "name": "checksums.txt", "size": 120, "browser_download_url": "https://example.invalid/checksums.txt" },
            { "name": "AxClaude-1.2.3-win-x64.zip", "size": 43500000, "browser_download_url": "https://github.com/KyleKeane/AxClaude/releases/download/v1.2.3/AxClaude-1.2.3-win-x64.zip" }
          ]
        }
        """;

    [Fact]
    public void The_latest_release_gives_its_version_notes_page_and_zip()
    {
        var release = UpdateCheck.Parse(Release);

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 3), release.Version);
        Assert.Equal("v1.2.3", release.Tag);
        Assert.Equal("## 1.2.3 - 2026-10-01\n\n- Reads faster.", release.Notes);
        Assert.Equal("https://github.com/KyleKeane/AxClaude/releases/tag/v1.2.3", release.PageUrl);
        Assert.Equal("https://github.com/KyleKeane/AxClaude/releases/download/v1.2.3/AxClaude-1.2.3-win-x64.zip", release.ZipUrl);
        Assert.Equal(43500000, release.ZipSize);
    }

    [Theory]
    [InlineData("1.2.2", true)]
    [InlineData("1.2.3", false)]
    [InlineData("1.3.0", false)]
    [InlineData("2.0.0", false)]
    [InlineData("0.9.9", true)]
    [InlineData("not a version", false)]
    public void A_release_is_newer_only_when_its_version_is_above_the_running_one(string running, bool newer)
    {
        var release = UpdateCheck.Parse(Release)!;

        Assert.Equal(newer, UpdateCheck.IsNewer(release, running));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V1.2.3.4", "1.2.3")]
    [InlineData(" 1.2 ", "1.2.0")]
    public void Tags_and_version_strings_give_a_three_part_version(string text, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateCheck.ParseVersion(text));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1.2.3-beta")]
    public void Anything_that_is_not_a_version_gives_null(string text)
    {
        Assert.Null(UpdateCheck.ParseVersion(text));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "tag_name": "latest" }""")]
    [InlineData("""{ "tag_name": 123 }""")]
    public void Anything_that_is_not_a_release_with_a_version_tag_gives_null(string json)
    {
        Assert.Null(UpdateCheck.Parse(json));
    }

    [Fact]
    public void A_release_without_the_windows_zip_has_no_zip_url_and_the_page_falls_back_to_the_releases_page()
    {
        var release = UpdateCheck.Parse("""{ "tag_name": "v2.0.0", "assets": [ { "name": "source.tar.gz", "browser_download_url": "https://example.invalid/s.tgz" } ] }""");

        Assert.NotNull(release);
        Assert.Equal(new Version(2, 0, 0), release.Version);
        Assert.Null(release.ZipUrl);
        Assert.Equal(0, release.ZipSize);
        Assert.Equal(string.Empty, release.Notes);
        Assert.Equal(UpdateCheck.ReleasesPage, release.PageUrl);
    }
}
