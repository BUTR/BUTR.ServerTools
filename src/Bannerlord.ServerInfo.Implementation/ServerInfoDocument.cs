using System.Collections.Generic;

namespace Bannerlord.ServerInfo;

/// <summary>
/// Represents the JSON payload returned by <c>GET /butr/v1/server</c> (Schema Version 1).
/// The formal schema specification is defined in <c>docs/server-info-endpoint.md</c>.
/// Schema version 1 evolves strictly via additive, backward-compatible fields.
/// </summary>
internal sealed class ServerInfoDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string? Description { get; init; }
    public List<LinkDocument> Links { get; init; } = new();
    public List<ModuleDocument> ServerModules { get; init; } = new();
    public List<ClientModuleDocument> ClientModules { get; init; } = new();
    public NexusModsExtensionDocument? NexusMods { get; init; }
}

internal sealed class NexusModsExtensionDocument
{
    public NexusModsCollectionDocument? Collection { get; init; }
}

internal sealed class NexusModsCollectionDocument
{
    public string Slug { get; init; } = "";
    public int? Revision { get; init; }
    public bool Required { get; init; } = true;
}

internal sealed class LinkDocument
{
    public string? Label { get; init; }
    public string? Url { get; init; }
}

internal class ModuleDocument
{
    public string Id { get; init; } = "";
    public string? Name { get; init; }
    public string? Version { get; init; }
}

internal sealed class ClientModuleDocument : ModuleDocument
{
    public bool Required { get; init; }
    public NexusModsDocument? NexusMods { get; init; }
    public string? Url { get; init; }
}

internal sealed class NexusModsDocument
{
    public int ModId { get; init; }
    public int? FileId { get; init; }
}
