using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DouyinSpark
{
    public class DataLocation
    {
        public string Root,ProgramRoot,Notice="";
        public bool Fallback,Changed;
        public static readonly string[] Files={"friends.json","config.json","following.json","sentences_pool.json","settings.json","conversations.json","conversation_candidates.json","group_task.json",".spark-safety-stop.json"};
        public static readonly string[] Folders={"logs","conversation_profile","browser_profile","browser_profile_edge","browser_profile_quark"};
        public static string Normalize(string path){return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).ToUpperInvariant();}
        public static string Hash(string text){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        public static bool CanWrite(string folder)
        {
            string probe=Path.Combine(folder,".spark-write-"+Guid.NewGuid().ToString("N"));
            try{Directory.CreateDirectory(folder);using(var stream=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.WriteByte(1);}File.Delete(probe);return true;}
            catch{return false;}finally{try{if(File.Exists(probe))File.Delete(probe);}catch{}}
        }
        public static DataLocation Resolve(string programRoot,string localRoot)
        {
            programRoot=Path.GetFullPath(programRoot);
            string fallback=Path.Combine(localRoot,"data",Hash(Normalize(programRoot)).Substring(0,16));
            bool sticky=File.Exists(Path.Combine(fallback,"storage.ready"));
            var result=new DataLocation {ProgramRoot=programRoot,Root=programRoot};
            if(!sticky&&CanWrite(programRoot))return result;
            if(!CanWrite(fallback))throw new IOException("程序目录和本机数据目录均不可写。请将完整软件解压到个人文件夹后重试。");
            result.Root=fallback;result.Fallback=true;
            if(!sticky)
            {
                string marker=Path.Combine(fallback,"migration.pending");
                bool resume=File.Exists(marker)&&File.ReadAllText(marker)==Normalize(programRoot);
                if(!resume&&Directory.EnumerateFileSystemEntries(fallback).Any())throw new IOException("备用数据目录已有未确认文件，为保护名单未覆盖。请查看："+fallback);
                // Validate source data before creating defaults or copying partial data.
                if(File.Exists(Path.Combine(programRoot,"friends.json")))SparkStorage.ReadFriends(Path.Combine(programRoot,"friends.json"));
                if(File.Exists(Path.Combine(programRoot,"config.json")))
                    if(SparkStorage.Json().Deserialize<Dictionary<string,object>>(SparkStorage.CleanJson(File.ReadAllText(Path.Combine(programRoot,"config.json"))))==null)throw new InvalidDataException("原配置无效，未创建空配置。");
                File.WriteAllText(marker,Normalize(programRoot));
                foreach(string file in Files)if(File.Exists(Path.Combine(programRoot,file)))File.Copy(Path.Combine(programRoot,file),Path.Combine(fallback,file),true);
                foreach(string folder in Folders)if(Directory.Exists(Path.Combine(programRoot,folder)))CopyDirectory(Path.Combine(programRoot,folder),Path.Combine(fallback,folder));
                File.WriteAllText(Path.Combine(fallback,"storage.ready"),Normalize(programRoot));File.Delete(marker);
                result.Notice="软件目录不可写，已将可读取的数据复制到本机用户目录。原文件保留；登录可能需要重新验证。";
            }
            else result.Notice="当前使用本机用户目录保存数据，原软件目录中的文件不会被覆盖。";
            return result;
        }
        static void CopyDirectory(string source,string target)
        {
            if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("数据目录包含链接，请手动核对后迁移。");
            Directory.CreateDirectory(target);
            foreach(string file in Directory.GetFiles(source)){
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("数据文件包含链接，请手动核对后迁移。");
                string name=Path.GetFileName(file);
                if(name=="lockfile"||name.StartsWith("Singleton",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase))continue;
                File.Copy(file,Path.Combine(target,name),true);
            }
            foreach(string child in Directory.GetDirectories(source)){
                if(new[]{"Cache","Code Cache","GPUCache","ShaderCache","GrShaderCache","DawnGraphiteCache","DawnWebGPUCache","BrowserMetrics","Crashpad"}.Contains(Path.GetFileName(child),StringComparer.OrdinalIgnoreCase))continue;
                CopyDirectory(child,Path.Combine(target,Path.GetFileName(child)));
            }
        }
    }

    public static class SchedulePolicy
    {
        public static DateTime Next(DateTime now,string time)
        {
            DateTime parsed;
            if(!DateTime.TryParseExact(time,"HH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out parsed))return DateTime.MaxValue;
            DateTime value=now.Date.Add(parsed.TimeOfDay);return value>now?value:value.AddDays(1);
        }
        public static bool Due(DateTime now,DateTime next,bool paused,string lastDay)
        {return !paused&&now>=next&&(now-next).TotalSeconds<60&&lastDay!=now.ToString("yyyy-MM-dd");}
        public static bool Review(Dictionary<string,object> config,string identity,string previous)
        {
            bool changed=identity!=previous;
            if(changed){object time;if(config.TryGetValue("schedule",out time)&&!string.IsNullOrWhiteSpace(Convert.ToString(time))){config["schedule_paused"]=true;config["schedule_pause_reason"]="运行位置或账户环境变化，请确认后重新启用";}}
            return changed;
        }
    }
}
