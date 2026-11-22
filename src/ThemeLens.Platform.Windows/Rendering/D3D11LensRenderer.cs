// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.CompilerServices;
using SharpGen.Runtime;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Rendering;
using ThemeLens.Engine;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The lens on the GPU: Direct3D 11 and the passes of <c>lens.hlsl</c>. It shows what
/// <see cref="ReferenceRenderer"/> defines, pass for pass; the platform tests compare the two.
/// </summary>
/// <remarks>
/// <para>
/// A hardware device is tried first. Without a usable GPU the same shaders run on WARP, the software
/// rasteriser that ships with Windows (the "CPU only" tier of section 7.6), and
/// <see cref="IsSoftwareRendered"/> says so.
/// </para>
/// <para>
/// The immediate context is not thread-safe. Everything that touches it, here and in the classes that share
/// this device, holds <see cref="Sync"/> for the whole operation.
/// </para>
/// </remarks>
public sealed class D3D11LensRenderer : IDisposable
{
    private const int ResourceSlots = 11;
    private const int SourceSlot = 0;
    private const int FineSlot = 1;
    private const int CoarseSlot = 2;
    private const int WideSlot = 3;
    private const int LutLightSlot = 4;
    private const int LutDarkSlot = 5;
    private const int MappedSlot = 6;
    private const int MappedFineSlot = 7;
    private const int MappedCoarseSlot = 8;
    private const int BlurredRowsSlot = 9;
    private const int ShrinkSourceSlot = 10;

    private const Format FrameFormat = Format.B8G8R8A8_UNorm;
    private const Format IntermediateFormat = Format.R8G8B8A8_UNorm;
    private const Format TableFormat = Format.R16G16B16A16_Float;
    private const int TableTexelBytes = 8;

