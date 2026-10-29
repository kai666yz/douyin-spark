using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace DouyinSpark
{
    class SearchInput:TextBox
    {
        protected override void WndProc(ref Message message){base.WndProc(ref message);if(message.Msg==0xF&&Text.Length==0&&!Focused)using(var g=Graphics.FromHwnd(Handle))TextRenderer.DrawText(g,"搜索联系人或群聊",Font,ClientRectangle,AppPalette.Muted,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);}
    }
    class RosterList:ListBox
    {
        public RosterList(){DrawMode=DrawMode.OwnerDrawFixed;ItemHeight=76;IntegralHeight=false;BorderStyle=BorderStyle.None;DoubleBuffered=true;}
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if(e.Index<0||e.Index>=Items.Count)return;var item=(RosterItem)Items[e.Index];bool selected=(e.State&DrawItemState.Selected)!=0;
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;var r=e.Bounds;
            using(var b=new SolidBrush(selected?AppPalette.Soft:AppPalette.Surface))g.FillRectangle(b,r);
            if(selected)using(var b=new SolidBrush(AppPalette.Accent))g.FillRectangle(b,r.Left,r.Top+15,3,r.Height-30);
            var avatar=new RectangleF(r.Left+17,r.Top+15,46,46);
            using(var p=AppPalette.Round(avatar,13))using(var b=new SolidBrush(item.Kind=="group"?AppPalette.SuccessSoft:AppPalette.Soft))g.FillPath(b,p);
            if(item.Kind=="group")LineIcons.Draw(g,"people",new Rectangle((int)avatar.X+11,(int)avatar.Y+11,24,24),AppPalette.Success);
            else using(var f=AppPalette.Font(15,true))using(var b=new SolidBrush(AppPalette.Accent))g.DrawString(string.IsNullOrEmpty(item.Name)?"友":StringInfo.GetNextTextElement(item.Name),f,b,avatar,new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center});
            using(var f=AppPalette.Font(10,true))using(var b=new SolidBrush(AppPalette.Text))g.DrawString(item.Name??"未命名",f,b,new RectangleF(r.Left+78,r.Top+16,Math.Max(50,r.Width-112),24),new StringFormat {Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap});
            using(var f=AppPalette.Font(9))using(var b=new SolidBrush(AppPalette.Muted))g.DrawString(item.Subtitle,f,b,new RectangleF(r.Left+78,r.Top+43,Math.Max(50,r.Width-100),22),new StringFormat {Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap});
            if(item.Status=="OK"||item.Status=="UNCONFIRMED")using(var b=new SolidBrush(item.Status=="OK"?AppPalette.Success:AppPalette.Warning))g.FillEllipse(b,r.Right-23,r.Top+24,6,6);
        }
    }
}
