using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TaskDesk
{
    public sealed class Item
    {
        private static readonly Regex DonePrefix = new Regex(@"^x\s+(\d{4}-\d{2}-\d{2})(?:\s+|$)");
        private static readonly Regex PriorityPrefix = new Regex(@"^\(([A-Z])\)(?:\s+|$)");
        private static readonly Regex CreatedPrefix = new Regex(@"^(\d{4}-\d{2}-\d{2})(?:\s+|$)");
        private static readonly Regex Metadata = new Regex(@"(?<!\S)([\p{L}_][\p{L}\p{N}_-]*):((?![/\\])\S+)(?!\S)");
        internal Guid Id { get; private set; }
        private string raw;
        private string created;
        private string[] metadata;
        public string Title { get; private set; }
        public string Priority { get; private set; }
        public DateTime? Completed { get; private set; }
        public bool Estimated { get; private set; }

        private Item() { }

        private static bool IsDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date);
        }

        internal static Item Parse(string line)
        {
            Item item = new Item { Id = Guid.NewGuid(), Priority = "", raw = line };
            string body = line.Trim();
            DateTime date;
            Match done = DonePrefix.Match(body);
            if (done.Success && IsDate(done.Groups[1].Value, out date))
            {
                item.Completed = date;
                body = body.Substring(done.Length);
            }
            Match priority = PriorityPrefix.Match(body);
            if (priority.Success)
            {
                item.Priority = priority.Groups[1].Value;
                body = body.Substring(priority.Length);
            }
            Match creation = CreatedPrefix.Match(body);
            if (creation.Success && IsDate(creation.Groups[1].Value, out date))
            {
                item.created = creation.Groups[1].Value;
                body = body.Substring(creation.Length);
            }
            List<string> preserved = new List<string>();
            string title = Metadata.Replace(body, delegate(Match token)
            {
                string key = token.Groups[1].Value;
                string value = token.Groups[2].Value;
                if (key == "pri" && Regex.IsMatch(value, "^[A-Z]$"))
                {
                    if (item.Priority.Length == 0) item.Priority = value;
                }
                else if (key == "inferred" && value == "1") item.Estimated = true;
                else preserved.Add(token.Value);
                return "";
            });
            item.Title = title.Trim();
            item.metadata = preserved.ToArray();
            return item;
        }

        public static Item New(string title, string priority, DateTime? completed)
        {
            return new Item { Id = Guid.NewGuid(), metadata = new string[0] }
                .With(title, priority, completed, false);
        }

        public Item With(string title, string priority, DateTime? completed, bool estimated)
        {
            if (String.IsNullOrWhiteSpace(title) || title.IndexOfAny(new char[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("Введите название задачи в одну строку.");
            priority = (priority ?? "").Trim().ToUpperInvariant();
            if (priority.Length != 0 && !Regex.IsMatch(priority, "^[A-Z]$"))
                throw new ArgumentException("Приоритет задаётся одной буквой от A до Z.");
            completed = completed.HasValue ? completed.Value.Date : (DateTime?)null;
            title = title.Trim();
            estimated = estimated && completed.HasValue && priority.Length > 0;
            if (Title == title && Priority == priority && Completed == completed && Estimated == estimated)
                return this;
            return new Item { Id = Id, Title = title, Priority = priority, Completed = completed,
                Estimated = estimated, created = created, metadata = metadata };
        }

        internal string Render()
        {
            if (raw != null) return raw;
            List<string> parts = new List<string>();
            if (Completed.HasValue)
                parts.Add("x " + Completed.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            else if (Priority.Length > 0) parts.Add("(" + Priority + ")");
            if (created != null) parts.Add(created);
            parts.Add(Title);
            parts.AddRange(metadata);
            if (Completed.HasValue && Priority.Length > 0) parts.Add("pri:" + Priority);
            if (Estimated) parts.Add("inferred:1");
            return String.Join(" ", parts);
        }
    }

    public sealed class Store
    {
        private sealed class Layout
        {
            public string Before;
            public string Ending;
        }
        private readonly Dictionary<Guid, Layout> layout = new Dictionary<Guid, Layout>();
        private readonly UTF8Encoding encoding = new UTF8Encoding(false, true);
        private byte[] baseline;
        private string trailing;
        private string newline = Environment.NewLine;
        private bool bom;
        public string PathName { get; private set; }
        public List<Item> Items { get; private set; }

        private Store() { Items = new List<Item>(); }

        public static Store Open(string path)
        {
            Store store = new Store { PathName = Path.GetFullPath(path) };
            store.baseline = File.ReadAllBytes(store.PathName);
            byte[] bytes = store.baseline;
            store.bom = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191;
            int offset = store.bom ? 3 : 0;
            string text = store.encoding.GetString(bytes, offset, bytes.Length - offset);
            Match first = Regex.Match(text, @"\r\n|\r|\n");
            if (first.Success) store.newline = first.Value;

            StringBuilder blank = new StringBuilder();
            foreach (Match row in Regex.Matches(text, @"([^\r\n]*)(\r\n|\n|\r|$)"))
            {
                if (row.Length == 0) continue;
                string line = row.Groups[1].Value;
                if (String.IsNullOrWhiteSpace(line))
                {
                    blank.Append(row.Value);
                    continue;
                }
                Item item = Item.Parse(line);
                store.Items.Add(item);
                store.layout.Add(item.Id, new Layout { Before = blank.ToString(), Ending = row.Groups[2].Value });
                blank.Clear();
            }
            store.trailing = blank.ToString();
            return store;
        }

        private byte[] Serialize()
        {
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < Items.Count; i++)
            {
                Item item = Items[i];
                Layout row;
                bool existing = layout.TryGetValue(item.Id, out row);
                if (existing) result.Append(row.Before);
                result.Append(item.Render());
                string ending = existing ? row.Ending : newline;
                if (ending.Length == 0 && (i + 1 < Items.Count || trailing.Length > 0)) ending = newline;
                result.Append(ending);
            }
            result.Append(trailing);
            byte[] body = encoding.GetBytes(result.ToString());
            return bom ? Encoding.UTF8.GetPreamble().Concat(body).ToArray() : body;
        }

        public void Save()
        {
            byte[] next = Serialize();
            string temporary = Path.Combine(Path.GetDirectoryName(PathName), ".taskdesk-" + Guid.NewGuid().ToString("N") + ".tmp");
            // The lock serializes TaskDesk instances. Holding the original open also
            // excludes in-place external writers until the atomic replacement.
            using (new FileStream(PathName + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 4096, FileOptions.DeleteOnClose))
            using (FileStream original = new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                using (MemoryStream current = new MemoryStream())
                {
                    original.CopyTo(current);
                    if (!baseline.SequenceEqual(current.ToArray()))
                        throw new IOException("Файл изменился в другой программе. Обновите список и повторите изменение.");
                }
                if (baseline.SequenceEqual(next)) return;
                try
                {
                    using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        output.Write(next, 0, next.Length);
                        output.Flush(true);
                    }
                    File.Replace(temporary, PathName, PathName + ".bak", true);
                    baseline = next;
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }
    }
}
