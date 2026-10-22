namespace GlassDock.App;

public interface IRenderBackend
{
    string Name { get; }
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);
    ValueTask DisposeAsync();
}