    private static readonly FeatureLevel[] FeatureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];

    private readonly bool _forceSoftware;
    private readonly byte[] _vertexCode;
    private readonly IReadOnlyDictionary<LensPass, byte[]> _pixelCode;
    private readonly Half[] _tableScratch = new Half[Lut3D.EntryCount * 4];
    private readonly ID3D11ShaderResourceView[] _resources = new ID3D11ShaderResourceView[ResourceSlots];
    private readonly ID3D11RenderTargetView[] _fineTargets = new ID3D11RenderTargetView[2];

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private ID3D11VertexShader _vertexShader = null!;
    private ID3D11PixelShader[] _pixelShaders = [];
    private ID3D11SamplerState _sampler = null!;
    private ID3D11RasterizerState _rasterizer = null!;
    private ID3D11Buffer _frameBuffer = null!;
    private ID3D11Buffer _rulesBuffer = null!;
    private ID3D11Texture3D _lightTable = null!;
    private ID3D11Texture3D _darkTable = null!;
    private ID3D11ShaderResourceView _lightTableView = null!;
    private ID3D11ShaderResourceView _darkTableView = null!;
    private Lut3D? _uploadedLight;
    private Lut3D? _uploadedDark;

    private int _width;
    private int _height;
    private RenderTexture? _fineVotes;
    private RenderTexture? _fineCentre;
    private RenderTexture? _coarse;
    private RenderTexture? _wide;
    private RenderTexture? _mapped;
    private RenderTexture? _mappedFine;
    private RenderTexture? _mappedCoarse;
    private RenderTexture? _blurredRows;
    private ID3D11Texture2D? _regionCopy;
    private bool _fineValid;

    private ID3D11Texture2D? _readback;
    private int _readbackWidth;
    private int _readbackHeight;
    private bool _readbackPending;

    private bool _deviceLost;
    private bool _disposed;

    /// <param name="forceSoftware">True to render on WARP even when a GPU is present. The platform tests use this.</param>
    public D3D11LensRenderer(bool forceSoftware = false)
    {
        if (Unsafe.SizeOf<FrameConstants>() != FrameConstants.SizeInBytes || Unsafe.SizeOf<RuleConstants>() != RuleConstants.SizeInBytes)
        {
            throw new InvalidOperationException("The constant buffer structs no longer match lens.hlsl.");
        }

        _forceSoftware = forceSoftware;
        (_vertexCode, _pixelCode) = LensShaders.CompileAll();
        CreateDeviceObjects();
    }

    /// <summary>True when rendering runs on the CPU (WARP), because there is no usable GPU or because it was asked for.</summary>
    public bool IsSoftwareRendered { get; private set; }

    /// <summary>Hold this for every operation that uses <see cref="Context"/>.</summary>
    public Lock Sync { get; } = new();

    public ID3D11Device Device => _device;

    public ID3D11DeviceContext Context => _context;

    /// <summary>Goes up each time the device has been replaced after a loss. Whoever keeps device objects compares it.</summary>
    public int DeviceGeneration { get; private set; }

    /// <summary>True once a call has failed because the GPU device was removed or reset. <see cref="RecreateDevice"/> clears it.</summary>
    public bool IsDeviceLost => Volatile.Read(ref _deviceLost);

    /// <summary>
    /// Recolours the part of <paramref name="source"/> that lies under the lens and draws it into the top-left
    /// corner of <paramref name="target"/>, at the size of <paramref name="region"/>.
    /// </summary>
    /// <param name="source">A captured frame, B8G8R8A8.</param>
    /// <param name="region">Where the lens is inside the frame, in the frame's pixels.</param>
    /// <param name="tables">The look.</param>
    /// <param name="target">A view of a target at least as large as the region.</param>
    public void Render(ID3D11Texture2D source, PixelRect region, LensTables tables, ID3D11RenderTargetView target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(target);
        if (region.IsEmpty)
        {
            return;
        }

        lock (Sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                RenderLocked(source, region, tables, target);
            }
            catch (SharpGenException e)
            {
                NoteFailure(e.ResultCode);
                throw;
            }
            finally
            {
                // Leave nothing bound: a captured frame must not stay alive because a shader slot still points at it.
                Array.Clear(_resources);
                _context.PSUnsetShaderResources(0, ResourceSlots);
                _context.UnsetRenderTargets();
            }
        }
    }

    /// <summary>Fills a whole target with one colour.</summary>
    public void Fill(ID3D11RenderTargetView target, Rgb8 colour)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (Sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                _context.ClearRenderTargetView(target, ToColor(colour));
            }
            catch (SharpGenException e)
            {
                NoteFailure(e.ResultCode);
                throw;
            }
        }
    }

    /// <summary>
    /// Runs the whole lens off screen on a picture held in memory and reads the result back. This is how the
    /// platform tests compare the shader with the reference renderer; the frame loop never calls it.
    /// </summary>
    public ImageRgb8 RenderToImage(ImageRgb8 source, LensTables tables)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tables);
        lock (Sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int width = source.Width;
            int height = source.Height;
            using ID3D11Texture2D input = CreateFrameTexture(source);
            using var output = new RenderTexture(_device, width, height, FrameFormat);
            Render(input, new PixelRect(0, 0, width, height), tables, output.Target);

            using ID3D11Texture2D staging = _device.CreateTexture2D(StagingDescription(width, height, FrameFormat));
            _context.CopyResource(staging, output.Texture);
            _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out MappedSubresource mapped).CheckError();
            try
            {
                return ReadBgra(mapped, width, height);
            }
            finally
            {
                _context.Unmap(staging, 0);
            }
        }
    }

    /// <summary>
    /// The analysis picture of the frame rendered most recently: the same samples as
    /// <see cref="Neighbourhood.ToThumbnail"/>. It never waits for the GPU. The first call after a frame starts
    /// the copy and returns null; a later call returns the picture once the copy has landed.
    /// </summary>
    public Thumbnail? TryReadThumbnail()
    {
        lock (Sync)
        {
            if (_disposed || IsDeviceLost)
            {
                return null;
            }

            try
            {
                if (!_readbackPending)
                {
                    if (!_fineValid || _fineCentre is null)
                    {
                        return null;
                    }

                    if (_readback is null || _readbackWidth != _fineCentre.Width || _readbackHeight != _fineCentre.Height)
                    {
                        _readback?.Dispose();
                        _readbackWidth = _fineCentre.Width;
                        _readbackHeight = _fineCentre.Height;
                        _readback = _device.CreateTexture2D(StagingDescription(_readbackWidth, _readbackHeight, IntermediateFormat));
                    }

                    _context.CopyResource(_readback, _fineCentre.Texture);
                    _context.Flush();
                    _readbackPending = true;
                    return null;
                }

                Result result = _context.Map(_readback!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait, out MappedSubresource mapped);
                if (result.Code == HResults.DxgiWasStillDrawing)
                {
                    return null;
                }

                _readbackPending = false;
                if (result.Failure)
                {
                    NoteFailure(result);
                    return null;
                }

                try
                {
                    return ReadThumbnail(mapped, _readbackWidth, _readbackHeight);
                }
                finally
                {
                    _context.Unmap(_readback!, 0);
                }
            }
            catch (SharpGenException e)
            {
                _readbackPending = false;
                NoteFailure(e.ResultCode);
                return null;
            }
        }
    }

    /// <summary>Uploads a picture as a texture in the format of a captured frame.</summary>
    public ID3D11Texture2D CreateFrameTexture(ImageRgb8 image)
    {
        ArgumentNullException.ThrowIfNull(image);
        int width = image.Width;
        int height = image.Height;
        byte[] bgra = new byte[width * height * 4];
        byte[] rgb = image.Data;
        for (int s = 0, d = 0; s < rgb.Length; s += 3, d += 4)
        {
            bgra[d] = rgb[s + 2];
            bgra[d + 1] = rgb[s + 1];
            bgra[d + 2] = rgb[s];
            bgra[d + 3] = 255;
        }

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = FrameFormat,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };

        unsafe
        {
            fixed (byte* data = bgra)
            {
                return _device.CreateTexture2D(in description, new SubresourceData(data, (uint)(width * 4), 0));
            }
        }
    }

    /// <summary>
    /// Checks whether the device is still there. A removed device does not always fail the call in progress,
    /// so the runtime asks from time to time.
    /// </summary>
    public bool CheckDevice()
    {
        lock (Sync)
        {
            if (!_disposed && !IsDeviceLost && _device.DeviceRemovedReason.Failure)
            {
                Volatile.Write(ref _deviceLost, true);
            }

            return !IsDeviceLost;
        }
    }

    /// <summary>
    /// Replaces a lost device with a new one. Everything created from the old device is gone afterwards;
    /// <see cref="DeviceGeneration"/> tells the owners of such objects.
    /// </summary>
    public void RecreateDevice()
    {
        lock (Sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DisposeDeviceObjects();
            CreateDeviceObjects();
            Volatile.Write(ref _deviceLost, false);
            DeviceGeneration++;
        }
    }

    public void Dispose()
    {
        lock (Sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeDeviceObjects();
        }
    }

    internal static Color4 ToColor(Rgb8 colour) => new(colour.R / 255f, colour.G / 255f, colour.B / 255f, 1f);

    internal void NoteFailure(Result result)
    {
        if (HResults.IsDeviceLoss(result.Code))
        {
            Volatile.Write(ref _deviceLost, true);
        }
    }

    private static Texture2DDescription StagingDescription(int width, int height, Format format) => new()
    {
        Width = (uint)width,
        Height = (uint)height,
        MipLevels = 1,
        ArraySize = 1,
        Format = format,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Staging,
        BindFlags = BindFlags.None,
        CPUAccessFlags = CpuAccessFlags.Read,
        MiscFlags = ResourceOptionFlags.None,
    };

    private static unsafe ImageRgb8 ReadBgra(MappedSubresource mapped, int width, int height)
    {
        var image = new ImageRgb8(width, height);
        byte[] rgb = image.Data;
        byte* data = (byte*)mapped.DataPointer;
        for (int y = 0; y < height; y++)
        {
            byte* row = data + ((long)y * mapped.RowPitch);
            int d = y * width * 3;
            for (int x = 0; x < width; x++, d += 3)
            {
                byte* texel = row + (x * 4);
                rgb[d] = texel[2];
                rgb[d + 1] = texel[1];
                rgb[d + 2] = texel[0];
            }
        }

        return image;
    }

    /// <summary><see cref="Neighbourhood.ToThumbnail"/>, read from the second target of the fine pass (centre pixel in RGB, luma range in alpha).</summary>
    private static unsafe Thumbnail ReadThumbnail(MappedSubresource mapped, int fineWidth, int fineHeight)
    {
        int stride = Thumbnail.StrideFor(fineWidth, fineHeight);
        int width = (fineWidth + stride - 1) / stride;
        int height = (fineHeight + stride - 1) / stride;
        var pixels = new Rgb8[width * height];
        byte[] range = new byte[width * height];
        byte* data = (byte*)mapped.DataPointer;
        for (int j = 0; j < height; j++)
        {
            int fy = Math.Min((j * stride) + (stride / 2), fineHeight - 1);
            byte* row = data + ((long)fy * mapped.RowPitch);
            for (int i = 0; i < width; i++)
            {
                int fx = Math.Min((i * stride) + (stride / 2), fineWidth - 1);
                byte* texel = row + (fx * 4);
                pixels[(j * width) + i] = new Rgb8(texel[0], texel[1], texel[2]);
                range[(j * width) + i] = texel[3];
            }
        }

        return new Thumbnail(width, height, pixels, range, stride * LensConstants.BlockSize);
    }

    private static bool IsSoftwareAdapter(ID3D11Device device)
    {
        const uint microsoftVendor = 0x1414;
        const uint basicRenderDriver = 0x8C;
        using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        using IDXGIAdapter1? adapter1 = adapter.QueryInterfaceOrNull<IDXGIAdapter1>();
        if (adapter1 is null)
        {
            return false;
        }

        AdapterDescription1 description = adapter1.Description1;
        return (description.Flags & AdapterFlags.Software) != 0
            || (description.VendorId == microsoftVendor && description.DeviceId == basicRenderDriver);
    }

    private void CreateDeviceObjects()
    {
        const DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        bool software = _forceSoftware;
        if (!_forceSoftware)
        {
            Result hardware = D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, flags, FeatureLevels, out device, out context);
            if (hardware.Failure)
            {
                device?.Dispose();
                context?.Dispose();
                device = null;
                context = null;
            }
        }

        if (device is null || context is null)
        {
            // No usable GPU (a basic display driver, some virtual machines and remote sessions): the same shaders on the CPU.
            D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Warp, flags, FeatureLevels, out device, out context).CheckError();
            software = true;
        }

        _device = device;
        _context = context;
        IsSoftwareRendered = software || IsSoftwareAdapter(device);

        // Capture and the windows use this device from other threads; make single calls safe on top of Sync.
        using (ID3D11Multithread? multithread = context.QueryInterfaceOrNull<ID3D11Multithread>())
        {
            multithread?.SetMultithreadProtected(true);
        }

        // At most one finished frame waits for the display, so a slow frame never queues up behind others.
        using (IDXGIDevice1? dxgiDevice = device.QueryInterfaceOrNull<IDXGIDevice1>())
        {
            dxgiDevice?.SetMaximumFrameLatency(1);
        }

        _vertexShader = device.CreateVertexShader(_vertexCode);
        LensPass[] passes = Enum.GetValues<LensPass>();
        _pixelShaders = new ID3D11PixelShader[passes.Length];
        foreach (LensPass pass in passes)
        {
            _pixelShaders[(int)pass] = device.CreatePixelShader(_pixelCode[pass]);
        }

        _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _rasterizer = device.CreateRasterizerState(RasterizerDescription.CullNone);

        _frameBuffer = device.CreateBuffer(new BufferDescription(FrameConstants.SizeInBytes, BindFlags.ConstantBuffer));
        _rulesBuffer = device.CreateBuffer(new BufferDescription(RuleConstants.SizeInBytes, BindFlags.ConstantBuffer));
        RuleConstants rules = RuleConstants.FromLensConstants();
        context.UpdateSubresource(in rules, _rulesBuffer);

        var tableDescription = new Texture3DDescription
        {
            Width = Lut3D.Size,
            Height = Lut3D.Size,
            Depth = Lut3D.Size,
            MipLevels = 1,
            Format = TableFormat,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        };
        _lightTable = device.CreateTexture3D(in tableDescription);
        _darkTable = device.CreateTexture3D(in tableDescription);
        _lightTableView = device.CreateShaderResourceView(_lightTable);
        _darkTableView = device.CreateShaderResourceView(_darkTable);
        _uploadedLight = null;
        _uploadedDark = null;
    }

    private void DisposeDeviceObjects()
    {
        DisposeTargets();
        _readback?.Dispose();
        _readback = null;
        _readbackPending = false;
        _lightTableView?.Dispose();
        _darkTableView?.Dispose();
        _lightTable?.Dispose();
        _darkTable?.Dispose();
        _frameBuffer?.Dispose();
        _rulesBuffer?.Dispose();
        _rasterizer?.Dispose();
        _sampler?.Dispose();
        foreach (ID3D11PixelShader shader in _pixelShaders)
        {
            shader?.Dispose();
        }

        _pixelShaders = [];
        _vertexShader?.Dispose();
        if (_context is not null)
        {
            _context.ClearState();
            _context.Flush();
            _context.Dispose();
        }

        _device?.Dispose();
    }

    private void DisposeTargets()
    {
        _fineVotes?.Dispose();
        _fineCentre?.Dispose();
        _coarse?.Dispose();
        _wide?.Dispose();
        _mapped?.Dispose();
        _mappedFine?.Dispose();
        _mappedCoarse?.Dispose();
        _blurredRows?.Dispose();
        _regionCopy?.Dispose();
        _fineVotes = _fineCentre = _coarse = _wide = _mapped = _mappedFine = _mappedCoarse = _blurredRows = null;
        _regionCopy = null;
        _fineValid = false;
        _width = 0;
        _height = 0;
    }

    private void RenderLocked(ID3D11Texture2D source, PixelRect region, LensTables tables, ID3D11RenderTargetView target)
    {
        int width = region.Width;
        int height = region.Height;
        if (width != _width || height != _height)
        {
            DisposeTargets();
            _width = width;
            _height = height;
        }

        // The sizes of Neighbourhood.Build and ReferenceRenderer.Shrink: each level is the one before, divided by four and rounded up.
        const int block = LensConstants.BlockSize;
        int fineWidth = (width + block - 1) / block;
        int fineHeight = (height + block - 1) / block;
        int coarseWidth = (fineWidth + block - 1) / block;
        int coarseHeight = (fineHeight + block - 1) / block;
        int wideWidth = (coarseWidth + block - 1) / block;
        int wideHeight = (coarseHeight + block - 1) / block;

        FrameParameters p = tables.Parameters;
        bool oneTable = ReferenceEquals(tables.Light, tables.Dark);

        // A frame that cannot be read by a shader as it is (no shader-resource binding) is first copied, lens area only.
        Texture2DDescription sourceDescription = source.Description;
        int originX = region.X;
        int originY = region.Y;
        if ((sourceDescription.BindFlags & BindFlags.ShaderResource) == 0)
        {
            source = CopyRegion(source, sourceDescription, region);
            sourceDescription = source.Description;
            originX = 0;
            originY = 0;
        }

        var constants = new FrameConstants
        {
            PageEncoded = p.PageEncoded,
            Direction = p.Direction,
            TextEncoded = p.TextEncoded,
            PageLuminance = p.PageLuminance,
            MaxAreaContrast = p.MaxAreaContrast,
            GuardStrength = p.GuardStrength,
            GuardSoftGate = p.GuardSoftGate,
            MaxLuminance = p.MaxLuminance,
            WholeLensLightFraction = p.WholeLensLightFraction,
            LocalContrast = p.LocalContrast,
            Sharpen = p.Sharpen,
            StrongSharpen = p.StrongSharpen,
            SourceOriginX = originX,
            SourceOriginY = originY,
            SourceLimitX = (int)sourceDescription.Width - 1,
            SourceLimitY = (int)sourceDescription.Height - 1,
            LensWidth = width,
            LensHeight = height,
            FineWidth = fineWidth,
            FineHeight = fineHeight,
            CoarseWidth = coarseWidth,
            CoarseHeight = coarseHeight,
            WideWidth = wideWidth,
            WideHeight = wideHeight,
            OneTable = oneTable ? 1f : 0f,
        };
        _context.UpdateSubresource(in constants, _frameBuffer);

        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetInputLayout(null!);
        _context.VSSetShader(_vertexShader);
        _context.RSSetState(_rasterizer);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetConstantBuffer(0, _frameBuffer);
        _context.PSSetConstantBuffer(1, _rulesBuffer);

        using ID3D11ShaderResourceView sourceView = _device.CreateShaderResourceView(source);

        if (p.Identity)
        {
            Array.Clear(_resources);
            _resources[SourceSlot] = sourceView;
            Draw(LensPass.Copy, target, width, height);
        }
        else
        {
            UploadTables(tables, oneTable);

            // Pass 1: neighbourhood. The fine level always runs: it is also where the analysis thumbnail comes from.
            _fineVotes ??= new RenderTexture(_device, fineWidth, fineHeight, IntermediateFormat);
            _fineCentre ??= new RenderTexture(_device, fineWidth, fineHeight, IntermediateFormat);
            Array.Clear(_resources);
            _resources[SourceSlot] = sourceView;
            _fineTargets[0] = _fineVotes.Target;
            _fineTargets[1] = _fineCentre.Target;
            Draw(LensPass.Fine, _fineTargets, fineWidth, fineHeight);
            _fineValid = true;

            bool softGate = p.GuardOn && p.GuardSoftGate > 0f;
            if (!oneTable || softGate)
            {
                _coarse ??= new RenderTexture(_device, coarseWidth, coarseHeight, IntermediateFormat);
                Array.Clear(_resources);
                _resources[FineSlot] = _fineVotes.Resource;
                Draw(LensPass.Coarse, _coarse.Target, coarseWidth, coarseHeight);
            }

            if (!oneTable)
            {
                _wide ??= new RenderTexture(_device, wideWidth, wideHeight, IntermediateFormat);
                Array.Clear(_resources);
                _resources[CoarseSlot] = _coarse!.Resource;
                Draw(LensPass.Wide, _wide.Target, wideWidth, wideHeight);
            }

            // Pass 2: recolour.
            Array.Clear(_resources);
            _resources[SourceSlot] = sourceView;
            _resources[LutLightSlot] = _lightTableView;
            _resources[LutDarkSlot] = oneTable ? _lightTableView : _darkTableView;
            if (!oneTable)
            {
                _resources[CoarseSlot] = _coarse!.Resource;
                _resources[WideSlot] = _wide!.Resource;
            }

            if (!p.NeedsFinishPass)
            {
                Draw(LensPass.RecolourFinal, target, width, height);
            }
            else
            {
                _mapped ??= new RenderTexture(_device, width, height, IntermediateFormat);
                Draw(LensPass.RecolourMapped, _mapped.Target, width, height);

                // Pass 3: the recoloured picture at a quarter and a sixteenth of its size, and the row blur.
                bool guard = p.GuardOn;
                bool localContrast = p.LocalContrast > 0f;
                bool strongSharpen = p.StrongSharpen > 0f;
                if (guard || localContrast)
                {
                    _mappedFine ??= new RenderTexture(_device, fineWidth, fineHeight, IntermediateFormat);
                    Array.Clear(_resources);
                    _resources[ShrinkSourceSlot] = _mapped.Resource;
                    Draw(LensPass.Shrink, _mappedFine.Target, fineWidth, fineHeight);
                }

                if (localContrast)
                {
                    _mappedCoarse ??= new RenderTexture(_device, coarseWidth, coarseHeight, IntermediateFormat);
                    Array.Clear(_resources);
                    _resources[ShrinkSourceSlot] = _mappedFine!.Resource;
                    Draw(LensPass.Shrink, _mappedCoarse.Target, coarseWidth, coarseHeight);
                }

                if (strongSharpen)
                {
                    // The reference keeps the row blur in floats, so this one target is not 8-bit.
                    _blurredRows ??= new RenderTexture(_device, width, height, Format.R32_Float);
                    Array.Clear(_resources);
                    _resources[MappedSlot] = _mapped.Resource;
                    Draw(LensPass.BlurRows, _blurredRows.Target, width, height);
                }

                // Pass 4: finish.
                Array.Clear(_resources);
                _resources[MappedSlot] = _mapped.Resource;
                if (guard)
                {
                    _resources[MappedFineSlot] = _mappedFine!.Resource;
                }

                if (softGate)
                {
                    _resources[CoarseSlot] = _coarse!.Resource;
                }

                if (localContrast)
                {
                    _resources[MappedCoarseSlot] = _mappedCoarse!.Resource;
                }

                if (strongSharpen)
                {
                    _resources[BlurredRowsSlot] = _blurredRows!.Resource;
                }

                Draw(LensPass.Finish, target, width, height);
            }
        }
    }

    private void Draw(LensPass pass, ID3D11RenderTargetView target, int width, int height)
    {
        _context.PSUnsetShaderResources(0, ResourceSlots);
        _context.OMSetRenderTargets(target);
        DrawBound(pass, width, height);
    }

    private void Draw(LensPass pass, ID3D11RenderTargetView[] targets, int width, int height)
    {
        _context.PSUnsetShaderResources(0, ResourceSlots);
        _context.OMSetRenderTargets(targets);
        DrawBound(pass, width, height);
    }

    private void DrawBound(LensPass pass, int width, int height)
    {
        _context.RSSetViewport(0f, 0f, width, height);
        _context.PSSetShaderResources(0, _resources);
        _context.PSSetShader(_pixelShaders[(int)pass]);
        _context.Draw(3, 0);
    }

    private ID3D11Texture2D CopyRegion(ID3D11Texture2D source, Texture2DDescription sourceDescription, PixelRect region)
    {
        _regionCopy ??= _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)region.Width,
            Height = (uint)region.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = sourceDescription.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        });

        int left = Math.Clamp(region.X, 0, (int)sourceDescription.Width);
        int top = Math.Clamp(region.Y, 0, (int)sourceDescription.Height);
        int right = Math.Clamp(region.Right, left, (int)sourceDescription.Width);
        int bottom = Math.Clamp(region.Bottom, top, (int)sourceDescription.Height);
        if (right > left && bottom > top)
        {
            _context.CopySubresourceRegion(
                _regionCopy, 0, (uint)(left - region.X), (uint)(top - region.Y), 0, source, 0, new Box(left, top, 0, right, bottom, 1));
        }

        return _regionCopy;
    }

    private void UploadTables(LensTables tables, bool oneTable)
    {
        if (!ReferenceEquals(tables.Light, _uploadedLight))
        {
            UploadTable(_lightTable, tables.Light);
            _uploadedLight = tables.Light;
        }

        if (!oneTable && !ReferenceEquals(tables.Dark, _uploadedDark))
        {
            UploadTable(_darkTable, tables.Dark);
            _uploadedDark = tables.Dark;
        }
    }

    /// <summary>
    /// A table is stored red-fastest (<see cref="Lut3D.Index"/>), which is the layout of a 3D texture with red
    /// on x, green on y and blue on z: the entries go up in the order they are.
    /// </summary>
    private void UploadTable(ID3D11Texture3D texture, Lut3D table)
    {
        float[] data = table.Data;
        Half[] scratch = _tableScratch;
        Half one = (Half)1f;
        for (int s = 0, d = 0; s < data.Length; s += 3, d += 4)
        {
            scratch[d] = (Half)data[s];
            scratch[d + 1] = (Half)data[s + 1];
            scratch[d + 2] = (Half)data[s + 2];
            scratch[d + 3] = one;
        }

        const uint rowPitch = Lut3D.Size * TableTexelBytes;
        const uint slicePitch = Lut3D.Size * Lut3D.Size * TableTexelBytes;
        _context.UpdateSubresource(scratch, texture, 0, rowPitch, slicePitch);
    }
}
