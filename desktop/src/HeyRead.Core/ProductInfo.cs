using System.Reflection;
namespace HeyRead.Core;
public static class ProductInfo
{
    public const string PackageName = "xiaoheihe-community-formac";
    public const string DisplayName = "小黑盒Community For Mac";
    public const string BundleId = "com.3egirlsdream.xiaoheihe-community-formac";
    public const string Repository = "3egirlsdream/HeyRead";
    public const string TagPrefix = "mac-v";
    public static string Version => typeof(ProductInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    // Preserve the existing local session, history, drafts and font directory across the product rename.
    public static string DataRoot => OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "HeyRead") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeyRead");
}
