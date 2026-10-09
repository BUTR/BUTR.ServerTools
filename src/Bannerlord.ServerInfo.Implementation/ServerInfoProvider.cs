using System;
using System.Collections.Generic;
using System.Linq;

namespace Bannerlord.ServerInfo;

internal sealed class ServerInfoProvider
{
    private readonly ModuleSnapshot _modules;
    private readonly ServerInfoConfigFile _configFile;
    private volatile byte[] _response;

    public ServerInfoProvider(ModuleSnapshot modules, ServerInfoConfigFile configFile)
    {
        _modules = modules;
        _configFile = configFile;
        _response = Json.Serialize(Build(configFile.Read() ?? ServerInfoConfig.Empty));
    }

    public byte[] Response => _response;

    public bool Reload()
    {
        if (_configFile.Read() is not { } config)
            return false;

        _response = Json.Serialize(Build(config));
        return true;
    }

    internal ServerInfoDocument Build(ServerInfoConfig config)
    {
        var configured = (config.ClientModules ?? new List<ClientModuleConfig>())
            .Where(module => !string.IsNullOrWhiteSpace(module.Id))
            .GroupBy(module => module.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var hidden = new HashSet<string>(config.HiddenServerModules ?? new List<string>(), StringComparer.Ordinal);

        var community = _modules.Modules.Where(module => !module.IsOfficial).ToList();

        // Module requirement status is governed by the game based on loaded modules; configuration only provides download references.
        var clientModules = community
            .Where(module => module.IsClientModule)
            .Select(module =>
            {
                configured.TryGetValue(module.Id, out var extra);
                return new ClientModuleDocument
                {
                    Id = module.Id,
                    Name = module.Name,
                    Version = module.Version,
                    Required = module.IsClientRequired,
                    NexusMods = extra?.NexusMods,
                    Url = extra?.Url,
                };
            })
            .ToList();

        var loadedIds = new HashSet<string>(_modules.Modules.Select(module => module.Id), StringComparer.Ordinal);
        clientModules.AddRange(configured.Values
            .Where(module => !loadedIds.Contains(module.Id!))
            .Select(module => new ClientModuleDocument
            {
                Id = module.Id!,
                Name = module.Name,
                Version = module.Version,
                // Modules not loaded by the server are not enforced by the lobby, so they are always marked as optional.
                Required = false,
                NexusMods = module.NexusMods,
                Url = module.Url,
            }));

        var serverModules = community
            .Where(module => !module.IsClientModule && !hidden.Contains(module.Id))
            .Select(module => new ModuleDocument { Id = module.Id, Name = module.Name, Version = module.Version })
            .ToList();

        return new ServerInfoDocument
        {
            Description = string.IsNullOrWhiteSpace(config.Description) ? null : config.Description,
            Links = config.Links ?? new List<LinkDocument>(),
            ServerModules = serverModules,
            ClientModules = clientModules,
            NexusMods = ToNexusMods(config.NexusMods),
        };
    }

    /// <summary>
    /// Converts Nexus Mods collection configuration to its document representation. Returns <see langword="null"/> if unconfigured or empty.
    /// </summary>
    private static NexusModsExtensionDocument? ToNexusMods(NexusModsExtensionConfig? nexusMods) =>
        ToCollection(nexusMods?.Collection) is { } collection ? new NexusModsExtensionDocument { Collection = collection } : null;

    private static NexusModsCollectionDocument? ToCollection(NexusModsCollectionConfig? collection)
    {
        var slug = collection?.Slug?.Trim().ToLowerInvariant();
        if (slug is null or "")
            return null;

        return new NexusModsCollectionDocument
        {
            Slug = slug,
            Revision = collection!.Revision is > 0 ? collection.Revision : null,
            Required = collection.Required ?? true,
        };
    }
}
