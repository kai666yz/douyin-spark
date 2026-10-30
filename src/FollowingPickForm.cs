using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DouyinSpark
{
    class FollowingPickForm:ThemedForm
    {
        readonly DataGridView grid;
        readonly Label count;
        public FollowingPickForm(string root,List<string[]> items,HashSet<string> existing)
        {
            Text="选择要添加的好友";Size=new Size(740,650);MinimumSize=new Size(660,520);StartPosition=FormStartPosition.CenterParent;KeyPreview=true;
            var page=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=5};
            page.RowStyles.Add(new RowStyle(SizeType.Absolute,64));page.RowStyles.Add(new RowStyle(SizeType.Absolute,46));page.RowStyles.Add(new RowStyle(SizeType.Percent,100));page.RowStyles.Add(new RowStyle(SizeType.Absolute,32));page.RowStyles.Add(new RowStyle(SizeType.Absolute,52));Controls.Add(page);
            page.Controls.Add(new Label{Text="选择好友\r\n点击整行勾选；Ctrl+A 全选。已发送或待确认的好友不会被“今日未发送”选中。",Dock=DockStyle.Fill,Font=AppPalette.Font(10)},0,0);
            grid=new DataGridView{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,EditMode=DataGridViewEditMode.EditOnEnter};
            grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="pick",HeaderText="选择",Width=58,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="name",HeaderText="好友名称",ReadOnly=true});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="state",HeaderText="当前状态",ReadOnly=true});
            var sent=ContactSelection.SentToday(root);
            foreach(var item in items){int i=grid.Rows.Add(false,item[0],sent.Contains(item[1].TrimEnd('/'))?"今日已发送 / 待确认":existing.Contains(item[1].TrimEnd('/'))?"已在名单":"待添加");grid.Rows[i].Tag=item[1];}
            count=new Label{Dock=DockStyle.Fill,Font=AppPalette.Font(10,true),Padding=new Padding(0,6,0,0)};
            page.Controls.Add(ContactSelection.Toolbar(grid,UpdateCount,row=>ContactSelection.Unsent(Convert.ToString(row.Tag),sent)),0,1);page.Controls.Add(grid,0,2);page.Controls.Add(count,0,3);
            grid.CurrentCellDirtyStateChanged+=delegate{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};grid.CellValueChanged+=delegate{UpdateCount();};
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.A){grid.EndEdit();foreach(DataGridViewRow row in grid.Rows)row.Cells["pick"].Value=true;UpdateCount();e.Handled=true;e.SuppressKeyPress=true;}};
            var save=new RoundButton{Text="导入选中好友",Dock=DockStyle.Right,Width=170};save.Click+=delegate{try{
                grid.EndEdit();var selected=grid.Rows.Cast<DataGridViewRow>().Where(x=>Convert.ToBoolean(x.Cells["pick"].Value??false)).Select(x=>new FriendRecord{name=Convert.ToString(x.Cells["name"].Value),url=Convert.ToString(x.Tag)}).ToList();
                if(selected.Count==0){count.Text="请先选择要添加的好友。";return;}int n=SparkStorage.AddFriends(System.IO.Path.Combine(root,"friends.json"),selected);
                SparkStorage.AppendLog(System.IO.Path.Combine(root,"logs","selection.log"),DateTime.Now.ToString("s")+" | following_selected="+selected.Count+" | added="+n+Environment.NewLine);
                MessageBox.Show(this,"已添加 "+n+" 位新好友，重复主页已跳过。","导入完成");Close();
            }catch(Exception ex){MessageBox.Show(this,ex.Message,"导入失败，原名单保留");}};page.Controls.Add(save,0,4);AppPalette.StyleGrid(grid);UpdateCount();
        }
        void UpdateCount(){if(count!=null)count.Text="已选 "+grid.Rows.Cast<DataGridViewRow>().Count(x=>Convert.ToBoolean(x.Cells["pick"].Value??false))+" / "+grid.Rows.Count+" 人";}
    }
}
