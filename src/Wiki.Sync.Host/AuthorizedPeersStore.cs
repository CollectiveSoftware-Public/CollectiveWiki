// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Wiki.Sync;

namespace Wiki.Sync.Host;

/// <summary>Persists the owner-signed authorized-peers roster to `.cwiki/sync/peers.json` so collaborators
/// survive a restart. Round-trips through <see cref="AuthorizedPeersList"/>'s public ctor with the signature
/// stored verbatim, so the reload still verifies against the pinned owner id. Not sealed — the roster is
/// public (it is what the owner distributes to every peer).</summary>
public sealed class AuthorizedPeersStore(ISyncStore store, string name = "peers.json")
{
    private sealed record PeerDto(string DeviceId, string PublicKey, int Role, string Name, string Email);
    private sealed record ListDto(string OwnerDeviceId, List<PeerDto> Peers, string Signature);

    public void Save(AuthorizedPeersList list)
    {
        var dto = new ListDto(
            list.OwnerDeviceId,
            list.Peers.Select(p => new PeerDto(
                p.DeviceId, Convert.ToBase64String(p.PublicKey), (int)p.Role, p.Name, p.Email)).ToList(),
            Convert.ToBase64String(list.Signature));
        store.WriteBytes(name, JsonSerializer.SerializeToUtf8Bytes(dto));
    }

    public AuthorizedPeersList? Load()
    {
        var blob = store.ReadBytes(name);
        if (blob is null) return null;
        var dto = JsonSerializer.Deserialize<ListDto>(blob);
        if (dto is null) return null;
        var peers = dto.Peers
            .Select(p => new AuthorizedPeer(
                p.DeviceId, Convert.FromBase64String(p.PublicKey), (PeerRole)p.Role, p.Name, p.Email))
            .ToList();
        return new AuthorizedPeersList(dto.OwnerDeviceId, peers, Convert.FromBase64String(dto.Signature));
    }
}
