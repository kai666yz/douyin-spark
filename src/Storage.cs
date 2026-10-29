using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DouyinSpark
{
    public class FriendRecord
    {
        public string name { get; set; }
        public string url { get; set; }
        public override string ToString() { return (name ?? "未命名") + "  ·  " + (ValidUrl(url) ? url : "待补主页链接"); }
        public static bool ValidUrl(string value) { return value != null && Regex.IsMatch(value, @"^https://www\.douyin\.com/user/[A-Za-z0-9_-]+/?$"); }
    }

    public static class SparkStorage
    {
        static readonly object Gate = new object();
        public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }; }

        // Accept BOM and dangling commas without changing text inside JSON strings.
        public static string CleanJson(string input)
        {
            var result = new StringBuilder(); bool quoted = false, escape = false;
            foreach (char ch in input.TrimStart('\uFEFF'))
            {
                if (!quoted && (ch == ']' || ch == '}'))
                {
                    int i = result.Length - 1;
                    while (i >= 0 && char.IsWhiteSpace(result[i])) i--;
                    if (i >= 0 && result[i] == ',') result.Remove(i, 1);
                }
                result.Append(ch);
                if (quoted) { if (escape) escape = false; else if (ch == '\\') escape = true; else if (ch == '"') quoted = false; }
                else if (ch == '"') quoted = true;
            }
            return result.ToString();
        }

        public static List<FriendRecord> ReadFriends(string path)
        {
            lock (Gate)
            {
                if (!File.Exists(path)) return new List<FriendRecord>();
                string raw = CleanJson(File.ReadAllText(path, Encoding.UTF8));
                if (!raw.TrimStart().StartsWith("[")) throw new InvalidDataException("好友文件不是 JSON 数组，已保留原文件，请检查或恢复 .bak 备份。");
                var result = Json().Deserialize<List<FriendRecord>>(raw);
                if (result == null || result.Any(f => f == null)) throw new InvalidDataException("好友文件包含无效条目，原文件未修改。");
                return result;
            }
        }

        public static void WriteJson(string path, object data)
        {
            lock (Gate)
            {
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, Json().Serialize(data), new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                    else File.Move(temp, path);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }

        public static int AddFriends(string path, IEnumerable<FriendRecord> incoming)
        {
            lock (Gate)
            {
                var all = ReadFriends(path);
                var known = new HashSet<string>(all.Where(f => !string.IsNullOrEmpty(f.url)).Select(f => f.url.TrimEnd('/')));
                int added = 0;
                foreach (var friend in incoming)
                {
                    if (!FriendRecord.ValidUrl(friend.url)) throw new InvalidDataException("仅能保存完整的抖音主页链接。");
                    if (known.Add(friend.url.TrimEnd('/'))) { all.Add(friend); added++; }
                }
                WriteJson(path, all);
                return added;
            }
        }

        public static void DeleteFriend(string path, FriendRecord target)
        {
            lock (Gate)
            {
                var all = ReadFriends(path);
                all.RemoveAll(f => f.name == target.name && f.url == target.url);
                WriteJson(path, all);
            }
        }

        public static void AppendLog(string path, string line)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                {
                    if (File.Exists(path + ".5")) File.Delete(path + ".5");
                    for (int i = 4; i >= 1; i--) if (File.Exists(path + "." + i)) File.Move(path + "." + i, path + "." + (i + 1));
                    File.Move(path, path + ".1");
                }
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }

        public static List<string[]> ReadCsv(string path)
        {
            var result = new List<string[]>();
            if (!File.Exists(path)) return result;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(stream, Encoding.UTF8, true))
            {
                parser.TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited;
                parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true;
                if (!parser.EndOfData) parser.ReadFields();
                while (!parser.EndOfData) result.Add(parser.ReadFields());
            }
            return result;
        }
    }
}
