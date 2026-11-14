using PhotoViewer.Common.Types;
using PhotoViewer.Common.ServiceProviders.FileSearchService;
using ImageMagick;
using SkiaSharp;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Photoing;

public partial class PhotoMetadata : PhDisposable
{

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value) return;
            _filePath = value;

            SetFilePath__(value);
        }
    }

    public string FileName { get; private set; } = string.Empty;

    public string FileExtension { get; private set; } = string.Empty;
    public string FolderPath { get; private set; } = string.Empty;
    public string FolderName { get; private set; } = string.Empty;
    public long FileSizeInBytes { get; private set; } = 0;

    public string FileSizeFormatted => BHelper.FormatSize(FileSizeInBytes);

    public DateTime FileCreationTimeUtc { get; private set; }
    public DateTime FileLastAccessTimeUtc { get; private set; }
    public DateTime FileLastWriteTimeUtc { get; private set; }
    public string FileCreationTimeFormatted => BHelper.FormatDateTime(FileCreationTimeUtc.ToLocalTime());
    public string FileLastAccessTimeFormatted => BHelper.FormatDateTime(FileLastAccessTimeUtc.ToLocalTime());
    public string FileLastWriteTimeFormatted => BHelper.FormatDateTime(FileLastWriteTimeUtc.ToLocalTime());

    public uint OriginalWidth { get; set; } = 0;

    public uint OriginalHeight { get; set; } = 0;

    public uint Width { get; set; } = 0;

    public uint Height { get; set; } = 0;

    public double DpiX { get; set; } = 0;
    public double DpiY { get; set; } = 0;

    public uint FrameCount { get; set; } = 0;

    public int DefaultFrameIndex { get; set; } = 0;

    public string FrameCountFormatted => FrameCount > 1 ? FrameCount.ToString() : string.Empty;
    public uint AnimationLoop { get; set; } = 0;
    public IImmutableList<FrameMetadata> Frames { get; set; } = [];
    public bool HasAlpha { get; set; } = false;
    public bool CanAnimate { get; set; } = false;
    public SKEncodedOrigin Orientation { get; set; } = SKEncodedOrigin.Default;

    public bool IsVector { get; set; } = false;

    public HdrTransferFunction HdrTransferFn { get; set; } = HdrTransferFunction.None;

    public bool IsHdr => HdrTransferFn != HdrTransferFunction.None;

    public bool IsWideGamut { get; set; } = false;

    public double ContentPeakNits { get; set; } = 0;

    public int BitsPerChannel { get; set; } = 8;

    public long EmbeddedVideoOffsetFromEnd { get; set; } = 0;

    public bool IsLivePhoto => EmbeddedVideoOffsetFromEnd > 0;

    public ColorSpace ColorSpace { get; set; } = ColorSpace.Undefined;
    public string ColorProfileName { get; set; } = string.Empty;

    public IColorProfile? MagickColorProfile { get; set; } = null;

    public SKColorSpace? SkiaColorSpace { get; set; } = null;

    public IImageProfile? RawThumbnail { get; set; } = null;

    public uint PreviewWidth { get; set; } = 0;

    public uint PreviewHeight { get; set; } = 0;

    public bool IsEmbeddedPreviewLargeEnough(int minWidth, int minHeight) =>
        PreviewWidth > 0 && PreviewHeight > 0
        && PreviewWidth >= minWidth && PreviewHeight >= minHeight;

    public IExifProfile? ExifProfile { get; set; } = null;
    public int ExifRatingPercent { get; set; } = 0;
    public string ExifRatingFormatted => BHelper.FormatStarRatingText(ExifRatingPercent);
    public DateTime? ExifDateTimeOriginal { get; set; } = null;
    public DateTime? ExifDateTime { get; set; } = null;

    public string ExifDateTimeOriginalFormatted => BHelper.FormatDateTime(ExifDateTimeOriginal?.ToLocalTime());
    public string ExifDateTimeFormatted => BHelper.FormatDateTime(ExifDateTime?.ToLocalTime());

    public string? ExifImageDescription { get; set; } = null;
    public string? ExifModel { get; set; } = null;
    public string? ExifArtist { get; set; } = null;
    public string? ExifCopyright { get; set; } = null;
    public string? ExifSoftware { get; set; } = null;
    public float? ExifExposureTime { get; set; } = null;
    public float? ExifFNumber { get; set; } = null;
    public int? ExifISOSpeed { get; set; } = null;
    public float? ExifFocalLength { get; set; } = null;

    public PhotoMetadata() { }

    public PhotoMetadata(string? filePath)
    {
        FilePath = filePath ?? string.Empty;
    }

    public PhotoMetadata(FileSearchEntry entry)
    {
        _filePath = entry.FilePath;

        FileName = Path.GetFileName(entry.FilePath);
        FileExtension = Path.GetExtension(entry.FilePath).ToLowerInvariant();
        FolderPath = Path.GetDirectoryName(entry.FilePath) ?? string.Empty;
        FolderName = Path.GetFileName(FolderPath);
        FileSizeInBytes = entry.FileSizeInBytes;
        FileCreationTimeUtc = entry.FileCreationTimeUtc;
        FileLastWriteTimeUtc = entry.FileLastWriteTimeUtc;
        FileLastAccessTimeUtc = entry.FileLastAccessTimeUtc;
    }

    protected override void OnDisposing()
    {
        base.OnDisposing();

        SkiaColorSpace?.Dispose();
        SkiaColorSpace = null;
        MagickColorProfile = null;

        RawThumbnail = null;
        PreviewWidth = PreviewHeight = 0;
        ExifProfile = null;
        FrameCount = 0;
        Frames.Clear();

        EmbeddedVideoOffsetFromEnd = 0;
        HdrTransferFn = HdrTransferFunction.None;
        ContentPeakNits = 0;
    }

    private void SetFilePath__(string? filePath)
    {
        try
        {
            var fi = new FileInfo(filePath!);

            FileName = fi.Name;
            FileExtension = fi.Extension.ToLowerInvariant();
            FolderPath = fi.DirectoryName ?? string.Empty;
            FolderName = fi.Directory?.Name ?? string.Empty;

            FileSizeInBytes = fi.Length;
            FileCreationTimeUtc = fi.CreationTimeUtc;
            FileLastWriteTimeUtc = fi.LastWriteTimeUtc;
            FileLastAccessTimeUtc = fi.LastAccessTimeUtc;
        }
        catch
        {
            FileName = string.Empty;
            FileExtension = string.Empty;
            FolderPath = string.Empty;
            FolderName = string.Empty;

            FileSizeInBytes = 0;
            FileCreationTimeUtc = DateTime.MinValue;
            FileLastWriteTimeUtc = DateTime.MinValue;
            FileLastAccessTimeUtc = DateTime.MinValue;
        }
    }

    public bool IsOutdated()
    {
        if (FrameCount == 0) return true;

        var hasOutdatedCache = true;

        try
        {
            var fi = new FileInfo(FilePath);
            hasOutdatedCache = FileLastWriteTimeUtc < fi.LastWriteTimeUtc;
        }
        catch { }

        return hasOutdatedCache;
    }

    public MagickImage? GetEmbeddedPreview()
    {
        if (RawThumbnail is null && ExifProfile is null) return null;

        MagickImage? thumbM = null;

        if (RawThumbnail is not null && PreviewWidth > 0)
        {
            try
            {
                thumbM = new MagickImage(RawThumbnail.ToReadOnlySpan());
            }
            catch
            {
                thumbM = null;
            }
        }

        if (thumbM is null && ExifProfile is not null)
        {
            thumbM = (MagickImage?)ExifProfile.CreateThumbnail();
        }

        if (thumbM is not null)
        {
            thumbM.Orientation = SkiaCodec.ToMagickOrientation(Orientation);
            thumbM?.AutoOrient();
        }

        return thumbM;
    }

    public async Task OpenLivePhotoVideoAsync()
    {
        if (!IsLivePhoto || EmbeddedVideoOffsetFromEnd == 0) return;

        var videoPath = await Task
            .Run(() => LivePhotoDetector.ExtractEmbeddedVideoAsync(FilePath, EmbeddedVideoOffsetFromEnd))
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(videoPath)) return;

        var psi = new ProcessStartInfo(videoPath)
        {
            UseShellExecute = true,
        };
        using var proc = Process.Start(psi);
    }

}
