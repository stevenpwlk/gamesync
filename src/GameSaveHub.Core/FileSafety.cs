using System.Security.Cryptography;

namespace GameSaveHub.Core;

public static class FileSafety
{
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public static string GetSafeRelativePath(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Le chemin sort de la racine autorisée.");
        }

        var relative = Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
        if (relative is "." or ".." || relative.StartsWith("../", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chemin relatif invalide.");
        }

        return relative;
    }

    public static bool IsSameOrDescendant(string candidate, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static void RejectReparsePoint(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"Lien ou point de réanalyse refusé : {info.FullName}");
        }
    }

    public static string ResolveDirectoryLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException("Chemin sans racine.");
        var current = root;
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".") return current;

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, segment);
            var info = new DirectoryInfo(candidate);
            if (info.Exists && info.LinkTarget is not null)
            {
                current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new InvalidOperationException($"Lien de dossier impossible à résoudre : {candidate}");
            }
            else
            {
                current = candidate;
            }
        }

        return Path.GetFullPath(current);
    }

    public static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        return !string.IsNullOrWhiteSpace(root) && new DriveInfo(root).DriveType == DriveType.Network;
    }
}
