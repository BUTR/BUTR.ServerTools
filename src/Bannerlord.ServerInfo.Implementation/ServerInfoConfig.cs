using System;
using System.Collections.Generic;
using System.IO;

namespace Bannerlord.ServerInfo;

/// <summary>
/// Model representing the configuration structure of <c>server-info.json</c> defined by the server administrator.
/// </summary>
internal sealed class ServerInfoConfig
{
    public static readonly ServerInfoConfig Empty = new();

    public string? Description { get; init; }
    public List<LinkDocument>? Links { get; init; }
    public List<ClientModuleConfig>? ClientModules { get; init; }
    public List<string>? HiddenServerModules { get; init; }
    public NexusModsExtensionConfig? NexusMods { get; init; }
}

internal sealed class NexusModsExtensionConfig
{
    public NexusModsCollectionConfig? Collection { get; init; }
}

internal sealed class NexusModsCollectionConfig
{
    public string? Slug { get; init; }
    public int? Revision { get; init; }
    public bool? Required { get; init; }
}

internal sealed class ClientModuleConfig
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Version { get; init; }
    public NexusModsDocument? NexusMods { get; init; }
    public string? Url { get; init; }
}

/// <summary>
/// Manages reading and parsing <c>server-info.json</c> located in the module directory.
/// Invoked during initial startup and subsequently by the <c>reload_server_info</c> console command.
/// </summary>
internal sealed class ServerInfoConfigFile
{
    private readonly Action<string> _info;
    private readonly Action<string> _error;

    public string Path { get; }

    public ServerInfoConfigFile(string path, Action<string> info, Action<string> error)
    {
        Path = path;
        _info = info;
        _error = error;
    }

    /// <summary>
    /// Reads and deserializes the configuration file.
    /// Returns <see cref="ServerInfoConfig.Empty"/> if the file does not exist,
    /// or <see langword="null"/> if the file contains invalid JSON or cannot be read.
    /// </summary>
    public ServerInfoConfig? Read()
    {
        if (!File.Exists(Path))
            return ServerInfoConfig.Empty;

        try
        {
            var config = Json.Deserialize<ServerInfoConfig>(File.ReadAllText(Path)) ?? ServerInfoConfig.Empty;
            _info($"Loaded configuration from '{Path}'.");
            return config;
        }
        catch (Exception e) when (Json.IsInvalidDocument(e) || e is IOException or UnauthorizedAccessException)
        {
            _error($"Failed to read '{Path}': {e.Message}");
            return null;
        }
    }
}
