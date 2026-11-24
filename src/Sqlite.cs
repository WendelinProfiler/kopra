using System.Runtime.InteropServices;
namespace Kopra;
// Windows ships winsqlite3; no database runtime download is needed.
sealed class Sqlite : IDisposable
{
    nint db; readonly object gate = new();
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint db, int flags, nint vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(nint db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out nint stmt, nint tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(nint stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(nint stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_bind_text(nint stmt, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int bytes, nint destructor);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern nint sqlite3_column_text(nint stmt, int column);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] static extern nint sqlite3_errmsg(nint db);
    public Sqlite(string path)
    {
        bool existing = File.Exists(path);
        if (sqlite3_open_v2(path, out db, 6 | 0x10000, 0) != 0)
            throw new IOException("Verlauf-Datenbank konnte nicht geöffnet werden");
        Run("PRAGMA busy_timeout=3000");
        Run("PRAGMA secure_delete=ON");
        string? previous = Run("PRAGMA auto_vacuum").FirstOrDefault();
        Run("PRAGMA auto_vacuum=INCREMENTAL");
        if (existing && previous != "2" && new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace > 5L * 1073741824 + Math.Max(64L * 1048576, new FileInfo(path).Length * 2))
            Run("VACUUM");
        Run("PRAGMA journal_mode=WAL");
        Run("PRAGMA wal_autocheckpoint=128");
        Run("PRAGMA journal_size_limit=1048576");
    }
    public List<string> Run(string sql, params string[] args)
    {
        lock (gate)
        {
            nint stmt = 0;
            try
            {
                if (sqlite3_prepare_v2(db, sql, -1, out stmt, 0) != 0)
                    throw new IOException(Marshal.PtrToStringUTF8(sqlite3_errmsg(db)));
                for (int i = 0; i < args.Length; i++)
                if (sqlite3_bind_text(stmt, i + 1, args[i], -1, -1) != 0)
                    throw new IOException("SQLite binding failed");
                var rows = new List<string>();
                int status;
                while ((status = sqlite3_step(stmt)) == 100)
                    rows.Add(Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, 0)) ?? "");
                if (status != 101)
                    throw new IOException(Marshal.PtrToStringUTF8(sqlite3_errmsg(db)));
                return rows;
            }
            finally { if (stmt != 0) sqlite3_finalize(stmt); }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (db != 0)
            {
                sqlite3_close(db);
                db = 0;
            }
        }
    }
}
