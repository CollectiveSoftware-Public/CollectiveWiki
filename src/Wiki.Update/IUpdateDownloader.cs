// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update;

public interface IUpdateDownloader
{
    /// <summary>Fetch a resource fully into memory, hard-capped at <paramref name="maxBytes"/>. An
    /// implementation MUST throw rather than buffer beyond the cap — a hostile mirror or a lying/absent
    /// Content-Length must never be able to exhaust memory.</summary>
    Task<byte[]> GetBytesAsync(Uri url, long maxBytes, IProgress<double>? progress, CancellationToken ct);
}
