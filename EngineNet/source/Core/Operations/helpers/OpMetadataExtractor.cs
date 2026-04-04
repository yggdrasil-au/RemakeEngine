
namespace EngineNet.Core.Operations.helpers;

internal class OpMetadataExtractor {

    /// <summary>
    /// Try to get the list of operations defined in the "onsuccess" or "on_success" field of the given operation.
    /// </summary>
    /// <param name="op"></param>
    /// <param name="ops"></param>
    /// <returns></returns>
    internal static bool ExtractSuccessActions(
        IDictionary<string, object?> op,
        out List<Dictionary<string, object?>>? ops
    ) {
        ops = null;
        if (op is null) return false;

        static List<Dictionary<string, object?>>? Coerce(object? value) {
            if (value is null) return null;
            List<Dictionary<string, object?>> list = new List<Dictionary<string, object?>>();
            if (value is IList<object?> arr) {
                foreach (object? item in arr) {
                    if (item is IDictionary<string, object?> map) {
                        list.Add(new Dictionary<string, object?>(map, System.StringComparer.OrdinalIgnoreCase));
                    }
                }
            } else if (value is IDictionary<string, object?> single) {
                list.Add(new Dictionary<string, object?>(single, System.StringComparer.OrdinalIgnoreCase));
            }
            return list.Count > 0 ? list : null;
        }

        if (op.TryGetValue("onsuccess", out object? v1)) {
            ops = Coerce(v1);
            if (ops is not null) return true;
        }
        if (op.TryGetValue("on_success", out object? v2)) {
            ops = Coerce(v2);
            if (ops is not null) return true;
        }
        return false;
    }
}
