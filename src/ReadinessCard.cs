using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        static ReadinessCard readiness;
        static bool environmentReady;
        static void RefreshEnvironmentBanner()
        {
            if(readiness==null||readiness.IsDisposed)return;
            readiness.Attention=schedulePaused||GroupPaused||!environmentReady;
            readiness.Title=schedulePaused?"私信定时已暂停，请确认后启用":GroupPaused?"群聊定时已暂停，请确认后启用":environmentReady?"环境已准备好":"有组件待准备，点击检查";
            readiness.Detail=dataLocation!=null&&dataLocation.Fallback?"数据 · 本机用户目录":"数据 · 便携目录";
            readiness.AccessibleName=readiness.Title+"。"+readiness.Detail+(schedulePaused?"。打开定时设置":"。打开环境与数据检查");readiness.Invalidate();
        }
    }
    class ReadinessCard:Control
    {
        public string Title="环境检查",Detail="";public bool Attention;
        public ReadinessCard(){DoubleBuffered=true;Cursor=Cursors.Hand;TabStop=true;SetStyle(ControlStyles.ResizeRedraw,true);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){OnClick(EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.Clear(AppPalette.Canvas);g.SmoothingMode=SmoothingMode.AntiAlias;
            Color soft=Attention?AppPalette.WarningSoft:AppPalette.Soft,accent=Attention?AppPalette.Warning:AppPalette.AccentDeep;
            using(var path=AppPalette.Round(new RectangleF(0,0,Width-1,Height-4),12))using(var brush=new SolidBrush(soft))g.FillPath(brush,path);
            using(var brush=new SolidBrush(accent))g.FillEllipse(brush,14,15,7,7);
            using(var title=AppPalette.Font(9,true))using(var detail=AppPalette.Font(8)){
                TextRenderer.DrawText(g,Title,title,new Rectangle(30,1,Math.Max(40,Width-220),Height-5),accent,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g,Detail+"  ›",detail,new Rectangle(Math.Max(100,Width-190),1,175,Height-5),AppPalette.Muted,TextFormatFlags.Right|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
            }
        }
    }
}
