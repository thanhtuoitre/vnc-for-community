using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VncServer.Core.Codecs;

namespace VncServer.Core.Capture;

public sealed unsafe class DxgiDesktopCapture : IDisposable
{
    private const int D3D11_SDK_VERSION = 7;
    private const int D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const int D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const int D3D11_MAP_READ = 1;

    private const int S_OK = 0;
    private const uint DXGI_ERROR_ACCESS_LOST = 0x887A0026;
    private const uint DXGI_ERROR_WAIT_TIMEOUT = 0x887A0027;

    [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", SetLastError = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr Software,
        uint Flags,
        int[]? pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_IDXGIOutput1 = new("00cddea8-939b-4b82-bc17-cd0cf09743e2");
    private static readonly Guid IID_ID3D11Texture2D = new("6f156113-ed4f-42e0-9324-124634f4e16c");

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public int RectsCoalesced;
        public int ProtectedContentMaskedOut;
        public int PointerPositionX;
        public int PointerPositionY;
        public int PointerVisible;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public uint SampleCount;
        public uint SampleQuality;
        public int Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    private IntPtr _d3dDevice;
    private IntPtr _d3dContext;
    private IntPtr _outputDuplication;
    private IntPtr _stagingTexture;

    private readonly IFrameEncoder _encoder;
    private uint _frameIndex;
    private bool _isDisposed;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Quality { get; private set; } = 70;
    public bool IsInitialized => _outputDuplication != IntPtr.Zero;
    public bool IsAccessLost { get; private set; }

    public DxgiDesktopCapture(int quality = 70, IFrameEncoder? encoder = null)
    {
        Quality = Math.Clamp(quality, 10, 100);
        _encoder = encoder ?? new JpegFrameEncoder();
        TryInitialize();
    }

    public bool TryInitialize(int outputIndex = 0)
    {
        CleanupDxgi();
        IsAccessLost = false;

        try
        {
            int hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                null,
                0,
                D3D11_SDK_VERSION,
                out _d3dDevice,
                out _,
                out _d3dContext);

            if (hr != S_OK || _d3dDevice == IntPtr.Zero)
            {
                return false;
            }

            // Query IDXGIDevice
            var iidDevice = IID_IDXGIDevice;
            hr = Marshal.QueryInterface(_d3dDevice, ref iidDevice, out IntPtr dxgiDevice);
            if (hr != S_OK || dxgiDevice == IntPtr.Zero) return false;

            // GetAdapter
            IntPtr* vtblDevice = *(IntPtr**)dxgiDevice;
            var getAdapter = (delegate* unmanaged[Stdcall]<IntPtr, out IntPtr, int>)vtblDevice[9];
            hr = getAdapter(dxgiDevice, out IntPtr dxgiAdapter);
            Marshal.Release(dxgiDevice);
            if (hr != S_OK || dxgiAdapter == IntPtr.Zero) return false;

            // EnumOutputs(outputIndex)
            IntPtr* vtblAdapter = *(IntPtr**)dxgiAdapter;
            var enumOutputs = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)vtblAdapter[7];
            hr = enumOutputs(dxgiAdapter, (uint)outputIndex, out IntPtr dxgiOutput);
            Marshal.Release(dxgiAdapter);
            if (hr != S_OK || dxgiOutput == IntPtr.Zero) return false;

            // Query IDXGIOutput1
            var iidOutput1 = IID_IDXGIOutput1;
            hr = Marshal.QueryInterface(dxgiOutput, ref iidOutput1, out IntPtr dxgiOutput1);
            Marshal.Release(dxgiOutput);
            if (hr != S_OK || dxgiOutput1 == IntPtr.Zero) return false;

            // DuplicateOutput
            IntPtr* vtblOutput1 = *(IntPtr**)dxgiOutput1;
            var duplicateOutput = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, out IntPtr, int>)vtblOutput1[22];
            hr = duplicateOutput(dxgiOutput1, _d3dDevice, out _outputDuplication);
            Marshal.Release(dxgiOutput1);

            if (hr != S_OK || _outputDuplication == IntPtr.Zero)
            {
                return false;
            }

