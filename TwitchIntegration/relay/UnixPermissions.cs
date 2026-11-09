using System.Runtime.Versioning;

namespace TwitchRelay;

/// <summary>
/// Restricts relay state files to the service account on Unix-like systems.
/// Windows deployments rely on NTFS ACLs instead.
/// </summary>
public static class UnixPermissions
{
    public static void RestrictDirectory(string path)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
            // Best effort; the relay still runs if the filesystem rejects chmod.
        }
    }

    public static void RestrictFile(string path)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort.
        }
    }
}
