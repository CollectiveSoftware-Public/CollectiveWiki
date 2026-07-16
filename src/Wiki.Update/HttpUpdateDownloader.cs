// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net.Http;

namespace Wiki.Update;

public sealed class HttpUpdateDownloader(HttpClient http) : IUpdateDownloader
{
    public async Task<byte[]> GetBytesAsync(Uri url, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        using var mem = new MemoryStream();
        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            mem.Write(buf, 0, n);
            read += n;
            if (total is > 0) progress?.Report((double)read / total.Value);
        }
        return mem.ToArray();
    }
}
