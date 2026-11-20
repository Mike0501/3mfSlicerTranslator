using System.Text.Json;

namespace Translator;

internal static class AppUpdates
{
    private const string Repository = "Mike0501/3mfSlicerTranslator";
    private static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{Repository}/releases/latest");
    internal static readonly Uri ReleasesPage = new($"https://github.com/{Repository}/releases/latest");
    internal static Version InstalledVersion { get; } = DisplayVersion(typeof(AppUpdates).Assembly.GetName().Version!);
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(12),
        MaxResponseContentBufferSize = 512 * 1024
    };

    internal static async Task<Version> CheckLatestVersion(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.ParseAdd($"3mfSlicerTranslator/{InstalledVersion}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument release = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ReadReleaseVersion(release.RootElement);
    }

    internal static Version ReadReleaseVersion(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            !release.TryGetProperty("tag_name", out JsonElement name) ||
            name.ValueKind != JsonValueKind.String || name.GetString() is not string tag ||
            !Version.TryParse(tag.StartsWith('v') ? tag[1..] : tag, out Version? version) || version.Revision >= 0)
            throw new InvalidDataException("GitHub returned an invalid release version.");

        return DisplayVersion(version);
    }

    private static Version DisplayVersion(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
