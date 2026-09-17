using System.Text;
using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

internal static partial class SetupEnvironment {


    /// <summary>
    /// Creates the restricted os table for the Lua environment.
    /// </summary>
    private static void CreateOsTable(LuaWorld _LuaWorld) {

        // date and time functions

        // os.date -
        _LuaWorld.Os[key: "date"] = (System.Func<string?, DynValue>)((format) => {
            if (string.IsNullOrEmpty(format)) return DynValue.NewNumber(num: System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            if (format.StartsWith("*t") || format.StartsWith("!*t")) {
                System.DateTime dt = format.StartsWith('!') ? System.DateTime.UtcNow : System.DateTime.Now;
                Table DateTable = new(owner: _LuaWorld.LuaScript);
                DateTable[key: "year"] = dt.Year;
                DateTable[key: "month"] = dt.Month;
                DateTable[key: "day"] = dt.Day;
                DateTable[key: "hour"] = dt.Hour;
                DateTable[key: "min"] = dt.Minute;
                DateTable[key: "sec"] = dt.Second;
                DateTable[key: "wday"] = (int)dt.DayOfWeek + 1;
                DateTable[key: "yday"] = dt.DayOfYear;
                DateTable[key: "isdst"] = dt.IsDaylightSavingTime();
                return DynValue.NewTable(table: DateTable);
            }

            if (TryTranslateLuaDateFormat(format: format, dotNetFormat: out string dotNetFormat, useUtc: out bool useUtc)) {
                System.DateTime dt = useUtc ? System.DateTime.UtcNow : System.DateTime.Now;
                return DynValue.NewString(str: dt.ToString(format: dotNetFormat));
            }

            return DynValue.NewString(str: System.DateTime.Now.ToString(format: "yyyy-MM-dd HH:mm:ss"));
        });
        _LuaWorld.Os[key: "time"] = (System.Func<DynValue?, double>)((DynValue? timeTable) => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _LuaWorld.Os[key: "clock"] = () => System.Environment.TickCount / 1000.0;

        //

        // getenv - deny access to a specific set of environment variables to prevent information leaks
        _LuaWorld.Os[key: "getenv"] = (string env) => {
            if (DisallowedEnv.Contains(item: env)) return null;
            return System.Environment.GetEnvironmentVariable(variable: env);
        };
        // removed os.execute for better alternatives via sdk.exec/run_process etc
        _LuaWorld.Os[key: "execute"] = DynValue.Nil;

        _LuaWorld.Os[key: "exit"] = (System.Action<int?>)(code => {
            throw new ScriptExitException(exitCode: code ?? 0);
        });

        _LuaWorld.LuaScript.Globals[key: "os"] = _LuaWorld.Os;
    }

    private static bool TryTranslateLuaDateFormat(string format, out string dotNetFormat, out bool useUtc) {
        useUtc = false;
        dotNetFormat = string.Empty;

        Dictionary<char, string> LuaDateFormatMap = new() {
            { 'Y', "yyyy" },
            { 'y', "yy" },
            { 'm', "MM" },
            { 'd', "dd" },
            { 'H', "HH" },
            { 'M', "mm" },
            { 'S', "ss" },
            { 'b', "MMM" },
            { 'B', "MMMM" },
            { 'a', "ddd" },
            { 'A', "dddd" }
        };

        if (string.IsNullOrWhiteSpace(format)) {
            return false;
        }

        if (format.StartsWith('!')) {
            useUtc = true;
            format = format[1..];
        }

        if (format.StartsWith("*t")) {
            return false;
        }

        StringBuilder builder = new(capacity: format.Length * 2);

        for (int i = 0; i < format.Length; i++) {
            char current = format[index: i];

            if (current == '%') {
                if (i + 1 >= format.Length) {
                    return false;
                }

                char token = format[index: i + 1];
                if (token == '%') {
                    builder.Append("%%");
                } else if (LuaDateFormatMap.TryGetValue(key: token, out string? mapped)) {
                    builder.Append(mapped);
                } else {
                    return false;
                }

                i++;
                continue;
            }

            if (!IsLuaDateLiteral(current)) {
                return false;
            }

            builder.Append(current);
        }

        dotNetFormat = builder.ToString();
        return dotNetFormat.Length > 0;
    }

    private static bool IsLuaDateLiteral(char value) {
        if (char.IsDigit(c: value) || char.IsWhiteSpace(c: value)) {
            return true;
        }

        return value switch {
            '-' or '_' or ':' or '/' or '.' or ',' or '(' or ')' or '[' or ']' or '|' => true,
            _ => false
        };
    }
}
