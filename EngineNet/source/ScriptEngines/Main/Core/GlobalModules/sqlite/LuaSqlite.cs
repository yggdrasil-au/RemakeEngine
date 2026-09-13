using MoonSharp.Interpreter;

namespace EngineNet.ScriptEngines.Lua.Global;

/// <summary>
/// SQLite database functionality for Lua scripts.
/// Provides secure database access with path validation.
/// </summary>
internal static class Sqlite {
    internal static void CreateSqliteModule(LuaWorld _LuaWorld) {

        _LuaWorld.SqliteModule[key: "open"] = DynValue.NewCallback(callBack: (ctx, args) => {
            if (args.Count < 1 || args[index: 0].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite.open(path) requires a string path");
            }

            string path = args[index: 0].String;

            // Security: Restrict SQLite database paths to within the project directory
            if (!EngineNet.ScriptEngines.Security.IsAllowedPath(path: path)) {
                throw new ScriptRuntimeException($"Access denied: SQLite database path '{path}' is outside allowed workspace areas");
            }

            SqliteHandle handle = new SqliteHandle(script: _LuaWorld.LuaScript, path: path);
            return DynValue.NewTable(table: CreateSqliteHandleTable(_LuaWorld: _LuaWorld, handle: handle));
        });
        // return _LuaWorld.SqliteModule;
        _LuaWorld.LuaScript.Globals[key: "sqlite"] = _LuaWorld.SqliteModule;
    }

    private static Table CreateSqliteHandleTable(LuaWorld _LuaWorld, SqliteHandle handle) {
        Table SqliteHandleTable = new Table(owner: _LuaWorld.LuaScript);
        SqliteHandleTable[key: "exec"] = DynValue.NewCallback(callBack: (ctx, args) => {
            int offset = args.Count > 0 && args[index: 0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[index: offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle exec(sql [, params])");
            }

            string sql = args[index: offset].String;
            Table? paramTable = args.Count > offset + 1 && args[index: offset + 1].Type == DataType.Table ? args[index: offset + 1].Table : null;
            int affected = handle.Execute(sql: sql, parameters: paramTable);
            return DynValue.NewNumber(num: affected);
        });
        SqliteHandleTable[key: "query"] = DynValue.NewCallback(callBack: (ctx, args) => {
            int offset = args.Count > 0 && args[index: 0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[index: offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle query(sql [, params])");
            }

            string sql = args[index: offset].String;
            Table? paramTable = args.Count > offset + 1 && args[index: offset + 1].Type == DataType.Table ? args[index: offset + 1].Table : null;
            return handle.Query(sql: sql, parameters: paramTable);
        });
        SqliteHandleTable[key: "begin"] = DynValue.NewCallback(callBack: (ctx, args) => {
            handle.BeginTransaction();
            return DynValue.Nil;
        });
        SqliteHandleTable[key: "commit"] = DynValue.NewCallback(callBack: (ctx, args) => {
            handle.Commit();
            return DynValue.Nil;
        });
        SqliteHandleTable[key: "rollback"] = DynValue.NewCallback(callBack: (ctx, args) => {
            handle.Rollback();
            return DynValue.Nil;
        });
        SqliteHandleTable[key: "close"] = DynValue.NewCallback(callBack: (ctx, args) => {
            handle.Dispose();
            return DynValue.Nil;
        });
        SqliteHandleTable[key: "dispose"] = SqliteHandleTable.Get(key: "close");
        SqliteHandleTable[key: "__handle"] = UserData.Create(o: handle);
        return SqliteHandleTable;
    }
}
