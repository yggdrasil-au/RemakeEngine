
namespace EngineNet.ScriptEngines.Lua.Global;

/// <summary>
/// SQLite connection handle for Lua scripts.
/// </summary>
internal sealed class SqliteHandle:System.IDisposable {
    private readonly MoonSharp.Interpreter.Script _script;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private Microsoft.Data.Sqlite.SqliteTransaction? _transaction;
    private bool _disposed;

    internal SqliteHandle(MoonSharp.Interpreter.Script script, string path) {
        _script = script;
        string fullPath = System.IO.Path.GetFullPath(path: path);
        Microsoft.Data.Sqlite.SqliteConnectionStringBuilder builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
            DataSource = fullPath
        };
        _connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString: builder.ConnectionString);
        _connection.Open();
    }

    internal int Execute(string sql, MoonSharp.Interpreter.Table? parameters) {
        EnsureNotDisposed();
        using Microsoft.Data.Sqlite.SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        if (_transaction != null) {
            command.Transaction = _transaction;
        }

        BindParameters(command: command, parameters: parameters);
        return command.ExecuteNonQuery();
    }

    internal MoonSharp.Interpreter.DynValue Query(string sql, MoonSharp.Interpreter.Table? parameters) {
        EnsureNotDisposed();
        using Microsoft.Data.Sqlite.SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        if (_transaction != null) {
            command.Transaction = _transaction;
        }

        BindParameters(command: command, parameters: parameters);
        using Microsoft.Data.Sqlite.SqliteDataReader reader = command.ExecuteReader();
        MoonSharp.Interpreter.Table result = new MoonSharp.Interpreter.Table(owner: _script);
        int index = 1;
        while (reader.Read()) {
            MoonSharp.Interpreter.Table row = new MoonSharp.Interpreter.Table(owner: _script);
            for (int i = 0; i < reader.FieldCount; i++) {
                string columnName = reader.GetName(ordinal: i);
                object? value = reader.GetValue(ordinal: i);
                row[key: columnName] = Lua.Globals.Utils.ToDynValue(lua: _script, value);
            }
            result[key: index++] = MoonSharp.Interpreter.DynValue.NewTable(table: row);
        }
        return MoonSharp.Interpreter.DynValue.NewTable(table: result);
    }

    internal void BeginTransaction() {
        EnsureNotDisposed();
        _transaction ??= _connection.BeginTransaction();
    }

    internal void Commit() {
        if (_disposed) {
            return;
        }

        if (_transaction != null) {
            _transaction.Commit();
            _transaction.Dispose();
            _transaction = null;
        }
    }

    internal void Rollback() {
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
            throw new System.ObjectDisposedException(objectName: nameof(SqliteHandle));
        }
    }

    private static void BindParameters(Microsoft.Data.Sqlite.SqliteCommand command, MoonSharp.Interpreter.Table? parameters) {
        if (parameters == null) {
            return;
        }

        IDictionary<string, object?> dict = Lua.Globals.Utils.TableToDictionary(table: parameters);
        foreach (KeyValuePair<string, object?> kv in dict) {
            Microsoft.Data.Sqlite.SqliteParameter parameter = command.CreateParameter();
            string name = kv.Key;
            if (!name.StartsWith(":", comparisonType: System.StringComparison.Ordinal) && !name.StartsWith("@", comparisonType: System.StringComparison.Ordinal) && !name.StartsWith("$", comparisonType: System.StringComparison.Ordinal)) {
                name = ":" + name;
            }

            parameter.ParameterName = name;
            parameter.Value = kv.Value ?? System.DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
