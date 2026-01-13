using System.Net.Http.Headers;
using System.Text.Json;
using Installer.Models;

namespace Installer.Services;

public class GitHubReleaseService : IDisposable
{
    private const string RepoOwner = "AccessMods";
    private const string RepoName = "PhoenixWrightTrilogy";
    private const string UserAgent = "PWAATAccessibilityInstaller/1.2.0";

    private readonly HttpClient _httpClient;

    public GitHubReleaseService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );
    }

    /// <summary>
    /// Gets the latest release from GitHub.
    /// </summary>
    /// <param name="includePrerelease">If true, includes prerelease versions.</param>
    public async Task<GitHubRelease?> GetLatestReleaseAsync(bool includePrerelease = false)
    {
        try
        {
            if (includePrerelease)
            {
                // Fetch all releases and return the first one (most recent)
                var url =
                    $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=1";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json);
                return releases?.FirstOrDefault();
            }
            else
            {
                // Use the /latest endpoint which excludes prereleases
                var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<GitHubRelease>(json);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new Exception(
                $"Failed to fetch release information from GitHub: {ex.Message}",
                ex
            );
        }
    }

    /// <summary>
    /// Downloads a release asset to the specified path.
    /// </summary>
    public async Task DownloadAssetAsync(
        GitHubAsset asset,
        string destinationPath,
        Action<int>? progressCallback = null
    )
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                asset.BrowserDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead
            );
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
            var downloadedBytes = 0L;
            var lastReportedProgress = -1;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                8192,
                true
            );

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;

                if (totalBytes > 0 && progressCallback != null)
                {
                    var progress = (int)(downloadedBytes * 100 / totalBytes);
                    if (progress != lastReportedProgress)
                    {
                        lastReportedProgress = progress;
                        progressCallback(progress);
                    }
                }
            }
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Failed to download release asset: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Finds the mod zip asset in a release.
    /// </summary>
    public GitHubAsset? FindModAsset(GitHubRelease release)
    {
        // First try to find a file with the standard naming convention
        var asset = release.Assets.FirstOrDefault(a =>
            a.Name.StartsWith(
                "PhoenixWrightTrilogyAccessibilityMod",
                StringComparison.OrdinalIgnoreCase
            ) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        );

        // Fall back to any .zip file (for prereleases with simpler naming)
        asset ??= release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        );

        return asset;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
