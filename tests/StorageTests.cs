using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DouyinSpark;

class StorageTests
{
    static void Assert(bool condition,string message) {if(!condition) throw new Exception(message);}
    static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"spark-storage-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path=Path.Combine(root,"friends.json");
        string url="https://www.douyin.com/user/local_test";
        string name="引号\" 逗号, 换行\n emoji😀 反斜杠\\";
        Assert(SparkStorage.AddFriends(path,new[]{new FriendRecord{name=name,url=url}})==1,"add");
        Assert(SparkStorage.ReadFriends(path)[0].name==name,"escaped-name roundtrip");
        Assert(SparkStorage.AddFriends(path,new[]{new FriendRecord{name="duplicate",url=url+"/"}})==0,"dedup");
        Assert(File.Exists(path+".bak"),"backup");
        File.WriteAllText(path,"\uFEFF[{\"name\":\"comma,] stays\",\"url\":\""+url+"\",},]",new UTF8Encoding(true));
        Assert(SparkStorage.ReadFriends(path)[0].name=="comma,] stays","BOM / trailing-comma / string integrity");
        File.WriteAllText(path,"{not valid json}");
        bool failed=false;
        try{SparkStorage.AddFriends(path,new[]{new FriendRecord{name="x",url=url}});}catch{failed=true;}
        Assert(failed && File.ReadAllText(path)=="{not valid json}","invalid JSON must not be overwritten");
        string log=Path.Combine(root,"app_log.txt");
        File.WriteAllText(log,new string('x',2*1024*1024+1));
        SparkStorage.AppendLog(log,"new");
        Assert(File.Exists(log+".1") && File.ReadAllText(log).Contains("new"),"log rotation");
        Console.WriteLine("PASS: JSON roundtrip, duplicate URL, BOM, trailing comma, invalid-file preservation, backup and bounded text logs.");
        // All created files are under the fresh temporary test directory.
        Directory.Delete(root,true);
    }
}
