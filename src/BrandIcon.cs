using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DouyinSpark
{
    public static class SparkBrand
    {
        public static void Paint(Graphics g,RectangleF bounds)
        {
            var state=g.Save();g.TranslateTransform(bounds.X,bounds.Y);g.ScaleTransform(bounds.Width/256,bounds.Height/256);g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=AppPalette.Round(new RectangleF(8,8,240,240),62))using(var fill=new LinearGradientBrush(new Point(30,20),new Point(220,240),Color.FromArgb(86,162,250),Color.FromArgb(52,101,208)))g.FillPath(fill,shape);
            using(var bubble=new GraphicsPath()){
                bubble.AddBezier(63,69,74,56,174,56,192,73);bubble.AddBezier(192,73,205,91,205,156,187,170);bubble.AddBezier(187,170,172,180,119,180,105,178);bubble.AddLine(105,178,72,200);bubble.AddLine(72,200,78,169);bubble.AddBezier(78,169,54,158,49,91,63,69);bubble.CloseFigure();using(var brush=new SolidBrush(Color.White))g.FillPath(brush,bubble);
            }
            var points=new[]{new PointF(132,78),new PointF(145,107),new PointF(174,120),new PointF(145,133),new PointF(132,161),new PointF(119,133),new PointF(90,120),new PointF(119,107)};
            using(var fill=new SolidBrush(Color.FromArgb(255,179,71)))g.FillPolygon(fill,points);
            using(var fill=new SolidBrush(Color.FromArgb(220,237,255)))g.FillEllipse(fill,185,39,19,19);
            g.Restore(state);
        }
    }
    class SparkBrandBadge:Control
    {
        public SparkBrandBadge(){DoubleBuffered=true;SetStyle(ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
        protected override void OnPaint(PaintEventArgs e){SparkBrand.Paint(e.Graphics,new RectangleF(0,0,Width,Height));}
    }
}