            return true;
        }
        catch
        {
            CleanupDxgi();
            return false;
        }
    }

    public void SetQuality(int quality)
    {
        Quality = Math.Clamp(quality, 10, 100);
    }

    public byte[]? CaptureJpeg(out uint frameIndex, bool force = false)
    {
        frameIndex = _frameIndex;
        if (!IsInitialized || IsAccessLost) return null;

        IntPtr* vtblDupl = *(IntPtr**)_outputDuplication;
        var acquireNextFrame = (delegate* unmanaged[Stdcall]<IntPtr, uint, out DXGI_OUTDUPL_FRAME_INFO, out IntPtr, int>)vtblDupl[4];
        var releaseFrame = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtblDupl[10];

        int hr = acquireNextFrame(_outputDuplication, 16, out var frameInfo, out IntPtr desktopResource);

        if ((uint)hr == DXGI_ERROR_ACCESS_LOST)
        {
            IsAccessLost = true;
            CleanupDxgi();
            return null;
        }

        if ((uint)hr == DXGI_ERROR_WAIT_TIMEOUT)
        {
            // Timeout: screen hasn't updated
            return null;
        }

        if (hr != S_OK || desktopResource == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!force && frameInfo.AccumulatedFrames == 0)
            {
                // Screen is static, hardware reports 0 changes
                return null;
            }

            // Query ID3D11Texture2D from resource
            var iidTexture = IID_ID3D11Texture2D;
            hr = Marshal.QueryInterface(desktopResource, ref iidTexture, out IntPtr desktopTexture);
            if (hr != S_OK || desktopTexture == IntPtr.Zero) return null;

            try
            {
                EnsureStagingTexture(desktopTexture);
                if (_stagingTexture == IntPtr.Zero) return null;

                // CopyResource: context->CopyResource(stagingTexture, desktopTexture)
                IntPtr* vtblContext = *(IntPtr**)_d3dContext;
                var copyResource = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)vtblContext[47];
                copyResource(_d3dContext, _stagingTexture, desktopTexture);

                // Map staging texture for CPU read
                var map = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, out D3D11_MAPPED_SUBRESOURCE, int>)vtblContext[14];
                var unmap = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)vtblContext[15];

                hr = map(_d3dContext, _stagingTexture, 0, D3D11_MAP_READ, 0, out var mapped);
                if (hr != S_OK || mapped.pData == IntPtr.Zero) return null;

                try
                {
                    // Convert mapped BGRA memory into Bitmap and encode JPEG
                    using var bmp = new Bitmap(Width, Height, (int)mapped.RowPitch, PixelFormat.Format32bppRgb, mapped.pData);
                    byte[] jpeg = _encoder.Encode(bmp, Quality);

                    frameIndex = ++_frameIndex;
                    return jpeg;
                }
                finally
                {
                    unmap(_d3dContext, _stagingTexture, 0);
                }
            }
            finally
            {
                Marshal.Release(desktopTexture);
            }
        }
        finally
        {
            releaseFrame(_outputDuplication);
            Marshal.Release(desktopResource);
        }
    }

    private void EnsureStagingTexture(IntPtr sourceTexture)
    {
        if (_stagingTexture != IntPtr.Zero) return;

        IntPtr* vtblTex = *(IntPtr**)sourceTexture;
        var getDesc = (delegate* unmanaged[Stdcall]<IntPtr, out D3D11_TEXTURE2D_DESC, void>)vtblTex[10];
        getDesc(sourceTexture, out var desc);

        Width = (int)desc.Width;
        Height = (int)desc.Height;

        desc.Usage = D3D11_USAGE_STAGING;
        desc.BindFlags = 0;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        desc.MiscFlags = 0;

        IntPtr* vtblDevice = *(IntPtr**)_d3dDevice;
        var createTexture2D = (delegate* unmanaged[Stdcall]<IntPtr, in D3D11_TEXTURE2D_DESC, IntPtr, out IntPtr, int>)vtblDevice[5];
        createTexture2D(_d3dDevice, in desc, IntPtr.Zero, out _stagingTexture);
    }

    private void CleanupDxgi()
    {
        if (_stagingTexture != IntPtr.Zero)
        {
            Marshal.Release(_stagingTexture);
            _stagingTexture = IntPtr.Zero;
        }

        if (_outputDuplication != IntPtr.Zero)
        {
            Marshal.Release(_outputDuplication);
            _outputDuplication = IntPtr.Zero;
        }

        if (_d3dContext != IntPtr.Zero)
        {
            Marshal.Release(_d3dContext);
            _d3dContext = IntPtr.Zero;
        }

        if (_d3dDevice != IntPtr.Zero)
        {
            Marshal.Release(_d3dDevice);
            _d3dDevice = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            CleanupDxgi();
            _encoder.Dispose();
        }
    }
}
