using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Checks GitHub for the latest official PKHeX release and compares it against the
/// PKHeX.Core version this app was built with.
/// </summary>
public static class UpdateCheckService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/kwsch/PKHeX/releases/latest";

    // One client for the process: the check runs once per launch, and sharing the handler
    // is the documented way to use HttpClient regardless.
    private static readonly HttpClient Http = CreateClient();

    public sealed record UpdateInfo(string RemoteTag, DateOnly RemoteDate, string LocalVersion, bool IsBehind);

    /// <summary>
    /// Returns update info, or null if the check failed (offline, rate-limited, parse error).
    /// Never throws.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString();
            var publishedAt = root.GetProperty("published_at").GetDateTimeOffset();
            if (string.IsNullOrEmpty(tag))
                return null;

            var local = EngineVersion;
            var localText = $"{local.Major:00}.{local.Minor:00}.{local.Build:00}";
            var isBehind = CompareTagToLocal(tag, local) > 0;
            return new UpdateInfo(tag, DateOnly.FromDateTime(publishedAt.UtcDateTime), localText, isBehind);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null; // offline, rate limited, or the schema changed — stay quiet
        }
    }

    /// <summary>The PKHeX.Core version compiled into this build.</summary>
    public static Version EngineVersion =>
        typeof(SaveUtil).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>This app's own version, for the User-Agent and the About box.</summary>
    public static Version AppVersion =>
        typeof(UpdateCheckService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PKHeX-Mac", AppVersion.ToString(3)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>
    /// PKHeX tags are date-based (yy.MM.dd, e.g. "25.07.07"). Returns &gt;0 if remote is newer.
    /// </summary>
    internal static int CompareTagToLocal(string tag, Version local)
    {
        var parts = tag.TrimStart('v').Split('.');
        if (parts.Length < 3
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || !int.TryParse(parts[2], out var build))
        {
            return 0; // unrecognized tag scheme — don't nag
        }
        var remote = new Version(major, minor, build);
        return remote.CompareTo(new Version(local.Major, local.Minor, local.Build));
    }
}
