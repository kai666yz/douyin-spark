using System;using System.IO;using System.Linq;using System.Collections.Generic;using DouyinSpark;
class GroupTaskTests {
 static int count;static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;}
 static void Main(string[] args){string root=Path.Combine(args[0],"group-model-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var a=ConversationRecord.Normalize(new ConversationRecord{name="同名群",kind="group",conversation_id="g:111"});var b=ConversationRecord.Normalize(new ConversationRecord{name="同名群",kind="group",conversation_id="g:222"});var p=ConversationRecord.Normalize(new ConversationRecord{name="私信",kind="private",conversation_id="p:1",url="https://www.douyin.com/user/qa"});
  Check(GroupTaskSettings.Resolve(new[]{a,b,p},new[]{b.id}).Single().conversation_id=="g:222","same names use exact ID");
  bool rejected=false;try{GroupTaskSettings.Resolve(new[]{a,b,p},new[]{p.id});}catch(InvalidDataException){rejected=true;}Check(rejected,"private cannot enter group plan");
  rejected=false;try{GroupTaskSettings.Resolve(new[]{a},new[]{b.id});}catch(InvalidDataException){rejected=true;}Check(rejected,"removed target rejected");
  var plan=new GroupTaskSettings{message="群聊独立消息😀",targets=new List<string>{a.id,a.id},schedule="13:10",paused=false};plan.Save(root);var read=GroupTaskSettings.Read(root);Check(read.targets.Count==1&&read.message==plan.message&&!read.paused,"settings persist and dedup");
  Check(read.interval_seconds==60,"groups default to slower 60-second spacing");
  plan.interval_seconds=1;rejected=false;try{plan.Save(root);}catch(InvalidDataException){rejected=true;}Check(rejected,"too-fast group spacing rejected");plan.interval_seconds=30;plan.Save(root);Check(GroupTaskSettings.Read(root).interval_seconds==30,"independent group interval persists");
  File.WriteAllText(Path.Combine(root,"group_task.json"),"{\"message\":\"legacy\",\"targets\":[],\"schedule\":\"\",\"paused\":true}");Check(GroupTaskSettings.Read(root).interval_seconds==60&&GroupTaskSettings.Read(root).message_mode=="fixed","old fixed config remains fixed without new keys");
  var modes=new GroupTaskSettings{message_mode="rotation",targets=new List<string>{a.id}};rejected=false;try{modes.Save(root);}catch(InvalidDataException){rejected=true;}Check(rejected,"empty rotation rejected");modes.custom_messages=new List<string>{"A","B"};modes.Save(root);Check(GroupTaskSettings.Read(root).custom_messages.Count==2,"rotation source persisted separately");modes.message_mode="random";modes.categories.Clear();rejected=false;try{modes.Save(root);}catch(InvalidDataException){rejected=true;}Check(rejected,"random source requires a category");
  plan.schedule="invalid";rejected=false;try{plan.Save(root);}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid enabled schedule rejected");
  plan.schedule="13:10";plan.paused=true;plan.Save(root);Check(GroupTaskSettings.Read(root).paused,"pause survives restart");
  DateTime day=new DateTime(2026,10,29,12,0,0);var attempt=GroupJournal.Begin(root,a,day);Check(attempt.status=="UNCONFIRMED"&&GroupJournal.Blocked(GroupJournal.Read(root,day),a.id,day),"attempt persisted before click");
  rejected=false;try{GroupJournal.Begin(root,a,day);}catch(InvalidOperationException){rejected=true;}Check(rejected,"uncertain cannot auto retry");
  attempt.status="SUBMITTED";GroupJournal.Write(root,attempt);Check(GroupSummary.Build(new[]{a.id,b.id},GroupJournal.Read(root,day),day).Unconfirmed==1,"submission is not success");
  attempt.status="CONFIRMED";GroupJournal.Write(root,attempt);Check(GroupSummary.Build(new[]{a.id,b.id},GroupJournal.Read(root,day),day).Succeeded==1,"manual confirmation counted separately");
  Check(!GroupJournal.Blocked(GroupJournal.Read(root),a.id,day.AddDays(1)),"new day eligible");
  attempt.status="FAILED";GroupJournal.Write(root,attempt);Check(!GroupJournal.Blocked(GroupJournal.Read(root,day),a.id,day),"explicitly checked failed result may retry");
  var again=GroupJournal.Begin(root,a,day);var summary=GroupSummary.Build(new[]{a.id,b.id},GroupJournal.Read(root,day),day);Check(summary.Unconfirmed==1&&summary.Failed==0&&summary.Pending==1,"same-timestamp later submission wins over failure");
  rejected=false;try{GroupJournal.Write(root,new GroupAttempt{id="../escape",group_id=a.id});}catch(InvalidDataException){rejected=true;}Check(rejected,"journal path traversal rejected");
  using(var first=GroupJournal.Acquire(root)){rejected=false;try{using(var second=GroupJournal.Acquire(root)){};}catch(InvalidOperationException){rejected=true;}Check(rejected,"shared task lock prevents overlapping sends");}
  using(var released=GroupJournal.Acquire(root))Check(released!=null,"lock released after task");
  GroupJournal.Cancel(root,b);var cancelled=GroupSummary.Build(new[]{b.id},GroupJournal.Read(root,DateTime.Now),DateTime.Now);Check(cancelled.Failed==0&&cancelled.Pending==1,"cancelled groups remain pending, not failed");
  Console.WriteLine("PASS: "+count+" group-plan, journal, duplicate prevention, exact identity, pause and lock assertions; no browser/messages.");
 }
}
