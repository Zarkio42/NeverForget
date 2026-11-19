// Data layer: turns commands from the UI into database operations, and runs automation rules.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NeverForget
{
    public sealed class Store
    {
        const int MaxTitle = 500, MaxBody = 20000;

        readonly Db _db;
        static readonly HashSet<string> Status = new HashSet<string> { "todo", "doing", "done" };
        static readonly HashSet<string> Colors = new HashSet<string> { "yellow", "green", "blue", "pink", "purple", "gray" };
        static readonly HashSet<string> SettingKeys = new HashSet<string> { "view", "lang" };

        public Store(Db db) { _db = db; }

        public object Handle(string cmd, Dictionary<string, object> a)
        {
            switch (cmd)
            {
                case "list": return List();
                case "create": return Create(a);
                case "update": return Update(a);
                case "move": return Move(a);
                case "delete": _db.Exec("DELETE FROM items WHERE id=?", L(a, "id")); return true;
                case "archiveDone":
                    _db.Exec("UPDATE items SET archived=1, archived_at=" + Now + ", updated_at=" + Now + " WHERE status='done' AND archived=0");
                    return true;
                case "listArchived":
                    return _db.Query("SELECT * FROM items WHERE archived=1 ORDER BY COALESCE(archived_at, updated_at) DESC LIMIT 300");
                case "unarchive":
                    // reset status_at so an archiving rule does not immediately take the note back
                    _db.Exec("UPDATE items SET archived=0, archived_at=NULL, status_at=" + Now + ", updated_at=" + Now + " WHERE id=?", L(a, "id"));
                    return Get(L(a, "id"));
                case "restore": return Restore(a);
                case "listRules": return ListRules();
                case "saveRule": return SaveRule(a);
                case "deleteRule": _db.Exec("DELETE FROM rules WHERE id=?", L(a, "id")); return true;
                case "toggleRule":
                    _db.Exec("UPDATE rules SET enabled=? WHERE id=?", B(a, "on") ? 1 : 0, L(a, "id"));
                    return true;
                case "runRule": return ApplyRule(GetRule(L(a, "id")), false);
                case "previewRule": return ApplyRule(a, true);
                case "getSetting": return GetSetting(Key(a));
                case "setSetting": SetSetting(Key(a), Trim(S(a, "value"), 50)); return true;
                default: throw new ArgumentException("Unknown command: " + cmd);
            }
        }

        // Local time formatted in C#: the Windows SQLite build does not support the 'localtime' modifier.
        // The value is a fixed-format literal, never user input.
        static string Now { get { return "'" + DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "'"; } }

        public string GetSetting(string key) { return _db.Scalar("SELECT value FROM settings WHERE key=?", key) as string; }
        public void SetSetting(string key, string value)
        {
            _db.Exec("INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", key, value);
        }

        static string Key(Dictionary<string, object> a)
        {
            string k = S(a, "key");
            if (k == null || !SettingKeys.Contains(k)) throw new ArgumentException("Unknown setting.");
            return k;
        }

        List<Dictionary<string, object>> List()
        {
            return _db.Query("SELECT * FROM items WHERE archived=0 ORDER BY status, sort, id");
        }

        Dictionary<string, object> Get(long id)
        {
            var r = _db.Query("SELECT * FROM items WHERE id=?", id);
            return r.Count > 0 ? r[0] : null;
        }

        object Create(Dictionary<string, object> a)
        {
            string status = Valid(S(a, "status") ?? "todo", Status, "todo");
            object minSort = _db.Scalar("SELECT MIN(sort) FROM items WHERE status=? AND archived=0", status);
            double sort = minSort == null ? 1000 : Convert.ToDouble(minSort) - 1000;
            object maxZ = _db.Scalar("SELECT MAX(z) FROM items");
            long z = maxZ == null ? 1 : Convert.ToInt64(maxZ) + 1;
            long id = _db.Insert(
                "INSERT INTO items(title, body, color, status, priority, due, sort, x, y, z, created_at, updated_at, status_at, done_at) " +
                "VALUES(?,?,?,?,?,?,?,?,?,?," + Now + "," + Now + "," + Now + "," + (status == "done" ? Now : "NULL") + ")",
                Trim(S(a, "title"), MaxTitle) ?? "", Trim(S(a, "body"), MaxBody) ?? "",
                Valid(S(a, "color") ?? "yellow", Colors, "yellow"),
                status, Priority(a), Due(a), sort,
                Coord(a, "x"), Coord(a, "y"), z);
            return Get(id);
        }

        object Update(Dictionary<string, object> a)
        {
            long id = L(a, "id");
            var cur = Get(id);
            if (cur == null) throw new ArgumentException("Item not found.");
            var sets = new List<string>();
            var vals = new List<object>();
            if (a.ContainsKey("title")) { sets.Add("title=?"); vals.Add(Trim(S(a, "title"), MaxTitle) ?? ""); }
            if (a.ContainsKey("body")) { sets.Add("body=?"); vals.Add(Trim(S(a, "body"), MaxBody) ?? ""); }
            if (a.ContainsKey("color")) { sets.Add("color=?"); vals.Add(Valid(S(a, "color"), Colors, "yellow")); }
            if (a.ContainsKey("priority")) { sets.Add("priority=?"); vals.Add(Priority(a)); }
            if (a.ContainsKey("due")) { sets.Add("due=?"); vals.Add(Due(a)); }
            if (a.ContainsKey("x")) { sets.Add("x=?"); vals.Add(Coord(a, "x")); }
            if (a.ContainsKey("y")) { sets.Add("y=?"); vals.Add(Coord(a, "y")); }
            if (a.ContainsKey("z")) { sets.Add("z=?"); vals.Add((long)D(a, "z", 0)); }
            if (a.ContainsKey("status"))
            {
                string st = Valid(S(a, "status"), Status, "todo");
                sets.Add("status=?"); vals.Add(st);
                sets.Add("done_at=" + (st == "done" ? "COALESCE(done_at," + Now + ")" : "NULL"));
                if (st != (string)cur["status"]) sets.Add("status_at=" + Now);
            }
            if (sets.Count == 0) return cur;
            sets.Add("updated_at=" + Now);
            vals.Add(id);
            _db.Exec("UPDATE items SET " + string.Join(", ", sets) + " WHERE id=?", vals.ToArray());
            return Get(id);
        }

        // Moves a card into a column between two neighbours (fractional ordering: one row changes).
        object Move(Dictionary<string, object> a)
        {
            long id = L(a, "id");
            string status = Valid(S(a, "status"), Status, "todo");
            object before = a.ContainsKey("beforeId") ? a["beforeId"] : null; // card above
            object after = a.ContainsKey("afterId") ? a["afterId"] : null;    // card below
            double? sb = before == null ? (double?)null : SortOf(Convert.ToInt64(before));
            double? sa = after == null ? (double?)null : SortOf(Convert.ToInt64(after));
            double sort;
            if (sb.HasValue && sa.HasValue) sort = (sb.Value + sa.Value) / 2;
            else if (sb.HasValue) sort = sb.Value + 1000;
            else if (sa.HasValue) sort = sa.Value - 1000;
            else sort = 1000;
            _db.Exec("UPDATE items SET status_at=CASE WHEN status=? THEN status_at ELSE " + Now + " END, status=?, sort=?, done_at=" +
                     (status == "done" ? "COALESCE(done_at," + Now + ")" : "NULL") +
                     ", updated_at=" + Now + " WHERE id=?", status, status, sort, id);
            return Get(id);
        }

        // Re-creates a deleted item (undo), keeping its id.
        object Restore(Dictionary<string, object> a)
        {
            var it = a.ContainsKey("item") ? a["item"] as Dictionary<string, object> : null;
            if (it == null) throw new ArgumentException("Missing item.");
            if (Get(L(it, "id")) != null) throw new ArgumentException("Item already exists.");
            string nowS = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            _db.Exec(@"INSERT INTO items(id,title,body,color,status,priority,due,sort,x,y,z,archived,created_at,updated_at,done_at,status_at)
                       VALUES(?,?,?,?,?,?,?,?,?,?,?,0,?,?,?,?)",
                L(it, "id"), Trim(S(it, "title"), MaxTitle) ?? "", Trim(S(it, "body"), MaxBody) ?? "", Valid(S(it, "color"), Colors, "yellow"),
                Valid(S(it, "status"), Status, "todo"), Priority(it), Due(it),
                D(it, "sort", 0), Coord(it, "x"), Coord(it, "y"), (long)D(it, "z", 0),
                Stamp(S(it, "created_at")) ?? nowS, nowS, Stamp(S(it, "done_at")), Stamp(S(it, "status_at")) ?? nowS);
            return Get(L(it, "id"));
        }

        // ================= Automations =================
        // A rule = condition (cond + status + days) + action (action + value).
        //   cond:   inStatus | idle | overdue | dueSoon
        //   action: move (value = column) | archive | priority | color (value = color)

        static readonly HashSet<string> Conds = new HashSet<string> { "inStatus", "idle", "overdue", "dueSoon" };
        static readonly HashSet<string> Actions = new HashSet<string> { "move", "archive", "priority", "color" };

        List<Dictionary<string, object>> ListRules()
        {
            var rules = _db.Query("SELECT * FROM rules ORDER BY id");
            foreach (var r in rules) r["pending"] = ApplyRule(r, true);
            return rules;
        }

        Dictionary<string, object> GetRule(long id)
        {
            var r = _db.Query("SELECT * FROM rules WHERE id=?", id);
            if (r.Count == 0) throw new ArgumentException("Rule not found.");
            return r[0];
        }

        object SaveRule(Dictionary<string, object> a)
        {
            string cond = S(a, "cond"), action = S(a, "action"), status = S(a, "status") ?? "any", value = S(a, "value");
            if (cond == null || !Conds.Contains(cond)) throw new ArgumentException("Invalid condition.");
            if (action == null || !Actions.Contains(action)) throw new ArgumentException("Invalid action.");
            if (status != "any" && !Status.Contains(status)) status = "any";
            if (cond == "inStatus" && status == "any") throw new ArgumentException("Choose a column for this rule.");
            if (action == "move" && (value == null || !Status.Contains(value))) throw new ArgumentException("Choose the target column.");
            if (action == "color" && (value == null || !Colors.Contains(value))) throw new ArgumentException("Choose a color.");
            if (action != "move" && action != "color") value = null;
            long days = Math.Max(0, Math.Min(365, (long)D(a, "days", 5)));
            long id = _db.Insert("INSERT INTO rules(cond,status,days,action,value,enabled,created_at) VALUES(?,?,?,?,?,1," + Now + ")",
                cond, status, days, action, value);
            var r = GetRule(id);
            r["pending"] = ApplyRule(r, true);
            return r;
        }

        /// <summary>Runs every enabled rule. Returns how many items changed.</summary>
        public int RunAll()
        {
            int total = 0;
            foreach (var r in _db.Query("SELECT * FROM rules WHERE enabled=1 ORDER BY id"))
            {
                try { total += ApplyRule(r, false); }
                catch (Exception ex) { Program.Log(ex); }
            }
            return total;
        }

        int ApplyRule(Dictionary<string, object> r, bool dryRun)
        {
            string cond = S(r, "cond"), status = S(r, "status") ?? "any", action = S(r, "action"), value = S(r, "value");
            if (cond == null || !Conds.Contains(cond) || action == null || !Actions.Contains(action)) return 0;
            int days = (int)Math.Max(0, Math.Min(365, D(r, "days", 5)));
            string cutoff = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

            var where = new List<string> { "archived=0" };
            var wargs = new List<object>();
            switch (cond)
            {
                case "inStatus":
                    if (status == "any") return 0;
                    where.Add("COALESCE(status_at, updated_at) <= ?"); wargs.Add(cutoff);
                    break;
                case "idle":
                    where.Add("updated_at <= ?"); wargs.Add(cutoff);
                    break;
                case "overdue": // due date passed more than N days ago (0 = any overdue)
                    where.Add("due IS NOT NULL AND due < ? AND status <> 'done'");
                    wargs.Add(DateTime.Today.AddDays(-days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    break;
                case "dueSoon": // due within the next N days (includes overdue)
                    where.Add("due IS NOT NULL AND due <= ? AND status <> 'done'");
                    wargs.Add(DateTime.Today.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    break;
            }
            if (status != "any" && Status.Contains(status)) { where.Add("status = ?"); wargs.Add(status); }

            // items already in the target state are not counted
            var sets = new List<string>();
            var sargs = new List<object>();
            switch (action)
            {
                case "move":
                    if (value == null || !Status.Contains(value)) return 0;
                    where.Add("status <> ?"); wargs.Add(value);
                    object minSort = _db.Scalar("SELECT MIN(sort) FROM items WHERE status=? AND archived=0", value);
                    sets.Add("status=?"); sargs.Add(value);
                    sets.Add("sort=?"); sargs.Add(minSort == null ? 1000.0 : Convert.ToDouble(minSort) - 1000);
                    sets.Add("status_at=" + Now);
                    sets.Add("done_at=" + (value == "done" ? "COALESCE(done_at," + Now + ")" : "NULL"));
                    break;
                case "archive":
                    sets.Add("archived=1"); sets.Add("archived_at=" + Now);
                    break;
                case "priority":
                    where.Add("priority = 0");
                    sets.Add("priority=1");
                    break;
                case "color":
                    if (value == null || !Colors.Contains(value)) return 0;
                    where.Add("color <> ?"); wargs.Add(value);
                    sets.Add("color=?"); sargs.Add(value);
                    break;
            }
            string w = string.Join(" AND ", where);
            int count = Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM items WHERE " + w, wargs.ToArray()));
            if (dryRun) return count;

            if (count > 0)
            {
                sets.Add("updated_at=" + Now);
                var all = new List<object>(sargs);
                all.AddRange(wargs);
                _db.Exec("UPDATE items SET " + string.Join(", ", sets) + " WHERE " + w, all.ToArray());
                // only runs that changed something are recorded ("last time it acted")
                if (r.ContainsKey("id") && r["id"] != null)
                    _db.Exec("UPDATE rules SET last_run=" + Now + ", last_count=? WHERE id=?", count, Convert.ToInt64(r["id"]));
            }
            return count;
        }

        // ================= Helpers / input validation =================

        double SortOf(long id)
        {
            object v = _db.Scalar("SELECT sort FROM items WHERE id=?", id);
            return v == null ? 0 : Convert.ToDouble(v);
        }

        static string Due(Dictionary<string, object> a)
        {
            string d = S(a, "due");
            if (string.IsNullOrEmpty(d)) return null;
            DateTime dt;
            return DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ? d : null;
        }

        static string Stamp(string s)
        {
            DateTime dt;
            return s != null && DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ? s : null;
        }

        static long Priority(Dictionary<string, object> a) { return D(a, "priority", 0) > 0 ? 1 : 0; }
        static double Coord(Dictionary<string, object> a, string k) { return Math.Max(0, Math.Min(100000, D(a, k, 40))); }
        static string Trim(string s, int max) { return s == null || s.Length <= max ? s : s.Substring(0, max); }
        static string Valid(string v, HashSet<string> set, string def) { return v != null && set.Contains(v) ? v : def; }
        static string S(Dictionary<string, object> a, string k) { object v; return a.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null; }
        static bool B(Dictionary<string, object> a, string k) { object v; return a.TryGetValue(k, out v) && v is bool && (bool)v; }
        static long L(Dictionary<string, object> a, string k)
        {
            object v;
            if (!a.TryGetValue(k, out v) || v == null) throw new ArgumentException("Missing field: " + k);
            return Convert.ToInt64(v, CultureInfo.InvariantCulture);
        }
        static double D(Dictionary<string, object> a, string k, double def)
        {
            object v;
            if (!a.TryGetValue(k, out v) || v == null) return def;
            try
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return double.IsNaN(d) || double.IsInfinity(d) ? def : d;
            }
            catch { return def; }
        }
    }
}
