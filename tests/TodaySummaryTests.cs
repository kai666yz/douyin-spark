using System;
using System.Collections.Generic;
using DouyinSpark;

class TodaySummaryTests
{
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static void Main()
    {
        var today=new DateTime(2026,10,29);
        var friends=new[]{new FriendRecord{name="A",url="https://www.douyin.com/user/a"},new FriendRecord{name="duplicate",url="https://www.douyin.com/user/a/"},new FriendRecord{name="B",url="https://www.douyin.com/user/b"},new FriendRecord{name="missing",url=""}};
        var rows=new List<string[]>{
            new[]{"2026-10-29 10:00:00","A",friends[0].url,"message","UNCONFIRMED","journal"},
            new[]{"2026-10-29 10:00:00","A",friends[0].url,"message","OK","confirmed"},
            new[]{"2026-10-29 09:00:00","A",friends[0].url,"message","ERROR","late import of an older record"},
            new[]{"2026-10-28 11:00:00","B",friends[2].url,"message","OK","yesterday"},
            new[]{"2026-10-29 11:00:00","removed","https://www.douyin.com/user/removed","message","OK","not in current roster"}
        };
        var summary=TodaySummary.Build(friends,rows,today);
        Assert(summary.Total==2 && summary.Succeeded==1 && summary.Failed==0 && summary.Unconfirmed==0 && summary.Pending==1 && summary.MissingLinks==1,"unique friends, latest state, current day, active roster");
        rows.Add(new[]{"2026-10-29 12:00:00","B",friends[2].url,"message","UNCONFIRMED","unknown result"});
        summary=TodaySummary.Build(friends,rows,today);
        Assert(summary.Processed==2 && summary.Unconfirmed==1 && summary.Failed==0 && summary.Pending==0,"unconfirmed is not a failure");
        rows.Add(new[]{"2026-10-29 12:01:00","B",friends[2].url,"message","CANCELLED","cancelled before submission"});
        summary=TodaySummary.Build(friends,rows,today);
        Assert(summary.Pending==1 && summary.Processed==1,"cancelled work remains pending");
        summary=TodaySummary.Build(new FriendRecord[0],rows,today);
        Assert(summary.Total==0 && summary.Processed==0 && summary.Pending==0,"empty roster");
        Console.WriteLine("PASS: dashboard dedup, timestamp ordering, journal confirmation, date filtering, missing links, unconfirmed, cancellation and empty roster.");
    }
}
