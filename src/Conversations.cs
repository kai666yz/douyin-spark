using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DouyinSpark
{
    public class ConversationRecord
    {
        public string id {get;set;}
        public string kind {get;set;}
        public string name {get;set;}
        public string url {get;set;}
        public string conversation_id {get;set;}
        public string source {get;set;}
        public string updated_at {get;set;}
        public bool CanSend {get{return CanSendPrivate || kind=="group"&&!string.IsNullOrWhiteSpace(conversation_id);}}
        [System.Web.Script.Serialization.ScriptIgnore]
        public bool CanSendPrivate {get{return kind=="private"&&FriendRecord.ValidUrl(url);}}
        public static ConversationRecord Normalize(ConversationRecord item)
        {
            if(item==null || string.IsNullOrWhiteSpace(item.name))throw new InvalidDataException("会话缺少名称。");
            if(!new[]{"private","group","unknown"}.Contains(item.kind))throw new InvalidDataException("会话类型无效。");
            item.url=FriendRecord.ValidUrl(item.url)?item.url.TrimEnd('/'):"";
            item.conversation_id=item.conversation_id??"";
            if(item.conversation_id.Length>240 || (item.conversation_id.Length>0 && !Regex.IsMatch(item.conversation_id,@"^[A-Za-z0-9_:.-]+$")))throw new InvalidDataException("会话标识无效。");
            if(item.kind=="group")item.url="";
            if(item.conversation_id.Length>0)item.id="conversation:"+item.conversation_id;
            else if(item.kind!="group" && item.url.Length>0)item.id="profile:"+item.url;
            else throw new InvalidDataException("缺少可验证的会话标识或个人主页，不能仅凭昵称导入。");
            item.name=item.name.Trim();if(item.name.Length>120)item.name=item.name.Substring(0,120);
            item.source="messages";return item;
        }
    }
    public static class ConversationStore
    {
        static readonly object Gate=new object();
        public static List<ConversationRecord> Read(string path)
        {
            lock(Gate){
                if(!File.Exists(path))return new List<ConversationRecord>();
                var items=SparkStorage.Json().Deserialize<List<ConversationRecord>>(SparkStorage.CleanJson(File.ReadAllText(path,System.Text.Encoding.UTF8)));
                if(items==null)throw new InvalidDataException("会话名单损坏，原文件已保留。");
                return items.Select(ConversationRecord.Normalize).ToList();
            }
        }
        public static int Merge(string path,IEnumerable<ConversationRecord> incoming)
        {
            lock(Gate){
                var added=incoming.Select(ConversationRecord.Normalize).ToList();
                if(added.Any(x=>x.kind=="unknown"))throw new InvalidDataException("请先确认未识别会话是私信还是群聊。");
                var existing=Read(path);var byId=existing.ToDictionary(x=>x.id);int count=0;
                foreach(var item in added){
                    ConversationRecord old;
                    if(byId.TryGetValue(item.id,out old)){
                        if(item.kind!=old.kind)throw new InvalidDataException("同一会话的类型不一致，原名单已保留。");
                        if(item.url.Length==0)item.url=old.url;
                    }else count++;
                    item.updated_at=DateTime.Now.ToString("s");byId[item.id]=item;
                }
                SparkStorage.WriteJson(path,byId.Values.ToList());return count;
            }
        }
        public static List<ConversationRecord> FromPayload(string json)
        {
            var result=new Dictionary<string,ConversationRecord>();
            Walk(SparkStorage.Json().DeserializeObject(json),0,result);return result.Values.ToList();
        }
        static string Text(Dictionary<string,object> d,params string[] keys){foreach(string key in keys){object v;if(d.TryGetValue(key,out v)&&v!=null&&!(v is IDictionary)&&!(v is IEnumerable && !(v is string)))return Convert.ToString(v);}return "";}
        static void Walk(object value,int depth,Dictionary<string,ConversationRecord> result)
        {
            if(depth>10 || result.Count>2000)return;
            var d=value as Dictionary<string,object>;
            if(d!=null){
                string cid=Text(d,"conversation_id","conversationId","conversation_short_id");
                if(cid.Length>0){
                    string name=Text(d,"conversation_name","group_name","name","title"),kind=Text(d,"kind","conversation_kind");
                    string sid=Text(d,"sec_uid","secUid"),url="";
                    object peer;var user=(d.TryGetValue("other_user",out peer)||d.TryGetValue("peer_user",out peer))?peer as Dictionary<string,object>:null;
                    if(user!=null){sid=Text(user,"sec_uid","secUid");if(name.Length==0)name=Text(user,"nickname","name");}
                    if(Regex.IsMatch(sid,@"^[A-Za-z0-9_-]+$"))url="https://www.douyin.com/user/"+sid;
                    object group;bool isGroup=(d.TryGetValue("is_group",out group)&&group is bool&&(bool)group)||Text(d,"group_name").Length>0;
                    if(isGroup||kind=="group")kind="group";else if(kind=="private"||url.Length>0)kind="private";else kind="unknown";
                    try{var item=ConversationRecord.Normalize(new ConversationRecord {name=name,kind=kind,conversation_id=cid,url=url});result[item.id]=item;}catch(InvalidDataException){}
                }
                foreach(var child in d.Values)if(!(child is string))Walk(child,depth+1,result);
            }else if(value is IEnumerable && !(value is string))foreach(var child in (IEnumerable)value)Walk(child,depth+1,result);
        }
    }
}
