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

            string path = args[0].String;

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
            int offset = args.Count > 0 && args[0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle exec(sql [, params])");
            }

            string sql = args[offset].String;
            Table? paramTable = args.Count > offset + 1 && args[offset + 1].Type == DataType.Table ? args[offset + 1].Table : null;
            int affected = handle.Execute(sql, paramTable);
            return DynValue.NewNumber(affected);
        });
        table["query"] = DynValue.NewCallback((ctx, args) => {
            int offset = args.Count > 0 && args[0].Type == DataType.Table ? 1 : 0;
            if (args.Count <= offset || args[offset].Type != DataType.String) {
                throw new ScriptRuntimeException("sqlite handle query(sql [, params])");
            }

            string sql = args[offset].String;
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
internal sealed class SqliteHandle:System.IDisposable {
    private readonly Script _script;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private Microsoft.Data.Sqlite.SqliteTransaction? _transaction;
    private bool _disposed;

    public SqliteHandle(Script script, string path) {
        _script = script;
        string fullPath = System.IO.Path.GetFullPath(path);
        Microsoft.Data.Sqlite.SqliteConnectionStringBuilder builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
            DataSource = fullPath
        };
        _connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ConnectionString);
        _connection.Open();
    }

    public int Execute(string sql, Table? parameters) {
        EnsureNotDisposed();
        using Microsoft.Data.Sqlite.SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        if (_transaction != null) {
            command.Transaction = _transaction;
        }

        BindParameters(command, parameters);
        return command.ExecuteNonQuery();
    }

    public DynValue Query(string sql, Table? parameters) {
        EnsureNotDisposed();
        using Microsoft.Data.Sqlite.SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        if (_transaction != null) {
            command.Transaction = _transaction;
        }

        BindParameters(command, parameters);
        using Microsoft.Data.Sqlite.SqliteDataReader reader = command.ExecuteReader();
        Table result = new Table(_script);
        int index = 1;
        while (reader.Read()) {
            Table row = new Table(_script);
            for (int i = 0; i < reader.FieldCount; i++) {
                string columnName = reader.GetName(i);
                object? value = reader.GetValue(i);
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
            throw new System.ObjectDisposedException(nameof(SqliteHandle));
        }
    }

    private static void BindParameters(Microsoft.Data.Sqlite.SqliteCommand command, Table? parameters) {
        if (parameters == null) {
            return;
        }

        IDictionary<string, object?> dict = LuaUtilities.TableToDictionary(parameters);
        foreach (KeyValuePair<string, object?> kv in dict) {
            Microsoft.Data.Sqlite.SqliteParameter parameter = command.CreateParameter();
            string name = kv.Key;
            if (!name.StartsWith(":", System.StringComparison.Ordinal) && !name.StartsWith("@", System.StringComparison.Ordinal) && !name.StartsWith("$", System.StringComparison.Ordinal)) {
                name = ":" + name;
            }

            parameter.ParameterName = name;
            parameter.Value = kv.Value ?? System.DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}