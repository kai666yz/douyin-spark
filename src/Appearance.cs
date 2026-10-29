using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DouyinSpark
{
    static class AppPalette
    {
        public static bool Dark;
        public static Color Canvas {get{return Dark?Color.FromArgb(33,30,34):Color.FromArgb(255,247,249);}}
        public static Color Sidebar {get{return Dark?Color.FromArgb(40,35,41):Color.FromArgb(249,244,244);}}
        public static Color Surface {get{return Dark?Color.FromArgb(47,42,48):Color.White;}}
        public static Color Soft {get{return Dark?Color.FromArgb(65,49,59):Color.FromArgb(248,234,239);}}
        public static Color Stripe {get{return Dark?Color.FromArgb(51,46,52):Color.FromArgb(252,249,250);}}
        public static Color Accent {get{return Dark?Color.FromArgb(213,161,178):Color.FromArgb(164,94,118);}}
        public static Color AccentDeep {get{return Dark?Color.FromArgb(237,192,207):Color.FromArgb(144,78,101);}}
        public static Color Text {get{return Dark?Color.FromArgb(243,234,238):Color.FromArgb(58,46,51);}}
        public static Color Muted {get{return Dark?Color.FromArgb(190,170,181):Color.FromArgb(138,112,120);}}
        public static Color Border {get{return Dark?Color.FromArgb(71,61,70):Color.FromArgb(238,227,231);}}
        public static Color Success {get{return Dark?Color.FromArgb(145,180,157):Color.FromArgb(70,119,86);}}
        public static Color SuccessSoft {get{return Dark?Color.FromArgb(47,70,57):Color.FromArgb(222,240,228);}}
        public static Color SuccessBar {get{return Color.FromArgb(168,216,185);}}
        public static Color Error {get{return Dark?Color.FromArgb(227,155,162):Color.FromArgb(175,81,96);}}
        public static Color ErrorSoft {get{return Dark?Color.FromArgb(79,49,58):Color.FromArgb(250,233,236);}}
        public static Color Warning {get{return Dark?Color.FromArgb(216,186,132):Color.FromArgb(149,111,54);}}
        public static Color WarningSoft {get{return Dark?Color.FromArgb(72,61,43):Color.FromArgb(249,240,221);}}
        public static Font Font(float size=10,bool bold=false) {return new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular);}
        public static GraphicsPath Round(RectangleF box,float radius)
        {
            float r=Math.Min(radius,Math.Min(box.Width,box.Height)/2),d=r*2;
            var path=new GraphicsPath();
            if(d<=0) {path.AddRectangle(box);return path;}
            path.AddArc(box.X,box.Y,d,d,180,90);path.AddArc(box.Right-d,box.Y,d,d,270,90);
            path.AddArc(box.Right-d,box.Bottom-d,d,d,0,90);path.AddArc(box.X,box.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        public static void Apply(Control root)
        {
            bool own=root is OverviewCard || root is CardSurface || root is EmptyHistory || root is RoundButton || root is SparkProgress;
            if(root is Form) {root.BackColor=Canvas;root.ForeColor=Text;WindowChrome.Apply((Form)root);}
            else if(root is Label) {root.ForeColor=Convert.ToString(root.Tag)=="muted"?Muted:Text;root.BackColor=Color.Transparent;}
            else if(root is TextBoxBase || root is ListBox) {root.BackColor=Surface;root.ForeColor=Text;}
            else if(root is DataGridView) StyleGrid((DataGridView)root);
            else if(root is CheckBox || root is RadioButton) {root.ForeColor=Text;root.BackColor=Color.Transparent;}
            else if(root is Button && !own) {var b=(Button)root;b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Border;b.BackColor=Soft;b.ForeColor=Text;}
            else if(root is Panel || root is TableLayoutPanel || root is FlowLayoutPanel)
                root.BackColor=Convert.ToString(root.Tag)=="sidebar"?Sidebar:Convert.ToString(root.Tag)=="canvas"?Canvas:Surface;
            if(root is CardSurface) root.BackColor=Canvas;
            if(root is OverviewCard || root is SparkProgress) root.BackColor=Canvas;
            if(root is EmptyHistory) root.BackColor=Surface;
            foreach(Control child in root.Controls) Apply(child);
            root.Invalidate();
        }
        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor=Surface;grid.BorderStyle=BorderStyle.None;grid.CellBorderStyle=DataGridViewCellBorderStyle.None;
            grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;grid.RowHeadersVisible=false;grid.EnableHeadersVisualStyles=false;
            grid.ColumnHeadersHeight=38;grid.ColumnHeadersDefaultCellStyle.BackColor=Soft;grid.ColumnHeadersDefaultCellStyle.ForeColor=Text;
            grid.ColumnHeadersDefaultCellStyle.Font=Font(9,true);grid.ColumnHeadersDefaultCellStyle.Padding=new Padding(12,0,0,0);
            grid.DefaultCellStyle.BackColor=Surface;grid.DefaultCellStyle.ForeColor=Text;grid.DefaultCellStyle.SelectionBackColor=Soft;grid.DefaultCellStyle.SelectionForeColor=Text;
            grid.DefaultCellStyle.Font=Font(9);grid.DefaultCellStyle.Padding=new Padding(12,0,0,0);
            grid.RowsDefaultCellStyle.BackColor=Surface;grid.AlternatingRowsDefaultCellStyle.BackColor=Stripe;
            grid.RowTemplate.Height=34;foreach(DataGridViewRow row in grid.Rows) row.Height=34;
        }
    }

    static class WindowChrome
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        public static void Apply(Form form)
        {
            if(!form.IsHandleCreated)return;
            try {int round=2;DwmSetWindowAttribute(form.Handle,33,ref round,4);int dark=AppPalette.Dark?1:0;DwmSetWindowAttribute(form.Handle,20,ref dark,4);}catch{}
        }
    }
    class ThemedForm:Form
    {
        protected override void OnShown(EventArgs e){AppPalette.Apply(this);base.OnShown(e);}
    }
    class CardSurface:Panel
    {
        public CardSurface(){DoubleBuffered=true;Padding=new Padding(16);BackColor=AppPalette.Canvas;}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(AppPalette.Canvas);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=AppPalette.Round(new RectangleF(.5f,.5f,Width-1,Height-1),16))
            using(var brush=new SolidBrush(AppPalette.Surface))
            using(var pen=new Pen(AppPalette.Border)){e.Graphics.FillPath(brush,path);e.Graphics.DrawPath(pen,path);}
        }
    }

    static class LineIcons
    {
        public static void Draw(Graphics g,string name,Rectangle box,Color color)
        {
            var state=g.Save();g.TranslateTransform(box.Left,box.Top);g.ScaleTransform(box.Width/24f,box.Height/24f);
            using(var pen=new Pen(color,1.7f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            using(var brush=new SolidBrush(color))
            {
                if(name=="people") {g.DrawEllipse(pen,5,3,7,7);g.DrawArc(pen,2,11,14,13,180,180);g.DrawArc(pen,13,4,7,7,270,180);g.DrawArc(pen,14,12,8,11,270,180);}
                else if(name=="stats") {g.DrawLine(pen,4,20,21,20);g.DrawRectangle(pen,5,12,3,7);g.DrawRectangle(pen,11,5,3,14);g.DrawRectangle(pen,17,9,3,10);}
                else if(name=="chat") {g.DrawRectangle(pen,3,4,18,13);g.DrawLines(pen,new[]{new Point(6,17),new Point(6,21),new Point(11,17)});g.DrawLine(pen,7,8,17,8);g.DrawLine(pen,7,12,14,12);}
                else if(name=="more") {for(int i=0;i<3;i++)g.FillEllipse(brush,4+i*7,10,3,3);}
                else if(name=="home") {g.DrawLines(pen,new[]{new Point(3,11),new Point(12,3),new Point(21,11)});g.DrawLines(pen,new[]{new Point(6,10),new Point(6,21),new Point(18,21),new Point(18,10)});g.DrawRectangle(pen,10,14,4,7);}
                else if(name=="play") g.FillPolygon(brush,new[]{new Point(7,4),new Point(20,12),new Point(7,20)});
                else if(name=="moon") {g.DrawArc(pen,4,3,16,18,65,280);g.DrawArc(pen,9,1,14,15,75,145);}
                else if(name=="download") {g.DrawLine(pen,12,3,12,15);g.DrawLines(pen,new[]{new Point(7,10),new Point(12,15),new Point(17,10)});g.DrawLines(pen,new[]{new Point(4,16),new Point(4,21),new Point(20,21),new Point(20,16)});}
                else {g.DrawArc(pen,4,4,16,16,35,290);g.DrawLines(pen,new[]{new Point(17,2),new Point(21,5),new Point(17,8)});}
            }
            g.Restore(state);
        }
    }
}
