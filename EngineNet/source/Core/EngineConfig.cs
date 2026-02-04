using System.Collections.Generic;

namespace EngineNet.Core;

internal sealed class EngineConfig {
    internal IDictionary<string, object?> Data => _data;

    private Dictionary<string, object?> _data = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);

}

