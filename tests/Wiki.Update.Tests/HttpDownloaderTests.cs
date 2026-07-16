// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Http;

namespace Wiki.Update.Tests;

public class HttpDownloaderTests
{
    sealed class FakeHandler(byte[] body, bool declareLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new ByteArrayContent(body);
            if (!declareLength) content.Headers.ContentLength = null;   // force the "unknown length" path
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    static HttpUpdateDownloader Downloader(byte[] body, bool declareLength)
        => new(new HttpClient(new FakeHandler(body, declareLength)));

    [Fact] public async Task Returns_a_within_cap_body()
    {
        var got = await Downloader(new byte[500], declareLength: true).GetBytesAsync(new Uri("https://x/f"), 1000, null, default);
        Assert.Equal(500, got.Length);
    }

    [Fact] public async Task Rejects_an_oversized_body_declared_by_content_length()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Downloader(new byte[2000], declareLength: true).GetBytesAsync(new Uri("https://x/f"), 1000, null, default));
    }

    [Fact] public async Task Rejects_an_oversized_body_with_no_content_length()
    {
        // The dangerous case: a hostile mirror that omits Content-Length. The read-loop cap must still stop it.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Downloader(new byte[2000], declareLength: false).GetBytesAsync(new Uri("https://x/f"), 1000, null, default));
    }
}
