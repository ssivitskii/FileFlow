namespace FileFlow.Core.Operations;

public static class FileFlowPaths
{
    public static string GetDefaultApplicationDataRoot()
    {
        string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            localApplicationData = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
                localApplicationData = Path.Combine(userProfile, ".local", "share");
        }

        if (!Path.IsPathFullyQualified(localApplicationData))
            throw new InvalidOperationException("The platform local application-data directory is unavailable.");

        return Path.Combine(localApplicationData, "FileFlow");
    }
}
