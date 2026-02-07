using MoonSharp.Interpreter;

using System.Collections.Generic;

namespace EngineNet.ScriptEngines.Lua.Global;

/// <summary>
/// SQLite database functionality for Lua scripts.
/// Provides secure database access with path validation.
/// </summary>
internal static class Sqlite {
    internal static Table CreateSqliteModule(LuaWorld LuaEnvObj) {

        LuaEnvObj.SqliteModule["open"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite.open(path) requires a string path");
            }

            string path = args[0].String;

            // Security: Restrict SQLite database paths to within the project directory
            if (!EngineNet.ScriptEngines.Security.IsAllowedPath(path)) {
                throw new ScriptRuntimeException($"Access denied: SQLite database path '{path}' is outside allowed workspace areas");
            }

            SqliteHandle handle = new SqliteHandle(LuaEnvObj.LuaScript, path);
            return DynValue.NewTable(CreateSqliteHandleTable(LuaEnvObj, handle));
        });
        return LuaEnvObj.SqliteModule;
    }

    private static Table CreateSqliteHandleTable(LuaWorld LuaEnvObj, SqliteHandle handle) {
        Table SqliteHandleTable = new Table(LuaEnvObj.LuaScript);
        SqliteHandleTable["exec"] = DynValue.NewCallback((ctx, args) => {
            int offset = args.Count > 0 && args[0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle exec(sql [, params])");
            }

            string sql = args[offset].String;
            Table? paramTable = args.Count > offset + 1 && args[offset + 1].Type == DataType.Table ? args[offset + 1].Table : null;
            int affected = handle.Execute(sql, paramTable);
            return DynValue.NewNumber(affected);
        });
        SqliteHandleTable["query"] = DynValue.NewCallback((ctx, args) => {
            int offset = args.Count > 0 && args[0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle query(sql [, params])");
            }

            string sql = args[offset].String;
            Table? paramTable = args.Count > offset + 1 && args[offset + 1].Type == DataType.Table ? args[offset + 1].Table : null;
            return handle.Query(sql, paramTable);
        });
        SqliteHandleTable["begin"] = DynValue.NewCallback((ctx, args) => {
            handle.BeginTransaction();
            return DynValue.Nil;
        });
        SqliteHandleTable["commit"] = DynValue.NewCallback((ctx, args) => {
            handle.Commit();
            return DynValue.Nil;
        });
        SqliteHandleTable["rollback"] = DynValue.NewCallback((ctx, args) => {
            handle.Rollback();
            return DynValue.Nil;
        });
        SqliteHandleTable["close"] = DynValue.NewCallback((ctx, args) => {
            handle.Dispose();
            return DynValue.Nil;
        });
        SqliteHandleTable["dispose"] = SqliteHandleTable.Get("close");
        SqliteHandleTable["__handle"] = UserData.Create(handle);
        return SqliteHandleTable;
    }
}
