using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;
using DouyinSpark;

class DeploymentTests
{
    static int checks;
    static void Assert(bool value,string label){if(!value)throw new Exception(label);checks++;}
    static void DenyWrite(string path,Action action)
    {
        var old=Directory.GetAccessControl(path,AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        var access=new DirectorySecurity();access.SetSecurityDescriptorBinaryForm(old,AccessControlSections.Access);
        access.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.Write|FileSystemRights.Delete,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Deny));
        try{Directory.SetAccessControl(path,access);action();}
        finally{var restore=new DirectorySecurity();restore.SetSecurityDescriptorBinaryForm(old,AccessControlSections.Access);Directory.SetAccessControl(path,restore);}
    }
    static void Main(string[] args)
    {
        string test=Path.Combine(args[0],"部署检测 中文 空格 "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(test);
        string source=Path.Combine(test,"只读程序😀"),local=Path.Combine(test,"用户数据");Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source,"friends.json"),"[{\"name\":\"测试😀\",\"url\":\"https://www.douyin.com/user/qa_only\"}]",new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(source,"config.json"),"{\"browser\":\"msedge\",\"schedule\":\"13:00\"}",new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(source,"logs"));File.WriteAllText(Path.Combine(source,"logs","spark_log.csv"),"FAKE_HISTORY");
        Assert(DataLocation.Resolve(source,local).Root==source,"portable stays next to app");
        byte[] before=File.ReadAllBytes(Path.Combine(source,"friends.json"));DataLocation result=null;
        DenyWrite(source,delegate{
            Assert(!DataLocation.CanWrite(source),"real write ACL enforced");result=DataLocation.Resolve(source,local);
            Assert(result.Fallback&&result.Root.StartsWith(local),"fallback to local user directory");
            Assert(SparkStorage.ReadFriends(Path.Combine(result.Root,"friends.json")).Count==1,"friends copied");
            Assert(File.ReadAllText(Path.Combine(result.Root,"logs","spark_log.csv"))=="FAKE_HISTORY","history copied");
            Assert(File.Exists(Path.Combine(result.Root,"storage.ready"))&&!File.Exists(Path.Combine(result.Root,"migration.pending")),"transaction committed");
        });
        Assert(before.SequenceEqual(File.ReadAllBytes(Path.Combine(source,"friends.json"))),"original friend bytes retained");
        SparkStorage.WriteJson(Path.Combine(result.Root,"friends.json"),new[]{new FriendRecord{name="新数据",url="https://www.douyin.com/user/qa_new"}});
        Assert(DataLocation.Resolve(source,local).Root==result.Root,"fallback stays selected after permission recovery");
        Assert(SparkStorage.ReadFriends(Path.Combine(result.Root,"friends.json"))[0].name=="新数据","restart does not overwrite newer fallback data");
        var config=new Dictionary<string,object>{{"schedule","13:00"}};
        Assert(SchedulePolicy.Review(config,"first","")&&(bool)config["schedule_paused"],"upgrade without old identity pauses");
        string file=Path.Combine(result.Root,"config.json");SparkStorage.WriteJson(file,config);
        var restored=SparkStorage.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));
        Assert((bool)restored["schedule_paused"],"pause persists across reload");
        restored["schedule_paused"]=false;Assert(!SchedulePolicy.Review(restored,"same","same")&&!(bool)restored["schedule_paused"],"same location respects explicit resume");
        Assert(SchedulePolicy.Review(restored,"moved","same")&&(bool)restored["schedule_paused"],"moved machine or folder pauses again");
        DateTime now=new DateTime(2026,10,29,14,0,0),next=SchedulePolicy.Next(now,"13:00");
        Assert(next==new DateTime(2026,10,30,13,0,0),"no startup catch-up");
        Assert(SchedulePolicy.Next(now,"invalid")==DateTime.MaxValue,"reject invalid schedule");
        Assert(SchedulePolicy.Due(next.AddSeconds(5),next,false,""),"normal tick due");
        Assert(!SchedulePolicy.Due(next,next,true,""),"paused cannot dispatch");
        Assert(!SchedulePolicy.Due(next,next,false,"2026-10-30"),"daily attempt cannot repeat after restart");
        Assert(!SchedulePolicy.Due(next.AddMinutes(5),next,false,""),"sleep wake after missed window does not catch up");
        Assert(!SchedulePolicy.Due(next.AddSeconds(-1),next,false,""),"never dispatch before time");
        string broken=Path.Combine(test,"损坏名单");Directory.CreateDirectory(broken);File.WriteAllText(Path.Combine(broken,"friends.json"),"{broken");
        bool refused=false;DenyWrite(broken,delegate{try{DataLocation.Resolve(broken,local);}catch{refused=true;}});
        Assert(refused&&File.ReadAllText(Path.Combine(broken,"friends.json"))=="{broken","invalid source is preserved, not replaced by empty data");
        SparkStorage.WriteJson(Path.Combine(args[0],"deployment-test-routing.json"),new Dictionary<string,string>{{"program",source},{"local",local},{"expected",result.Root}});
        Console.WriteLine("PASS: "+checks+" deployment/scheduling assertions; original data preserved, no messages or user registry access.");
    }
}
