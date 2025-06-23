using System.Text.Json.Serialization;

namespace Luau;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class DictionaryJsonSerializeContext : JsonSerializerContext;