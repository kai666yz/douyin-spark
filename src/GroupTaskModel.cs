using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DouyinSpark
{
    public class GroupTaskSettings
    {
        public string message {get;set;}
        public string message_mode {get;set;}
        public List<string> categories {get;set;}
        public List<string> custom_messages {get;set;}
        public List<string> targets {get;set;}
        public string schedule {get;set;}
        public bool paused {get;set;}
        public string last_trigger_day {get;set;}
        public int interval_seconds {get;set;}
        public GroupTaskSettings(){message="";targets=new List<string>();schedule="";paused=true;last_trigger_day="";interval_seconds=60;message_mode="random";categories=new List<string>{"废话文学"};custom_messages=new List<string>();}
        public static GroupTaskSettings Read(string root)
        {
            string path=Path.Combine(root,"group_task.json");if(!File.Exists(path))return new GroupTaskSettings();
            string text=SparkStorage.CleanJson(File.ReadAllText(path,Encoding.UTF8));var value=SparkStorage.Json().Deserialize<GroupTaskSettings>(text);
            if(value!=null&&!SparkStorage.Json().Deserialize<Dictionary<string,object>>(text).ContainsKey("message_mode"))value.message_mode=string.IsNullOrWhiteSpace(value.message)?"random":"fixed";
            if(value==null||value.targets==null)throw new InvalidDataException("群聊任务配置损坏，原文件已保留。");value.categories=value.categories??new List<string>();value.custom_messages=value.custom_messages??new List<string>();return value;
        }
        public void Save(string root)
        {
            if(interval_seconds<15||interval_seconds>600)throw new InvalidDataException("群间间隔须为15～600秒。");
            if(message==null||message.Length>2000)throw new InvalidDataException("群聊消息请控制在1～2000字以内。");
            if(!new[]{"random","fixed","rotation"}.Contains(message_mode))throw new InvalidDataException("请选择消息模式。");
            categories=categories??new List<string>();custom_messages=custom_messages??new List<string>();
            if(message_mode=="random"&&categories.Count==0)throw new InvalidDataException("请至少选择一个群聊语录分类。");
            if(message_mode=="rotation"&&!custom_messages.Any(x=>!string.IsNullOrWhiteSpace(x)))throw new InvalidDataException("请输入多条轮换语录，每行一条。");
            if(custom_messages.Any(x=>x!=null&&x.Length>2000))throw new InvalidDataException("每条语录不超过2000字。");
            targets=(targets??new List<string>()).Distinct().ToList();
            if(!paused&&(message_mode=="fixed"&&string.IsNullOrWhiteSpace(message)||targets.Count==0||SchedulePolicy.Next(DateTime.Now,schedule)==DateTime.MaxValue))throw new InvalidDataException("启用群聊定时前，请选择目标群、填写消息及HH:mm时间。");
            SparkStorage.WriteJson(Path.Combine(root,"group_task.json"),this);
        }
        public static List<ConversationRecord> Resolve(IEnumerable<ConversationRecord> records,IEnumerable<string> ids)
        {
            var selected=new HashSet<string>(ids??new string[0]);var groups=records.Where(x=>x.kind=="group"&&x.conversation_id.Length>0&&selected.Contains(x.id)).ToList();
            if(groups.Count!=selected.Count)throw new InvalidDataException("部分目标群已移出名单或标识无效，请重新核对群聊任务。");return groups;
        }
    }
    public class GroupAttempt
    {
        public string id {get;set;}
        public string group_id {get;set;}
        public string name {get;set;}
        public string time {get;set;}
        public string status {get;set;}
        public string note {get;set;}
        public string message_hash {get;set;}
    }
    public static class GroupJournal
    {
        public static string Folder(string root){return Path.Combine(root,"logs","group_attempts");}
        public static List<GroupAttempt> Read(string root,DateTime? day=null)
        {
            var result=new List<GroupAttempt>();string folder=Folder(root);if(!Directory.Exists(folder))return result;
            foreach(string file in Directory.GetFiles(folder,day.HasValue?day.Value.ToString("yyyyMMdd")+"_*.json":"*.json")){
                var entry=SparkStorage.Json().Deserialize<GroupAttempt>(File.ReadAllText(file,Encoding.UTF8));
                if(entry==null||string.IsNullOrEmpty(entry.group_id)||string.IsNullOrEmpty(entry.status)||string.IsNullOrEmpty(entry.time)||!ValidId(entry.id))throw new InvalidDataException("群聊记录无法读取，已暂停以免重复发送。");result.Add(entry);
            }
            return result.OrderByDescending(x=>x.time).ToList();
        }
        public static bool Blocked(IEnumerable<GroupAttempt> entries,string id,DateTime now)
        {return entries.Any(x=>x.group_id==id&&x.time.StartsWith(now.ToString("yyyy-MM-dd"))&&new[]{"UNCONFIRMED","SUBMITTED","OK","CONFIRMED"}.Contains(x.status));}
        public static GroupAttempt Begin(string root,ConversationRecord group,DateTime now)
        {
            if(Blocked(Read(root,now),group.id,now))throw new InvalidOperationException("该群今日已有提交或待确认记录，不会自动重发。");
            var item=new GroupAttempt{id=now.ToString("yyyyMMdd")+"_"+Guid.NewGuid().ToString("N"),group_id=group.id,name=group.name,time=now.ToString("s"),status="UNCONFIRMED",note="已准备提交，结果需确认"};
            Directory.CreateDirectory(Folder(root));Write(root,item);return item;
        }
        static bool ValidId(string value){return value!=null&&Regex.IsMatch(value,@"^\d{8}_[a-f0-9]{32}$");}
        public static void Write(string root,GroupAttempt item){if(item==null||!ValidId(item.id))throw new InvalidDataException("记录标识无效，未覆盖文件。");SparkStorage.WriteJson(Path.Combine(Folder(root),item.id+".json"),item);}
        public static void Failure(string root,ConversationRecord group,string note)
        {
            Directory.CreateDirectory(Folder(root));Write(root,new GroupAttempt{id=DateTime.Now.ToString("yyyyMMdd")+"_"+Guid.NewGuid().ToString("N"),group_id=group.id,name=group.name,time=DateTime.Now.ToString("s"),status="FAILED",note=note});
        }
        public static void Cancel(string root,ConversationRecord group)
        {Directory.CreateDirectory(Folder(root));Write(root,new GroupAttempt{id=DateTime.Now.ToString("yyyyMMdd")+"_"+Guid.NewGuid().ToString("N"),group_id=group.id,name=group.name,time=DateTime.Now.ToString("s"),status="CANCELLED",note="用户停止，未开始处理"});}
        public static FileStream Acquire(string root)
        {
            string path=Path.Combine(root,".spark-group-run.lock");var file=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);
            try{if(file.Length==0){file.WriteByte(0);file.Flush();}file.Lock(0,1);return file;}catch{file.Dispose();throw new InvalidOperationException("已有发送或导入任务，请稍后重试。");}
        }
    }
}
