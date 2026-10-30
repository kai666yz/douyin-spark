using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DouyinSpark
{
    class GroupHistoryForm
    {
        public static void Open(IWin32Window owner,string root)
        {
            using(var form=new ThemedForm{Text="群聊发送记录",Size=new Size(850,530),StartPosition=FormStartPosition.CenterParent}){
                var grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AutoGenerateColumns=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};foreach(string name in new[]{"时间","群聊","状态","说明"})grid.Columns.Add(name,name);
                grid.CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e){if(e.RowIndex<0||e.ColumnIndex!=2)return;var record=grid.Rows[e.RowIndex].Tag as GroupAttempt;if(record==null)return;e.Value="●  "+Convert.ToString(e.Value);e.CellStyle.ForeColor=record.status=="CONFIRMED"||record.status=="OK"?AppPalette.Success:record.status=="SUBMITTED"||record.status=="UNCONFIRMED"?AppPalette.Warning:record.status=="CANCELLED"?AppPalette.Muted:AppPalette.Error;e.FormattingApplied=true;};
                Action reload=delegate{grid.Rows.Clear();foreach(var item in GroupJournal.Read(root).Take(300)){int i=grid.Rows.Add(item.time,item.name,item.status=="SUBMITTED"?"已提交待核对":item.status=="CONFIRMED"?"已核对送达":item.status=="OK"?"页面已确认":item.status=="UNCONFIRMED"?"结果不确定":item.status=="CANCELLED"?"已取消":item.status=="STOPPED"?"已暂停":"未提交",item.note);grid.Rows[i].Tag=item;}};
                var tools=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=62,Padding=new Padding(12)};var ok=new RoundButton{Text="已核对送达",Size=new Size(140,36)};var no=new RoundButton{Text="已核对未送达",Primary=false,Size=new Size(150,36)};var export=new RoundButton{Text="导出CSV",Primary=false,Size=new Size(120,36)};
                try{using(var check=GroupJournal.Acquire(root)){};}catch(InvalidOperationException){ok.Enabled=no.Enabled=false;}
                Action<bool> mark=delegate(bool delivered){if(grid.CurrentRow==null)return;var entry=grid.CurrentRow.Tag as GroupAttempt;if(entry==null)return;if(MessageBox.Show(form,"请先在抖音网页核对该条消息。确认标为“"+(delivered?"已送达":"未送达，可手动重试")+"”？","核对发送结果",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;entry.status=delivered?"CONFIRMED":"FAILED";entry.note="用户在网页核对后手动标记";GroupJournal.Write(root,entry);reload();};ok.Click+=delegate{mark(true);};no.Click+=delegate{mark(false);};
                export.Click+=delegate{using(var dialog=new SaveFileDialog{Filter="CSV文件|*.csv",FileName="群聊记录.csv"})if(dialog.ShowDialog(form)==DialogResult.OK){Func<string,string> cell=delegate(string s){s=s??"";if(s.Length>0&&"=+-@\t\r".IndexOf(s[0])>=0)s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";};var lines=new List<string>{"时间,群聊,状态,说明"};lines.AddRange(GroupJournal.Read(root).Select(x=>string.Join(",",new[]{x.time,x.name,x.status,x.note}.Select(cell).ToArray())));File.WriteAllLines(dialog.FileName,lines,new UTF8Encoding(true));}};
                tools.Controls.AddRange(new Control[]{ok,no,export});form.Controls.Add(grid);form.Controls.Add(tools);reload();form.ShowDialog(owner);
            }
        }
    }
}
