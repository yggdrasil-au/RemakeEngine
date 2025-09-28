using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using MoonSharp.Interpreter;

namespace EngineNet.Core.ScriptEngines.LuaModules;

/// <summary>
/// SQLite database functionality for Lua scripts.
/// Provides secure database access with path validation.
/// </summary>
internal static class LuaSqliteModule {
    public static Table CreateSqliteModule(Script lua) {
        Table module = new Table(lua);
        module["open"] = DynValue.NewCallback((ctx, args) => {
            if (args.Count < 1 || args[0].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite.open(path) requires a string path");
            }

            String path = args[0].String;
            
            // Security: Restrict SQLite database paths to within the project directory
            if (!LuaSecurity.IsAllowedPath(path)) {
                throw new ScriptRuntimeException($"Access denied: SQLite database path '{path}' is outside allowed workspace areas");
            }
            
            SqliteHandle handle = new SqliteHandle(lua, path);
            return DynValue.NewTable(CreateSqliteHandleTable(lua, handle));
        });
        return module;
    }

    private static Table CreateSqliteHandleTable(Script lua, SqliteHandle handle) {
        Table table = new Table(lua);
        table["exec"] = DynValue.NewCallback((ctx, args) => {
            Int32 offset = args.Count > 0 && args[0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle exec(sql [, params])");
            }

            String sql = args[offset].String;
            Table? paramTable = args.Count > offset + 1 && args[offset + 1].Type == DataType.Table ? args[offset + 1].Table : null;
            Int32 affected = handle.Execute(sql, paramTable);
            return DynValue.NewNumber(affected);
        });
        table["query"] = DynValue.NewCallback((ctx, args) => {
            Int32 offset = args.Count > 0 && args[0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle query(sql [, params])");
            }

            String sql = args[offset].String;
            Table? paramTable = args.Count > offset + 1 && args[offset + 1].Type == DataType.Table ? args[offset + 1].Table : null;
            return handle.Query(sql, paramTable);
        });
        table["begin"] = DynValue.NewCallback((ctx, args) => {
            handle.BeginTransaction();
            return DynValue.Nil;
        });
        table["commit"] = DynValue.NewCallback((ctx, args) => {
            handle.Commit();
            return DynValue.Nil;
        });
        table["rollback"] = DynValue.NewCallback((ctx, args) => {
            handle.Rollback();
            return DynValue.Nil;
        });
        table["close"] = DynValue.NewCallback((ctx, args) => {
            handle.Dispose();
            return DynValue.Nil;
        });
        table["dispose"] = table.Get("close");
        table["__handle"] = UserData.Create(handle);
        return table;
    }
}

/// <summary>
/// SQLite connection handle for Lua scripts.
/// </summary>
internal sealed class SqliteHandle : IDisposable {
    private readonly Script _script;
    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;
    private Boolean _disposed;

    public SqliteHandle(Script script, String path) {
        _script = script;
        String fullPath = Path.GetFullPath(path);
        SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder {
            DataSource = fullPath
        };
        _connection = new SqliteConnection(builder.ConnectionString);
        _connection.Open();
    }

    public Int32 Execute(String sql, Table? parameters) {
        EnsureNotDisposed();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        if (_transaction != null) {
            command.Transaction = _transaction;
        }

        BindParameters(command, parameters);
        return command.ExecuteNonQuery();
    }

    public DynValue Query(String sql, Table? parameters) {
        EnsureNotDisposed();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        if (_transaction != null) {
            command.Transaction = _transaction;
        }

        BindParameters(command, parameters);
        using SqliteDataReader reader = command.ExecuteReader();
        Table result = new Table(_script);
        Int32 index = 1;
        while (reader.Read()) {
            Table row = new Table(_script);
            for (Int32 i = 0; i < reader.FieldCount; i++) {
                String columnName = reader.GetName(i);
                Object? value = reader.GetValue(i);
                row[columnName] = LuaUtilities.ToDynValue(_script, value);
            }
            result[index++] = DynValue.NewTable(row);
        }
        return DynValue.NewTable(result);
    }

    public void BeginTransaction() {
        EnsureNotDisposed();
        _transaction ??= _connection.BeginTransaction();
    }

    public void Commit() {
        if (_disposed) {
            return;
        }

        if (_transaction != null) {
            _transaction.Commit();
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public void Rollback() {
        if (_disposed) {
            return;
        }

        if (_transaction != null) {
            _transaction.Rollback();
            _transaction.Dispose();
            _transaction = null;
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        try {
            _transaction?.Dispose();
            _connection.Dispose();
        } finally {
            _transaction = null;
            _disposed = true;
        }
    }

    private void EnsureNotDisposed() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(SqliteHandle));
        }
    }

    private static void BindParameters(SqliteCommand command, Table? parameters) {
        if (parameters == null) {
            return;
        }

        IDictionary<String, Object?> dict = LuaUtilities.TableToDictionary(parameters);
        foreach (KeyValuePair<String, Object?> kv in dict) {
            SqliteParameter parameter = command.CreateParameter();
            String name = kv.Key;
            if (!name.StartsWith(":", StringComparison.Ordinal) && !name.StartsWith("@", StringComparison.Ordinal) && !name.StartsWith("$", StringComparison.Ordinal)) {
                name = ":" + name;
            }

            parameter.ParameterName = name;
            parameter.Value = kv.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}