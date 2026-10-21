using AoOniOnlineResDownloader;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;

public record ResourceVersion(int Id, string AppVersion, string IosRes, string AndroidRes)
{
    public override string ToString() => $"{AppVersion}    [{AndroidRes}]";
}

public static class Core
{
    public const string BaseUrl = "https://diz1jo2c4y7pn.cloudfront.net/prd/";
    public static string AddressablesUrl(string resVersion) => $"{BaseUrl}res/{resVersion}/Android/Addressables/";
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    static readonly Regex BundleRx = new(@"[A-Za-z0-9_.\-]+_assets_all_[0-9a-f]{32}\.bundle", RegexOptions.Compiled);

    public static async Task<List<ResourceVersion>> FetchVersionsAsync(Action<string> log)
    {
        string url = BaseUrl + "version.json.encrypted.zip";
        log(Loc.Tf("Log_Downloading", url));
        byte[] zipBytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var entry = zip.Entries.First(e => !string.IsNullOrEmpty(e.Name));
        using var ms = new MemoryStream();
        using (var s = entry.Open()) await s.CopyToAsync(ms).ConfigureAwait(false);
        byte[] data = ms.ToArray();
        for (int i = 0; i < data.Length; i++) data[i] ^= 0x31;
        string json = DecodeUtf8(data);
        return ParseVersions(json);
    }

    public static List<ResourceVersion> ParseVersions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var rr = doc.RootElement.GetProperty("gameMaster").GetProperty("released_resource");
        var list = new List<ResourceVersion>();
        int n = 0;
        foreach (var e in rr.EnumerateArray())
        {
            if (n++ < 2) continue;
            list.Add(new ResourceVersion(
                e.GetProperty("key_0").GetInt32(),
                Str(e.GetProperty("key_1")),
                Str(e.GetProperty("key_2")),
                Str(e.GetProperty("key_3"))));
        }
        return list;
    }

    static string Str(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText();

    static string DecodeUtf8(byte[] bytes)
    {
        int offset = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
        var strict = new UTF8Encoding(false, true);
        try { return strict.GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException) { }
        return new UTF8Encoding(false, false).GetString(Deobf(bytes, offset));
    }

    static byte[] Deobf(byte[] src, int start)
    {
        var dst = new byte[src.Length - start];
        int w = 0, i = start;
        while (i < src.Length)
        {
            byte b = src[i];
            if (b < 0x80) { dst[w++] = b; i++; continue; }
            int lead = b ^ 0x31;
            int len = (lead >= 0xC2 && lead <= 0xDF) ? 2
                    : (lead >= 0xE0 && lead <= 0xEF) ? 3
                    : (lead >= 0xF0 && lead <= 0xF4) ? 4 : 0;
            if (len == 0 || i + len > src.Length) { dst[w++] = b; i++; continue; }
            for (int k = 0; k < len - 1; k++) dst[w++] = (byte)(src[i + k] ^ 0x31);
            dst[w++] = src[i + len - 1];
            i += len;
        }
        Array.Resize(ref dst, w);
        return dst;
    }

    public static async Task<byte[]> FetchCatalogAsync(string resVersion, string saveDir)
    {
        string baseUrl = AddressablesUrl(resVersion);
        byte[] bin = await Http.GetByteArrayAsync(baseUrl + "catalog_0.bin").ConfigureAwait(false);
        Directory.CreateDirectory(saveDir);
        await File.WriteAllBytesAsync(Path.Combine(saveDir, "catalog_0.bin"), bin).ConfigureAwait(false);
        try
        {
            byte[] hash = await Http.GetByteArrayAsync(baseUrl + "catalog_0.hash").ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(saveDir, "catalog_0.hash"), hash).ConfigureAwait(false);
        }
        catch { }
        return bin;
    }

    public static List<string> ParseBundleNames(byte[] catalog)
    {
        string text = Encoding.Latin1.GetString(catalog);
        return BundleRx.Matches(text).Select(m => m.Value).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    public static async Task<(int ok, int skipped, int failed)> DownloadBundlesAsync(
        IReadOnlyList<string> names, string baseUrl, IReadOnlyList<string> fallbackBaseUrls, string outDir, int parallel,
        IProgress<int> progress, Action<string> log, CancellationToken ct)
    {
        Directory.CreateDirectory(outDir);
        int ok = 0, skipped = 0, failed = 0, done = 0;
        await Parallel.ForEachAsync(names,
            new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = ct },
            async (name, token) =>
            {
                string dest = Path.Combine(outDir, name);
                try
                {
                    if (File.Exists(dest)) { Interlocked.Increment(ref skipped); return; }
                    HttpResponseMessage? resp = null;
                    string? usedBase = null;
                    foreach (var b in new[] { baseUrl }.Concat(fallbackBaseUrls))
                    {
                        var r = await Http.GetAsync(b + name, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                        if (r.IsSuccessStatusCode) { resp = r; usedBase = b; break; }
                        r.Dispose();
                    }
                    if (resp == null)
                    {
                        Interlocked.Increment(ref failed);
                        log(Loc.Tf("Log_FailAllVersions", name));
                        return;
                    }
                    if (usedBase != baseUrl) log(Loc.Tf("Log_FromOldDir", name, usedBase));
                    using (resp)
                    {
                        string tmp = dest + ".tmp";
                        await using (var src = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                        await using (var fs = File.Create(tmp))
                            await src.CopyToAsync(fs, token).ConfigureAwait(false);
                        File.Move(tmp, dest, true);
                    }
                    Interlocked.Increment(ref ok);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    log(Loc.Tf("Log_Error", name, ex.Message));
                }
                finally
                {
                    progress.Report(Interlocked.Increment(ref done));
                }
            }).ConfigureAwait(false);
        return (ok, skipped, failed);
    }
}