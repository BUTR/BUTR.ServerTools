using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

using Bannerlord.ServerInfo;

using Newtonsoft.Json;

using Xunit;

namespace Bannerlord.ServerTools.Tests;

public sealed class ServerInfoTests : IDisposable
{
    private static readonly ModuleSnapshot Modules = new(
    [
        new LoadedModule("Native", "Native", "v1.4.8.0", IsOfficial: true, IsClientModule: false, IsClientRequired: false),
        new LoadedModule("Multiplayer", "Multiplayer", "v1.4.8.0", IsOfficial: true, IsClientModule: false, IsClientRequired: false),
        new LoadedModule("MyArmorMod", "My Armor Mod", "v2.1.0.0", IsOfficial: false, IsClientModule: true, IsClientRequired: true),
        // MultiplayerOptional module: included in session metadata, but clients may connect without installing it
        new LoadedModule("MyOptionalHud", "My Optional HUD", "v1.1.0.0", IsOfficial: false, IsClientModule: true, IsClientRequired: false),
        new LoadedModule("Bannerlord.ServerInfo", "BUTR Server Info", "v1.0.0.0", IsOfficial: false, IsClientModule: false, IsClientRequired: false),
        new LoadedModule("MyPrivateAdminTools", "Admin Tools", "v1.0.0.0", IsOfficial: false, IsClientModule: false, IsClientRequired: false),
    ]);

