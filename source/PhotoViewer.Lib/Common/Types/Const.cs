using Avalonia.Media;
using System.Globalization;

namespace PhotoViewer.Common.Types;

public static class Const
{
    public const int TOOLBAR_ICON_HEIGHT = 24;
    public const string CONFIG_CMD_PREFIX = "-p:";
    public const string DATETIME_FORMAT = "yyyy/MM/dd HH:mm:ss";
    public const string DATE_FORMAT = "yyyy/MM/dd";
    public const string APP_PROTOCOL = "photo-viewer";

    public const string PORTABLE_MARKER_FILE = ".igportable";

    public static readonly string SIGN_POSITIVE = NumberFormatInfo.CurrentInfo.PositiveSign;
    public static readonly string SIGN_NEGATIVE = NumberFormatInfo.CurrentInfo.NegativeSign;
    public static readonly string DECIMAL_SEPARATOR = NumberFormatInfo.CurrentInfo.NumberDecimalSeparator;
    public static readonly Color COLOR_EMPTY = new Color(0, 0, 0, 0);
    public static readonly int MOUSE_WHEEL_SCROLL_DELTA = 120;

    public const string FILE_MACRO = "<file>";
    public const string THEME_SYSTEM_ACCENT = "accent";

    public const double QUICK_SETUP_VERSION = 10f;

    public const string DEFAULT_THEME = "Kobe";

    public const string DEFAULT_LIGHT_THEME = "Kobe-Light";

    public const string IMAGE_FORMATS = ".3fr;.ari;.arw;.avif;.bay;.bmp;.cap;.cr2;.cr3;.crw;.cur;.cut;.dcr;.dcs;.dds;.dib;.dng;.drf;.eip;.emf;.erf;.exif;.exr;.fax;.fff;.fits;.flif;.gif;.gifv;.gpr;.hdr;.heic;.heif;.hif;.ico;.iiq;.jfif;.jp2;.jpe;.jpeg;.jpg;.jxl;.k25;.kdc;.mdc;.mef;.mjpeg;.mos;.mrw;.nef;.nrw;.obm;.orf;.pbm;.pcx;.pef;.pgm;.png;.ppm;.psb;.psd;.ptx;.pxn;.qoi;.r3d;.raf;.raw;.rw2;.rwl;.rwz;.sr2;.srf;.srw;.svg;.svgz;.tga;.tif;.tiff;.viff;.webp;.wmf;.wpg;.x3f;.xbm;.xpm;.xv";

    public static readonly string FONT_CODE = BHelper.OS switch
    {
        OSType.Mac => "SF Mono, Menlo, Monaco, Courier New, monospace",
        OSType.Windows => "Cascadia Code, Cascadia Mono, Consolas, Courier New, monospace",
        _ => "DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace",
    };

    private static readonly double FONT_SIZE_MAC_OFFSET = BHelper.OS != OSType.Mac ? -1 : 0;
    public static readonly double FONT_SIZE_BODY = BHelper.OS == OSType.Mac ? 12.5 : 13;
    public static readonly double FONT_SIZE_TITLE = 22 + FONT_SIZE_MAC_OFFSET;
    public static readonly double FONT_SIZE_SUBTITLE = 18 + FONT_SIZE_MAC_OFFSET;
    public static readonly double FONT_SIZE_SMALL = 13 + FONT_SIZE_MAC_OFFSET;

}
