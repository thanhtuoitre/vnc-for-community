using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VncServer.Core.Codecs;
using VncServer.Core.Native;

namespace VncServer.Core.Capture;

public sealed unsafe class DxgiDesktopCapture : IDisposable
{
    private const int D3D11_SDK_VERSION = 7;
    private const int D3D_DRIVER_TYPE_UNKNOWN = 0;
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

    [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1", SetLastError = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_IDXGIOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

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
            DesktopHelper.AttachToInputDesktop();

            // 1. First attempt: Enumerate all adapters via CreateDXGIFactory1 (vital for Dual-GPU / NVIDIA Optimus)
            var iidFactory1 = IID_IDXGIFactory1;
            int hr = CreateDXGIFactory1(ref iidFactory1, out IntPtr factory);
            if (hr == S_OK && factory != IntPtr.Zero)
            {
                try
                {
                    IntPtr* vtblFactory = *(IntPtr**)factory;
                    var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)vtblFactory[12];

                    for (uint adpIdx = 0; adpIdx < 4; adpIdx++)
                    {
                        hr = enumAdapters1(factory, adpIdx, out IntPtr adapter);
                        if (hr != S_OK || adapter == IntPtr.Zero) break;

                        try
                        {
                            IntPtr* vtblAdapter = *(IntPtr**)adapter;
                            var enumOutputs = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)vtblAdapter[7];
                            hr = enumOutputs(adapter, (uint)outputIndex, out IntPtr output);
                            if (hr != S_OK || output == IntPtr.Zero) continue;

                            try
                            {
                                var iidOutput1 = IID_IDXGIOutput1;
                                hr = Marshal.QueryInterface(output, ref iidOutput1, out IntPtr output1);
                                if (hr != S_OK || output1 == IntPtr.Zero) continue;

                                try
                                {
                                    hr = D3D11CreateDevice(
                                        adapter,
                                        D3D_DRIVER_TYPE_UNKNOWN,
                                        IntPtr.Zero,
                                        D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                                        null,
                                        0,
                                        D3D11_SDK_VERSION,
                                        out _d3dDevice,
                                        out _,
                                        out _d3dContext);

                                    if (hr == S_OK && _d3dDevice != IntPtr.Zero)
                                    {
                                        IntPtr* vtblOutput1 = *(IntPtr**)output1;
                                        var duplicateOutput = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, out IntPtr, int>)vtblOutput1[22];

                                        for (int retry = 0; retry < 3; retry++)
                                        {
                                            hr = duplicateOutput(output1, _d3dDevice, out _outputDuplication);
                                            if (hr == S_OK && _outputDuplication != IntPtr.Zero) break;
                                            Thread.Sleep(50);
                                        }

                                        if (hr == S_OK && _outputDuplication != IntPtr.Zero)
                                        {
                                            var getDesc = (delegate* unmanaged[Stdcall]<IntPtr, out DXGI_OUTPUT_DESC, int>)vtblOutput1[7];
                                            if (getDesc(output1, out var desc) == S_OK)
                                            {
                                                Width = desc.Right - desc.Left;
                                                Height = desc.Bottom - desc.Top;
                                            }
                                            return true;
                                        }
                                    }
                                    CleanupDxgi();
                                }
                                finally
                                {
                                    Marshal.Release(output1);
                                }
                            }
                            finally
                            {
                                Marshal.Release(output);
                            }
                        }
                        finally
                        {
                            Marshal.Release(adapter);
                        }
                    }
                }
                finally
                {
                    Marshal.Release(factory);
                }
            }

            // 2. Fallback: Default adapter device creation
            hr = D3D11CreateDevice(
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

            var iidDevice = IID_IDXGIDevice;
            hr = Marshal.QueryInterface(_d3dDevice, ref iidDevice, out IntPtr dxgiDevice);
            if (hr != S_OK || dxgiDevice == IntPtr.Zero) return false;

            IntPtr* vtblDevice = *(IntPtr**)dxgiDevice;
            var getAdapter = (delegate* unmanaged[Stdcall]<IntPtr, out IntPtr, int>)vtblDevice[7]; // Slot 7 is GetAdapter
            hr = getAdapter(dxgiDevice, out IntPtr dxgiAdapter);
            Marshal.Release(dxgiDevice);
            if (hr != S_OK || dxgiAdapter == IntPtr.Zero) return false;

            IntPtr* vtblAdp = *(IntPtr**)dxgiAdapter;
            var enumOut = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)vtblAdp[7];
            hr = enumOut(dxgiAdapter, (uint)outputIndex, out IntPtr dxgiOutput);
            Marshal.Release(dxgiAdapter);
            if (hr != S_OK || dxgiOutput == IntPtr.Zero) return false;

            var iidOut1 = IID_IDXGIOutput1;
            hr = Marshal.QueryInterface(dxgiOutput, ref iidOut1, out IntPtr dxgiOutput1);
            Marshal.Release(dxgiOutput);
            if (hr != S_OK || dxgiOutput1 == IntPtr.Zero) return false;

            IntPtr* vtblOut1 = *(IntPtr**)dxgiOutput1;
            var duplOut = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, out IntPtr, int>)vtblOut1[22];

            for (int retry = 0; retry < 3; retry++)
            {
                hr = duplOut(dxgiOutput1, _d3dDevice, out _outputDuplication);
                if (hr == S_OK && _outputDuplication != IntPtr.Zero) break;
                Thread.Sleep(50);
            }

            if (hr == S_OK && _outputDuplication != IntPtr.Zero)
            {
                var getDesc = (delegate* unmanaged[Stdcall]<IntPtr, out DXGI_OUTPUT_DESC, int>)vtblOut1[7];
                if (getDesc(dxgiOutput1, out var desc) == S_OK)
                {
                    Width = desc.Right - desc.Left;
                    Height = desc.Bottom - desc.Top;
                }
                Marshal.Release(dxgiOutput1);
                return true;
            }

            Marshal.Release(dxgiOutput1);
            CleanupDxgi();
            return false;
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
        var acquireNextFrame = (delegate* unmanaged[Stdcall]<IntPtr, uint, out DXGI_OUTDUPL_FRAME_INFO, out IntPtr, int>)vtblDupl[8];
        var releaseFrame = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtblDupl[14];

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