    private readonly string _path = Path.Combine(Directory.CreateTempSubdirectory("ServerTools.Tests.").FullName, "server-info.json");
    private readonly List<string> _info = [];
    private readonly List<string> _errors = [];

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);

    private ServerInfoProvider Start(string? config = null)
    {
        if (config is not null)
            File.WriteAllText(_path, config);
        return new ServerInfoProvider(Modules, new ServerInfoConfigFile(_path, _info.Add, _errors.Add));
    }

    /// <summary>
    /// Deserializes and returns the published JSON payload currently cached in the provider.
    /// </summary>
    private static ServerInfoDocument Served(ServerInfoProvider provider) =>
        JsonConvert.DeserializeObject<ServerInfoDocument>(Encoding.UTF8.GetString(provider.Response))!;

    private ServerInfoDocument Build(string? config = null) => Served(Start(config));

    [Fact]
    public void WithoutAConfig_PublishesTheLoadedCommunityModules()
    {
        var document = Build();

        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal(
            [("MyArmorMod", "My Armor Mod", "v2.1.0.0", true), ("MyOptionalHud", "My Optional HUD", "v1.1.0.0", false)],
            document.ClientModules.Select(client => (client.Id, client.Name, client.Version, client.Required)));
        Assert.Equal(["Bannerlord.ServerInfo", "MyPrivateAdminTools"], document.ServerModules.Select(module => module.Id));
        Assert.Null(document.Description);
        Assert.Null(document.NexusMods);
        Assert.Empty(_errors);
    }

    [Fact]
    public void TheConfig_AddsDownloadsAndClientOnlyModules()
    {
        var document = Build("""
        {
          // JSON comments and trailing commas are supported
          "Description": "Siege every evening.",
          "links": [ { "label": "Discord", "url": "https://discord.gg/example" } ],
          "clientModules": [
            { "id": "MyArmorMod", "required": false, "nexusMods": { "modId": "1234", "fileId": 5678 } },
            { "id": "MyOptionalHud", "required": true, "url": "https://example.com/hud" },
            { "id": "MyUiMod", "name": "My UI Mod", "version": "v1.0.0", "required": true, "url": "https://example.com" },
            { "id": "" },
          ],
          "hiddenServerModules": [ "MyPrivateAdminTools" ],
        }
        """);

        Assert.Equal("Siege every evening.", document.Description);
        Assert.Equal("Discord", Assert.Single(document.Links).Label);

        // Module requirement status is governed by game enforcement rather than configuration overrides:
        // - Required when enforced by the lobby
        // - Optional for MultiplayerOptional modules
        // - Optional for client-only modules not loaded by the server (as the lobby does not validate them)
        var armor = document.ClientModules.Single(module => module.Id == "MyArmorMod");
        Assert.True(armor.Required);
        var hud = document.ClientModules.Single(module => module.Id == "MyOptionalHud");
        Assert.Equal((false, "https://example.com/hud"), (hud.Required, hud.Url));
        Assert.Equal((1234, (int?) 5678), (armor.NexusMods!.ModId, armor.NexusMods.FileId));

        var ui = document.ClientModules.Single(module => module.Id == "MyUiMod");
        Assert.Equal(("My UI Mod", "v1.0.0", false, "https://example.com"), (ui.Name, ui.Version, ui.Required, ui.Url));
        Assert.Equal(3, document.ClientModules.Count);

        Assert.Equal(["Bannerlord.ServerInfo"], document.ServerModules.Select(module => module.Id));
        Assert.Single(_info);
    }

    [Theory]
    [InlineData("""{ "nexusMods": { "collection": { "slug": " ABC123 ", "revision": 4 } } }""", "abc123", 4, true)]
    [InlineData("""{ "nexusMods": { "collection": { "slug": "abc123", "revision": 0, "required": false } } }""", "abc123", null, false)]
    public void TheCollection_IsNormalized(string config, string slug, int? revision, bool required)
    {
        var collection = Build(config).NexusMods?.Collection;

        Assert.NotNull(collection);
        Assert.Equal((slug, revision, required), (collection.Slug, collection.Revision, collection.Required));
    }

    [Fact]
    public void ACollectionWithoutASlug_LeavesOutNexusMods() =>
        Assert.Null(Build("""{ "nexusMods": { "collection": { "slug": "  " } } }""").NexusMods);

    [Fact]
    public void ABrokenConfigAtStartup_PublishesTheAutomaticData()
    {
        var document = Build("""{ "description": "unfinished""");

        Assert.Null(document.Description);
        Assert.Equal(2, document.ClientModules.Count);
        Assert.Contains(_path, Assert.Single(_errors));
    }

    [Fact]
    public void TheResponse_IsServedFromMemoryUntilReloaded()
    {
        var provider = Start("""{ "description": "Before" }""");
        var before = provider.Response;

        File.WriteAllText(_path, """{ "description": "After" }""");
        Assert.Same(before, provider.Response);

        Assert.True(provider.Reload());
        Assert.Equal("After", Served(provider).Description);
        Assert.Same(provider.Response, provider.Response);
    }

    [Fact]
    public void Reload_WithoutAFile_PublishesTheAutomaticData()
    {
        var provider = Start("""{ "description": "Before" }""");
        File.Delete(_path);

        Assert.True(provider.Reload());
        Assert.Null(Served(provider).Description);
    }

    [Fact]
    public void Reload_OfABrokenConfig_KeepsThePublishedOne()
    {
        var provider = Start("""{ "description": "Before" }""");
        var before = provider.Response;

        File.WriteAllText(_path, """{ "description": "unfinished""");

        Assert.False(provider.Reload());
        Assert.Same(before, provider.Response);
        Assert.Contains(_path, Assert.Single(_errors));
    }

    [Fact]
    public void TheDocument_IsWrittenInCamelCaseWithoutNulls()
    {
        var json = Encoding.UTF8.GetString(Start("""{ "nexusMods": { "collection": { "slug": "abc123" } } }""").Response);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.TryGetProperty("description", out _));
        Assert.Equal("MyArmorMod", root.GetProperty("clientModules")[0].GetProperty("id").GetString());
        Assert.False(root.GetProperty("clientModules")[0].TryGetProperty("nexusMods", out _));
        Assert.False(root.TryGetProperty("collection", out _));
        var collection = root.GetProperty("nexusMods").GetProperty("collection");
        Assert.Equal("abc123", collection.GetProperty("slug").GetString());
        Assert.False(collection.TryGetProperty("revision", out _));
    }
}
