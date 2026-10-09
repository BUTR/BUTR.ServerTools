using System;
using System.IO;
using System.Linq;

using Bannerlord.ServerTools;

using Xunit;

namespace Bannerlord.ServerTools.Tests;

public class ImplementationSelectorTests
{
    private static readonly Implementation[] Implementations =
    [
        new("Bannerlord.ServerJoin.v1.0.0.dll", new Version(1, 0, 0)),
        new("Bannerlord.ServerJoin.v1.1.0.dll", new Version(1, 1, 0)),
        new("Bannerlord.ServerJoin.v1.2.6.dll", new Version(1, 2, 6)),
        new("Bannerlord.ServerJoin.v1.2.9.dll", new Version(1, 2, 9)),
        new("Bannerlord.ServerJoin.v1.5.2.dll", new Version(1, 5, 2)),
    ];

    [Theory]
    [InlineData("v1.2.9", "1.2.9")]
    [InlineData("v1.2.9.38256", "1.2.9")]
    [InlineData("e1.8.1", "1.8.1")]
    [InlineData("1.5.2.121216-beta", "1.5.2")]
    [InlineData("v1.10.12", "1.10.12")]
    public void ParseVersion_ReadsMajorMinorRevision(string value, string expected) =>
        Assert.Equal(Version.Parse(expected), ImplementationSelector.ParseVersion(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1.2")]
    [InlineData("Empty")]
    public void ParseVersion_ReturnsNullWithoutAVersion(string? value) =>
        Assert.Null(ImplementationSelector.ParseVersion(value));

    [Theory]
    // Exact baseline match at the start of a range
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.2.9", "1.2.9")]
    // Intermediate patch version within a range
    [InlineData("1.0.3", "1.0.0")]
    [InlineData("1.1.6", "1.1.0")]
    [InlineData("1.2.8", "1.2.6")]
    [InlineData("1.4.8", "1.2.9")]
    [InlineData("1.5.1", "1.2.9")]
    // Version newer than all defined baselines: selects newest available implementation
    [InlineData("1.5.4", "1.5.2")]
    [InlineData("2.0.0", "1.5.2")]
    public void Select_PicksTheHighestBuildNotNewerThanTheGame(string game, string expected) =>
        Assert.Equal(Version.Parse(expected), ImplementationSelector.Select(Implementations, Version.Parse(game))!.GameVersion);

    [Fact]
    public void Select_PicksNothingForAGameOlderThanEveryBuild() =>
        Assert.Null(ImplementationSelector.Select(Implementations, new Version(0, 9, 0)));

    [Fact]
    public void Select_PicksTheNewestBuildWhenTheGameVersionIsUnknown() =>
        Assert.Equal(new Version(1, 5, 2), ImplementationSelector.Select(Implementations, null)!.GameVersion);

    [Fact]
    public void Find_ListsTheModulesBuildsOldestFirst()
    {
        var directory = Directory.CreateTempSubdirectory("ServerTools.Tests.").FullName;
        try
        {
            foreach (var file in new[]
                     {
                         "Bannerlord.ServerJoin.dll", "Bannerlord.ServerJoin.v1.2.9.dll", "Bannerlord.ServerJoin.v1.10.0.dll",
                         "Bannerlord.ServerJoin.v1.0.0.dll", "Bannerlord.ServerJoin.vNext.dll", "Bannerlord.ServerInfo.v1.0.0.dll",
                     })
                File.WriteAllBytes(Path.Combine(directory, file), []);

            var found = ImplementationSelector.Find(directory, "Bannerlord.ServerJoin");

            Assert.Equal(["Bannerlord.ServerJoin.v1.0.0.dll", "Bannerlord.ServerJoin.v1.2.9.dll", "Bannerlord.ServerJoin.v1.10.0.dll"],
                found.Select(implementation => implementation.ToString()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
