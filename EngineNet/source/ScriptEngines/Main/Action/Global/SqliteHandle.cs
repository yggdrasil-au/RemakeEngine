
namespace EngineNet.ScriptEngines.Lua.Global;

/// <summary>
/// SQLite connection handle for Lua scripts.
/// </summary>
internal sealed class SqliteHandle:System.IDisposable {
    private readonly MoonSharp.Interpreter.Script _script;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private Microsoft.Data.Sqlite.SqliteTransaction? _transaction;
    private bool _disposed;

    public SqliteHandle(MoonSharp.Interpreter.Script script, string path) {
        _script = script;
        string fullPath = System.IO.Path.GetFullPath(path);
        Microsoft.Data.Sqlite.SqliteConnectionStringBuilder builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
            DataSource = fullPath
        };
        _connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ConnectionString);
        _connection.Open();
    }

    public int Execute(string sql, MoonSharp.Interpreter.Table? parameters) {
        EnsureNotDisposed();
        using Microsoft.Data.Sqlite.SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        if (_transaction != null) {
            command.Transaction = _transaction;
        }

        BindParameters(command, parameters);
        return command.ExecuteNonQuery();
    }

    public MoonSharp.Interpreter.DynValue Query(string sql, MoonSharp.Interpreter.Table? parameters) {
        EnsureNotDisposed();
        using Microsoft.Data.Sqlite.SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        if (_transaction != null) {
            command.Transaction = _transaction;
        }

        BindParameters(command, parameters);
        using Microsoft.Data.Sqlite.SqliteDataReader reader = command.ExecuteReader();
        MoonSharp.Interpreter.Table result = new MoonSharp.Interpreter.Table(_script);
        int index = 1;
        while (reader.Read()) {
            MoonSharp.Interpreter.Table row = new MoonSharp.Interpreter.Table(_script);
            for (int i = 0; i < reader.FieldCount; i++) {
                string columnName = reader.GetName(i);
                object? value = reader.GetValue(i);
                row[columnName] = Lua.Globals.Utils.ToDynValue(_script, value);
            }
            result[index++] = MoonSharp.Interpreter.DynValue.NewTable(row);
        }
        return MoonSharp.Interpreter.DynValue.NewTable(result);
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

    private static void BindParameters(Microsoft.Data.Sqlite.SqliteCommand command, MoonSharp.Interpreter.Table? parameters) {
        if (parameters == null) {
            return;
        }

        IDictionary<string, object?> dict = Lua.Globals.Utils.TableToDictionary(parameters);
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