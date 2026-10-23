using System.Text.Json;
using GlassDock.App;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GlassDock.PoC.WinUI;

internal sealed class CompositionBackdropBlurBrush(float blurAmountDip, string probeRole) : XamlCompositionBrushBase
{
    protected override void OnConnected()
    {
        base.OnConnected();
        var stage = "GetCompositor";
        try
        {
            var compositor = CompositionTarget.GetCompositorForCurrentThread();
            stage = "CreateGaussianBlurEffectFactory";
            var effect = new GaussianBlurEffect
            {
                Name = "M0BackdropBlur",
                BlurAmount = blurAmountDip,
                BorderMode = EffectBorderMode.Hard,
                Optimization = EffectOptimization.Balanced,
                Source = new CompositionEffectSourceParameter("backdrop")
            };
            var effectFactory = compositor.CreateEffectFactory(effect);
            stage = "CreateCompositionBackdropBrush";
            var backdrop = compositor.CreateBackdropBrush();
            stage = "ConnectEffectBrush";
            var effectBrush = effectFactory.CreateBrush();
            effectBrush.SetSourceParameter("backdrop", backdrop);
            CompositionBrush = effectBrush;
            FallbackColor = Color.FromArgb(255, 255, 0, 255);
            WriteStatus(new
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                Role = probeRole,
                Status = "EffectBrushCreated",
                Stage = "Complete",
                Source = "Microsoft.UI.Composition.CompositionBackdropBrush",
                SourceFactory = "Compositor.CreateBackdropBrush()",
                SamplingScope = "Composition backdrop; Microsoft documents application content or pixels behind a desktop app window. Visual capture is required to establish which source was actually sampled.",
                Effect = "Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect",
                BlurAmountDip = blurAmountDip,
                EffectTarget = "XamlCompositionBrushBase applied as XAML UIElement.Background",
                FallbackColor = "#FFFF00FF",
                FallbackActivated = false,
                VisualSamplingResult = "NOT_MEASURED_BY_BRUSH_CREATION",
                HResult = "0x00000000"
            });
        }
        catch (Exception exception)
        {
            FallbackColor = Color.FromArgb(255, 255, 0, 255);
            WriteStatus(new
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                Role = probeRole,
                Status = "Failed",
                Stage = stage,
                Source = "Microsoft.UI.Composition.CompositionBackdropBrush",
                SourceFactory = "Compositor.CreateBackdropBrush()",
                SamplingScope = "Composition backdrop; Microsoft documents application content or pixels behind a desktop app window. Visual capture is required to establish which source was actually sampled.",
                Effect = "Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect",
                BlurAmountDip = blurAmountDip,
                EffectTarget = "XamlCompositionBrushBase applied as XAML UIElement.Background",
                FallbackColor = "#FFFF00FF",
                FallbackActivated = true,
                VisualSamplingResult = "NOT_MEASURED_BY_BRUSH_CREATION",
                HResult = $"0x{exception.HResult:X8}",
                ExceptionType = exception.GetType().FullName,
                Exception = exception.ToString()
            });
            DockLogger.Error($"Composition backdrop blur probe failed at {stage}.", exception);
        }
    }

    protected override void OnDisconnected()
    {
        CompositionBrush?.Dispose();
        CompositionBrush = null;
        base.OnDisconnected();
    }

    private static void WriteStatus(object value)
    {
        var root = Environment.GetEnvironmentVariable("GLASSDOCK_M0_COMPOSITION_STATUS_ROOT");
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || !root.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "composition-brush-status.json"),
                JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            DockLogger.Error("Could not write the local M0 composition probe status file.", exception);
        }
    }
}
