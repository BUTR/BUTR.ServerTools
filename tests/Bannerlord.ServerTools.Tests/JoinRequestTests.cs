using System;

using Bannerlord.ServerJoin;

using Xunit;

namespace Bannerlord.ServerTools.Tests;

public class JoinRequestTests
{
    private const string LobbyId = "0f8fad5b-d9cb-469f-a165-70867728950e";

    private static JoinRequest? Parse(string commandLine) => JoinRequest.FromCommandLine(commandLine.Split(' '));

    [Fact]
    public void ReadsTheLobbyIdAndTheAddress()
    {
        var request = Parse($"Bannerlord.exe /multiplayer _MODULES_*Native*Multiplayer*_MODULES_ /joinserver {LobbyId} 203.0.113.7:7210");

        Assert.NotNull(request);
        Assert.Equal(Guid.Parse(LobbyId), request.LobbyId);
        Assert.Equal("203.0.113.7", request.Address);
        Assert.Equal(7210, request.Port);
    }

    [Fact]
    public void ReadsTheValuesInEitherOrder()
    {
        var request = Parse($"/joinserver 203.0.113.7:7210 {LobbyId}");

        Assert.Equal(Guid.Parse(LobbyId), request!.LobbyId);
        Assert.Equal("203.0.113.7", request.Address);
    }

    [Fact]
    public void ReadsOnlyTheLobbyId()
    {
        var request = Parse($"/joinserver {LobbyId} /singleplayer");

        Assert.Equal(Guid.Parse(LobbyId), request!.LobbyId);
        Assert.Null(request.Address);
        Assert.Equal(LobbyId, request.ToString());
    }

    [Theory]
    [InlineData("203.0.113.7:7210", "203.0.113.7", 7210)]
    [InlineData("eu1.example.com:7211", "eu1.example.com", 7211)]
    [InlineData("[2001:db8::1]:7210", "2001:db8::1", 7210)]
    public void ReadsOnlyTheAddress(string value, string address, int port)
    {
        var request = Parse($"/joinserver {value}");

        Assert.Null(request!.LobbyId);
        Assert.Equal(address, request.Address);
        Assert.Equal(port, request.Port);
        Assert.Equal($"{address}:{port}", request.ToString());
    }

    [Fact]
    public void TheArgumentIsCaseInsensitive() => Assert.NotNull(Parse($"/JoinServer {LobbyId}"));

    [Theory]
    [InlineData("Bannerlord.exe /multiplayer")]
    [InlineData("/joinserver")]
    [InlineData("/joinserver /multiplayer")]
    [InlineData("/joinserver 203.0.113.7")]
    [InlineData("/joinserver 203.0.113.7:0")]
    [InlineData("/joinserver 203.0.113.7:65536")]
    [InlineData("/joinserver 203.0.113.7:port")]
    [InlineData("/joinserver :7210")]
    [InlineData("/joinserver not-a-guid")]
    public void IgnoresACommandLineWithoutAServer(string commandLine) => Assert.Null(Parse(commandLine));

    [Fact]
    public void StopsAtTheNextArgument()
    {
        var request = Parse($"/joinserver {LobbyId} /continuegame 203.0.113.7:7210");

        Assert.Null(request!.Address);
    }

    [Fact]
    public void ReadsAtMostTwoValues()
    {
        var request = Parse($"/joinserver {LobbyId} {LobbyId} 203.0.113.7:7210");

        Assert.Null(request!.Address);
    }
}
