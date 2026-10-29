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
    class CardForm : ThemedForm
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
            using (var brush = new SolidBrush(AppPalette.Surface))
                e.Graphics.FillRoundedRect(brush, 0, 0, Width - 1, Height - 1, 16);
            base.OnPaint(e);
        }
    }

    class RoundButton : Button
    {
        public Color MainColor=AppPalette.Accent, HoverColor=AppPalette.AccentDeep;
        public bool Primary=true, Navigation=false, Selected=false;
        public int CornerRadius=16;
        public string IconName="";
        bool hover,pressed;
        public RoundButton()
        {
            FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;TabStop=true;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            MouseEnter+=delegate{hover=true;Invalidate();};MouseLeave+=delegate{hover=false;pressed=false;Invalidate();};
            MouseDown+=delegate{pressed=true;Invalidate();};MouseUp+=delegate{pressed=false;Invalidate();};
            EnabledChanged+=delegate{Invalidate();};
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            Color parentColor=Parent is OverviewCard || Parent is CardSurface ? AppPalette.Surface : Parent!=null?Parent.BackColor:AppPalette.Surface;
            g.Clear(parentColor);
            int shift=pressed?1:0;
            var box=new RectangleF(1,1+shift,Width-3,Height-5);
            Color fill=Primary?(Enabled?(hover?AppPalette.AccentDeep:AppPalette.Accent):AppPalette.Soft):Selected||hover?AppPalette.Soft:parentColor;
            Color foreground=Primary&&Enabled?(AppPalette.Dark?AppPalette.Canvas:Color.White):Selected?AppPalette.AccentDeep:Enabled?AppPalette.Text:AppPalette.Muted;
            if(Primary&&Enabled)
            {
                using(var path=AppPalette.Round(new RectangleF(box.X,box.Y+2,box.Width,box.Height),CornerRadius))
                using(var brush=new SolidBrush(Color.FromArgb(20,80,40,57)))g.FillPath(brush,path);
            }
            using(var path=AppPalette.Round(box,CornerRadius))
            using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
            bool icon=IconName.Length>0;
            if(icon)
            {
                int x=Navigation||Primary?18:12;
                LineIcons.Draw(g,IconName,new Rectangle(x,(Height-20)/2+shift-1,20,20),foreground);
            }
            var textBox=Navigation?new Rectangle(48,shift,Width-54,Height-4):icon?new Rectangle(Primary?48:32,shift,Width-(Primary?54:38),Height-4):new Rectangle(0,shift,Width,Height-4);
            TextRenderer.DrawText(g,Text,Font,textBox,foreground,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|(Navigation?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
            if(Focused)using(var path=AppPalette.Round(new RectangleF(2,2,Width-5,Height-6),CornerRadius))using(var pen=new Pen(AppPalette.Accent,1))g.DrawPath(pen,path);
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
