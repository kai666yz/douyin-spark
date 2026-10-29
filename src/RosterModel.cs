using System;
using System.Collections.Generic;
using System.Linq;

namespace DouyinSpark
{
    public class RosterItem
    {
        public string Key,Name,Kind,Url,Status,ConversationId;
        public bool Sendable;
        public string Subtitle {get{return Kind=="group"?"群聊 · 仅管理":!Sendable?"私信 · 待补主页":Status=="OK"?"私信 · 今日已发送":Status=="UNCONFIRMED"?"私信 · 发送待确认":Status==""?"私信 · 今日待发送":"私信 · 请检查发送结果";}}
    }
    public static class RosterModel
    {
        public static List<RosterItem> Build(IEnumerable<FriendRecord> friends,IEnumerable<ConversationRecord> conversations,IEnumerable<string[]> rows,DateTime day)
        {
            var latest=new Dictionary<string,Tuple<DateTime,string>>();
            foreach(var row in rows){DateTime stamp;if(row.Length<5||!DateTime.TryParse(row[0],out stamp)||stamp.Date!=day.Date)continue;string url=row[2].TrimEnd('/');Tuple<DateTime,string> old;if(!latest.TryGetValue(url,out old)||stamp>=old.Item1)latest[url]=Tuple.Create(stamp,row[4]);}
            var result=new List<RosterItem>();var urls=new HashSet<string>();
            foreach(var friend in friends){
                string url=(friend.url??"").TrimEnd('/');if(url.Length>0&&!urls.Add(url))continue;Tuple<DateTime,string> state;
                result.Add(new RosterItem {Key="friend:"+(url.Length>0?url:friend.name),Name=friend.name,Url=url,Kind="private",Status=latest.TryGetValue(url,out state)?state.Item2:"",Sendable=FriendRecord.ValidUrl(url)});
            }
            foreach(var record in conversations){
                if(record.kind=="private"&&record.url.Length>0&&urls.Contains(record.url)){var item=result.First(x=>x.Url==record.url);item.ConversationId=record.id;continue;}
                result.Add(new RosterItem {Key=record.id,ConversationId=record.id,Name=record.name,Kind=record.kind,Url=record.url,Status="",Sendable=false});
            }
            return result.OrderBy(x=>x.Kind=="group"?1:0).ThenBy(x=>x.Name).ToList();
        }
    }
}
