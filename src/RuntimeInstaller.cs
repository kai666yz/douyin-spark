using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DouyinSpark
{
    static class RuntimeInstaller
    {
        public const string DownloadPage="https://developer.microsoft.com/microsoft-edge/webview2/#download-section";
        // Microsoft-signed Evergreen bootstrapper obtained from Microsoft's distribution link.
        public const string BootstrapperHash="13f6d9cdc8c3e7186e4b180db7f48e199ee97d631480e8391fb3106f338fb21a";
        public static bool Verified(string path)
        {
            if(!File.Exists(path))return false;
            using(var file=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant()==BootstrapperHash;
        }
        public static Task InstallAsync(string programRoot)
        {
            return Task.Run(delegate{
                string path=Path.Combine(programRoot,"runtime","MicrosoftEdgeWebview2Setup.exe");
                if(!Verified(path))throw new IOException("微软安装器缺失或校验失败，请重新完整解压，或使用微软官方下载页。");
                using(var process=Process.Start(new ProcessStartInfo(path,"/silent /install"){UseShellExecute=false,CreateNoWindow=true})){
                    if(!process.WaitForExit(600000))throw new IOException("准备组件超过10分钟。安装器可能仍在后台处理，请稍后重新检查，勿重复安装。");
                    if(process.ExitCode!=0&&process.ExitCode!=3010)throw new IOException("微软安装器返回 "+process.ExitCode+"，请检查网络或稍后重试。");
                }
            });
        }
    }
}
