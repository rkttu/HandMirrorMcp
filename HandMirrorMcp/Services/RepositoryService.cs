using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HandMirrorMcp.Services;

/// <summary>
/// Service for fetching GitHub/GitLab repository information
/// </summary>
public sealed class RepositoryService : IDisposable
{
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public RepositoryService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HandMirrorMcp/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Parses repository information from a URL.
    /// </summary>
    public static RepositoryInfo? ParseRepositoryUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();
            var pathParts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (pathParts.Length < 2)
                return null;

            var owner = pathParts[0];
            var repo = pathParts[1];

            // Remove .git extension
            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repo = repo[..^4];
            }

            RepositoryType type;
            if (host.Contains("github"))
            {
                type = RepositoryType.GitHub;
            }
            else if (host.Contains("gitlab"))
            {
                type = RepositoryType.GitLab;
            }
            else if (host.Contains("bitbucket"))
            {
                type = RepositoryType.Bitbucket;
            }
            else if (host.Contains("dev.azure.com") || host.Contains("visualstudio.com"))
            {
                type = RepositoryType.AzureDevOps;
            }
            else
            {
                return null;
            }

            return new RepositoryInfo
            {
                Type = type,
                Host = uri.Host,
                Owner = owner,
                Name = repo,
                Url = url
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets README content from a GitHub repository.
    /// </summary>
    public async Task<ReadmeResult?> GetGitHubReadmeAsync(
        string owner, string repo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get README info via GitHub API
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/readme";
            var response = await _httpClient.GetAsync(apiUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var readmeInfo = await response.Content.ReadFromJsonAsync<GitHubReadmeResponse>(cancellationToken: cancellationToken);

            if (readmeInfo == null)
                return null;

            // Get raw content
            string? content = null;
            if (!string.IsNullOrEmpty(readmeInfo.DownloadUrl))
            {
                var contentResponse = await _httpClient.GetAsync(readmeInfo.DownloadUrl, cancellationToken);
                if (contentResponse.IsSuccessStatusCode)
                {
                    content = await contentResponse.Content.ReadAsStringAsync(cancellationToken);
                }
            }

            return new ReadmeResult
            {
                FileName = readmeInfo.Name ?? "README.md",
                HtmlUrl = readmeInfo.HtmlUrl,
                Content = content,
                Size = readmeInfo.Size
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets README content from a GitLab repository.
    /// </summary>
    public async Task<ReadmeResult?> GetGitLabReadmeAsync(
        string host, string owner, string repo, CancellationToken cancellationToken = default)
    {
        try
        {
            var projectPath = Uri.EscapeDataString($"{owner}/{repo}");
            var apiUrl = $"https://{host}/api/v4/projects/{projectPath}/repository/files/README.md?ref=main";

            var response = await _httpClient.GetAsync(apiUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // If main branch not found, try master
                apiUrl = $"https://{host}/api/v4/projects/{projectPath}/repository/files/README.md?ref=master";
                response = await _httpClient.GetAsync(apiUrl, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;
            }

            var fileInfo = await response.Content.ReadFromJsonAsync<GitLabFileResponse>(cancellationToken: cancellationToken);

            if (fileInfo == null)
                return null;

            // Base64 decoding
            string? content = null;
            if (!string.IsNullOrEmpty(fileInfo.Content) && fileInfo.Encoding == "base64")
            {
                var bytes = Convert.FromBase64String(fileInfo.Content);
                content = System.Text.Encoding.UTF8.GetString(bytes);
            }

            return new ReadmeResult
            {
                FileName = fileInfo.FileName ?? "README.md",
                HtmlUrl = $"https://{host}/{owner}/{repo}/-/blob/{fileInfo.Ref ?? "main"}/README.md",
                Content = content,
                Size = fileInfo.Size
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets wiki information from a GitHub repository.
    /// </summary>
    public async Task<WikiResult?> GetGitHubWikiAsync(
        string owner, string repo, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check wiki existence via repository info
            var repoUrl = $"https://api.github.com/repos/{owner}/{repo}";
            var repoResponse = await _httpClient.GetAsync(repoUrl, cancellationToken);

            if (!repoResponse.IsSuccessStatusCode)
                return null;

            var repoInfo = await repoResponse.Content.ReadFromJsonAsync<GitHubRepoResponse>(cancellationToken: cancellationToken);

            if (repoInfo?.HasWiki != true)
            {
                return new WikiResult
                {
                    HasWiki = false,
                    WikiUrl = null,
                    Pages = []
                };
            }

            var wikiUrl = $"https://github.com/{owner}/{repo}/wiki";

            // Get wiki page list (GitHub API does not provide wiki page list directly)
            // Instead, need to scrape wiki homepage or fetch via git clone
            // Here we provide common wiki page URL patterns

            var pages = new List<WikiPage>
            {
                new WikiPage { Title = "Home", Url = wikiUrl },
            };

            // Try to extract page list from wiki sidebar
            try
            {
                var wikiResponse = await _httpClient.GetAsync(wikiUrl, cancellationToken);
                if (wikiResponse.IsSuccessStatusCode)
                {
                    var html = await wikiResponse.Content.ReadAsStringAsync(cancellationToken);
                    pages = ExtractWikiPagesFromHtml(html, owner, repo);
                }
            }
            catch
            {
                // If page list extraction fails, return only default Home
            }

            return new WikiResult
            {
                HasWiki = true,
                WikiUrl = wikiUrl,
                Pages = pages
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<WikiPage> ExtractWikiPagesFromHtml(string html, string owner, string repo)
    {
        var pages = new List<WikiPage>();
        var baseUrl = $"https://github.com/{owner}/{repo}/wiki";

        // Extract wiki links with simple pattern matching
        // Pattern: href="/owner/repo/wiki/PageName"
        var pattern = $"href=\"/{owner}/{repo}/wiki/";
        var index = 0;
        var foundPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add Home page
        pages.Add(new WikiPage { Title = "Home", Url = baseUrl });
        foundPages.Add("Home");

        while ((index = html.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var startIndex = index + pattern.Length;
            var endIndex = html.IndexOf('"', startIndex);

            if (endIndex > startIndex)
            {
                var pageName = html[startIndex..endIndex];

                // Decode URL-encoded characters
                pageName = Uri.UnescapeDataString(pageName);

                // Remove anchor
                var anchorIndex = pageName.IndexOf('#');
                if (anchorIndex >= 0)
                {
                    pageName = pageName[..anchorIndex];
                }

                // Remove query string
                var queryIndex = pageName.IndexOf('?');
                if (queryIndex >= 0)
                {
                    pageName = pageName[..queryIndex];
                }

                if (!string.IsNullOrWhiteSpace(pageName) && 
                    !foundPages.Contains(pageName) &&
                    !pageName.Contains('/') &&
                    !pageName.StartsWith("_"))
                {
                    foundPages.Add(pageName);
                    pages.Add(new WikiPage
                    {
                        Title = pageName.Replace("-", " "),
                        Url = $"{baseUrl}/{Uri.EscapeDataString(pageName)}"
                    });
                }
            }

            index = startIndex;
        }

        return pages;
    }

    /// <summary>
    /// Gets release list from a GitHub repository.
    /// </summary>
    public async Task<List<GitHubRelease>?> GetGitHubReleasesAsync(
        string owner, string repo, int maxResults = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page={maxResults}";
            var response = await _httpClient.GetAsync(apiUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var releases = await response.Content.ReadFromJsonAsync<List<GitHubReleaseResponse>>(cancellationToken: cancellationToken);

            if (releases == null || releases.Count == 0)
            {
                return [];
            }

            return releases.Select(r => new GitHubRelease
            {
                TagName = r.TagName ?? "",
                Name = r.Name ?? r.TagName ?? "",
                Body = r.Body,
                HtmlUrl = r.HtmlUrl ?? "",
                PublishedAt = r.PublishedAt,
                Prerelease = r.Prerelease,
                Draft = r.Draft
            }).ToList();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }

    // JSON response classes
    private sealed class GitHubReadmeResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    private sealed class GitHubRepoResponse
    {
        [JsonPropertyName("has_wiki")]
        public bool HasWiki { get; set; }

        [JsonPropertyName("default_branch")]
        public string? DefaultBranch { get; set; }
    }

    private sealed class GitLabFileResponse
    {
        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("encoding")]
        public string? Encoding { get; set; }

        [JsonPropertyName("ref")]
        public string? Ref { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
    }
}

public enum RepositoryType
{
    GitHub,
    GitLab,
    Bitbucket,
    AzureDevOps
}

public sealed class RepositoryInfo
{
    public required RepositoryType Type { get; init; }
    public required string Host { get; init; }
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string Url { get; init; }
}

public sealed class ReadmeResult
{
    public required string FileName { get; init; }
    public string? HtmlUrl { get; init; }
    public string? Content { get; init; }
    public long Size { get; init; }
}

public sealed class WikiResult
{
    public bool HasWiki { get; init; }
    public string? WikiUrl { get; init; }
    public required List<WikiPage> Pages { get; init; }
}

public sealed class WikiPage
{
    public required string Title { get; init; }
    public required string Url { get; init; }
}

public sealed class GitHubRelease
{
    public required string TagName { get; init; }
    public required string Name { get; init; }
    public string? Body { get; init; }
    public required string HtmlUrl { get; init; }
    public DateTime? PublishedAt { get; init; }
    public bool Prerelease { get; init; }
    public bool Draft { get; init; }
}

