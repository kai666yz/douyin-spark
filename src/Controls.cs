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
