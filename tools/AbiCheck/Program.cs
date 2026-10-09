// AbiCheck: Validates a laid-out Bannerlord module against every game build published as reference assemblies.
//
// Usage:
//   dotnet run --project tools/AbiCheck -- --module artifacts/Bannerlord.ServerJoin/bin/Win64_Shipping_Client \
//       --id Bannerlord.ServerJoin --package Bannerlord.ReferenceAssemblies.Core --package Bannerlord.ReferenceAssemblies.Multiplayer
//
// For each version of the primary package (from the minimum game version supported by the module, including betas):
// verifies that the module loader and the implementation build selected by ImplementationSelector can resolve every
// referenced TaleWorlds type and member with the exact expected method and field signatures. Additional packages
// are included when they share the same build version.
// Exits with code 1 if any referenced types or members are missing or have mismatched signatures.

using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;

using AbiCheck;

using Bannerlord.ServerTools;

string? module = null, id = null, cache = null, steamApp = null;
var packages = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--module": module = args[++i]; break;
        case "--id": id = args[++i]; break;
        case "--package": packages.Add(args[++i]); break;
        case "--cache": cache = args[++i]; break;
        case "--steam-app": steamApp = args[++i]; break;
        default:
            Console.Error.WriteLine($"Unknown argument: '{args[i]}'");
            return 2;
    }
}
if (module is null || id is null || packages.Count == 0)
{
    Console.Error.WriteLine("Usage: AbiCheck --module <module bin folder> --id <ModuleId> --package <reference package id>... [--steam-app <app id>] [--cache <folder>]");
    Console.Error.WriteLine("Options:");
    Console.Error.WriteLine("  --steam-app <app id>  Target Steam App ID (e.g. 261550 for game client, 1863440 for dedicated server).");
    Console.Error.WriteLine("                        Checks only builds actively served by Steam branches from BUTR's build registry.");
    Console.Error.WriteLine("                        If omitted, checks every build published as reference assemblies.");
    Console.Error.WriteLine("  --cache <folder>      Directory where downloaded reference assemblies are cached.");
    return 2;
}
cache ??= Path.Combine(Path.GetTempPath(), "abicheck");

var loader = Path.Combine(module, id + ".dll");
var implementations = ImplementationSelector.Find(module, id);
if (!File.Exists(loader) || implementations.Count == 0)
{
    Console.Error.WriteLine($"Could not find '{id}.dll' or any '{id}.v*.dll' implementations in '{module}'. Please build the module first.");
    return 2;
}

using var http = new HttpClient();
var versionsByPackage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
foreach (var package in packages)
{
    var index = await http.GetFromJsonAsync<PackageIndex>($"https://api.nuget.org/v3-flatcontainer/{package.ToLowerInvariant()}/index.json");
    versionsByPackage[package] = new HashSet<string>(index?.Versions ?? [], StringComparer.OrdinalIgnoreCase);
}

var published = versionsByPackage[packages[0]];
var candidates = steamApp is null ? published : await ServedBuilds(steamApp, published);
var builds = candidates
    .Select(version => (Package: version, Game: ImplementationSelector.ParseVersion(version)))
    .Where(build => build.Game is not null && build.Game >= implementations[0].GameVersion)
    .OrderBy(build => build.Game).ThenBy(build => build.Package, StringComparer.Ordinal)
    .ToList();

