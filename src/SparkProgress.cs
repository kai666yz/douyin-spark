using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DouyinSpark
{
    class SparkProgress:Control
    {
        public int Done,Total;
        public string Info="";
        public bool Finished,ErrorState;
        double displayed;
        int finishStart;
        string lastInfo="";
        readonly Timer animation;
        public SparkProgress()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            BackColor=AppPalette.Canvas;
            animation=new Timer {Interval=30};
            animation.Tick+=delegate
            {
                double target=Total>0?Math.Min(1d,Math.Max(0d,(double)Done/Total)):0;
                bool moving=Math.Abs(target-displayed)>.001;
                if(moving)displayed+=(target-displayed)*.20;
                else displayed=target;
                if(moving || lastInfo!=Info || Finished && unchecked(Environment.TickCount-finishStart)<450) {lastInfo=Info;Invalidate();}
            };
            animation.Start();
        }
        public void Reset(){Finished=false;ErrorState=false;displayed=0;Info="";Invalidate();}
        public void MarkFinished(bool error=false){Finished=true;ErrorState=error;finishStart=Environment.TickCount;Invalidate();}
        protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.Clear(AppPalette.Canvas);g.SmoothingMode=SmoothingMode.AntiAlias;
            var slot=new RectangleF(0,5,Math.Max(1,Width-1),7);
            using(var path=AppPalette.Round(slot,4))using(var brush=new SolidBrush(AppPalette.Border))g.FillPath(brush,path);
            float width=(float)(slot.Width*displayed);
            Color color=Finished?(ErrorState?AppPalette.Warning:AppPalette.SuccessBar):AppPalette.Accent;
            if(width>1)using(var path=AppPalette.Round(new RectangleF(0,5,width,7),4))
            using(var brush=new LinearGradientBrush(new RectangleF(0,5,Math.Max(2,width),7),color,Finished?color:AppPalette.AccentDeep,0f))g.FillPath(brush,path);
            int x=0;
            if(Finished)
            {
                float scale=(float)Math.Min(1d,Math.Max(.6,unchecked(Environment.TickCount-finishStart)/200d));
                var state=g.Save();g.TranslateTransform(10,31);g.ScaleTransform(scale,scale);
                using(var pen=new Pen(ErrorState?AppPalette.Warning:AppPalette.Success,2){StartCap=LineCap.Round,EndCap=LineCap.Round})
                {
                    if(ErrorState){g.DrawEllipse(pen,-7,-7,14,14);g.DrawLine(pen,0,-4,0,1);g.DrawLine(pen,0,4,0,4.3f);}
                    else g.DrawLines(pen,new[]{new Point(-6,0),new Point(-1,5),new Point(8,-5)});
                }
                g.Restore(state);x=28;
            }
            string stateText=Finished?(ErrorState?"本轮结束 · 请查看异常记录":"本轮处理完成"):Total>0?"发送进度 "+Done+" / "+Total:"准备就绪";
            using(var font=AppPalette.Font(9,true))using(var brush=new SolidBrush(AppPalette.Text))g.DrawString(stateText,font,brush,new RectangleF(x,22,310,24));
            string detail=Info.Length>0?Info:Total==0?"先预览，再发送；结果将保存在本机。":((int)(displayed*100))+"%";
            using(var font=AppPalette.Font(8.5f))using(var brush=new SolidBrush(AppPalette.Muted))
            using(var format=new StringFormat {Alignment=StringAlignment.Far,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
                g.DrawString(detail,font,brush,new RectangleF(320,24,Math.Max(1,Width-322),22),format);
        }
    }
}
