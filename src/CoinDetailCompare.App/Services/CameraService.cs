using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace CoinDetailCompare.App.Services;

/// <summary>Camera device abstraction (webcam capture + frame streaming).</summary>
public interface ICameraDevice : IDisposable
{
    string? Description { get; }
    bool IsOpen { get; }
    bool Open();
    bool TryGetFrame(out BitmapSource? frame);
    void Close();
}

/// <summary>
/// Real webcam capture via OpenCV (VideoCapture).
/// On Windows OpenCvSharp uses DShow/MSMF; device 0 is the default camera.
/// </summary>
public class OpenCvCameraDevice : ICameraDevice
{
    private readonly int _deviceIndex;
    private VideoCapture? _capture;

    public string? Description { get; private set; }
    public bool IsOpen { get; private set; }

    public OpenCvCameraDevice(int deviceIndex = 0)
    {
        _deviceIndex = deviceIndex;
    }

    /// <summary>
    /// Finds the first usable camera index in 0..(maxIndex-1), or -1 if none.
    /// </summary>
    public static int DetectFirstCamera(int maxIndex = 4)
    {
        for (int i = 0; i < maxIndex; i++)
        {
            using var cap = new VideoCapture(i);
            if (cap.IsOpened())
            {
                if (cap.Read(new Mat()))
                    return i;
            }
        }
        return -1;
    }

    public bool Open()
    {
        if (IsOpen)
            return true;
        _capture = new VideoCapture(_deviceIndex);
        if (!_capture.IsOpened())
        {
            _capture.Dispose();
            _capture = null;
            return false;
        }
        // Reasonable defaults for a desktop webcam.
        _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
        _capture.Set(VideoCaptureProperties.FrameHeight, 1080);
        _capture.Set(VideoCaptureProperties.Fps, 30);
        Description = $"Camera #{_deviceIndex}";
        IsOpen = true;
        return true;
    }

    /// <summary>
    /// Returns the full camera frame (no cropping — the viewfinder shows
    /// its center via stretch, and capture always saves the whole frame).
    /// </summary>
    public bool TryGetFrame(out BitmapSource? frame)
    {
        frame = null;
        if (_capture is null || !_capture.IsOpened())
            return false;

        using var mat = new Mat();
        if (!_capture.Read(mat) || mat.Empty())
            return false;

        try
        {
            // Direct pixel hand-off (temporary replacement for the PNG
            // round-trip, which throttled the ~25 fps preview): BGR -> BGRA
            // copy straight into a frozen BitmapSource, no encode/decode.
            int w = mat.Cols, h = mat.Rows;
            using var bgra = new Mat();
            var t = mat.Type();
            if (t == MatType.CV_8UC3)
                Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
            else if (t == MatType.CV_8UC4)
                mat.CopyTo(bgra);
            else
                throw new InvalidOperationException($"Unexpected mat type: {t}");

            int stride = bgra.Cols * 4;
            var pixels = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
            var direct = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            direct.Freeze(); // safe across threads
            frame = direct;
            return true;
        }
        catch
        {
            // Fallback: PNG bytes -> BitmapImage (always works).
            Cv2.ImEncode(".png", mat, out var buf);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(buf);
            bmp.EndInit();
            bmp.Freeze();
            frame = bmp;
            return true;
        }
    }

    public void Close()
    {
        _capture?.Dispose();
        _capture = null;
        IsOpen = false;
    }

    public void Dispose() => Close();
}
