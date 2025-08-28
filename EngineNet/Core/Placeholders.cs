using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RemakeEngine.Core;

public static class Placeholders
{
    private static readonly Regex PlaceholderRe = new(@"\{\{([\w\.]+)\}\}", RegexOptions.Compiled);

    public static object? Resolve(object? value, IDictionary<string, object?> context)
    {
        if (value is null) return null;

        if (value is IDictionary<string, object?> dict)
        {
            var outDict = new Dictionary<string, object?>(dict.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in dict)
                outDict[kv.Key] = Resolve(kv.Value, context);
            return outDict;
        }

        if (value is IList list)
        {
            var outList = new List<object?>(list.Count);
            foreach (var item in list)
                outList.Add(Resolve(item, context));
            return outList;
        }

        if (value is string s)
        {
            return PlaceholderRe.Replace(s, m => Lookup(context, m.Groups[1].Value) ?? m.Value);
        }

        return value;
    }

    private static string? Lookup(IDictionary<string, object?> ctx, string dotted)
    {
        object? current = ctx;
        foreach (var part in dotted.Split('.'))
        {
            if (current is IDictionary<string, object?> d)
            {
                if (!d.TryGetValue(part, out current))
                    return null;
            }
            else return null;
        }
        return current?.ToString();
    }
}

