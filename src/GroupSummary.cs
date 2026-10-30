using System;
using System.Collections.Generic;
using System.Linq;

namespace DouyinSpark
{
    public static class GroupSummary
    {
        public static TodaySummary Build(IEnumerable<string> targets,IEnumerable<GroupAttempt> attempts,DateTime day)
        {
            var ids=new HashSet<string>(targets??new string[0]);var result=new TodaySummary{Total=ids.Count};
            foreach(var entries in attempts.Where(x=>ids.Contains(x.group_id)&&x.time!=null&&x.time.StartsWith(day.ToString("yyyy-MM-dd"))).GroupBy(x=>x.group_id)){
                var entry=entries.OrderByDescending(x=>x.time).ThenByDescending(x=>new[]{"SUBMITTED","UNCONFIRMED","CONFIRMED","OK"}.Contains(x.status)?1:0).First();
                if(entry.status=="CONFIRMED"||entry.status=="OK")result.Succeeded++;
                else if(entry.status=="SUBMITTED"||entry.status=="UNCONFIRMED")result.Unconfirmed++;
                else if(entry.status!="CANCELLED")result.Failed++;
            }
            result.Pending=Math.Max(0,result.Total-result.Processed);return result;
        }
    }
}
