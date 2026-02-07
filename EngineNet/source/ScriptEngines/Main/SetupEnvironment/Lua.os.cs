using System.Text;
using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua;

public static partial class SetupEnvironment {


    /// <summary>
    /// Creates the restricted os table for the Lua environment.
    /// </summary>
    public static void CreateOsTable(LuaWorld _LuaWorld) {

        // date and time functions

        // os.date -
        _LuaWorld.os["date"] = (System.Func<string?, DynValue>)((format) => {
            if (string.IsNullOrEmpty(format)) return DynValue.NewNumber(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            if (format.StartsWith("*t") || format.StartsWith("!*t")) {
                System.DateTime dt = format.StartsWith("!") ? System.DateTime.UtcNow : System.DateTime.Now;
                Table DateTable = new Table(_LuaWorld.LuaScript);
                DateTable["year"] = dt.Year;
                DateTable["month"] = dt.Month;
                DateTable["day"] = dt.Day;
                DateTable["hour"] = dt.Hour;
                DateTable["min"] = dt.Minute;
                DateTable["sec"] = dt.Second;
                DateTable["wday"] = (int)dt.DayOfWeek + 1;
                DateTable["yday"] = dt.DayOfYear;
                DateTable["isdst"] = dt.IsDaylightSavingTime();
                return DynValue.NewTable(DateTable);
            }

            if (TryTranslateLuaDateFormat(format, out string dotNetFormat, out bool useUtc)) {
                System.DateTime dt = useUtc ? System.DateTime.UtcNow : System.DateTime.Now;
                return DynValue.NewString(dt.ToString(dotNetFormat));
            }

            return DynValue.NewString(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        });
        _LuaWorld.os["time"] = (System.Func<DynValue?, double>)((DynValue? timeTable) => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _LuaWorld.os["clock"] = () => System.Environment.TickCount / 1000.0;

        //

        // getenv - deny access to a specific set of environment variables to prevent information leaks
        _LuaWorld.os["getenv"] = (string env) => {
            if (DisallowedEnv.Contains(env)) return null;
            return System.Environment.GetEnvironmentVariable(env);
        };
        // removed os.execute for better alternatives via sdk.exec/run_process etc
        _LuaWorld.os["execute"] = DynValue.Nil;

        _LuaWorld.os["exit"] = (System.Action<int?>)(code => {
            throw new ScriptExitException(code ?? 0);
        });

        _LuaWorld.LuaScript.Globals["os"] = _LuaWorld.os;
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

        if (format.StartsWith("!")) {
            useUtc = true;
            format = format[1..];
        }

        if (format.StartsWith("*t")) {
            return false;
        }

        StringBuilder builder = new StringBuilder(format.Length * 2);

        for (int i = 0; i < format.Length; i++) {
            char current = format[i];

            if (current == '%') {
                if (i + 1 >= format.Length) {
                    return false;
                }

                char token = format[i + 1];
                if (token == '%') {
                    builder.Append("%%");
                } else if (LuaDateFormatMap.TryGetValue(token, out string? mapped)) {
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
        if (char.IsDigit(value) || char.IsWhiteSpace(value)) {
            return true;
        }

        return value switch {
            '-' or '_' or ':' or '/' or '.' or ',' or '(' or ')' or '[' or ']' or '|' => true,
            _ => false
        };
    }
}
