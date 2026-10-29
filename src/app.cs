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
        static bool scheduleFired = false;   // 当天是否已触发
        static string scheduleLastDay = "";  // 上次触发的日期
        const string AppVersion = "1.9.1";

        // HelloKitty 主题别名（Program 内直接使用）
        static readonly Color KPink = KittyTheme.KPink;
        static readonly Color KPinkDeep = KittyTheme.KPinkDeep;
        static readonly Color KPinkSoft = KittyTheme.KPinkSoft;
        static readonly Color KPinkBg = KittyTheme.KPinkBg;
        static readonly Color KPinkBox = KittyTheme.KPinkBox;
        static readonly Color KPinkLine = KittyTheme.KPinkLine;
        static readonly Color KPinkText = KittyTheme.KPinkText;
        static void PaintKittyFace(Graphics g, float cx, float cy, float s) { KittyTheme.PaintKittyFace(g, cx, cy, s); }
        static string CrashLogPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "crash.log"); } }

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
            PyExe = FindPython();
            if (string.IsNullOrEmpty(PyExe) && FindBackend().Length == 0)
            {
                MessageBox.Show("没有找到 Python。请先运行 setup.ps1，或安装 Python 并加入 PATH。");
                return;
            }
            string friendsFile = Path.Combine(BaseDir, "friends.json");
            if (!File.Exists(friendsFile))
                File.WriteAllText(friendsFile, "[]", new System.Text.UTF8Encoding(false));

            try { fixedMessage=ConfigString("message"); } catch(Exception ex) { MessageBox.Show("配置无法读取，原文件已保留："+ex.Message); return; }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 启动动画 splash（炫酷发光文字 + 双缓冲防闪烁）
            if (!minimized)
            {
                var splash = new SplashForm();
                splash.FormBorderStyle = FormBorderStyle.None;
                splash.StartPosition = FormStartPosition.CenterScreen;
                splash.Size = new Size(900, 600);
                splash.BackColor = KPinkBg;
                splash.TopMost = true;
                int startTick = Environment.TickCount;
                splash.Paint += delegate(object s, PaintEventArgs e)
                {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    int elapsed = Environment.TickCount - startTick;
                    double t = elapsed / 1000.0;
                    int cx = splash.ClientSize.Width / 2;
                    int cy = splash.ClientSize.Height / 2;
                    var sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;

                    // 背景柔光
                    using (var glow = new SolidBrush(Color.FromArgb(90, 255, 200, 224)))
                        g.FillEllipse(glow, cx - 260, cy - 210, 520, 420);

                    // Kitty 猫脸：上下轻浮 + 蝴蝶结
                    float kx = cx;
                    float ky = cy - 62 + (float)(7 * Math.Sin(t * 1.6));
                    PaintKittyFace(g, kx, ky, 88f);

                    // 粉色粒子光点：8 个环绕
                    int nP = 8;
                    for (int i = 0; i < nP; i++)
                    {
                        double angle = t * 0.9 * (Math.PI * 2) + i * (Math.PI * 2 / nP);
                        double px = kx + Math.Cos(angle) * 195 * (1 + 0.10 * Math.Sin(t * 2));
                        double py = ky + Math.Sin(angle) * 95 * (1 + 0.10 * Math.Cos(t * 2));
                        int pa = (int)(150 * (0.35 + 0.65 * (1 + Math.Sin(t * 3 + i * 1.3)) / 2));
                        float pr = 2.5f + (float)(1.5 * (1 + Math.Sin(t * 2.2 + i * 1.7)) / 2);
                        using (var pb = new SolidBrush((i % 2 == 0) ? Color.FromArgb(pa, KPink) : Color.FromArgb(pa, 255, 176, 205)))
                            g.FillEllipse(pb, (float)(px - pr), (float)(py - pr), pr * 2, pr * 2);
                    }

                    // 主标题：弹性缩放 + 深粉→粉渐变，发光晕
                    double scale;
                    if (t < 0.35) { scale = 0.4 + (t / 0.35) * 0.8; }
                    else if (t < 0.5) { double k = (t - 0.35) / 0.15; scale = 1.2 - k * 0.2; }
                    else scale = 1.0;
                    int alpha = (int)(255 * Math.Min(1, t / 0.4));
                    using (var f = new Font("Microsoft YaHei UI", (float)(42 * scale), FontStyle.Bold))
                    {
                        for (int i = 6; i >= 1; i--)
                        {
                            int ga = (int)(alpha * 0.07);
                            using (var glowb = new SolidBrush(Color.FromArgb(ga, KPink)))
                                g.DrawString("抖音续火花", f, glowb, new RectangleF(0 - i, cy - 62 - i, splash.ClientSize.Width, 90), sf);
                        }
                        using (var lg = new LinearGradientBrush(
                            new Rectangle(0, cy - 62, splash.ClientSize.Width, 90),
                            Color.FromArgb(alpha, KPinkDeep),
                            Color.FromArgb(alpha, KPink),
                            LinearGradientMode.Horizontal))
                            g.DrawString("抖音续火花", f, lg, new RectangleF(0, cy - 62, splash.ClientSize.Width, 90), sf);
                    }

                    // 副标题
                    if (t > 0.5)
                    {
                        double sa = Math.Min(1, (t - 0.5) / 0.4);
                        using (var f = new Font("Microsoft YaHei UI", 11F))
                        using (var b = new SolidBrush(Color.FromArgb((int)(190 * sa), KPinkText)))
                            g.DrawString("Spark every day  ✿ 每天一句废话文学", f, b, new RectangleF(0, cy + 42, splash.ClientSize.Width, 30), sf);
                    }

                    // 底部粉色流光进度条
                    if (t > 0.3)
                    {
                        double prog = Math.Min(1, (t - 0.3) / 1.6);
                        int bw = 300, bh = 5;
                        int bx = cx - bw / 2, by = cy + 118;
                        using (var back = new SolidBrush(Color.FromArgb(255, 228, 238)))
                            g.FillRectangle(back, bx, by, bw, bh);
                        if (prog > 0.01)
                        {
                            int w = (int)(bw * prog);
                            using (var fore = new SolidBrush(KPink))
                                g.FillRectangle(fore, bx, by, w, bh);
                            double hi = (w - 46) * (0.5 + 0.5 * Math.Sin(t * 6));
                            if (hi < 0) hi = 0;
                            using (var hot = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                                g.FillRectangle(hot, bx + (float)hi, by, 46, bh);
                        }
                    }
                };
                var animTimer = new System.Windows.Forms.Timer();
                animTimer.Interval = 30;
                animTimer.Tick += delegate { splash.Invalidate(); };
                animTimer.Start();
                var closeTimer = new System.Windows.Forms.Timer();
                closeTimer.Interval = 2200;
                closeTimer.Tick += delegate { animTimer.Stop(); splash.Close(); closeTimer.Stop(); };
                closeTimer.Start();
                splash.ShowDialog();
            }

            var form = new CardForm();
            mainForm = form;
            form.Text = "抖音续火花";
            form.Size = new Size(960, 760);
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Font = new Font("Microsoft YaHei UI", 9F);
            form.BackColor = Color.FromArgb(245, 246, 250);

            // HelloKitty 少女粉主题
            form.BackColor = KPinkBg;

            // 右侧内容区先建（Dock.Fill 要先加）
            var content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = KPinkBg;
            content.Padding = new Padding(30, 26, 30, 26);
            form.Controls.Add(content);

            // 左侧边栏
            var sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 180;
            sidebar.BackColor = KPinkSoft;
            form.Controls.Add(sidebar);

            var kittyBadge = new KittyBadge();
            kittyBadge.Size = new Size(46, 42);
            kittyBadge.Location = new Point(12, 14);
            kittyBadge.BackColor = KPinkSoft;

            var lblLogo = new Label();
            lblLogo.Text = "抖音续火花";
            lblLogo.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            lblLogo.ForeColor = KPinkDeep;
            lblLogo.AutoSize = false;
            lblLogo.Size = new Size(132, 36);
            lblLogo.Location = new Point(58, 18);
            lblLogo.BackColor = Color.Transparent;

            var lblVer = new Label();
            lblVer.Text = "v" + AppVersion + "  STABLE";
            lblVer.Font = new Font("Microsoft YaHei UI", 7F);
            lblVer.ForeColor = Color.FromArgb(205, 125, 155);
            lblVer.AutoSize = false;
            lblVer.Size = new Size(160, 14);
            lblVer.Location = new Point(20, 60);
            lblVer.BackColor = Color.Transparent;

            // 边栏按钮容器
            var sideBtns = new FlowLayoutPanel();
            sideBtns.FlowDirection = FlowDirection.TopDown;
            sideBtns.WrapContents = false;
            sideBtns.Dock = DockStyle.None;
            sideBtns.Location = new Point(14, 90);
            sideBtns.Size = new Size(160, 560);
            sideBtns.BackColor = Color.Transparent;

            btnStart = new RoundButton();
            btnStart.Text = "开始续火花";
            btnStart.Size = new Size(150, 36);
            btnStart.Margin = new Padding(0, 0, 0, 5);
            btnStart.MainColor = KPink; btnStart.HoverColor = Color.FromArgb(255, 92, 138);
            btnStart.ForeColor = Color.White;
            btnStart.Click += delegate { DoStart(); };
            sideBtns.Controls.Add(btnStart);

            Action<string, EventHandler> addSideBtn = delegate(string text, EventHandler onClick)
            {
                var b = new RoundButton();
                b.Text = text;
                b.Size = new Size(150, 34);
                b.Margin = new Padding(0, 0, 0, 5);
                b.MainColor = Color.White;
                b.HoverColor = Color.FromArgb(255, 228, 238);
                b.ForeColor = KPinkDeep;
                b.Click += onClick;
                sideBtns.Controls.Add(b);
            };

            // 添加好友 = 一键导入抖音关注列表（可勾选添加），替代手动粘贴
            addSideBtn("添加好友", delegate { ImportFollowing(); });
            addSideBtn("好友状态", delegate { ShowFriendStatus(); });
            addSideBtn("语录管理", delegate { ShowSentenceManager(); });
            // 更多功能：收进下拉菜单
            var moreMenu = new ContextMenuStrip();
            moreMenu.Items.Add("预览句子", null, delegate { DoDry(); });
            moreMenu.Items.Add("管理好友", null, delegate { AddFriend(); });
            moreMenu.Items.Add("登录自检", null, delegate { DoLogin(); });
            moreMenu.Items.Add("抖音消息", null, delegate { OpenMessages(); });
            moreMenu.Items.Add("固定消息设置", null, delegate { ShowFixedMessage(); });
            moreMenu.Items.Add("导出历史 CSV", null, delegate { ExportHistory(); });
            moreMenu.Items.Add("清理旧历史", null, delegate { ClearHistory(); });
            moreMenu.Items.Add("定时设置", null, delegate { ShowScheduleDialog(); });
            moreMenu.Items.Add("开机自启", null, delegate { ToggleAutostart(); });
            moreMenu.Items.Add("浏览器选择", null, delegate { ShowBrowserDialog(); });
            moreMenu.Items.Add("换肤", null, delegate
            {
                var cd = new ColorDialog();
                cd.Color = form.BackColor;
                if (cd.ShowDialog() == DialogResult.OK)
                {
                    Color bg = cd.Color;
                    bool dark = (bg.R + bg.G + bg.B) / 3 < 128;
                    Color fg = dark ? Color.White : Color.FromArgb(30, 30, 40);
                    Color sub = dark ? ControlPaint.Dark(bg, 0.15f) : ControlPaint.Light(bg, 0.15f);
                    Color box = dark ? ControlPaint.Dark(bg, 0.3f) : ControlPaint.Light(bg, 0.1f);
                    form.BackColor = bg; sidebar.BackColor = sub; content.BackColor = bg;
                    txtLog.BackColor = box; txtLog.ForeColor = dark ? Color.FromArgb(200, 220, 210) : Color.FromArgb(40, 40, 50);
                    dgv.BackgroundColor = box; dgv.DefaultCellStyle.BackColor = box; dgv.DefaultCellStyle.ForeColor = fg;
                    dgv.RowsDefaultCellStyle.BackColor = box;
                    dgv.AlternatingRowsDefaultCellStyle.BackColor = dark ? ControlPaint.Light(box, 0.15f) : ControlPaint.Dark(box, 0.08f);
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = sub; dgv.ColumnHeadersDefaultCellStyle.ForeColor = fg;
                    lblLogo.ForeColor = fg; lblStatus.BackColor = sub; form.Invalidate();
                }
            });
            moreMenu.Items.Add("启动游戏(Steam)", null, delegate
            {
                try { Process.Start("steam://rungameid/431960"); Log("已启动 Steam 游戏"); }
                catch (Exception ex) { Log("启动失败: " + ex.Message); }
            });
            addSideBtn("更多功能", delegate (object s, EventArgs e)
            {
                var btn = s as Control;
                if (btn != null) moreMenu.Show(btn, new Point(0, btn.Height));
            });
            addSideBtn("检查更新", delegate { DoCheckUpdate(); });

            lblStatus = new Label();
            lblStatus.Text = "● 就绪";
            lblStatus.ForeColor = KPink;
            lblStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblStatus.Dock = DockStyle.Bottom;
            lblStatus.Height = 40;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Padding = new Padding(18, 0, 0, 0);
            lblStatus.BackColor = Color.FromArgb(255, 198, 222);

            sidebar.Controls.Add(lblStatus);
            sidebar.Controls.Add(sideBtns);
            sidebar.Controls.Add(lblVer);
            sidebar.Controls.Add(lblLogo);
            sidebar.Controls.Add(kittyBadge);

            // 内容区标题
            var lblTitle = new Label();
            lblTitle.Text = "服务与设置 ✿";
            lblTitle.Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold);
            lblTitle.ForeColor = KPinkDeep;
            lblTitle.AutoSize = false;
            lblTitle.Size = new Size(600, 34);
            lblTitle.Location = new Point(0, 0);
            lblTitle.BackColor = Color.Transparent;

            var lblSub = new Label();
            lblSub.Text = "每日自动给好友发一句废话文学，维持火苗";
            lblSub.Font = new Font("Microsoft YaHei UI", 9F);
            lblSub.ForeColor = Color.FromArgb(175, 125, 145);
            lblSub.AutoSize = false;
            lblSub.Size = new Size(600, 18);
            lblSub.Location = new Point(2, 36);
            lblSub.BackColor = Color.Transparent;

            progress = new SparkProgress();
            progress.Location = new Point(0, 60);
            progress.Size = new Size(720, 64);

            // 日志区
            var lblLogTitle = new Label();
            lblLogTitle.Text = "运行记录";
            lblLogTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblLogTitle.ForeColor = KPinkDeep;
            lblLogTitle.AutoSize = false;
            lblLogTitle.Size = new Size(200, 22);
            lblLogTitle.Location = new Point(0, 132);
            lblLogTitle.BackColor = Color.Transparent;

            txtLog = new RichTextBox();
            txtLog.Location = new Point(0, 160);
            txtLog.Size = new Size(720, 240);
            txtLog.Font = new Font("Consolas", 9.5F);
            txtLog.BackColor = Color.White;
            txtLog.ForeColor = KPinkText;
            txtLog.BorderStyle = BorderStyle.FixedSingle;
            txtLog.ReadOnly = true;
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // 记录表格
            var lblRecTitle = new Label();
            lblRecTitle.Text = "历史记录";
            lblRecTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblRecTitle.ForeColor = KPinkDeep;
            lblRecTitle.AutoSize = false;
            lblRecTitle.Size = new Size(200, 22);
            lblRecTitle.Location = new Point(0, 412);
            lblRecTitle.BackColor = Color.Transparent;
            lblRecTitle.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            dgv = new DataGridView();
            dgv.Location = new Point(0, 440);
            dgv.Size = new Size(720, 170);
            dgv.ReadOnly = true; dgv.AllowUserToAddRows = false;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgv.BackgroundColor = KPinkBox;
            dgv.BorderStyle = BorderStyle.None;
            dgv.RowHeadersVisible = false;
            dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersHeight = 28;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(255, 210, 228);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = KPinkDeep;
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 8F);
            dgv.DefaultCellStyle.BackColor = KPinkBox;
            dgv.DefaultCellStyle.ForeColor = KPinkText;
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 170, 205);
            dgv.DefaultCellStyle.SelectionForeColor = Color.White;
            dgv.RowsDefaultCellStyle.BackColor = KPinkBox;
            dgv.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(255, 240, 246);
            dgv.RowTemplate.Height = 24;
            dgv.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            content.Controls.Add(lblTitle);
            content.Controls.Add(lblSub);
            content.Controls.Add(progress);
            content.Controls.Add(lblLogTitle);
            content.Controls.Add(txtLog);
            content.Controls.Add(lblRecTitle);
            content.Controls.Add(dgv);

            form.Controls.Add(content);
            form.Controls.Add(sidebar);

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
            if (!string.IsNullOrEmpty(scheduleTime)) Log("已恢复每日定时：每天 " + scheduleTime + " 自动发送");
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
                if (string.IsNullOrEmpty(scheduleTime)) return;
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (scheduleLastDay != today) { scheduleLastDay = today; scheduleFired = false; }
                if (scheduleFired) return;
                string now = DateTime.Now.ToString("HH:mm");
                if (now == scheduleTime)
                {
                    scheduleFired = true;
                    Log("定时触发，自动开始发送...");
                    DoStart();
                }
                else if (string.CompareOrdinal(now, scheduleTime) > 0)
                {
                    // 已过设定时间：今天还没发过就补发一次，发过就标记防重复
                    scheduleFired = true;
                    if (!LogHasTodayOk())
                    {
                        Log("定时时间已过，自动补发...");
                        DoStart();
                    }
                }
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
            var dlg = new Form();
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
            else lblStatus.Text = "● 定时 " + scheduleTime + " 每日自动";
            lblStatus.ForeColor = KPink;
        }

        static void ShowScheduleDialog()
        {
            var dlg = new Form();
            dlg.Text = "每日定时"; dlg.Size = new Size(330, 200);
            dlg.StartPosition = FormStartPosition.CenterParent; dlg.BackColor = Color.White;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog; dlg.MaximizeBox = false; dlg.MinimizeBox = false;
            var l1 = new Label(); l1.Text = "每天自动发送时间（HH:mm）："; l1.Location = new Point(22, 22); l1.AutoSize = true;
            var t1 = new TextBox(); t1.Text = scheduleTime; t1.Location = new Point(22, 50); t1.Size = new Size(270, 25);
            var l2 = new Label(); l2.Text = "留空保存 = 取消定时。到点自动开始续火花。"; l2.Location = new Point(22, 82); l2.AutoSize = true; l2.ForeColor = Color.Gray; l2.Font = new Font("Microsoft YaHei UI", 8F);
            var b1 = new RoundButton(); b1.Text = "保存"; b1.Size = new Size(95, 32); b1.Location = new Point(65, 120);
            b1.MainColor = KPink; b1.HoverColor = Color.FromArgb(255, 92, 138); b1.ForeColor = Color.White;
            b1.Click += delegate
            {
                string v = t1.Text.Trim();
                if (v.Length == 0) { scheduleTime = ""; scheduleFired = true; SaveAllConfig(GetBrowserChoice(), ""); dlg.Close(); Log("已取消定时"); UpdateStatus(); return; }
                DateTime dt;
                if (DateTime.TryParse(v, out dt))
                {
                    scheduleTime = dt.ToString("HH:mm");
                    scheduleFired = false;
                    scheduleLastDay = "";
                    SaveAllConfig(GetBrowserChoice(), scheduleTime);
                    dlg.Close();
                    Log("定时已设置：每天 " + scheduleTime + " 自动发送");
                    UpdateStatus();
                }
                else { MessageBox.Show("时间格式不对，请填 HH:mm，如 08:30"); }
            };
            var b2 = new RoundButton(); b2.Text = "取消"; b2.Size = new Size(95, 32); b2.Location = new Point(170, 120);
            b2.MainColor = Color.FromArgb(60, 60, 70); b2.HoverColor = Color.FromArgb(80, 80, 90); b2.ForeColor = Color.White;
            b2.Click += delegate { dlg.Close(); };
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
        {
            if (btnStart.Enabled == false) { Log("正在运行中，稍等..."); return; }
            if (IsProfileLocked())
            {
                MessageBox.Show("检测到抖音消息窗口（Chrome）正占用登录状态。\n请先关闭那个 Chrome 窗口，再点开始发送。", "提示");
                Log("发送被拦截：请先关闭抖音消息窗口");
                return;
            }
            btnStart.Enabled = false; Log("开始发送...");
            progress.Reset();
            progress.Done = 0; progress.Total = 0; progress.Info = "准备中..."; progress.Invalidate();
            Log(FindBackend().Length>0 ? "使用独立后端" : "Python: "+PyExe);
            var psi = BackendInfo(fixedMessage.Length>0 ? "--message "+QuoteArg(fixedMessage) : "");
            try
            {
                var p = new Process(); p.StartInfo = psi; p.EnableRaisingEvents = true;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) {
                    if (e.Data != null)
                    {
                        string d = e.Data;
                        if(d.StartsWith("ATTENTION|"))
                        {
                            try { mainForm.BeginInvoke((Action)delegate { if(tray!=null) tray.ShowBalloonTip(10000,"需要人工处理",d.Split('|').Last(),ToolTipIcon.Warning); }); } catch { }
                        }
                        if (d.StartsWith("PROGRESS|"))
                        {
                            var parts = d.Split('|');
                            if (parts.Length >= 5)
                            {
                                int dn, tt;
                                if (int.TryParse(parts[1], out dn) && int.TryParse(parts[2], out tt) && tt > 0)
                                {
                                    string info = parts[3] + " " + (parts[4] == "OK" ? "\u2713" : "\u2717");
                                    try { progress.Invoke(new Action(delegate { progress.Done = dn; progress.Total = tt; progress.Info = info; })); } catch { }
                                }
                            }
                            return;
                        }
                        Log(d);
                        try { SparkStorage.AppendLog(Path.Combine(BaseDir, "logs", "run_out.txt"), d + "\r\n"); } catch { }
                    }
                };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) {
                    if (e.Data != null)
                    {
                        Log("[!] " + e.Data);
                        try { SparkStorage.AppendLog(Path.Combine(BaseDir, "logs", "run_err.txt"), e.Data + "\r\n"); } catch { }
                    }
                };
                p.Exited += delegate
                {
                    Log("完成，退出码 " + p.ExitCode);
                    try { progress.Invoke(new Action(delegate { progress.MarkFinished(); progress.Info = "\u2713 本轮发送完成"; })); } catch { }
                    try { btnStart.Invoke(new Action(delegate { btnStart.Enabled = true; })); } catch { }
                    // LoadLogs 操作 dgv 控件，必须在 UI 线程执行
                    try
                    {
                        if (mainForm != null && mainForm.IsHandleCreated && !mainForm.IsDisposed)
                            mainForm.BeginInvoke((Action)delegate { LoadLogs(); });
                    }
                    catch { }
                };
                p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                Log("已启动，日志文件: logs/run_out.txt");
            }
            catch (Exception ex)
            {
                Log("启动失败: " + ex.Message);
                btnStart.Enabled = true;
            }
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
                var form=new Form {Text="语录管理",Size=new Size(560,650),StartPosition=FormStartPosition.CenterParent,AutoScroll=true};
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
