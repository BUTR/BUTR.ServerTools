using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bannerlord.ServerTools;

/// <summary>
/// Represents a specific implementation build (<c>&lt;ModuleId&gt;.v&lt;version&gt;.dll</c>) compiled against a baseline game version.
/// </summary>
internal sealed class Implementation
{
    public string Path { get; }
    public Version GameVersion { get; }

    public Implementation(string path, Version gameVersion)
    {
        Path = path;
        GameVersion = gameVersion;
    }

    public override string ToString() => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Selects the appropriate implementation build for a given game version.
/// Each implementation is compiled against the earliest game version of its compatibility range;
/// therefore, the correct build for any game version is the highest baseline that is less than or equal to that game version.
/// Shared across the runtime loader, automated tests, and <c>AbiCheck</c> tooling to ensure consistency.
/// </summary>
internal static class ImplementationSelector
{
    private static readonly Regex VersionPattern = new(@"(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses the major, minor, and revision components from a game version string
    /// (e.g. <c>v1.2.9</c>, <c>v1.2.9.38256</c>, <c>e1.8.1</c>, or <c>1.5.2.121216-beta</c>).
    /// </summary>
    public static Version? ParseVersion(string? value)
    {
        var match = VersionPattern.Match(value ?? "");
        return match.Success
            ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
            : null;
    }

    /// <summary>
    /// Discovers all available implementation builds in a directory, ordered from oldest to newest.
    /// </summary>
    public static IReadOnlyList<Implementation> Find(string directory, string moduleId) => Directory
        .GetFiles(directory, $"{moduleId}.v*.dll")
        .Select(path => (Path: path, Version: ParseVersion(System.IO.Path.GetFileNameWithoutExtension(path).Substring(moduleId.Length + 2))))
        .Where(implementation => implementation.Version is not null)
        .Select(implementation => new Implementation(implementation.Path, implementation.Version!))
        .OrderBy(implementation => implementation.GameVersion)
        .ToList();

    /// <summary>
    /// Selects the implementation build matching the specified game version (the highest baseline not newer than the game).
    /// If the game version is unknown (<see langword="null"/>), falls back to the newest build.
    /// Returns <see langword="null"/> if the game version is older than all available builds.
    /// </summary>
    public static Implementation? Select(IEnumerable<Implementation> implementations, Version? gameVersion) => implementations
        .Where(implementation => gameVersion is null || implementation.GameVersion <= gameVersion)
        .OrderByDescending(implementation => implementation.GameVersion)
        .FirstOrDefault();
}
