using System.Collections.Generic;

namespace EngineNet.Core;

public sealed class EngineConfig {
    public IDictionary<string, object?> Data => _data;

    private Dictionary<string, object?> _data = new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);

}

