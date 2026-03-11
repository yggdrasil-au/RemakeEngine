
using System.Collections.Generic;
using System.Text.Json;
using Tomlyn.Model;

using EngineNet.Core.Serialization;

namespace EngineNet.Core.Utils;

public sealed class Operations {
    public static Dictionary<string, object?> ToMap(Tomlyn.Model.TomlTable table) => Serialization.DocModelConverter.FromTomlTable(table);

    public static Dictionary<string, object?> ToMap(System.Text.Json.JsonElement obj) => Serialization.DocModelConverter.FromJsonObject(obj);
}
