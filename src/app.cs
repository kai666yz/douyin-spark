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
    // ===== HelloKitty 少女粉主题（namespace 层，供所有控件类共用）=====
    static class KittyTheme
    {
        public static readonly Color KPink = Color.FromArgb(255, 111, 165);      // 主粉
        public static readonly Color KPinkDeep = Color.FromArgb(230, 70, 120);   // 深粉
        public static readonly Color KPinkSoft = Color.FromArgb(255, 214, 232);  // 浅粉
        public static readonly Color KPinkBg = Color.FromArgb(255, 247, 250);    // 淡粉背景
        public static readonly Color KPinkBox = Color.FromArgb(255, 244, 248);   // 卡片底
        public static readonly Color KPinkLine = Color.FromArgb(235, 120, 160);  // 描边粉
        public static readonly Color KPinkText = Color.FromArgb(120, 80, 95);    // 粉灰文字

        // 自绘 HelloKitty 风格猫脸（白猫脸 + 蝴蝶结 + 胡须），cx/cy 中心，s 缩放
        public static void PaintKittyFace(Graphics g, float cx, float cy, float s)
        {
            using (var ear = new SolidBrush(Color.White))
            using (var pen = new Pen(KPinkLine, Math.Max(1.5f, s * 0.045f)))
            {
                g.FillPolygon(ear, new PointF[] { new PointF(cx - 0.40f * s, cy - 0.16f * s), new PointF(cx - 0.52f * s, cy - 0.44f * s), new PointF(cx - 0.16f * s, cy - 0.34f * s) });
                g.FillPolygon(ear, new PointF[] { new PointF(cx + 0.40f * s, cy - 0.16f * s), new PointF(cx + 0.52f * s, cy - 0.44f * s), new PointF(cx + 0.16f * s, cy - 0.34f * s) });
                using (var face = new SolidBrush(Color.White))
                    g.FillEllipse(face, cx - 0.50f * s, cy - 0.32f * s, s, 0.82f * s);
                g.DrawEllipse(pen, cx - 0.50f * s, cy - 0.32f * s, s, 0.82f * s);
            }
            using (var eye = new SolidBrush(Color.FromArgb(40, 35, 45)))
            {
                g.FillEllipse(eye, cx - 0.26f * s, cy - 0.08f * s, 0.13f * s, 0.17f * s);
                g.FillEllipse(eye, cx + 0.13f * s, cy - 0.08f * s, 0.13f * s, 0.17f * s);
            }
            using (var nose = new SolidBrush(Color.FromArgb(250, 205, 90)))
                g.FillEllipse(nose, cx - 0.06f * s, cy + 0.06f * s, 0.13f * s, 0.11f * s);
            using (var whisker = new Pen(Color.FromArgb(175, 130, 150), Math.Max(1f, s * 0.03f)))
            {
                g.DrawLine(whisker, cx - 0.30f * s, cy - 0.02f * s, cx - 0.60f * s, cy - 0.11f * s);
                g.DrawLine(whisker, cx - 0.30f * s, cy + 0.07f * s, cx - 0.60f * s, cy + 0.03f * s);
                g.DrawLine(whisker, cx - 0.30f * s, cy + 0.16f * s, cx - 0.58f * s, cy + 0.16f * s);
                g.DrawLine(whisker, cx + 0.30f * s, cy - 0.02f * s, cx + 0.60f * s, cy - 0.11f * s);
                g.DrawLine(whisker, cx + 0.30f * s, cy + 0.07f * s, cx + 0.60f * s, cy + 0.03f * s);
                g.DrawLine(whisker, cx + 0.30f * s, cy + 0.16f * s, cx + 0.58f * s, cy + 0.16f * s);
            }
            using (var bow = new SolidBrush(Color.FromArgb(255, 92, 122)))
            {
                g.FillEllipse(bow, cx - 0.74f * s, cy - 0.44f * s, 0.24f * s, 0.17f * s);
                g.FillEllipse(bow, cx - 0.50f * s, cy - 0.50f * s, 0.24f * s, 0.17f * s);
                g.FillEllipse(bow, cx - 0.56f * s, cy - 0.43f * s, 0.15f * s, 0.15f * s);
            }
        }
    }

    class Program
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
        const string AppVersion = "1.7.2";   // 当前版本（与 update/version.json 对比）

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
                try { File.AppendAllText(CrashLogPath, DateTime.Now.ToString("HH:mm:ss") + " [UI] " + e.Exception + "\n\n"); } catch { }
                MessageBox.Show("发生错误（已记录到 logs\\crash.log）：\n" + e.Exception.Message, "抖音续火花");
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                var ex = e.ExceptionObject as Exception;
                try { File.AppendAllText(CrashLogPath, DateTime.Now.ToString("HH:mm:ss") + " [APP] " + (ex != null ? ex.ToString() : Convert.ToString(e.ExceptionObject)) + "\n\n"); } catch { }
                MessageBox.Show("发生未处理错误（已记录到 logs\\crash.log）：\n" + (ex != null ? ex.Message : "未知错误"), "抖音续火花");
            };
        }

        [STAThread]
        static void Main()
        {
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
            if (string.IsNullOrEmpty(PyExe))
            {
                MessageBox.Show("没有找到 Python。请先运行 setup.ps1，或安装 Python 并加入 PATH。");
                return;
            }
            string friendsFile = Path.Combine(BaseDir, "friends.json");
            if (!File.Exists(friendsFile))
                File.WriteAllText(friendsFile, "[]", new System.Text.UTF8Encoding(false));

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 启动动画 splash（炫酷发光文字 + 双缓冲防闪烁）
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

            addSideBtn("预览句子", delegate { DoDry(); });
            addSideBtn("添加好友", delegate { AddFriend(); });
            addSideBtn("导入关注", delegate { ImportFollowing(); });
            addSideBtn("登录自检", delegate { DoLogin(); });
            addSideBtn("抖音消息", delegate { OpenMessages(); });
            addSideBtn("换肤", delegate
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
                    form.BackColor = bg;
                    sidebar.BackColor = sub;
                    content.BackColor = bg;
                    txtLog.BackColor = box;
                    txtLog.ForeColor = dark ? Color.FromArgb(200, 220, 210) : Color.FromArgb(40, 40, 50);
                    dgv.BackgroundColor = box;
                    dgv.DefaultCellStyle.BackColor = box;
                    dgv.DefaultCellStyle.ForeColor = fg;
                    dgv.RowsDefaultCellStyle.BackColor = box;
                    dgv.AlternatingRowsDefaultCellStyle.BackColor = dark ? ControlPaint.Light(box, 0.15f) : ControlPaint.Dark(box, 0.08f);
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = sub;
                    dgv.ColumnHeadersDefaultCellStyle.ForeColor = fg;
                    lblLogo.ForeColor = fg;
                    lblStatus.BackColor = sub;
                    form.Invalidate();
                }
            });
            addSideBtn("启动游戏", delegate
            {
                try { Process.Start("steam://rungameid/431960"); Log("已启动 Steam 游戏"); }
                catch (Exception ex) { Log("启动失败: " + ex.Message); }
            });
            addSideBtn("定时", delegate { ShowScheduleDialog(); });
            addSideBtn("自启", delegate { ToggleAutostart(); });
            addSideBtn("浏览器", delegate { ShowBrowserDialog(); });
            addSideBtn("好友状态", delegate { ShowFriendStatus(); });
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
            // 启动时后台静默检查更新：有新版本才提示
            var updTh = new System.Threading.Thread(delegate()
            {
                try
                {
                    string vf = Path.Combine(BaseDir, "update", "version.json");
                    if (!File.Exists(vf)) return;
                    string j = File.ReadAllText(vf, System.Text.Encoding.UTF8);
                    string nv = GetJsonField(j, "version");
                    string zn = GetJsonField(j, "zip");
                    string nt = GetJsonField(j, "note");
                    if (nv.Length == 0 || CompareVersion(nv, AppVersion) <= 0 || zn.Length == 0) return;
                    if (!File.Exists(Path.Combine(BaseDir, "update", zn))) return;
                    var mi = new System.Threading.ManualResetEvent(false);
                    form.Invoke((Action)delegate
                    {
                        if (MessageBox.Show("发现新版本 v" + nv + "（当前 v" + AppVersion + "）\n更新说明：" + nt + "\n\n点确定开始更新：自动备份旧文件 → 覆盖升级 → 重启软件", "发现更新", MessageBoxButtons.OKCancel) == DialogResult.OK)
                            ApplyUpdate(nv, Path.Combine(BaseDir, "update", zn));
                    });
                }
                catch { }
            });
            updTh.IsBackground = true; updTh.Start();
            // 启动时恢复上次保存的每日定时（重启后无需重新设置）
            scheduleTime = LoadSchedule();
            if (!string.IsNullOrEmpty(scheduleTime)) Log("已恢复每日定时：每天 " + scheduleTime + " 自动发送");
            UpdateStatus();
            // 启动自检：关键配套文件缺失时给出警告（被杀软误删后能及时发现）
            try
            {
                string[] need = { "douyin_spark.py", "fetch_following.py", "friends.json", "sentences_pool.json" };
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
            Application.Run(form);
        }

        static void ToggleAutostart()
        {
            try
            {
                string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, true))
                {
                    string exe = Application.ExecutablePath;
                    if (key.GetValue("DouyinSpark") == null)
                    {
                        key.SetValue("DouyinSpark", "\"" + exe + "\"");
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
        static void SaveAllConfig(string browser, string schedule)
        {
            string js = "{\"browser\": \"" + browser + "\", \"schedule\": \"" + schedule + "\"}";
            File.WriteAllText(Path.Combine(BaseDir, "config.json"), js, new System.Text.UTF8Encoding(false));
        }

        // 启动时恢复上次保存的每日定时（HH:mm），未设置返回 ""
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

        static string FindPython()
        {
            string[] local = {
                Environment.GetEnvironmentVariable("DOUYIN_SPARK_PYTHON"),
                Path.Combine(BaseDir, ".venv", "Scripts", "python.exe"),
                Path.Combine(BaseDir, "runtime", "python", "python.exe")
            };
            foreach (string path in local)
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    string path = Path.Combine(dir.Trim('"'), "python.exe");
                    if (path.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0 && File.Exists(path)) return path;
                }
                catch { }
            }
            return "";
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

        static void DoCheckUpdate()
        {
            // 更新源：douyin_spark/update/version.json + 对应 zip（每次新版本发布时更新这里）
            string updDir = Path.Combine(BaseDir, "update");
            string vf = Path.Combine(updDir, "version.json");
            if (!File.Exists(vf)) { Log("未找到更新源（update/version.json）"); return; }
            try
            {
                string j = File.ReadAllText(vf, System.Text.Encoding.UTF8);
                string newVer = GetJsonField(j, "version");
                string zipName = GetJsonField(j, "zip");
                string note = GetJsonField(j, "note");
                if (newVer.Length == 0 || zipName.Length == 0) { Log("更新源格式不正确"); return; }
                if (CompareVersion(newVer, AppVersion) <= 0) { Log("已是最新版本 v" + AppVersion); return; }
                string zipPath = Path.Combine(updDir, zipName);
                if (!File.Exists(zipPath)) { Log("找不到更新包: " + zipName); return; }
                if (MessageBox.Show("发现新版本 v" + newVer + "（当前 v" + AppVersion + "）\n更新说明：" + note +
                    "\n\n点确定开始更新：自动备份旧文件 → 覆盖升级 → 重启软件", "检查更新", MessageBoxButtons.OKCancel) == DialogResult.OK)
                {
                    ApplyUpdate(newVer, zipPath);
                }
            }
            catch (Exception ex) { Log("检查更新失败: " + ex.Message); }
        }

        static void ApplyUpdate(string newVer, string zipPath)
        {
            try
            {
                // 生成应用脚本：等 exe 退出 → 备份 → 解压覆盖 → 重启 → 自删
                string ps1 = Path.Combine(BaseDir, "update_apply.ps1");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("$base = '" + BaseDir.Replace("'", "''") + "'");
                sb.AppendLine("$zip = '" + zipPath.Replace("'", "''") + "'");
                sb.AppendLine("$ver = '" + newVer.Replace("'", "''") + "'");
                sb.AppendLine("Start-Sleep -Seconds 2");
                sb.AppendLine("$bk = Join-Path $base ('backups\\v' + $ver)");
                sb.AppendLine("New-Item -ItemType Directory -Path $bk -Force | Out-Null");
                sb.AppendLine("Copy-Item (Join-Path $base '抖音续火花.exe'),(Join-Path $base 'douyin_spark.py'),(Join-Path $base 'sentences_pool.json'),(Join-Path $base 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $base 'Microsoft.Web.WebView2.WinForms.dll'),(Join-Path $base 'WebView2Loader.dll') -Destination $bk -Force -ErrorAction SilentlyContinue");
                sb.AppendLine("for($i=0;$i -lt 15;$i++){ try { Expand-Archive -Path $zip -DestinationPath $base -Force -ErrorAction Stop; break } catch { Start-Sleep -Seconds 1 } }");
                sb.AppendLine("Start-Process -FilePath (Join-Path $base '抖音续火花.exe')");
                sb.AppendLine("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue");
                // ps1 必须带 BOM，否则 PowerShell 5.1 会把中文路径读成乱码
                File.WriteAllText(ps1, sb.ToString(), new System.Text.UTF8Encoding(true));
                var psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                Log("开始更新到 v" + newVer + "，软件将自动备份并重启...");
                Application.Exit();
            }
            catch (Exception ex) { Log("应用更新失败: " + ex.Message); }
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
            try { File.AppendAllText(Path.Combine(BaseDir, "logs", "app_log.txt"), line); } catch { }
            txtLog.AppendText(line);
            txtLog.SelectionStart = txtLog.TextLength; txtLog.ScrollToCaret();
            // 日志上限：防止长时间运行内存膨胀
            if (txtLog.Lines.Length > 600)
            {
                int idx = txtLog.GetFirstCharIndexFromLine(150);
                if (idx > 0) { txtLog.Select(0, idx); txtLog.SelectedText = ""; }
            }
            lblStatus.Text = msg;
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
                var csv = Path.Combine(BaseDir, "logs", "spark_log.csv");
                if (!File.Exists(csv)) { dgv.DataSource = null; return; }
                string[] lines;
                // 用共享读方式打开，避免 Python 正在写 csv 时抛 IOException
                using (var fs = new FileStream(csv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8))
                {
                    lines = sr.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                }
                var dt = new System.Data.DataTable();
                if (lines.Length == 0) { dgv.DataSource = null; return; }
                var cols = ParseCsvLine(lines[0]);
                foreach (var c in cols) dt.Columns.Add(c.Trim());
                int added = 0;
                int last = lines.Length - 1;
                for (int i = last; i >= 1 && i > last - 50; i--)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var cells = ParseCsvLine(lines[i]);
                    if (cells.Length < 2) continue;
                    var row = dt.NewRow();
                    for (int j = 0; j < cols.Length && j < cells.Length; j++) row[j] = cells[j];
                    dt.Rows.Add(row);
                    added++;
                }
                dgv.DataSource = null;
                dgv.Columns.Clear();
                dgv.DataSource = dt;
                Log("历史记录加载：" + added + " 条");
            }
            catch (Exception ex) { Log("加载记录失败: " + ex.Message); }
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
            Log("Python: " + PyExe);
            string script = Path.Combine(BaseDir, "douyin_spark.py");
            if (!File.Exists(script)) { Log("找不到脚本: " + script); btnStart.Enabled = true; return; }
            var psi = new ProcessStartInfo();
            psi.FileName = PyExe; psi.Arguments = "\"" + script + "\"";
            psi.WorkingDirectory = BaseDir; psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
            SetUtf8Encoding(psi);
            try
            {
                var p = new Process(); p.StartInfo = psi; p.EnableRaisingEvents = true;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) {
                    if (e.Data != null)
                    {
                        string d = e.Data;
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
                        try { File.AppendAllText(Path.Combine(BaseDir, "logs", "run_out.txt"), d + "\r\n"); } catch { }
                    }
                };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) {
                    if (e.Data != null)
                    {
                        Log("[!] " + e.Data);
                        try { File.AppendAllText(Path.Combine(BaseDir, "logs", "run_err.txt"), e.Data + "\r\n"); } catch { }
                    }
                };
                p.Exited += delegate
                {
                    Log("完成，退出码 " + p.ExitCode);
                    try { progress.Invoke(new Action(delegate { progress.MarkFinished(); progress.Info = "\u2713 本轮发送完成"; })); } catch { }
                    try { btnStart.Invoke(new Action(delegate { btnStart.Enabled = true; })); } catch { }
                    try { LoadLogs(); } catch { }
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
            try
            {
                Log("预览今日句子...");
                string script = Path.Combine(BaseDir, "douyin_spark.py");
                var psi = new ProcessStartInfo();
                psi.FileName = PyExe; psi.Arguments = "\"" + script + "\" --dry-run";
                psi.WorkingDirectory = BaseDir; psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
                SetUtf8Encoding(psi);
                var p = Process.Start(psi);
                string outp = p.StandardOutput.ReadToEnd();
                string errs = p.StandardError.ReadToEnd();
                p.WaitForExit();
                txtLog.Text = outp + (errs.Length > 0 ? "\n[stderr] " + errs : "");
            }
            catch (Exception ex) { Log("预览失败: " + ex.Message); MessageBox.Show("预览失败: " + ex.Message, "预览句子"); }
        }

        static void DoLogin()
        {
            Log("正在检查登录状态...");
            string script = Path.Combine(BaseDir, "douyin_spark.py");
            var psi = new ProcessStartInfo();
            psi.FileName = PyExe; psi.Arguments = "\"" + script + "\" --selftest";
            psi.WorkingDirectory = BaseDir; psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.CreateNoWindow = true;
            SetUtf8Encoding(psi);
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception ex) { Log("启动检查失败: " + ex.Message); MessageBox.Show("启动检查失败: " + ex.Message, "登录状态"); return; }
            var th = new System.Threading.Thread(delegate()
            {
                string outp = "";
                try { outp = p.StandardOutput.ReadToEnd(); } catch { }
                string result;
                if (outp.Contains("登录状态: OK")) result = "已登录 ✓（浏览器后台完成检查）";
                else if (outp.Contains("登录状态: NEED_LOGIN")) result = "未登录或登录失效：已把浏览器移到屏幕中央，请扫码登录";
                else if (outp.Contains("Traceback") || outp.Length == 0) result = "检查异常（详见日志）";
                else result = "浏览器检查完成（详见日志）";
                Log("登录状态: " + result);
                MessageBox.Show("登录状态：" + result, "登录状态");
            });
            th.IsBackground = true; th.Start();
        }

        static void ShowFriendStatus()
        {
            // 好友今日发送状态一览：从 friends.json + 今日 spark_log.csv 汇总
            var dlg = new Form();
            dlg.Text = "好友状态（今日）"; dlg.Size = new Size(500, 640);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.BackColor = Color.FromArgb(37, 37, 46);
            var dgv = new DataGridView();
            dgv.Dock = DockStyle.Fill; dgv.ReadOnly = true; dgv.AllowUserToAddRows = false;
            dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgv.BackgroundColor = Color.FromArgb(28, 28, 36); dgv.BorderStyle = BorderStyle.None;
            dgv.RowHeadersVisible = false; dgv.EnableHeadersVisualStyles = false;
            dgv.ColumnHeadersHeight = 32;
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 50);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(180, 180, 190);
            dgv.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F);
            dgv.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F);
            dgv.DefaultCellStyle.ForeColor = Color.FromArgb(210, 210, 220);
            dgv.DefaultCellStyle.BackColor = Color.FromArgb(28, 28, 36);
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(60, 80, 70);
            dgv.RowTemplate.Height = 30;
            var dt = new System.Data.DataTable();
            dt.Columns.Add("好友");
            dt.Columns.Add("今日状态");
            var todayMap = new System.Collections.Generic.Dictionary<string, string>();
            try
            {
                var csv = Path.Combine(BaseDir, "logs", "spark_log.csv");
                if (File.Exists(csv))
                {
                    string today = DateTime.Now.ToString("yyyy-MM-dd");
                    foreach (var line in File.ReadAllLines(csv))
                    {
                        var cells = ParseCsvLine(line);
                        if (cells.Length >= 6 && cells[0].StartsWith(today))
                            todayMap[cells[1]] = cells[4];
                    }
                }
            }
            catch { }
            var names = new System.Collections.Generic.List<string>();
            try
            {
                var fc = File.ReadAllText(Path.Combine(BaseDir, "friends.json"), System.Text.Encoding.UTF8);
                var m = Regex.Matches(fc, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                foreach (Match mm in m) names.Add(mm.Groups[1].Value);
            }
            catch { }
            foreach (var n in names)
            {
                var row = dt.NewRow();
                row[0] = n;
                string st;
                if (!todayMap.TryGetValue(n, out st)) row[1] = "— 未发送";
                else if (st == "OK") row[1] = "✓ 已发送";
                else row[1] = "✗ " + st;
                dt.Rows.Add(row);
            }
            dgv.DataSource = dt;
            for (int i = 0; i < dgv.Rows.Count; i++)
            {
                string st = dgv.Rows[i].Cells[1].Value as string;
                if (st != null && st.StartsWith("✓")) dgv.Rows[i].DefaultCellStyle.ForeColor = Color.FromArgb(126, 232, 168);
                else if (st != null && st.StartsWith("✗")) dgv.Rows[i].DefaultCellStyle.ForeColor = Color.FromArgb(255, 130, 130);
                else dgv.Rows[i].DefaultCellStyle.ForeColor = Color.FromArgb(140, 140, 150);
            }
            dlg.Controls.Add(dgv);
            dlg.ShowDialog();
        }

        static void AddFriend()
        {
            var dlg = new Form();
            dlg.Text = "管理好友"; dlg.Size = new Size(420, 470);
            dlg.StartPosition = FormStartPosition.CenterParent; dlg.BackColor = Color.White;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog; dlg.MaximizeBox = false; dlg.MinimizeBox = false;

            string ff = Path.Combine(BaseDir, "friends.json");

            // 已有好友下拉 + 删除
            var l0 = new Label(); l0.Text = "已有好友（选一个可删除）："; l0.Location = new Point(20, 14); l0.AutoSize = true;
            var cb = new ComboBox(); cb.DropDownStyle = ComboBoxStyle.DropDownList; cb.Location = new Point(20, 36); cb.Size = new Size(360, 25);
            try
            {
                var fc = File.ReadAllText(ff, System.Text.Encoding.UTF8);
                var m = Regex.Matches(fc, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                foreach (Match mm in m) cb.Items.Add(mm.Groups[1].Value);
            }
            catch { }
            if (cb.Items.Count == 0) cb.Items.Add("（暂无好友）");
            cb.SelectedIndex = 0;
            var bDel = new RoundButton(); bDel.Text = "删除选中"; bDel.Size = new Size(360, 30); bDel.Location = new Point(20, 66);
            bDel.MainColor = Color.FromArgb(90, 90, 100); bDel.HoverColor = Color.FromArgb(120, 120, 130);
            bDel.Click += delegate
            {
                string sel = cb.SelectedItem as string;
                if (sel == null || sel == "（暂无好友）") { MessageBox.Show("没有可删除的好友"); return; }
                if (MessageBox.Show("确定删除好友「" + sel + "」？", "确认删除", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                try
                {
                    var fc = File.ReadAllText(ff, System.Text.Encoding.UTF8);
                    var items = Regex.Matches(fc, "\\{[^{}]*\\}")
                        .Cast<Match>().Select(m => m.Value)
                        .Where(m => !Regex.IsMatch(m, "\"name\"\\s*:\\s*\"" + Regex.Escape(sel) + "\"")).ToList();
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("[");
                    for (int i = 0; i < items.Count; i++)
                    {
                        sb.Append("  " + items[i]);
                        if (i < items.Count - 1) sb.Append(",");
                        sb.AppendLine();
                    }
                    sb.Append("]");
                    File.WriteAllText(ff, sb.ToString(), new System.Text.UTF8Encoding(false));
                    Log("已删除: " + sel);
                    dlg.Close();
                }
                catch (Exception ex) { MessageBox.Show("删除失败: " + ex.Message); }
            };

            var sep = new Label(); sep.Text = "───────── 添加新好友 ─────────"; sep.Location = new Point(20, 104); sep.AutoSize = true; sep.ForeColor = Color.Gray; sep.Font = new Font("Microsoft YaHei UI", 8F);

            // 剪贴板快速添加：复制主页链接 / 短链 / 抖音号后自动读取，支持多行批量
            var lTip = new Label();
            lTip.Text = "在抖音复制好友主页链接、v.douyin 短链或抖音号后，\n回到本窗口会自动读取剪贴板；支持一次复制多行。";
            lTip.Location = new Point(20, 128); lTip.AutoSize = true; lTip.ForeColor = Color.FromArgb(0, 150, 136); lTip.Font = new Font("Microsoft YaHei UI", 9F);

            var l1 = new Label(); l1.Text = "好友昵称："; l1.Location = new Point(20, 178); l1.AutoSize = true;
            var t1 = new TextBox(); t1.Location = new Point(110, 176); t1.Size = new Size(270, 25);
            var l2 = new Label(); l2.Text = "链接/抖音号："; l2.Location = new Point(20, 216); l2.AutoSize = true;
            var t2 = new TextBox(); t2.Location = new Point(110, 214); t2.Size = new Size(270, 64); t2.Multiline = true; t2.ScrollBars = ScrollBars.Vertical;
            var l3 = new Label(); l3.Text = "每行一个：主页链接 / v.douyin 短链 / 抖音号（可写「昵称|链接」）"; l3.Location = new Point(110, 282); l3.AutoSize = true; l3.ForeColor = Color.Gray; l3.Font = new Font("Microsoft YaHei UI", 8F);

            var lblClip = new Label(); lblClip.Text = ""; lblClip.Location = new Point(20, 306); lblClip.AutoSize = true; lblClip.ForeColor = Color.FromArgb(0, 150, 136); lblClip.Font = new Font("Microsoft YaHei UI", 8F);

            var b = new RoundButton(); b.Text = "保存添加"; b.Size = new Size(100, 32); b.Location = new Point(150, 336);
            b.MainColor = Color.FromArgb(255, 44, 85); b.HoverColor = Color.FromArgb(230, 30, 70);

            // 剪贴板监听：每 1.5 秒读一次，识别链接 / 抖音号自动填入（连粘贴都省了）
            System.Func<string> TryClip = delegate()
            {
                try { return Clipboard.GetText(); } catch { return ""; }
            };
            System.Func<string, bool> LooksLikeTarget = delegate(string x)
            {
                return Regex.IsMatch(x, @"(?i)https?://[^\s]+")
                    || Regex.IsMatch(x, @"(?i)(douyin\.com)")
                    || Regex.IsMatch(x, @"^[A-Za-z0-9_]{4,30}$");
            };
            var clipTimer = new System.Windows.Forms.Timer();
            clipTimer.Interval = 1500;
            string lastClip = null;
            clipTimer.Tick += delegate
            {
                string c = TryClip();
                if (c == null || c.Length == 0 || c == lastClip) return;
                lastClip = c;
                string one = c.Trim();
                if (LooksLikeTarget(one))
                {
                    string prev = t2.Text.Trim();
                    if (prev.Length > 0 && !t2.Text.EndsWith("\n")) t2.Text = t2.Text.TrimEnd() + "\n" + one;
                    else t2.Text = one;
                    lblClip.Text = "✓ 已自动读取剪贴板，可直接点保存";
                }
            };
            clipTimer.Start();
            { string c0 = TryClip(); if (c0 != null && c0.Trim().Length > 0) { lastClip = c0; string o = c0.Trim(); if (LooksLikeTarget(o)) { t2.Text = o; lblClip.Text = "✓ 已自动读取剪贴板，可直接点保存"; } } }

            // v.douyin 短链 → 最终主页链接（后台解析）
            System.Func<string, string> ResolveShort = delegate(string u)
            {
                try
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(u);
                    req.AllowAutoRedirect = true;
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36";
                    req.Timeout = 12000;
                    using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                    {
                        return resp.ResponseUri != null ? resp.ResponseUri.AbsoluteUri : "";
                    }
                }
                catch { return ""; }
            };

            b.Click += delegate
            {
                string name = t1.Text.Trim();
                string[] lines = t2.Text.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0) { MessageBox.Show("请复制好友主页链接、v.douyin 短链或抖音号（可多行）"); return; }
                int ok = 0;
                var toWrite = new System.Collections.Generic.List<string>();
                var idList = new System.Collections.Generic.List<string>();
                var shortList = new System.Collections.Generic.List<string>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    string nm = name;
                    string val = line;
                    if (line.Contains("|")) { int p = line.IndexOf('|'); nm = line.Substring(0, p).Trim(); val = line.Substring(p + 1).Trim(); }
                    if (val.Length == 0) continue;
                    if (Regex.IsMatch(val, @"^https?://www\.douyin\.com/user/[A-Za-z0-9_-]+/?$"))
                    {
                        string u = val;
                        string nn = (nm.Length > 0) ? nm : ("抖音好友" + (ok + 1));
                        toWrite.Add("{\"name\": \"" + nn.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\", \"url\": \"" + u.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
                        ok++;
                    }
                    else if (Regex.IsMatch(val, @"(?i)^https?://v\.douyin\.com/[A-Za-z0-9]+/?$"))
                    {
                        shortList.Add(val);
                    }
                    else if (Regex.IsMatch(val, @"^[A-Za-z0-9_]{4,30}$"))
                    {
                        idList.Add(val);
                    }
                    else { MessageBox.Show("第 " + (i + 1) + " 行无法识别：\n" + line + "\n\n请使用主页链接（https://www.douyin.com/user/...）、v.douyin 短链或抖音号"); }
                }
                if (toWrite.Count > 0)
                {
                    string c = File.ReadAllText(ff, System.Text.Encoding.UTF8).TrimEnd();
                    if (c.EndsWith("]"))
                    {
                        var sb = new System.Text.StringBuilder(c.Substring(0, c.Length - 1).TrimEnd());
                        if (!sb.ToString().EndsWith("[")) sb.Append(",");
                        for (int i = 0; i < toWrite.Count; i++) { sb.Append("\n  " + toWrite[i]); if (i < toWrite.Count - 1) sb.Append(","); }
                        sb.Append("\n]");
                        File.WriteAllText(ff, sb.ToString(), new System.Text.UTF8Encoding(false));
                    }
                    Log("已添加 " + toWrite.Count + " 位好友");
                }
                string msg = "添加完成：" + ok + " 个成功";
                if (shortList.Count > 0) msg += "；" + shortList.Count + " 个短链正在后台解析，完成会弹窗提示";
                if (idList.Count > 0)
                {
                    var tg = BrowserTarget(GetBrowserChoice());
                    if (File.Exists(tg[0]))
                    {
                        foreach (string idv in idList)
                        {
                            string q = Uri.EscapeDataString(idv);
                            try { Process.Start(new ProcessStartInfo { FileName = tg[0], Arguments = "--user-data-dir=\"" + tg[1] + "\" https://www.douyin.com/search/" + q + "?type=user", UseShellExecute = true }); }
                            catch { }
                        }
                        msg += "；已用浏览器打开 " + idList.Count + " 个抖音号搜索页，进主页复制链接后回来点保存即可";
                    }
                }
                dlg.Close();
                MessageBox.Show(msg, "管理好友");
                // 短链后台解析后自动写入 friends.json（完成后弹窗提示，避免跨线程写日志）
                if (shortList.Count > 0)
                {
                    var th = new System.Threading.Thread(delegate()
                    {
                        var got = new System.Collections.Generic.List<string>();
                        foreach (string su in shortList)
                        {
                            string fin = ResolveShort(su);
                            if (fin.Length > 0 && Regex.IsMatch(fin, @"(?i)^https?://(www\.)?douyin\.com/user/")) got.Add(fin);
                        }
                        if (got.Count > 0)
                        {
                            try
                            {
                                string c = File.ReadAllText(ff, System.Text.Encoding.UTF8).TrimEnd();
                                if (c.EndsWith("]"))
                                {
                                    var sb = new System.Text.StringBuilder(c.Substring(0, c.Length - 1).TrimEnd());
                                    if (!sb.ToString().EndsWith("[")) sb.Append(",");
                                    for (int i = 0; i < got.Count; i++) { sb.Append("\n  {\"name\": \"抖音好友（短链" + (i + 1) + "）\", \"url\": \"" + got[i].Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}"); if (i < got.Count - 1) sb.Append(","); }
                                    sb.Append("\n]");
                                    File.WriteAllText(ff, sb.ToString(), new System.Text.UTF8Encoding(false));
                                }
                            }
                            catch { }
                        }
                        MessageBox.Show("短链解析完成，已添加 " + got.Count + " 位好友", "管理好友");
                    });
                    th.IsBackground = true; th.Start();
                }
            };
            dlg.Controls.AddRange(new Control[] { l0, cb, bDel, sep, lTip, l1, t1, l2, t2, l3, lblClip, b });
            dlg.ShowDialog();
        }

        // 一键导入：自动抓取抖音全部关注对象 → 勾选窗口 → 批量加入
        static void ImportFollowing()
        {
            if (!btnStart.Enabled) { Log("正在发送中，发送完成后再导入"); return; }
            Log("开始抓取抖音关注列表（会弹出浏览器窗口，约30秒）…");
            var th = new System.Threading.Thread(delegate()
            {
                try
                {
                    var psi = new ProcessStartInfo();
                    psi.FileName = PyExe;
                    psi.Arguments = "\"" + Path.Combine(BaseDir, "fetch_following.py") + "\"";
                    psi.WorkingDirectory = BaseDir;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    using (var pr = Process.Start(psi))
                    {
                        pr.WaitForExit(150000);
                        if (!pr.HasExited) { try { pr.Kill(); } catch { } }
                    }
                    string ff = Path.Combine(BaseDir, "following.json");
                    if (!File.Exists(ff)) { Log("抓取失败：未生成 following.json"); return; }
                    var items = new System.Collections.Generic.List<string[]>();
                    string raw = File.ReadAllText(ff, System.Text.Encoding.UTF8);
                    var reN = new Regex("\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    var reU = new Regex("\"url\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    var msN = reN.Matches(raw); var msU = reU.Matches(raw);
                    int cnt = Math.Min(msN.Count, msU.Count);
                    for (int i = 0; i < cnt; i++)
                        items.Add(new[] { msN[i].Groups[1].Value, msU[i].Groups[1].Value });
                    if (items.Count == 0) { Log("抓取到 0 个关注对象"); return; }
                    var exist = new System.Collections.Generic.HashSet<string>();
                    try
                    {
                        string ffc = File.ReadAllText(Path.Combine(BaseDir, "friends.json"), System.Text.Encoding.UTF8);
                        foreach (Match m in Regex.Matches(ffc, "/user/([A-Za-z0-9_-]+)")) exist.Add(m.Groups[1].Value);
                    }
                    catch { }
                    mainForm.BeginInvoke((Action)delegate { ShowFollowingPick(items, exist); });
                }
                catch (Exception ex) { Log("导入关注失败: " + ex.Message); }
            });
            th.IsBackground = true; th.Start();
        }

        static void ShowFollowingPick(System.Collections.Generic.List<string[]> items, System.Collections.Generic.HashSet<string> exist)
        {
            var dlg = new Form();
            dlg.Text = "导入抖音关注列表"; dlg.Size = new Size(470, 620);
            dlg.StartPosition = FormStartPosition.CenterParent; dlg.BackColor = Color.White;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog; dlg.MaximizeBox = false; dlg.MinimizeBox = false;
            var l1 = new Label();
            l1.Text = "抓到 " + items.Count + " 个关注对象，勾选要续火花的人（已加过的默认勾选）：";
            l1.Location = new Point(15, 12); l1.AutoSize = true; l1.Font = new Font("Microsoft YaHei UI", 9F); l1.ForeColor = KPinkDeep;
            var clb = new CheckedListBox(); clb.Location = new Point(15, 42); clb.Size = new Size(430, 490);
            clb.CheckOnClick = true; clb.Font = new Font("Microsoft YaHei UI", 9F);
            foreach (var it in items)
            {
                string name = string.IsNullOrEmpty(it[0]) ? "(未取名)" : it[0];
                string show = name;
                int idx = clb.Items.Add(show);
                bool already = exist.Contains(it[1].Replace("https://www.douyin.com/user/", ""));
                clb.SetItemChecked(idx, already);
            }
            var bAll = new RoundButton(); bAll.Text = "全选"; bAll.Size = new Size(72, 28); bAll.Location = new Point(15, 546);
            bAll.MainColor = Color.FromArgb(120, 120, 130); bAll.HoverColor = Color.FromArgb(140, 140, 150); bAll.ForeColor = Color.White;
            bAll.Click += delegate { for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, true); };
            var bNone = new RoundButton(); bNone.Text = "清空"; bNone.Size = new Size(72, 28); bNone.Location = new Point(95, 546);
            bNone.MainColor = Color.FromArgb(120, 120, 130); bNone.HoverColor = Color.FromArgb(140, 140, 150); bNone.ForeColor = Color.White;
            bNone.Click += delegate { for (int i = 0; i < clb.Items.Count; i++) clb.SetItemChecked(i, false); };
            var bOk = new RoundButton(); bOk.Text = "导入选中"; bOk.Size = new Size(130, 32); bOk.Location = new Point(255, 544);
            bOk.MainColor = KPink; bOk.HoverColor = Color.FromArgb(255, 92, 138); bOk.ForeColor = Color.White;
            bOk.Click += delegate
            {
                var toAdd = new System.Collections.Generic.List<string[]>();
                for (int i = 0; i < clb.Items.Count; i++)
                    if (clb.GetItemChecked(i)) toAdd.Add(items[i]);
                if (toAdd.Count == 0) { MessageBox.Show("没勾选任何人"); return; }
                try
                {
                    string ff = Path.Combine(BaseDir, "friends.json");
                    string c = File.ReadAllText(ff, System.Text.Encoding.UTF8).TrimEnd();
                    var sb = new System.Text.StringBuilder(c.Substring(0, c.Length - 1).TrimEnd());
                    int n = 0;
                    foreach (var it in toAdd)
                    {
                        string uid = it[1].Replace("https://www.douyin.com/user/", "");
                        if (exist.Contains(uid)) continue;
                        string nm = string.IsNullOrEmpty(it[0]) ? "抖音好友" : it[0];
                        sb.Append(",\n  {\"name\": \"" + nm.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\", \"url\": \"" + it[1] + "\"}");
                        exist.Add(uid); n++;
                    }
                    sb.Append("\n]");
                    File.WriteAllText(ff, sb.ToString(), new System.Text.UTF8Encoding(false));
                    Log("已从关注列表导入 " + n + " 位新好友");
                    MessageBox.Show("成功导入 " + n + " 位新好友（跳过已有的）");
                    dlg.Close();
                }
                catch (Exception ex) { MessageBox.Show("写入失败: " + ex.Message); }
            };
            dlg.Controls.AddRange(new Control[] { l1, clb, bAll, bNone, bOk });
            dlg.ShowDialog();
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

    class SparkProgress : Control
    {
        public int Done = 0;
        public int Total = 0;
        public string Info = "";
        public bool Finished = false;
        private long finishStart = 0;
        private System.Windows.Forms.Timer animTimer;

        public SparkProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 50;
            animTimer.Tick += delegate { Invalidate(); };
            animTimer.Start();
        }

        public void MarkFinished()
        {
            Finished = true;
            finishStart = Environment.TickCount;
            Invalidate();
        }

        public void Reset()
        {
            Finished = false;
            finishStart = 0;
            if (!animTimer.Enabled) animTimer.Start();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int w = ClientSize.Width, h = ClientSize.Height;
            int barH = 24, barY = 8;
            var slot = new Rectangle(0, barY, w, barH);
            double pct = (Total <= 0) ? 0 : Math.Min(1.0, (double)Done / Total);
            if (Finished)
            {
                // 完成态：整条绿色铺满，不留粉边
                using (var lg = new LinearGradientBrush(slot, Color.FromArgb(126, 220, 150), Color.FromArgb(90, 195, 130), LinearGradientMode.Horizontal))
                    FillRounded(g, lg, slot, 12);
                // 白色对勾居中偏左
                using (var pen = new Pen(Color.White, 3))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLines(pen, new[] {
                        new PointF(w * 0.44f, barY + barH * 0.55f),
                        new PointF(w * 0.49f, barY + barH * 0.75f),
                        new PointF(w * 0.60f, barY + barH * 0.28f)
                    });
                }
                // 撒花粒子（3秒），播完停定时器
                int el = (int)((Environment.TickCount - finishStart) / 1000.0);
                if (el < 3)
                {
                    var rnd = new Random(2024);
                    for (int i = 0; i < 26; i++)
                    {
                        float px = rnd.Next(10, w - 10);
                        float py = barY + 20 + el * 60 + rnd.Next(0, 50);
                        if (py > h - 8) continue;
                        int rr = rnd.Next(200, 255), gg = rnd.Next(120, 190), bb2 = rnd.Next(180, 230);
                        g.FillEllipse(new SolidBrush(Color.FromArgb(220, rr, gg, bb2)), px, py, 6, 6);
                    }
                }
                else if (animTimer.Enabled) animTimer.Stop();
            }
            else
            {
                using (var bb = new SolidBrush(Color.FromArgb(255, 227, 238)))
                    FillRounded(g, bb, slot, 12);
                if (pct > 0 && Total > 0)
                {
                    int pw = (int)((w - 4) * pct);
                    var fill = new Rectangle(2, barY + 2, pw, barH - 4);
                    using (var lg = new LinearGradientBrush(fill, KittyTheme.KPink, Color.FromArgb(255, 168, 208), LinearGradientMode.Horizontal))
                        FillRounded(g, lg, fill, 10);
                    double fx = (fill.Width - 60) * (0.5 + 0.5 * Math.Sin(Environment.TickCount / 240.0));
                    if (fx < 0) fx = 0;
                    using (var hot = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                        g.FillRectangle(hot, (int)(2 + fx), barY + 2, 60, barH - 4);
                }
            }
            string pctText = Finished ? "100%" : ((Total <= 0) ? "0%" : ((int)(pct * 100) + "%"));
            string sub = Finished ? "✓ 全部完成" : ((Total <= 0) ? "点「开始续火花」启动" : (Done + " / " + Total));
            using (var f1 = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold))
            using (var b1 = new SolidBrush(Finished ? Color.FromArgb(70, 170, 110) : Color.FromArgb(205, 60, 115)))
            {
                g.DrawString(pctText, f1, b1, new PointF(8, barY + barH + 4));
                if (Total > 0 || Finished)
                {
                    using (var f2 = new Font("Microsoft YaHei UI", 9F))
                    using (var b2 = new SolidBrush(Color.FromArgb(180, 130, 150)))
                        g.DrawString(sub, f2, b2, new PointF(8 + g.MeasureString(pctText, f1).Width + 12, barY + barH + 9));
                }
            }
            if (Info.Length > 0)
            {
                using (var f3 = new Font("Microsoft YaHei UI", 9.5F))
                using (var b3 = new SolidBrush(Color.FromArgb(190, 130, 150)))
                {
                    var sz = g.MeasureString(Info, f3);
                    g.DrawString(Info, f3, b3, new PointF(w - sz.Width - 10, barY + barH + 8));
                }
            }
        }

        static void FillRounded(Graphics g, Brush b, Rectangle r, int rad)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            g.FillPath(b, path);
        }
    }

    class CardForm : Form
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var b = new SolidBrush(this.BackColor))
                e.Graphics.FillRectangle(b, this.ClientRectangle);
            base.OnPaint(e);
        }
    }

    class SplashForm : Form
    {
        public SplashForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
        }
    }

    class KittyBadge : Control
    {
        public KittyBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float s = Math.Min(Width, Height);
            KittyTheme.PaintKittyFace(e.Graphics, s * 0.55f, s * 0.55f, s * 0.92f);
            base.OnPaint(e);
        }
    }

    class CardPanel : Panel
    {
        public CardPanel() { BackColor = Color.White; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Color.White))
                e.Graphics.FillRoundedRect(brush, 0, 0, Width - 1, Height - 1, 12);
            base.OnPaint(e);
        }
    }

    class RoundButton : Button
    {
        public Color MainColor = Color.FromArgb(255, 111, 165);
        public Color HoverColor = Color.FromArgb(255, 92, 138);
        bool hover = false;
        public RoundButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.Transparent;
            ForeColor = Color.White; Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MouseEnter += delegate { hover = true; Invalidate(); };
            MouseLeave += delegate { hover = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = hover ? HoverColor : MainColor;
            Rectangle rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = new GraphicsPath())
            {
                int r = 6;
                path.AddArc(rc.X, rc.Y, r * 2, r * 2, 180, 90);
                path.AddArc(rc.Right - r * 2, rc.Y, r * 2, r * 2, 270, 90);
                path.AddArc(rc.Right - r * 2, rc.Bottom - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(rc.X, rc.Bottom - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                using (var brush = new SolidBrush(c)) g.FillPath(brush, path);
            }
            var sf = new StringFormat(); sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
            g.DrawString(Text, Font, new SolidBrush(ForeColor), new RectangleF(0, 0, Width, Height), sf);
        }
    }

    static class GraphicsExt
    {
        public static void FillRoundedRect(this Graphics g, Brush brush, int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath(); int d = r * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }
}
