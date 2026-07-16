// SPDX-License-Identifier: GPL-3.0-or-later
namespace Wiki.Update;

public interface IUpdateDownloader
{
    Task<byte[]> GetBytesAsync(Uri url, IProgress<double>? progress, CancellationToken ct);
}
