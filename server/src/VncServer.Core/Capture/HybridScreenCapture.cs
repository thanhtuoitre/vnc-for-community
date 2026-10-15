using VncServer.Core.Codecs;
using VncServer.Core.Logging;

namespace VncServer.Core.Capture;

public sealed class HybridScreenCapture : IScreenCapture
{
    private readonly ScreenCapture _gdiCapture;
    private readonly DxgiDesktopCapture _dxgiCapture;
    private readonly IVncLogger? _logger;
    private readonly object _syncLock = new();

    private DateTime _lastDxgiRetry = DateTime.MinValue;
    private bool _loggedDxgiFallback;

    public int VirtualX => IsUsingDxgi ? 0 : _gdiCapture.VirtualX;
    public int VirtualY => IsUsingDxgi ? 0 : _gdiCapture.VirtualY;
    public int Width => IsUsingDxgi ? _dxgiCapture.Width : _gdiCapture.Width;
    public int Height => IsUsingDxgi ? _dxgiCapture.Height : _gdiCapture.Height;
    public int Quality { get; private set; }

    public bool IsUsingDxgi => _dxgiCapture.IsInitialized && !_dxgiCapture.IsAccessLost;

    public HybridScreenCapture(int quality = 70, IVncLogger? logger = null, IFrameEncoder? encoder = null)
    {
        Quality = Math.Clamp(quality, 10, 100);
        _logger = logger;
        _gdiCapture = new ScreenCapture(Quality);
        _dxgiCapture = new DxgiDesktopCapture(Quality, encoder);

        if (IsUsingDxgi)
        {
            _logger?.Info($"HybridScreenCapture: Direct3D 11 / DXGI Desktop Duplication initialized ({_dxgiCapture.Width}x{_dxgiCapture.Height}).");
        }
        else
        {
            _logger?.Info("HybridScreenCapture: DXGI unavailable at startup, using GDI Desktop Capture fallback.");
        }
    }

    public void SetQuality(int quality)
    {
        lock (_syncLock)
        {
            Quality = Math.Clamp(quality, 10, 100);
            _gdiCapture.SetQuality(Quality);
            _dxgiCapture.SetQuality(Quality);
        }
    }

    public void RefreshResolution()
    {
        lock (_syncLock)
        {
            _gdiCapture.RefreshResolution();
            if (_dxgiCapture.IsAccessLost && (DateTime.UtcNow - _lastDxgiRetry).TotalSeconds >= 5)
            {
                _lastDxgiRetry = DateTime.UtcNow;
                if (_dxgiCapture.TryInitialize())
                {
                    _loggedDxgiFallback = false;
                    _logger?.Info("HybridScreenCapture: DXGI Desktop Duplication re-acquired successfully.");
                }
            }
        }
    }

    public byte[]? CaptureJpeg(out uint frameIndex, bool force = false)
    {
        lock (_syncLock)
        {
            // Attempt DXGI capture first
            if (IsUsingDxgi)
            {
                byte[]? frame = _dxgiCapture.CaptureJpeg(out frameIndex, force);
                if (frame != null)
                {
                    return frame;
                }

                // If DXGI lost access (e.g. UAC secure desktop or resolution change)
                if (_dxgiCapture.IsAccessLost)
                {
                    if (!_loggedDxgiFallback)
                    {
                        _loggedDxgiFallback = true;
                        _logger?.Warn("HybridScreenCapture: DXGI access lost (UAC/Secure Desktop/Mode Change). Falling back to GDI capture.");
                    }
                }
                else
                {
                    // Screen unchanged (null returned with active DXGI)
                    return null;
                }
            }
            else
            {
                // Periodically attempt to re-acquire DXGI every 5 seconds
                if ((DateTime.UtcNow - _lastDxgiRetry).TotalSeconds >= 5)
                {
                    _lastDxgiRetry = DateTime.UtcNow;
                    if (_dxgiCapture.TryInitialize())
                    {
                        _loggedDxgiFallback = false;
                        _logger?.Info("HybridScreenCapture: DXGI Desktop Duplication recovered. Resuming hardware-accelerated capture.");
                        byte[]? frame = _dxgiCapture.CaptureJpeg(out frameIndex, force);
                        if (frame != null) return frame;
                    }
                }
            }

            // Fallback to GDI capture
            return _gdiCapture.CaptureJpeg(out frameIndex, force);
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _dxgiCapture.Dispose();
            _gdiCapture.Dispose();
        }
    }
}
