using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DouyinSpark
{
    static class ContactSelection
    {
        public static HashSet<string> SentToday(string root)
        {
            return new HashSet<string>(SparkStorage.ReadCsv(System.IO.Path.Combine(root,"logs","spark_log.csv"))
                .Where(x=>x.Length>=5&&x[0].StartsWith(DateTime.Today.ToString("yyyy-MM-dd"))&&(x[4]=="OK"||x[4]=="UNCONFIRMED"))
                .Select(x=>x[2].TrimEnd('/')));
        }
        public static bool Unsent(string url,HashSet<string> sent){return FriendRecord.ValidUrl(url)&&!sent.Contains(url.TrimEnd('/'));}
        public static Control Toolbar(DataGridView grid,Action changed,Func<DataGridViewRow,bool> unsent)
        {
            var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};
            Action<Func<DataGridViewRow,bool>> select=predicate=>{grid.EndEdit();foreach(DataGridViewRow row in grid.Rows)row.Cells["pick"].Value=predicate(row);changed();};
            foreach(var label in new[]{"全选","全不选","仅选今日未发送"}){
                var b=new RoundButton{Text=label,Primary=false,Size=new Size(label.Length>4?170:90,36),Font=AppPalette.Font(9)};
                string action=label;b.Click+=delegate{select(action=="全选"?(Func<DataGridViewRow,bool>)(x=>true):action=="全不选"?x=>false:unsent);};bar.Controls.Add(b);
            }
            grid.CellClick+=delegate(object sender,DataGridViewCellEventArgs e){if(e.RowIndex<0||e.ColumnIndex<0)return;var cell=grid.Rows[e.RowIndex].Cells[e.ColumnIndex];if(e.ColumnIndex!=grid.Columns["pick"].Index&&cell.ReadOnly){var pick=grid.Rows[e.RowIndex].Cells["pick"];pick.Value=!Convert.ToBoolean(pick.Value??false);changed();}};
            grid.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.A&&!(grid.EditingControl is TextBoxBase)){select(x=>true);e.Handled=true;e.SuppressKeyPress=true;}};
            grid.RowTemplate.Height=44;grid.CellBorderStyle=DataGridViewCellBorderStyle.None;grid.AllowUserToResizeRows=false;
            return bar;
        }
    }
}