var failed = 0;
var summary = new StringBuilder($"### {id}\n\n| Game build | Implementation | Result |\n|---|---|---|\n");
foreach (var (packageVersion, gameVersion) in builds)
{
    var implementation = ImplementationSelector.Select(implementations, gameVersion)!;
    var referenceDirectories = new List<string>();
    foreach (var package in packages.Where(package => versionsByPackage[package].Contains(packageVersion)))
        referenceDirectories.AddRange(await ReferenceDirectories(package, packageVersion));

    var missing = new List<string>();
    var checkedCount = 0;
    foreach (var assembly in new[] { loader, implementation.Path })
    {
        var result = ReferenceChecker.Check(assembly, referenceDirectories);
        checkedCount += result.Checked;
        missing.AddRange(result.Missing.Select(reference => $"{Path.GetFileName(assembly)}: {reference}"));
    }

    var status = missing.Count == 0 ? $"OK ({checkedCount} references verified)" : $"FAILED ({missing.Count} of {checkedCount} references missing)";
    Console.WriteLine($"{packageVersion,-22} {implementation,-36} {status}");
    foreach (var reference in missing)
        Console.WriteLine($"    MISSING {reference}");
    summary.Append($"| {packageVersion} | {implementation} | {(missing.Count == 0 ? "✅" : "❌ " + string.Join("<br>", missing.Select(m => $"`{m}`")))} |\n");
    if (missing.Count > 0)
        failed++;
}

Console.WriteLine(failed == 0
    ? $"{id}: All {builds.Count} builds passed (from v{implementations[0].GameVersion})."
    : $"{id}: {failed} of {builds.Count} builds failed ABI verification.");
if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } stepSummary)
    File.AppendAllText(stepSummary, summary + "\n");
return failed == 0 ? 0 : 1;

// Retrieves the package versions of builds currently served by Steam branches, using the registry
// maintained by Bannerlord.ReferenceAssemblies. Builds can be published as <version>.<changeset>,
// repacked as <version>.<changeset>1, or marked as prerelease with '-beta'.
async Task<IEnumerable<string>> ServedBuilds(string app, HashSet<string> publishedVersions)
{
    var registry = await http.GetFromJsonAsync<BuildRegistry>($"https://raw.githubusercontent.com/BUTR/Bannerlord.ReferenceAssemblies/master/builds/steam/{app}.json")
                   ?? throw new InvalidOperationException($"Could not find a build registry for Steam App ID {app}.");
    var served = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var buildId in registry.Current.Values.Distinct())
    {
        if (registry.Builds.FirstOrDefault(build => build.BuildId == buildId) is not { Version: { } version, ChangeSet: { } changeSet })
            continue;
        var prefix = $"{version.TrimStart('v', 'e')}.{changeSet}";
        var package = new[] { prefix, prefix + "1", prefix + "-beta", prefix + "1-beta" }.FirstOrDefault(publishedVersions.Contains);
        if (package is null)
            Console.WriteLine($"{prefix,-22} Not published as reference assemblies; skipping check.");
        else
            served.Add(package);
    }
    return served;
}

// Downloads and extracts the reference assembly directories for a package version into the local cache.
// Directories are ordered with 'netstandard2.0' prioritized over 'net472' when an assembly exists in both.
async Task<IEnumerable<string>> ReferenceDirectories(string package, string version)
{
    var directory = Path.Combine(cache, package.ToLowerInvariant(), version.ToLowerInvariant());
    if (!Directory.Exists(Path.Combine(directory, "ref")))
    {
        var url = $"https://api.nuget.org/v3-flatcontainer/{package.ToLowerInvariant()}/{version.ToLowerInvariant()}/{package.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg";
        await using var download = await http.GetStreamAsync(url);
        using var buffer = new MemoryStream();
        await download.CopyToAsync(buffer);
        using var archive = new ZipArchive(buffer);
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith("ref/", StringComparison.Ordinal) && entry.Name.Length > 0))
        {
            var target = Path.GetFullPath(Path.Combine(directory, entry.FullName));
            if (!target.StartsWith(Path.GetFullPath(directory), StringComparison.Ordinal))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    var references = Path.Combine(directory, "ref");
    return Directory.Exists(references)
        ? Directory.GetDirectories(references).OrderBy(folder => Path.GetFileName(folder) switch { "netstandard2.0" => 0, "net472" => 1, _ => 2 })
        : [];
}

internal sealed record PackageIndex(List<string> Versions);

internal sealed record BuildRegistry(Dictionary<string, long> Current, List<RegistryBuild> Builds);

internal sealed record RegistryBuild(long BuildId, string? Version, long? ChangeSet);
