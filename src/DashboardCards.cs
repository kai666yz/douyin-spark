using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DouyinSpark
{
    class OverviewCard:Control
    {
        public TodaySummary Summary=new TodaySummary();
        public string NextText="未设置定时", ModeText="分类语录";
        public RoundButton StartButton;
        public OverviewCard(){DoubleBuffered=true;BackColor=AppPalette.Canvas;SetStyle(ControlStyles.ResizeRedraw,true);}
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if(StartButton!=null) StartButton.SetBounds(Width-212,65,188,56);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=AppPalette.Round(new RectangleF(.5f,.5f,Width-1,Height-1),16))
            using(var surface=new SolidBrush(AppPalette.Surface))
            using(var border=new Pen(AppPalette.Border)){g.FillPath(surface,path);g.DrawPath(border,path);}
            using(var text=new SolidBrush(AppPalette.Text))
            using(var muted=new SolidBrush(AppPalette.Muted))
            using(var title=AppPalette.Font(10,true))
            using(var large=AppPalette.Font(28,true))
            using(var small=AppPalette.Font(9))
            {
                g.DrawString("今日概览",title,text,24,18);
                g.DrawString(Summary.Processed+" / "+Summary.Total,large,text,22,48);
                g.DrawString("今日已处理 · 按好友去重",small,muted,26,106);
                Metric(g,"成功",Summary.Succeeded,216,AppPalette.Success);
                Metric(g,"失败",Summary.Failed,306,AppPalette.Error);
                Metric(g,"待发",Summary.Pending,396,AppPalette.Muted);
                var right=new StringFormat {Alignment=StringAlignment.Far,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};
                g.DrawString(NextText,small,muted,new RectangleF(Width-320,22,296,24),right);
                using(var pen=new Pen(AppPalette.Border))g.DrawLine(pen,24,139,Width-24,139);
                string detail=Summary.Unconfirmed>0?Summary.Unconfirmed+" 位待确认，请查看历史记录":Summary.MissingLinks>0?Summary.MissingLinks+" 位待补主页，暂不计入发送名单":"每一份问候，都有清晰的记录。";
                g.DrawString(detail,small,muted,new RectangleF(24,151,Width-260,20),new StringFormat {Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap});
                g.DrawString("消息模式 · "+ModeText,small,muted,new RectangleF(Width-230,151,206,20),right);
                right.Dispose();
            }
        }
        static void Metric(Graphics g,string label,int value,int x,Color color)
        {
            using(var font=AppPalette.Font(9))using(var brush=new SolidBrush(AppPalette.Muted))g.DrawString(label,font,brush,x,53);
            using(var font=AppPalette.Font(22,true))using(var brush=new SolidBrush(color))g.DrawString(value.ToString(),font,brush,x-2,76);
        }
    }
    class EmptyHistory:Control
    {
        public EmptyHistory(){DoubleBuffered=true;Dock=DockStyle.Fill;BackColor=AppPalette.Surface;AccessibleName="暂无发送记录，先添加好友，再点击上方开始发送。";}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.Clear(AppPalette.Surface);g.SmoothingMode=SmoothingMode.AntiAlias;
            float cx=Width/2f;float top=Math.Max(8,Height/2f-64);
            using(var brush=new SolidBrush(AppPalette.Soft))g.FillEllipse(brush,cx-35,top,70,70);
            KittyTheme.PaintKittyFace(g,cx+3,top+30,48);
            using(var font=AppPalette.Font(10,true))using(var brush=new SolidBrush(AppPalette.Text))
                g.DrawString("还没有发送记录",font,brush,new RectangleF(0,top+78,Width,25),new StringFormat {Alignment=StringAlignment.Center});
            using(var font=AppPalette.Font(9))using(var brush=new SolidBrush(AppPalette.Muted))
                g.DrawString("先添加好友，再点上方「开始发送」吧",font,brush,new RectangleF(0,top+107,Width,24),new StringFormat {Alignment=StringAlignment.Center});
        }
    }
}
