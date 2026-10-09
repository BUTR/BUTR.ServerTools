using System;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bannerlord.ServerInfo;

internal static class Json
{
    private static readonly JsonSerializerSettings WriteSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
    };

    // Default deserialization settings permit JSON comments, trailing commas, case-insensitive property matching, and string-to-number coercion.
    private static readonly JsonSerializerSettings ReadSettings = new();

    public static byte[] Serialize<T>(T value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, WriteSettings));

    public static T? Deserialize<T>(string json) where T : class => JsonConvert.DeserializeObject<T>(json, ReadSettings);

    public static bool IsInvalidDocument(Exception exception) => exception is JsonException;
}
