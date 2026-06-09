using RiftServe64.Sdk.Protocol;

public interface IRift64MenuExample
{
    char Key { get; }
    string MenuLabel { get; }
    Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken);
}
