using System.Runtime.InteropServices;

namespace PromptDC;

// Windows ships winsqlite3. Open an existing database read-only; never repair or modify Codex storage.
internal sealed class LocalSqlite : IDisposable
{
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string name, out nint db, int flags, nint vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(nint db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out nint statement, nint tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(nint statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int length, nint destructor);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(nint statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(nint statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern nint sqlite3_column_text(nint statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(nint statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(nint db, int milliseconds);
    private nint database;
    public LocalSqlite(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("未找到 Codex 本机聊天索引。");
        if (sqlite3_open_v2(path, out database, 1, 0) != 0) { Dispose(); throw new InvalidOperationException("暂时无法只读打开 Codex 聊天索引。"); }
        sqlite3_busy_timeout(database, 500);
    }
    public List<string[]> Query(string sql, params string[] parameters)
    {
        if (sqlite3_prepare_v2(database, sql, -1, out var statement, 0) != 0)
            throw new InvalidOperationException("当前 Codex 聊天索引结构暂不支持，原文未替换。");
        try
        {
            for (var i = 0; i < parameters.Length; i++)
                if (sqlite3_bind_text(statement, i + 1, parameters[i], -1, -1) != 0) throw new InvalidOperationException("无法查询聊天索引。");
            var rows = new List<string[]>();
            int code;
            while ((code = sqlite3_step(statement)) == 100)
            {
                var row = new string[sqlite3_column_count(statement)];
                for (var i = 0; i < row.Length; i++) row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, i)) ?? "";
                rows.Add(row);
            }
            if (code != 101) throw new InvalidOperationException("聊天索引正忙，请稍后重试。");
            return rows;
        }
        finally { sqlite3_finalize(statement); }
    }
    public void Dispose() { if (database != 0) { sqlite3_close_v2(database); database = 0; } }
}
