using System.IO.Compression;
namespace HeyRead.Core.Updates;
public static class UpdateArchive
{
    public static void Validate(string archive)
    {
        using var zip = ZipFile.OpenRead(archive); long size = 0;
        if (zip.Entries.Count is < 1 or > 20000) throw new InvalidDataException("更新压缩包文件数量无效");
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/'); var parts = name.Split('/');
            if (name.StartsWith('/') || parts.Any(p => p is ".." or "." || p.Contains(':') || p.Contains('\0')) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidDataException("更新压缩包含不安全路径或链接");
            if (parts[0] != ProductInfo.PackageName + ".app" && parts[0] != "__MACOSX") throw new InvalidDataException("更新压缩包包含其他应用");
            size = checked(size + entry.Length); if (size > 2147483648L) throw new InvalidDataException("更新压缩包展开后过大");
        }
        if (zip.GetEntry(ProductInfo.PackageName + ".app/Contents/Info.plist") == null) throw new InvalidDataException("更新包缺少应用信息");
    }
}
