using System;
using System.IO;
using DouyinSpark;

class FollowingReportTests
{
    static void Main()
    {
        string dir=Path.Combine(Path.GetTempPath(),"following-report-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string path=Path.Combine(dir,"report.json");
        try {
            foreach(string status in new[]{"empty","login_required","failed","cancelled"}) {
                File.WriteAllText(path,"{\"status\":\""+status+"\",\"count\":0,\"items\":[]}");
                if(FollowingReport.Read(path).status!=status) throw new Exception("status lost");
            }
            foreach(string status in new[]{"success","partial"}) {
                File.WriteAllText(path,"{\"status\":\""+status+"\",\"count\":1,\"items\":[{\"name\":\"测试\",\"url\":\"https://www.douyin.com/user/qa\"}]}");
                if(FollowingReport.Read(path).items.Count!=1) throw new Exception("items lost");
            }
            foreach(string bad in new[]{"{}","{\"status\":\"success\",\"items\":[]}","{\"status\":\"partial\",\"count\":1,\"items\":[]}","{\"status\":\"success\",\"count\":1,\"items\":[{\"url\":\"https://evil.test\"}]}","{\"status\":\"stale\"}"}) {
                File.WriteAllText(path,bad);bool rejected=false;
                try {FollowingReport.Read(path);}catch(InvalidDataException){rejected=true;}
                if(!rejected)throw new Exception("unsafe result accepted: "+bad);
            }
            Console.WriteLine("PASS: status handling, empty/malformed result rejection and URL validation.");
        } finally {File.Delete(path);Directory.Delete(dir);}
    }
}
