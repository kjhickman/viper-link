using System.Reflection;

namespace ViperLink.App;

public static class AppIdentity
{
    public const string ProductName = "ViperLink";
    public const string ExecutableName = "ViperLink";
    public const string LogDirectoryName = "ViperLink";

    public static string DisplayVersion
    {
        get
        {
            var informationalVersion = typeof(AppIdentity).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                var metadataSeparator = informationalVersion.IndexOf('+');
                return metadataSeparator >= 0
                    ? informationalVersion[..metadataSeparator]
                    : informationalVersion;
            }

            return typeof(AppIdentity).Assembly.GetName().Version?.ToString() ?? "dev";
        }
    }
}
