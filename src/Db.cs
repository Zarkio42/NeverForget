// Minimal SQLite access through winsqlite3.dll, the SQLite build that ships with Windows
// (C:\Windows\System32\winsqlite3.dll). No third-party native code is bundled.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace NeverForget
{
    // winsqlite3.dll is loaded only from System32 (see AssemblyInfo.cs: DefaultDllImportSearchPaths).
    [SuppressUnmanagedCodeSecurity]
    internal static class Native
    {
        const string Lib = "winsqlite3.dll";
        public const int SQLITE_OK = 0, SQLITE_ROW = 100, SQLITE_DONE = 101;
        public const int SQLITE_INTEGER = 1, SQLITE_FLOAT = 2, SQLITE_TEXT = 3, SQLITE_NULL = 5;
        public static readonly IntPtr TRANSIENT = new IntPtr(-1);

        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_text(IntPtr stmt, int idx, byte[] val, int n, IntPtr destructor);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_int64(IntPtr stmt, int idx, long val);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_double(IntPtr stmt, int idx, double val);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_bind_null(IntPtr stmt, int idx);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern IntPtr sqlite3_column_name(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_column_type(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern long sqlite3_column_int64(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern double sqlite3_column_double(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern int sqlite3_column_bytes(IntPtr stmt, int col);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Lib, CallingConvention = CallingConvention.StdCall)] public static extern long sqlite3_last_insert_rowid(IntPtr db);
    }

    public sealed class Db : IDisposable
    {
        IntPtr _db;

        public Db(string path)
        {
            // READWRITE | CREATE | FULLMUTEX
            int rc = Native.sqlite3_open_v2(Utf8(path), out _db, 0x02 | 0x04 | 0x10000, IntPtr.Zero);
            if (rc != Native.SQLITE_OK) throw new Exception("Could not open the database: " + Err());
            Exec("PRAGMA journal_mode=WAL;");
            Exec("PRAGMA synchronous=NORMAL;");
            Exec("PRAGMA foreign_keys=ON;");
            Exec("PRAGMA trusted_schema=OFF;");
            Migrate();
        }

        void Migrate()
        {
            Exec(@"CREATE TABLE IF NOT EXISTS items (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                title       TEXT    NOT NULL DEFAULT '',
                body        TEXT    NOT NULL DEFAULT '',
                color       TEXT    NOT NULL DEFAULT 'yellow',
                status      TEXT    NOT NULL DEFAULT 'todo',
                priority    INTEGER NOT NULL DEFAULT 0,
                due         TEXT,
                sort        REAL    NOT NULL DEFAULT 0,
                x           REAL    NOT NULL DEFAULT 40,
                y           REAL    NOT NULL DEFAULT 40,
                z           INTEGER NOT NULL DEFAULT 0,
                archived    INTEGER NOT NULL DEFAULT 0,
                created_at  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%S','now')),
                updated_at  TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%S','now')),
                done_at     TEXT
            );");
            Exec("CREATE INDEX IF NOT EXISTS ix_items_status ON items(archived, status, sort);");
            Exec("CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT);");

            // v2: when an item entered its current column, and when it was archived (used by automations)
            var cols = new HashSet<string>();
            foreach (var r in Query("PRAGMA table_info(items)")) cols.Add((string)r["name"]);
            if (!cols.Contains("status_at"))
            {
                Exec("ALTER TABLE items ADD COLUMN status_at TEXT");
                Exec("UPDATE items SET status_at = COALESCE(done_at, updated_at, created_at) WHERE status_at IS NULL");
            }
            if (!cols.Contains("archived_at"))
            {
                Exec("ALTER TABLE items ADD COLUMN archived_at TEXT");
                Exec("UPDATE items SET archived_at = updated_at WHERE archived=1");
            }

            Exec(@"CREATE TABLE IF NOT EXISTS rules (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                cond        TEXT    NOT NULL,
                status      TEXT    NOT NULL DEFAULT 'any',
                days        INTEGER NOT NULL DEFAULT 5,
                action      TEXT    NOT NULL,
                value       TEXT,
                enabled     INTEGER NOT NULL DEFAULT 1,
                created_at  TEXT,
                last_run    TEXT,
                last_count  INTEGER NOT NULL DEFAULT 0
            );");

            // v3: color keys renamed from Portuguese to English
            const string map = @"CASE {0}
                WHEN 'amarelo' THEN 'yellow' WHEN 'verde' THEN 'green' WHEN 'azul' THEN 'blue'
                WHEN 'rosa' THEN 'pink' WHEN 'lilas' THEN 'purple' WHEN 'cinza' THEN 'gray' ELSE {0} END";
            Exec("UPDATE items SET color = " + string.Format(map, "color") +
                 " WHERE color IN ('amarelo','verde','azul','rosa','lilas','cinza')");
            Exec("UPDATE rules SET value = " + string.Format(map, "value") +
                 " WHERE action='color' AND value IN ('amarelo','verde','azul','rosa','lilas','cinza')");
        }

        public void Exec(string sql, params object[] args)
        {
            IntPtr st = Prepare(sql, args);
            try
            {
                int rc = Native.sqlite3_step(st);
                if (rc != Native.SQLITE_DONE && rc != Native.SQLITE_ROW) throw new Exception(Err());
            }
            finally { Native.sqlite3_finalize(st); }
        }

        public long Insert(string sql, params object[] args)
        {
            Exec(sql, args);
            return Native.sqlite3_last_insert_rowid(_db);
        }

        public List<Dictionary<string, object>> Query(string sql, params object[] args)
        {
            var rows = new List<Dictionary<string, object>>();
            IntPtr st = Prepare(sql, args);
            try
            {
                int n = Native.sqlite3_column_count(st);
                var names = new string[n];
                for (int i = 0; i < n; i++) names[i] = FromUtf8(Native.sqlite3_column_name(st, i), -1);
                int rc;
                while ((rc = Native.sqlite3_step(st)) == Native.SQLITE_ROW)
                {
                    var row = new Dictionary<string, object>(n);
                    for (int i = 0; i < n; i++)
                    {
                        switch (Native.sqlite3_column_type(st, i))
                        {
                            case Native.SQLITE_INTEGER: row[names[i]] = Native.sqlite3_column_int64(st, i); break;
                            case Native.SQLITE_FLOAT: row[names[i]] = Native.sqlite3_column_double(st, i); break;
                            case Native.SQLITE_NULL: row[names[i]] = null; break;
                            default:
                                IntPtr p = Native.sqlite3_column_text(st, i);
                                row[names[i]] = FromUtf8(p, Native.sqlite3_column_bytes(st, i));
                                break;
                        }
                    }
                    rows.Add(row);
                }
                if (rc != Native.SQLITE_DONE) throw new Exception(Err());
            }
            finally { Native.sqlite3_finalize(st); }
            return rows;
        }

        public object Scalar(string sql, params object[] args)
        {
            var r = Query(sql, args);
            if (r.Count == 0) return null;
            foreach (var v in r[0].Values) return v;
            return null;
        }

        // Every value goes through a bound parameter; SQL text never contains user input.
        IntPtr Prepare(string sql, object[] args)
        {
            IntPtr st;
            if (Native.sqlite3_prepare_v2(_db, Utf8(sql), -1, out st, IntPtr.Zero) != Native.SQLITE_OK)
                throw new Exception(Err());
            for (int i = 0; i < args.Length; i++)
            {
                object a = args[i];
                int idx = i + 1;
                if (a == null) Native.sqlite3_bind_null(st, idx);
                else if (a is string) { byte[] b = Encoding.UTF8.GetBytes((string)a); Native.sqlite3_bind_text(st, idx, b, b.Length, Native.TRANSIENT); }
                else if (a is int || a is long || a is short) Native.sqlite3_bind_int64(st, idx, Convert.ToInt64(a));
                else if (a is bool) Native.sqlite3_bind_int64(st, idx, (bool)a ? 1 : 0);
                else if (a is double || a is float || a is decimal) Native.sqlite3_bind_double(st, idx, Convert.ToDouble(a));
                else { byte[] b = Encoding.UTF8.GetBytes(a.ToString()); Native.sqlite3_bind_text(st, idx, b, b.Length, Native.TRANSIENT); }
            }
            return st;
        }

        string Err() { return FromUtf8(Native.sqlite3_errmsg(_db), -1); }

        static byte[] Utf8(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            Array.Resize(ref b, b.Length + 1); // null terminator
            return b;
        }

        static string FromUtf8(IntPtr p, int len)
        {
            if (p == IntPtr.Zero) return null;
            if (len < 0) { len = 0; while (Marshal.ReadByte(p, len) != 0) len++; }
            byte[] b = new byte[len];
            Marshal.Copy(p, b, 0, len);
            return Encoding.UTF8.GetString(b);
        }

        public void Dispose()
        {
            if (_db != IntPtr.Zero) { Native.sqlite3_close_v2(_db); _db = IntPtr.Zero; }
        }
    }
}
