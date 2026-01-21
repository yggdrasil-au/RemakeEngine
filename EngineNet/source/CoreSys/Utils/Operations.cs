
using System.Collections.Generic;

namespace EngineNet.Core.Utils;

internal sealed class Operations {
    internal static Dictionary<string, object?> ToMap(Tomlyn.Model.TomlTable table)
        => Converters.DocModelConverter.FromTomlTable(table);

    internal static Dictionary<string, object?> ToMap(System.Text.Json.JsonElement obj)
        => Converters.DocModelConverter.FromJsonObject(obj);
}
