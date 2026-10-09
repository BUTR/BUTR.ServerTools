using System;
using System.Globalization;

namespace Bannerlord.ServerJoin;

/// <summary>
/// Parses and represents connection parameters passed via <c>/joinserver [lobby id] [address:port]</c>.
/// The game engine splits arguments by spaces and ignores unrecognized flags, so values must not contain spaces.
/// The lobby ID represents the server registration GUID originally discovered by the launcher;
/// the IP address and port provide a fallback if the server has restarted and re-registered with a new lobby ID.
/// </summary>
internal sealed class JoinRequest
{
    public const string Argument = "/joinserver";

    public Guid? LobbyId { get; private set; }
    public string? Address { get; private set; }
    public int Port { get; private set; }

    public override string ToString() =>
        Address is not null ? $"{Address}:{Port.ToString(CultureInfo.InvariantCulture)}" : LobbyId?.ToString() ?? "";

    public static JoinRequest? FromCommandLine(string[] args)
    {
        var index = Array.FindIndex(args, arg => string.Equals(arg, Argument, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return null;

        var request = new JoinRequest();
        for (var i = index + 1; i < args.Length && i <= index + 2 && !args[i].StartsWith("/", StringComparison.Ordinal); i++)
        {
            if (Guid.TryParse(args[i], out var id))
                request.LobbyId = id;
            else if (TryParseAddress(args[i], out var address, out var port))
                (request.Address, request.Port) = (address, port);
        }
        return request.LobbyId is not null || request.Address is not null ? request : null;
    }

    /// <summary>
    /// Parses an endpoint address and port formatted as <c>host:port</c> or <c>[ipv6]:port</c>.
    /// </summary>
    private static bool TryParseAddress(string value, out string address, out int port)
    {
        address = "";
        port = 0;
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
            return false;
        if (!int.TryParse(value.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
            return false;
        address = value.Substring(0, separator).Trim('[', ']');
        return address.Length > 0;
    }
}
