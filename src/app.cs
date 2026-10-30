using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace DouyinSpark
{
    partial class Program
    {
        static string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        static string PyExe = "";
        static Form mainForm = null;
        static RichTextBox txtLog;
        static DataGridView dgv;
        static RoundButton btnStart;
        static Label lblStatus;
        static SparkProgress progress;
        static string scheduleTime = "";     // 每日定时 "HH:mm"
        const string AppVersion = "2.4.2";

        // HelloKitty 主题别名（Program 内直接使用）
        static readonly Color KPink = KittyTheme.KPink;
        static readonly Color KPinkDeep = KittyTheme.KPinkDeep;
        static readonly Color KPinkSoft = KittyTheme.KPinkSoft;
        static readonly Color KPinkBg = KittyTheme.KPinkBg;
        static readonly Color KPinkBox = KittyTheme.KPinkBox;
        static readonly Color KPinkLine = KittyTheme.KPinkLine;
        static readonly Color KPinkText = KittyTheme.KPinkText;
        static void PaintKittyFace(Graphics g, float cx, float cy, float s) { KittyTheme.PaintKittyFace(g, cx, cy, s); }
        static string CrashLogPath { get { return Path.Combine(BaseDir, "logs", "crash.log"); } }

        static void InstallGlobalErrorCatch()
        {
            // 全局异常兜底：任何 UI/后台线程未捕获异常 → 写 crash.log + 弹窗显示详情（不再出现"继续/退出"崩溃框）
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                try { SparkStorage.AppendLog(CrashLogPath, DateTime.Now.ToString("HH:mm:ss") + " [UI] " + e.Exception + "\n\n"); } catch { }
                MessageBox.Show("发生错误（已记录到 logs\\crash.log）：\n" + e.Exception.Message, "抖音续火花");
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                var ex = e.ExceptionObject as Exception;
                try { SparkStorage.AppendLog(CrashLogPath, DateTime.Now.ToString("HH:mm:ss") + " [APP] " + (ex != null ? ex.ToString() : Convert.ToString(e.ExceptionObject)) + "\n\n"); } catch { }
                MessageBox.Show("发生未处理错误（已记录到 logs\\crash.log）：\n" + (ex != null ? ex.Message : "未知错误"), "抖音续火花");
            };
        }

        [STAThread]
        static void Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
            bool minimized = args.Contains("--minimized");
            // 启动前确保 logs/update 目录存在，避免写日志时崩溃
            try
            {
                Directory.CreateDirectory(Path.Combine(BaseDir, "logs"));
                Directory.CreateDirectory(Path.Combine(BaseDir, "update"));
            }
            catch { }
            InstallGlobalErrorCatch();
            // 单实例互斥：防止多开导致定时重复触发 / 脚本并发冲突
            bool createdNew;
            var singleMutex = new Mutex(true, "DouyinSparkSingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("抖音续火花已在运行，请勿重复打开。");
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { PrepareStorageWithProgress(); }
            catch(Exception ex) { MessageBox.Show("数据目录准备未完成，原数据已保留。\n"+ex.Message,"启动检查");return; }
            PyExe = FindBackend().Length>0 ? "" : FindPython();
            if (string.IsNullOrEmpty(PyExe) && FindBackend().Length == 0)
            {
                MessageBox.Show("没有找到配套后端。请重新完整解压便携包，并保留 spark_backend 文件夹。源码用户可运行 setup.ps1。");
                return;
            }
            string friendsFile = Path.Combine(BaseDir, "friends.json");
            if (!File.Exists(friendsFile))
                File.WriteAllText(friendsFile, "[]", new System.Text.UTF8Encoding(false));

            try { fixedMessage=ConfigString("message"); } catch(Exception ex) { MessageBox.Show("配置无法读取，原文件已保留："+ex.Message); return; }

            scheduleTime=LoadSchedule();nextScheduled=SchedulePolicy.Next(DateTime.Now,scheduleTime);
            if(startupReview||DependenciesMissing()||args.Contains("--check-environment"))ShowEnvironment();
            environmentReady=!DependenciesMissing();

            var form=BuildMainWindow();

            Log("就绪");
            // 清空上次的运行日志，防止无限增长
            try
            {
                File.WriteAllText(Path.Combine(BaseDir, "logs", "run_out.txt"), "");
                File.WriteAllText(Path.Combine(BaseDir, "logs", "run_err.txt"), "");
            }
            catch { }
            UpdateStatus();
            LoadLogs();
            // 启动时恢复上次保存的每日定时（重启后无需重新设置）
            scheduleTime = LoadSchedule();
            if (!string.IsNullOrEmpty(scheduleTime)) Log(schedulePaused?"已有定时已暂停，请在定时设置中确认后启用。":"已恢复每日定时，等待下一次计划时间，不补发已错过的任务。");
            RepairAutostart();
            UpdateStatus();
            // 启动自检：关键配套文件缺失时给出警告（被杀软误删后能及时发现）
            try
            {
                string[] need = FindBackend().Length > 0 ? new[] { "friends.json", "sentences_pool.json" } : new[] { "douyin_spark.py", "fetch_following.py", "spark_support.py", "friends.json", "sentences_pool.json" };
                var miss = new System.Collections.Generic.List<string>();
                foreach (var f in need) if (!File.Exists(Path.Combine(BaseDir, f))) miss.Add(f);
                if (miss.Count > 0) Log("⚠ 缺少配套文件：" + string.Join("、", miss) + " —— 请重新解压发布版恢复");
            }
            catch { }
            // 每日定时检查
            var schedTimer = new System.Windows.Forms.Timer();
            schedTimer.Interval = 5000;
            schedTimer.Tick += delegate
            {
                TickSchedule(DateTime.Now);
                TickGroupSchedule(DateTime.Now);
            };
            schedTimer.Start();
            SetupTray(form,minimized);
            Application.Run(form);
            GC.KeepAlive(singleMutex);
        }

        static void ToggleAutostart()
        {
            try
            {
                string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    string exe = Application.ExecutablePath;
                    if (key.GetValue("DouyinSpark") == null)
                    {
                        key.SetValue("DouyinSpark", "\"" + exe + "\" --minimized");
                        Log("已开启开机自启");
                    }
                    else
                    {
                        key.DeleteValue("DouyinSpark", false);
                        Log("已关闭开机自启");
                    }
                }
            }
            catch (Exception ex) { Log("自启设置失败: " + ex.Message); }
        }

        static string GetJsonField(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        // 统一保存 config.json（浏览器选择 + 每日定时），互不覆盖


        static string LoadSchedule()
        {
            try
            {
                string cf = Path.Combine(BaseDir, "config.json");
                if (File.Exists(cf))
                {
                    string j = File.ReadAllText(cf, System.Text.Encoding.UTF8);
                    string s = GetJsonField(j, "schedule");
                    DateTime dt;
                    if (DateTime.TryParse(s, out dt)) return dt.ToString("HH:mm");
                }
            }
            catch { }
            return "";
        }

        static string GetBrowserChoice()
        {
            try
            {
                string cf = Path.Combine(BaseDir, "config.json");
                if (File.Exists(cf))
                {
                    string j = File.ReadAllText(cf, System.Text.Encoding.UTF8);
                    string b = GetJsonField(j, "browser");
                    if (b == "msedge" || b == "quark" || b == "chrome") return b;
                }
            }
            catch { }
            return "chrome";
        }



        static string[] BrowserTarget(string b)
        {
            string relative = b == "msedge" ? @"Microsoft\Edge\Application\msedge.exe"
                : b == "quark" ? @"Programs\Quark\quark.exe" : @"Google\Chrome\Application\chrome.exe";
            string[] roots = {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetEnvironmentVariable("ProgramFiles") ?? "",
                Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? ""
            };
            string exe = b == "msedge" ? "msedge.exe" : b == "quark" ? "quark.exe" : "chrome.exe";
            foreach (string dir in roots)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string candidate = Path.Combine(dir, relative);
                if (File.Exists(candidate)) { exe = candidate; break; }
            }
            return new[] { exe, Path.Combine(BaseDir, b == "msedge" ? "browser_profile_edge" : b == "quark" ? "browser_profile_quark" : "browser_profile") };
        }

        static void SaveBrowserChoice(string b)
        {
            SaveAllConfig(b, scheduleTime);
        }

        static void ShowBrowserDialog()
        {
            var dlg = new ThemedForm();
            dlg.Text = "选择浏览器"; dlg.Size = new Size(320, 250);
            dlg.StartPosition = FormStartPosition.CenterParent; dlg.BackColor = Color.White;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog; dlg.MaximizeBox = false; dlg.MinimizeBox = false;
            var l1 = new Label(); l1.Text = "发送与登录使用的浏览器："; l1.Location = new Point(20, 16); l1.AutoSize = true;
            string cur = GetBrowserChoice();
            var rb1 = new RadioButton(); rb1.Text = "Google Chrome"; rb1.Location = new Point(30, 48); rb1.Checked = cur == "chrome";
            var rb2 = new RadioButton(); rb2.Text = "Microsoft Edge"; rb2.Location = new Point(30, 76); rb2.Checked = cur == "msedge";
            var rb3 = new RadioButton(); rb3.Text = "夸克浏览器"; rb3.Location = new Point(30, 104); rb3.Checked = cur == "quark";
            var l2 = new Label(); l2.Text = "切换后首次发送需重新扫码登录一次"; l2.Location = new Point(30, 136); l2.AutoSize = true; l2.ForeColor = Color.Gray; l2.Font = new Font("Microsoft YaHei UI", 8F);
            var b = new RoundButton(); b.Text = "确定"; b.Size = new Size(90, 32); b.Location = new Point(110, 170);
            b.MainColor = KPink; b.HoverColor = Color.FromArgb(255, 92, 138);
            b.Click += delegate
            {
                string pick = rb1.Checked ? "chrome" : rb2.Checked ? "msedge" : "quark";
                SaveBrowserChoice(pick);
                Log("浏览器已切换为: " + (rb1.Checked ? "Google Chrome" : rb2.Checked ? "Microsoft Edge" : "夸克浏览器"));
                dlg.Close();
            };
            dlg.Controls.AddRange(new Control[] { l1, rb1, rb2, rb3, l2, b });
            dlg.ShowDialog();
        }

        static int CompareVersion(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = 0, vb = 0;
                int.TryParse(i < pa.Length ? pa[i] : "0", out va);
                int.TryParse(i < pb.Length ? pb[i] : "0", out vb);
                if (va != vb) return va > vb ? 1 : -1;
            }
            return 0;
        }





        static void UpdateStatus()
        {
            if (string.IsNullOrEmpty(scheduleTime)) lblStatus.Text = "● 就绪";
            else lblStatus.Text = schedulePaused?"● 定时已暂停":"● 定时 " + scheduleTime + " 每日自动";
            lblStatus.ForeColor = AppPalette.Muted;
            RefreshDashboard();
        }

        static void ShowScheduleDialog()
        {
            var dlg = new ThemedForm();
            dlg.Text = "每日定时"; dlg.Size = new Size(410, 230);
            dlg.StartPosition = FormStartPosition.CenterParent; dlg.BackColor = Color.White;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog; dlg.MaximizeBox = false; dlg.MinimizeBox = false;
            var l1 = new Label(); l1.Text = "每天自动发送时间（HH:mm）："; l1.Location = new Point(22, 22); l1.AutoSize = true;
            var t1 = new TextBox(); t1.Text = scheduleTime; t1.Location = new Point(22, 50); t1.Size = new Size(270, 25);
            var l2 = new Label(); l2.Text = (schedulePaused?"当前已暂停。保存后明确启用。\n":"")+"留空取消；只执行下一次定时，不立即补发。"; l2.Location = new Point(22, 82); l2.AutoSize = true; l2.ForeColor = Color.Gray; l2.Font = new Font("Microsoft YaHei UI", 8F);
            var b1 = new RoundButton(); b1.Text = "保存启用"; b1.Size = new Size(95, 32); b1.Location = new Point(65, 145);
            b1.MainColor = KPink; b1.HoverColor = Color.FromArgb(255, 92, 138); b1.ForeColor = Color.White;
            b1.Click += delegate
            {
                string v = t1.Text.Trim();
                if (v.Length == 0) { scheduleTime = ""; schedulePaused=false; nextScheduled=DateTime.MaxValue; SaveAllConfig(GetBrowserChoice(), ""); dlg.Close(); Log("已取消定时"); UpdateStatus();RefreshEnvironmentBanner(); return; }
                DateTime dt;
                if (DateTime.TryParseExact(v,"HH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out dt))
                {
                    scheduleTime = dt.ToString("HH:mm");
                    schedulePaused=false;
                    nextScheduled=SchedulePolicy.Next(DateTime.Now,scheduleTime);
                    SaveAllConfig(GetBrowserChoice(), scheduleTime);
                    dlg.Close();
                    Log("定时已启用：下次 "+nextScheduled.ToString("MM-dd HH:mm")+"，不立即补发。");RefreshEnvironmentBanner();
                    UpdateStatus();
                }
                else { MessageBox.Show("时间格式不对，请填 HH:mm，如 08:30"); }
            };
            var b2 = new RoundButton(); b2.Text = "取消"; b2.Size = new Size(95, 32); b2.Location = new Point(170, 145);
            b2.MainColor = Color.FromArgb(60, 60, 70); b2.HoverColor = Color.FromArgb(80, 80, 90); b2.ForeColor = Color.White;
            b2.Click += delegate { dlg.Close(); };
            var pause=new RoundButton{Text="暂停",Primary=false,Size=new Size(95,32),Location=new Point(275,145)};pause.Click+=delegate{schedulePaused=true;SaveAllConfig(GetBrowserChoice(),scheduleTime);UpdateStatus();RefreshDashboard();RefreshEnvironmentBanner();dlg.Close();};dlg.Controls.Add(pause);
            dlg.Controls.Add(l1); dlg.Controls.Add(t1); dlg.Controls.Add(l2); dlg.Controls.Add(b1); dlg.Controls.Add(b2);
            dlg.ShowDialog();
        }

        static void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\n";
            try { SparkStorage.AppendLog(Path.Combine(BaseDir, "logs", "app_log.txt"), line); } catch { }
            // 线程安全：后台线程不得直接碰 WinForms 控件（RichTextBox 跨线程必崩）
            try
            {
                if (mainForm != null && mainForm.IsHandleCreated && !mainForm.IsDisposed)
                {
                    if (mainForm.InvokeRequired)
                    {
                        mainForm.BeginInvoke((Action)delegate { LogUi(msg); });
                    }
                    else LogUi(msg);
                }
            }
            catch { }
        }

        static void LogUi(string msg)
        {
            if (txtLog == null || txtLog.IsDisposed) return;
            try
            {
                if(Convert.ToString(txtLog.Tag)=="placeholder") {txtLog.Clear();txtLog.Tag=null;}
                txtLog.AppendText(msg + "\n");
                txtLog.SelectionStart = txtLog.TextLength; txtLog.ScrollToCaret();
                if (txtLog.Lines.Length > 600)
                {
                    int idx = txtLog.GetFirstCharIndexFromLine(150);
                    if (idx > 0) { txtLog.Select(0, idx); txtLog.SelectedText = ""; }
                }
            }
            catch { }
            try
            {
                if (lblStatus != null && !lblStatus.IsDisposed) lblStatus.Text = msg;
            }
            catch { }
        }

        static bool LogHasTodayOk()
        {
            // 查今天是否已成功发送过（错过补发防重复）
            try
            {
                var csv = Path.Combine(BaseDir, "logs", "spark_log.csv");
                if (!File.Exists(csv)) return false;
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                foreach (var line in File.ReadAllLines(csv))
                {
                    var cells = ParseCsvLine(line);
                    if (cells.Length >= 5 && cells[0].StartsWith(today) && cells[4] == "OK")
                        return true;
                }
            }
            catch { }
            return false;
        }

        static string[] ParseCsvLine(string line)
        {
            // 正确处理带引号和逗号的 CSV 字段（消息文本常含逗号）
            var list = new System.Collections.Generic.List<string>();
            bool inQ = false; string cur = "";
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQ)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur += '"'; i++; }
                        else inQ = false;
                    }
                    else cur += ch;
                }
                else
                {
                    if (ch == '"') inQ = true;
                    else if (ch == ',') { list.Add(cur); cur = ""; }
                    else cur += ch;
                }
            }
            list.Add(cur);
            return list.ToArray();
        }

        static void LoadLogs()
        {
            try
            {
                var table=new System.Data.DataTable();
                string[] headers={"时间","好友","主页","消息","状态","说明"};
                foreach(string header in headers) table.Columns.Add(header);
                var rows=SparkStorage.ReadCsv(Path.Combine(BaseDir,"logs","spark_log.csv"));
                foreach(var row in rows.Skip(Math.Max(0,rows.Count-500)).Reverse()) if(row.Length==6) table.Rows.Add(row.Cast<object>().ToArray());
                dgv.DataSource=table;
                FormatHistory();
                RefreshDashboard();
            }
            catch(Exception ex) {Log("加载历史失败："+ex.Message);}
        }

        static bool IsProfileLocked()
        {
            // 检测是否有 Chrome 正占用 browser_profile（抖音消息窗口），防止 playwright 启动冲突
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE Name='chrome.exe'"))
                {
                    foreach (var o in s.Get())
                    {
                        var cl = o["CommandLine"] as string;
                        if (cl != null && cl.IndexOf("browser_profile", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        static void DoStart()
        {StartPrivateSend(false);}

        static void StartPrivateSend(bool scheduled)
        {
            if(!scheduled&&!RequireSavedPrivateMessage())return;
            if(SendCooling){Log("停止后的冷却中，请稍候再开始。");return;}
            if(!CheckSafetyResume(mainForm,true))return;
            if(BrowserPath(GetBrowserChoice()).Length==0){Log("发送浏览器未找到，请先完成环境检查。");ShowEnvironment();return;}
            if(importingFollowing!=0) { Log("正在读取关注列表，定时/发送任务请等待导入结束后再试。"); return; }
            if (btnStart.Enabled == false) { Log("正在运行中，稍等..."); return; }
            if (IsProfileLocked())
            {
                MessageBox.Show("检测到抖音消息窗口（Chrome）正占用登录状态。\n请先关闭那个 Chrome 窗口，再点开始发送。", "提示");
                Log("发送被拦截：请先关闭抖音消息窗口");
                return;
            }
            StartPrivateRun();
        }

        static void SetUtf8Encoding(ProcessStartInfo psi)
        {
            // .NET 4 无 StandardOutputEncoding 属性，运行时反射设置 UTF-8 解码
            try
            {
                var pi = typeof(ProcessStartInfo).GetProperty("StandardOutputEncoding");
                if (pi != null) pi.SetValue(psi, System.Text.Encoding.UTF8, null);
                var pe = typeof(ProcessStartInfo).GetProperty("StandardErrorEncoding");
                if (pe != null) pe.SetValue(psi, System.Text.Encoding.UTF8, null);
            }
            catch { }
        }

        static void DoDry()
        {
            BackendAction("--dry-run"+(fixedMessage.Length>0?" --message "+QuoteArg(fixedMessage):""));
        }

        static void DoLogin()
        {
            BackendAction("--selftest");
        }

        static void ShowSentenceManager()
        {
            try
            {
                string path=Path.Combine(BaseDir,"sentences_pool.json");
                var data=SparkStorage.Json().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(path,System.Text.Encoding.UTF8));
                object value;
                var cats=data.TryGetValue("categories",out value) ? value as System.Collections.Generic.Dictionary<string,object> : null;
                if(cats==null) {cats=new System.Collections.Generic.Dictionary<string,object>();cats["语录池"]=data["sentences"];}
                var active=new System.Collections.Generic.HashSet<string>();
                if(data.TryGetValue("active_categories",out value)) foreach(var item in (System.Collections.IEnumerable)value) active.Add(Convert.ToString(item));
                if(active.Count==0 && cats.ContainsKey("语录池")) active.Add("语录池");
                if(!cats.ContainsKey("自定义")) cats["自定义"]=new string[0];
                var form=new ThemedForm {Text="语录管理",Size=new Size(560,650),StartPosition=FormStartPosition.CenterParent,AutoScroll=true};
                form.Controls.Add(new Label {Text="勾选启用分类；自定义语录每行一句。固定消息请在更多功能中设置。",Location=new Point(16,16),AutoSize=true});
                int y=48;
                var checks=new System.Collections.Generic.List<CheckBox>();
                foreach(string name in cats.Keys)
                {
                    var check=new CheckBox {Text=name,Checked=active.Contains(name),Location=new Point(20,y),AutoSize=true};
                    checks.Add(check);form.Controls.Add(check);y+=28;
                }
                var custom=new TextBox {Multiline=true,ScrollBars=ScrollBars.Vertical,Location=new Point(16,y+10),Size=new Size(505,250)};
                custom.Text=string.Join("\r\n",((System.Collections.IEnumerable)cats["自定义"]).Cast<object>().Select(Convert.ToString));
                var save=new Button {Text="保存",Location=new Point(400,y+275),Size=new Size(120,34)};
                save.Click+=delegate
                {
                    try
                    {
                        data["categories"]=cats;
                        data["active_categories"]=checks.Where(c=>c.Checked).Select(c=>c.Text).ToArray();
                        cats["自定义"]=custom.Text.Split('\n').Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();
                        SparkStorage.WriteJson(path,data);Log("语录已保存，下次发送生效。");form.Close();
                    }
                    catch(Exception ex) {MessageBox.Show(ex.Message,"保存失败");}
                };
                form.Controls.Add(custom);form.Controls.Add(save);form.ShowDialog(mainForm);
            }
            catch(Exception ex) {MessageBox.Show(ex.Message,"语录管理");}
        }

        static void OpenMessages()
        {
            if (!btnStart.Enabled) { Log("正在发送中，请发送完成后再查看消息"); return; }
            string b = GetBrowserChoice();
            var t = BrowserTarget(b);
            string exe = t[0]; string profile = t[1];
            if (!File.Exists(exe)) { Log("未找到所选浏览器，请先安装或在「浏览器」里切换"); return; }
            Directory.CreateDirectory(profile);
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = "--user-data-dir=\"" + profile + "\" https://www.douyin.com/";
                psi.UseShellExecute = true;
                Process.Start(psi);
                Log("已在登录浏览器中打开抖音，点右上角消息图标查看私信");
            }
            catch (Exception ex) { Log("打开失败: " + ex.Message); }
        }
    }

}
