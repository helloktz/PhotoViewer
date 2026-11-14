
namespace PhotoViewer.Common.Photoing;

public partial class PhotoColorProfile(byte[]? data)
{
    public byte[]? ProfileData { get; private set; } = data;

    public string GetIccDescription()
    {
        if (ProfileData is null) return string.Empty;

        try
        {
            var tagCount = ReadBE32__(ProfileData, 128);

            for (var i = 132; i + 12 <= ProfileData.Length && i < 132 + tagCount * 12; i += 12)
            {
                var tag = System.Text.Encoding.ASCII.GetString(ProfileData, i, 4);
                if (tag != "desc") continue;

                var offset = ReadBE32__(ProfileData, i + 4);
                var size = ReadBE32__(ProfileData, i + 8);
                if (offset < 0 || size < 8 || offset + size > ProfileData.Length) return string.Empty;

                var elementType = System.Text.Encoding.ASCII.GetString(ProfileData, offset, 4);

                return elementType switch
                {
                    "desc" => ReadTextDescription__(ProfileData, offset, size),
                    "mluc" => ReadMultiLocalizedUnicode__(ProfileData, offset, size),
                    _ => string.Empty,
                };
            }
        }
        catch { }

        return string.Empty;
    }

    private static string ReadTextDescription__(byte[] data, int offset, int size)
    {
        var length = ReadBE32__(data, offset + 8);
        if (length <= 1 || 12 + length > size) return string.Empty;

        return System.Text.Encoding.ASCII.GetString(data, offset + 12, length - 1);
    }

    private static string ReadMultiLocalizedUnicode__(byte[] data, int offset, int size)
    {
        var recordCount = ReadBE32__(data, offset + 8);
        var recordSize = ReadBE32__(data, offset + 12);
        if (recordCount <= 0 || recordSize < 12 || 16 + recordSize > size) return string.Empty;

        var strLength = ReadBE32__(data, offset + 16 + 4);
        var strOffset = ReadBE32__(data, offset + 16 + 8);
        if (strLength <= 0 || strOffset < 0 || strOffset + strLength > size) return string.Empty;

        return System.Text.Encoding.BigEndianUnicode.GetString(data, offset + strOffset, strLength);
    }

    private static int ReadBE32__(byte[] data, int index)
    {
        return (data[index] << 24)
             | (data[index + 1] << 16)
             | (data[index + 2] << 8)
             | (data[index + 3]);
    }
}
