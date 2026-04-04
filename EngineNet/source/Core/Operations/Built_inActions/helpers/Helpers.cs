namespace EngineNet.Core.Operations.Built_inActions;

internal class Helpers {

    internal static Dictionary<string, object?> BuildOperationContext(
        Engine.EngineContext context,
        string currentGame,
        Dictionary<string, Core.Data.GameModuleInfo> games
    ) {
        return Core.Utils.ExecutionContextBuilder.Build(currentGame, games, context.EngineConfig.Data);
    }

    internal static List<string> ResolveOperationArgs(IDictionary<string, object?> op,IDictionary<string, object?> ctx) {
        List<string> args = new List<string>();
        if (op.TryGetValue("args", out object? argsObject) && argsObject is System.Collections.IList rawArgs) {
            object? resolvedArgsObject = Core.Utils.Placeholders.Resolve(argsObject, ctx);
            System.Collections.IList resolvedArgs = resolvedArgsObject as System.Collections.IList ?? rawArgs;
            foreach (object? arg in resolvedArgs) {
                if (arg is not null) {
                    args.Add(arg.ToString()!);
                }
            }
        }

        return args;
    }

    internal static string? ResolveOperationValue(
        IDictionary<string, object?> op,
        string key,
        IDictionary<string, object?> ctx,
        bool fallbackToRawValue = false
    ) {
        if (!op.TryGetValue(key, out object? rawValue) || rawValue is null) {
            return null;
        }

        object? resolvedValue = Core.Utils.Placeholders.Resolve(rawValue, ctx);
        string? value = GetFirstValueAsString(resolvedValue);
        if (!string.IsNullOrWhiteSpace(value)) {
            return value;
        }

        if (!fallbackToRawValue) {
            return value;
        }

        return GetFirstValueAsString(rawValue);
    }

    internal static string? GetFirstValueAsString(object? value) {
        if (value is System.Collections.IList list) {
            if (list.Count == 0) {
                return null;
            }

            return list[0]?.ToString();
        }

        return value?.ToString();
    }

    internal static string? GetFieldOrFirstArgRawValue(IDictionary<string, object?> op, string fieldName) {
        if (op.TryGetValue(fieldName, out object? explicitValue) && explicitValue is not null) {
            return explicitValue.ToString();
        }

        if (op.TryGetValue("args", out object? argsObj) && argsObj is System.Collections.IList list && list.Count > 0) {
            return list[0]?.ToString();
        }

        return null;
    }
}