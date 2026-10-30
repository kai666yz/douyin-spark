using System;using System.IO;using System.Linq;using System.Collections.Generic;using DouyinSpark;
class ConversationTests
{
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Main(){
  string root=Path.Combine(Path.GetTempPath(),"conversation-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string path=Path.Combine(root,"conversations.json");
  try {
   var group=ConversationRecord.Normalize(new ConversationRecord {name="同名",kind="group",conversation_id="group:123",url="https://www.douyin.com/user/wrong"});
   Check(group.url==""&&group.CanSend&&!group.CanSendPrivate,"group sends only by ID, never through private profile path");
   var pending=ConversationRecord.Normalize(new ConversationRecord {name="待补",kind="private",conversation_id="peer:99"});Check(!pending.CanSend,"no profile must not send");
   var person=ConversationRecord.Normalize(new ConversationRecord {name="好友",kind="private",conversation_id="peer:1",url="https://www.douyin.com/user/qa"});
   Check(ConversationStore.Merge(path,new[]{group,pending,person})==3,"initial import");
   Check(ConversationStore.Merge(path,new[]{person})==0,"stable ID dedup");
   var other=ConversationRecord.Normalize(new ConversationRecord {name="同名",kind="group",conversation_id="group:456"});
   Check(ConversationStore.Merge(path,new[]{other})==1,"same-name different groups must remain distinct");
   string before=File.ReadAllText(path);bool rejected=false;
   try{ConversationStore.Merge(path,new[]{new ConversationRecord {name="wrong type",kind="group",conversation_id="peer:1"}});}catch(InvalidDataException){rejected=true;}
   Check(rejected&&File.ReadAllText(path)==before,"conflict must preserve original");
   rejected=false;try{ConversationStore.Merge(path,new[]{new ConversationRecord {name="unknown",kind="unknown",conversation_id="x"}});}catch(InvalidDataException){rejected=true;}
   Check(rejected&&File.ReadAllText(path)==before,"unknown type must require user classification");
   rejected=false;try{ConversationRecord.Normalize(new ConversationRecord {name="nickname only",kind="group"});}catch(InvalidDataException){rejected=true;}Check(rejected,"nickname is not identity");
   var parsed=ConversationStore.FromPayload("{\"conversations\":[{\"conversation_id\":\"g1\",\"group_name\":\"测试群\",\"last_message\":{\"content\":\"must not store\"}},{\"conversation_id\":\"p1\",\"other_user\":{\"nickname\":\"朋友\",\"sec_uid\":\"peer_qa\"}}]}");
   Check(parsed.Count==2&&parsed.Any(x=>x.kind=="group")&&parsed.Any(x=>x.CanSend),"metadata parser");
   Check(!SparkStorage.Json().Serialize(parsed).Contains("must not store"),"message bodies not stored");
   var friends=new[]{new FriendRecord {name="好友",url=person.url}};
   var rows=new[]{new[]{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),"好友",person.url,"test","OK",""}};
   var roster=RosterModel.Build(friends,ConversationStore.Read(path),rows,DateTime.Now);
   Check(roster.Count==4&&roster.Count(x=>x.Sendable)==1&&roster.Count(x=>x.Kind=="group")==2,"roster dedup and group isolation");
   Check(roster.Single(x=>x.Sendable).Status=="OK","latest status");
   Console.WriteLine("PASS: conversation identity, group isolation, missing profiles, dedup, same names, type conflicts, metadata privacy and roster state.");
  }finally{foreach(string file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);}
 }
}
