
using System.Collections.Generic;
using System.Text.Json;
using Tomlyn.Model;

using EngineNet.Core.Serialization;

namespace EngineNet.Core.Utils;

internal sealed class Operations {
    internal static Dictionary<string, object?> ToMap(Tomlyn.Model.TomlTable table) => Serialization.DocModelConverter.FromTomlTable(table);

    internal static Dictionary<string, object?> ToMap(System.Text.Json.JsonElement obj) => Serialization.DocModelConverter.FromJsonObject(obj);
}
