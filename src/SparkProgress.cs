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
                using (var pen = new Pen(Color.FromArgb(25,85,48), 2))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLines(pen, new[] {
                        new PointF(w * 0.5f - 9, barY + barH * 0.5f),
                        new PointF(w * 0.5f - 2, barY + barH * 0.72f),
                        new PointF(w * 0.5f + 10, barY + barH * 0.27f)
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

}
