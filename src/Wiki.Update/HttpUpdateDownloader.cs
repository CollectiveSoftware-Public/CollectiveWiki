// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net.Http;

namespace Wiki.Update;

public sealed class HttpUpdateDownloader(HttpClient http) : IUpdateDownloader
{
    public async Task<byte[]> GetBytesAsync(Uri url, long maxBytes, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength;
        if (total is > 0 && total > maxBytes)                       // reject early when the size is declared
            throw new InvalidOperationException($"response declares {total} bytes, over the {maxBytes}-byte cap");

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        using var mem = new MemoryStream();
        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            read += n;
            if (read > maxBytes)                                     // hard cap: guards a lying/absent Content-Length
                throw new InvalidOperationException($"response exceeded the {maxBytes}-byte cap");
            mem.Write(buf, 0, n);
            if (total is > 0) progress?.Report((double)read / total.Value);
        }
        return mem.ToArray();
    }
}
