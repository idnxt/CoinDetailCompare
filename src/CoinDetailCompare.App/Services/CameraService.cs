using System.Windows.Media.Imaging;

namespace CoinDetailCompare.App.Services;

/// <summary>Camera device abstraction. Real capture is bound in Phase 1.</summary>
public interface ICameraDevice
{
    string? Description { get; }
    bool IsOpen { get; }
    bool Open();
    bool TryGetFrame(out BitmapSource? frame);
    void Close();
}

/// <summary>
/// Placeholder device so the app builds and runs without a capture backend.
/// Phase 1: replace/extend with OpenCvSharp VideoCapture or Windows MediaCapture,
/// streaming frames into the same interface.
/// </summary>
public class CameraDeviceStub : ICameraDevice
{
    public string? Description { get; private set; }
    public bool IsOpen { get; private set; }

    public bool Open()
    {
        // TODO(Phase 1): open a real webcam and start streaming.
        Description = "stub (no camera)";
        IsOpen = true;
        return true;
    }

    public bool TryGetFrame(out BitmapSource? frame)
    {
        // TODO(Phase 1): grab the latest frame from the streaming device.
        frame = null;
        return false;
    }

    public void Close()
    {
        IsOpen = false;
    }
}
