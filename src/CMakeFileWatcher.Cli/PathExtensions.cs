using System.Text;

namespace Dragonhill.CMakeFileWatcher.Cli;

internal static class PathHelpers
{
    public static string[] GetPathParts(this string path)
    {
        return path.Split(Path.DirectorySeparatorChar);
    }
    
    public static string GetExtensionOnly(this string path)
    {
        return Path.GetExtension(path).TrimStart('.');
    }
    
    public static bool IsPathParentOf(this IReadOnlyList<string> parentPathParts, IReadOnlyList<string> testPathParts)
    {
        if (testPathParts.Count < parentPathParts.Count)
        {
            return false;
        }

        return !parentPathParts.Where((t, i) => t != testPathParts[i]).Any();
    }

    public static string[] GetRelativePathTo(this IReadOnlyList<string> path, IReadOnlyList<string> parentPath)
    {
        if (!parentPath.IsPathParentOf(path))
        {
            throw new InvalidOperationException($"Path '{string.Join(Path.DirectorySeparatorChar, path)}' is not a child of '{string.Join(Path.DirectorySeparatorChar, parentPath)}'");
        }

        var relativeParts = new string[path.Count - parentPath.Count];

        for (var i = parentPath.Count; i < path.Count; ++i)
        {
            relativeParts[i - parentPath.Count] = path[i];
        }

        return relativeParts;
    }

    public static string GetCombinedPathParts(this Span<string> pathParts)
    {
        StringBuilder stringBuilder = new();

        foreach (var pathPart in pathParts)
        {
            if (stringBuilder.Length > 0)
            {
                stringBuilder.Append(Path.DirectorySeparatorChar);
            }

            stringBuilder.Append(pathPart);
        }

        return stringBuilder.ToString();
    }
}
