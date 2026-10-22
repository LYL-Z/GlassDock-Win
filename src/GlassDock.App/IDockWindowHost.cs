using GlassDock.Core;

namespace GlassDock.App;

public interface IDockWindowHost
{
    ValueTask ShowAsync(CancellationToken cancellationToken = default);
    void MoveTo(PixelRect bounds);
    void RequestExit();
}
