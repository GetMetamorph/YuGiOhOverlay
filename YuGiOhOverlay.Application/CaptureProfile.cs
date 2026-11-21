using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Application;

// Keep the Hand property name for compatibility with existing assistant.json files.
// For a field profile it describes the allied field ROI, not the hand.
public sealed record CaptureProfile(NormalizedRect Hand, double AspectRatio, uint Dpi)
{
    public void Validate()
    {
        if (!Hand.IsValid || !double.IsFinite(AspectRatio) || AspectRatio <= 0 || Dpi == 0)
            throw new ArgumentException("Zone de calibration invalide.");
    }
}
