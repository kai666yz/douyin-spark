using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        static string FindBackend()
        {
            return new[] { Path.Combine(BaseDir, "spark_backend", "spark_backend.exe"), Path.Combine(BaseDir, "spark_backend.exe") }.FirstOrDefault(File.Exists) ?? "";
        }
        static string FindPython()
        {
            string[] preferred = { Path.Combine(BaseDir, ".venv", "Scripts", "python.exe"), Environment.GetEnvironmentVariable("DOUYIN_SPARK_PYTHON"), Path.Combine(BaseDir, "runtime", "python", "python.exe") };
            foreach (string path in preferred) if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { string p = Path.Combine(folder.Trim('"'), "python.exe"); if (p.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0 && File.Exists(p)) return p; } catch { }
            }
            // Compatibility fallback only. No username or sandbox-version directory is hardcoded.
            string sandbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Doubao\User Data\sandbox_runtime\bases");
            if (Directory.Exists(sandbox)) foreach (string folder in Directory.GetDirectories(sandbox))
            { string p = Path.Combine(folder, "python", "python.exe"); if (File.Exists(p)) return p; }
            return "";
        }
        public static string QuoteArg(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1).Append('"');
                else result.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        static ProcessStartInfo BackendInfo(string arguments)
        {
            string exe = FindBackend();
            var psi = new ProcessStartInfo();
            if (exe.Length > 0) { psi.FileName = exe; psi.Arguments = arguments; }
            else
            {
                if (string.IsNullOrEmpty(PyExe)) PyExe = FindPython();
                if (PyExe.Length == 0) throw new FileNotFoundException("缺少运行环境，请完整解压便携包或运行 setup.ps1。");
                psi.FileName = PyExe;
                psi.Arguments = QuoteArg(Path.Combine(BaseDir, "douyin_spark.py")) + " " + arguments;
            }
            psi.WorkingDirectory = BaseDir;
            psi.EnvironmentVariables["DOUYIN_SPARK_DATA_DIR"] = BaseDir;
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            SetUtf8Encoding(psi);
            return psi;
        }
        static string RunBackend(string arguments)
        {
            var output = new StringBuilder(); var errors = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = BackendInfo(arguments);
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock(output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock(errors) errors.AppendLine(e.Data); };
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException(errors.ToString().Length > 0 ? errors.ToString() : "后端退出码 " + process.ExitCode);
            }
            return output.ToString();
        }
        static void BackendAction(string arguments)
        {
            new System.Threading.Thread(delegate()
            {
                try { Log(RunBackend(arguments)); if (mainForm != null) mainForm.BeginInvoke((Action)LoadLogs); }
                catch (Exception ex) { Log(ex.Message); }
            }) { IsBackground = true }.Start();
        }
    }
}
