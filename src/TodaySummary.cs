using System;
using System.Collections.Generic;
using System.Linq;

namespace DouyinSpark
{
    public class TodaySummary
    {
        public int Total, Succeeded, Failed, Unconfirmed, Pending, MissingLinks;
        public int Processed { get { return Succeeded + Failed + Unconfirmed; } }
        public static TodaySummary Build(IEnumerable<FriendRecord> friends, IEnumerable<string[]> rows, DateTime date)
        {
            var result = new TodaySummary();
            var all = friends.ToList();
            result.MissingLinks = all.Count(f => !FriendRecord.ValidUrl(f.url));
            var urls = new HashSet<string>(all.Where(f => FriendRecord.ValidUrl(f.url)).Select(f => f.url.TrimEnd('/')));
            result.Total = urls.Count;
            var latest = new Dictionary<string,Tuple<DateTime,string>>();
            foreach (var row in rows)
                if (row.Length >= 5 && row[0].StartsWith(date.ToString("yyyy-MM-dd")) && urls.Contains(row[2].TrimEnd('/')))
                {
                    DateTime stamp;Tuple<DateTime,string> prior;
                    if(!DateTime.TryParse(row[0],out stamp))continue;
                    string url=row[2].TrimEnd('/');
                    if(!latest.TryGetValue(url,out prior) || stamp>=prior.Item1)latest[url]=Tuple.Create(stamp,row[4]);
                }
            foreach (var item in latest.Values)
            {
                string state=item.Item2;
                if (state == "OK") result.Succeeded++;
                else if (state == "UNCONFIRMED") result.Unconfirmed++;
                else if (state != "CANCELLED" && state != "STOPPED") result.Failed++;
            }
            result.Pending = Math.Max(0, result.Total - result.Processed);
            return result;
        }
    }
}
