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
    partial class Program
    {
        static string ConversationsPath {get{return Path.Combine(BaseDir,"conversations.json");}}
        static void StartConversationImport()
        {
            if(!btnStart.Enabled || importingFollowing!=0||groupTaskOpen){MessageBox.Show(mainForm,"请先结束发送，并关闭群聊网页窗口后再导入会话。","导入会话");return;}
            importingFollowing=1;
            try{using(var dialog=new ConversationImportForm(BaseDir)){dialog.ShowDialog(mainForm);}LoadLogs();RefreshRoster();}
            catch(Exception ex){MessageBox.Show(mainForm,"会话导入未完成，原名单已保留。\n"+ex.Message,"导入会话");}
            finally{importingFollowing=0;}
        }
    }

    class ConversationImportForm:ThemedForm
    {
        readonly string root;
        readonly Dictionary<string,ConversationRecord> candidates=new Dictionary<string,ConversationRecord>();
        readonly SplitContainer split;
        readonly DataGridView grid;
        readonly Label status;
        readonly RoundButton read,login;
        WebView2 web;
        bool initializing,busy;
        public ConversationImportForm(string dataRoot)
        {
            root=dataRoot;Text="从私信导入好友与群聊";StartPosition=FormStartPosition.CenterParent;
            MinimumSize=new Size(1000,660);Size=new Size(Math.Min(1320,Screen.PrimaryScreen.WorkingArea.Width-40),Math.Min(840,Screen.PrimaryScreen.WorkingArea.Height-40));
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=4};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,72));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
            var header=new Panel {Dock=DockStyle.Fill};
            var title=new Label {Text="选择你想保持联系的人",Font=AppPalette.Font(17,true),AutoSize=true,Location=new Point(0,0)};
            var subtitle=new Label {Text="复用软件内登录；只读取会话名称与标识。群聊导入后，可在消息任务中独立设置。",Font=AppPalette.Font(9),AutoSize=true,Location=new Point(1,38),Tag="muted"};
            header.Controls.AddRange(new Control[]{title,subtitle});layout.Controls.Add(header,0,0);
            var tools=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
            login=new RoundButton {Text="登录 / 刷新",Primary=false,Size=new Size(120,36),Margin=new Padding(0,0,8,0)};
            read=new RoundButton {Text="读取当前会话",Size=new Size(140,36),Margin=new Padding(0,0,8,0)};
            var scroll=new RoundButton {Text="加载更多",Primary=false,Size=new Size(104,36),Margin=new Padding(0,0,8,0)};
            var hide=new RoundButton {Text="收起网页",Primary=false,Size=new Size(104,36)};
            login.Click+=async delegate{await OpenWeb();};read.Click+=async delegate{await ReadCurrent();};
            scroll.Click+=async delegate{await LoadMore();};
            hide.Click+=delegate{split.Panel1Collapsed=!split.Panel1Collapsed;};tools.Controls.AddRange(new Control[]{login,read,scroll,hide});layout.Controls.Add(tools,0,1);
            split=new SplitContainer {Dock=DockStyle.Fill,Orientation=Orientation.Vertical,SplitterWidth=8,Panel1Collapsed=true};
            grid=new DataGridView {Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,EditMode=DataGridViewEditMode.EditOnEnter};
            grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="pick",HeaderText="选择",AutoSizeMode=DataGridViewAutoSizeColumnMode.None,Width=52});
            grid.Columns.Add(new DataGridViewComboBoxColumn {Name="kind",HeaderText="类型",FillWeight=24,DataSource=new[]{"私信","群聊","待确认"}});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name="name",HeaderText="名称",ReadOnly=true,FillWeight=40});
            grid.Columns.Add(new DataGridViewTextBoxColumn {Name="url",HeaderText="个人主页（可补填）",FillWeight=65});
            grid.CurrentCellDirtyStateChanged+=delegate{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            grid.CellValueChanged+=delegate(object s,DataGridViewCellEventArgs e){if(e.RowIndex>=0&&grid.Columns[e.ColumnIndex].Name=="pick")UpdateSelectionCount();};
            grid.DataError+=delegate(object s,DataGridViewDataErrorEventArgs e){e.ThrowException=false;};
            split.Panel2.Controls.Add(grid);layout.Controls.Add(split,0,2);
            var bottom=new Panel {Dock=DockStyle.Fill};status=new Label {Text="没有缓存时，请点“登录 / 刷新”，在软件内打开消息列表，再读取会话。",Location=new Point(0,12),Size=new Size(650,45),Font=AppPalette.Font(9),Tag="muted"};
            var save=new RoundButton {Text="导入选中",Size=new Size(128,40),Dock=DockStyle.Right,Margin=new Padding(0,12,0,0)};
            save.Click+=delegate{SaveSelection();};bottom.Controls.Add(status);bottom.Controls.Add(save);layout.Controls.Add(bottom,0,3);Controls.Add(layout);
            try{
                string path=Path.Combine(root,"conversation_candidates.json");
                if(File.Exists(path))foreach(var item in ConversationStore.Read(path))Merge(item);
                if(candidates.Count>0){RebuildGrid();status.Text="已载入上次读取的 "+candidates.Count+" 个会话，无需重新扫码。新会话请登录 / 刷新。";}
            }catch(Exception){status.Text="上次缓存无法读取，原文件已保留。请重新读取会话。";}
            FormClosed+=delegate{if(web!=null)web.Dispose();};
        }
        static bool Trusted(string address)
        {
            Uri uri;return Uri.TryCreate(address,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&(uri.Host=="douyin.com"||uri.Host.EndsWith(".douyin.com",StringComparison.OrdinalIgnoreCase));
        }
        async Task OpenWeb()
        {
            if(initializing)return;initializing=true;login.Enabled=false;
            try{
                split.Panel1Collapsed=false;split.SplitterDistance=Math.Max(440,split.Width*55/100);
                if(web==null){
                    status.Text="正在打开内嵌消息页面；首次可能需要扫码，此后保留软件内登录。";
                    web=new WebView2 {Dock=DockStyle.Fill};split.Panel1.Controls.Add(web);
                    var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"conversation_profile"));
                    await web.EnsureCoreWebView2Async(environment);
                    web.CoreWebView2.Settings.AreDevToolsEnabled=false;web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;
                    web.CoreWebView2.Settings.IsStatusBarEnabled=false;web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled=false;
                    web.CoreWebView2.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e){if(!Trusted(e.Uri))e.Cancel=true;};
                    web.CoreWebView2.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;if(Trusted(e.Uri))web.CoreWebView2.Navigate(e.Uri);};
                    web.CoreWebView2.DownloadStarting+=delegate(object s,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};
                    web.CoreWebView2.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
                    web.CoreWebView2.WebResourceResponseReceived+=OnResponse;
                    web.CoreWebView2.NavigationCompleted+=delegate(object s,CoreWebView2NavigationCompletedEventArgs e){status.Text=e.IsSuccess?"请在网页中打开“消息”。登录有效时无需扫码；加载后点“读取当前会话”。":"页面加载未完成，请检查网络后刷新；已选名单不受影响。";};
                }
                web.CoreWebView2.Navigate("https://www.douyin.com/");
            }catch(Exception ex){
                status.Text="内嵌网页未能启动，可继续使用关注列表或手动添加。";
                if(web!=null){web.Dispose();web=null;}
                MessageBox.Show(this,"内嵌页面需要 Microsoft Edge WebView2 Runtime。若已安装，请检查网络后重试。\n\n"+ex.Message,"打开消息页面");
            }finally{initializing=false;login.Enabled=true;}
        }
        async void OnResponse(object sender,CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            string uri=e.Request.Uri;
            if(!Trusted(uri)||!(uri.IndexOf("conversation",StringComparison.OrdinalIgnoreCase)>=0))return;
            try{
                if(!e.Response.Headers.Contains("Content-Type")||e.Response.Headers.GetHeader("Content-Type").IndexOf("json",StringComparison.OrdinalIgnoreCase)<0)return;
                using(var stream=await e.Response.GetContentAsync())using(var reader=new StreamReader(stream,Encoding.UTF8)){
                    char[] buffer=new char[2*1024*1024+1];int used=0,n;
                    while(used<buffer.Length&&(n=await reader.ReadAsync(buffer,used,buffer.Length-used))>0)used+=n;
                    if(used>2*1024*1024)return;
                    foreach(var item in ConversationStore.FromPayload(new string(buffer,0,used)))Merge(item);
                }
                // Rebuild only on explicit read so existing checkmarks/edits are not lost.
            }catch(Exception){status.Text="部分会话数据无法解析，可尝试“读取当前会话”。原名单已保留。";}
        }
        void Merge(ConversationRecord item)
        {
            item=ConversationRecord.Normalize(item);ConversationRecord old;
            if(candidates.TryGetValue(item.id,out old)){
                if(item.kind=="unknown"&&old.kind!="unknown")item.kind=old.kind;
                if(item.url.Length==0&&old.kind!="group")item.url=old.url;
            }
            candidates[item.id]=item;
        }
        async Task ReadCurrent()
        {
            if(busy||initializing)return;
            if(web==null)await OpenWeb();
            if(web==null||web.CoreWebView2==null)return;busy=true;read.Enabled=false;
            try{
                string script=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","read_conversations.js"),Encoding.UTF8);
                string openScript=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","open_messages.js"),Encoding.UTF8);
                string diagnostic="";int found=0;bool requested=false;
                status.Text="正在打开消息列表并读取联系人，请稍候…";
                for(int attempt=0;attempt<16;attempt++){
                    if(IsDisposed||web==null||web.CoreWebView2==null)return;
                    if(web.Source==null||!Trusted(web.Source.AbsoluteUri)){await Task.Delay(1000);continue;}
                    // Read mounted IM data first. A toolbar click can hide the popup.
                    string json=await web.ExecuteScriptAsync(script);
                    var items=SparkStorage.Json().Deserialize<List<ConversationRecord>>(json)??new List<ConversationRecord>();
                    foreach(var item in items)try{Merge(item);found++;}catch(InvalidDataException){}
                    diagnostic=await web.ExecuteScriptAsync("JSON.stringify(window.__sparkConversationReadReport || {})");
                    if(found>0)break;
                    if(!requested){string result=await web.ExecuteScriptAsync(openScript);requested=result.Contains("requested")||result.Contains("open");}
                    await Task.Delay(1000);
                }
                SparkStorage.AppendLog(Path.Combine(root,"logs","conversation_read.log"),DateTime.Now.ToString("s")+" | this_read="+found+" | "+diagnostic+Environment.NewLine);
                RebuildGrid();
                if(found==0){
                    status.Text=candidates.Count>0?"本次未读取到新数据，右侧保留上次列表。请确认网页已登录，再重试。":"尚未读取到联系人。请在左侧完成登录，再点“读取当前会话”。";
                    MessageBox.Show(this,"本次未取得可选择的联系人。\n请确认左侧已经登录，且可以看到消息列表，再重试读取。\n原名单和缓存均已保留；诊断记录位于 logs/conversation_read.log。","会话尚未读取",MessageBoxButtons.OK,MessageBoxIcon.Information);return;
                }
                SparkStorage.WriteJson(Path.Combine(root,"conversation_candidates.json"),candidates.Values.ToList());
                status.Text="已读取 "+candidates.Count+" 个会话；单击右侧“选择”复选框，再点“导入选中”。加载更多可继续读取。";
            }catch(Exception ex){status.Text="读取未完成，原名单已保留。";MessageBox.Show(this,ex.Message,"读取会话");}
            finally{busy=false;read.Enabled=true;}
        }
        async Task LoadMore()
        {
            if(busy||initializing)return;
            if(web==null){await ReadCurrent();return;}
            if(web.CoreWebView2==null)return;
            try{
                string open=File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","open_messages.js"),Encoding.UTF8);
                await web.ExecuteScriptAsync(open);await Task.Delay(500);
                string result=await web.ExecuteScriptAsync("(() => {const root=document.querySelector('[data-e2e=im-dialog]');if(!root)return false;const rows=[...root.querySelectorAll('[data-e2e=conversation-item]')];const nodes=[...root.querySelectorAll('[class*=ConversationList], [class*=conversationList], [class*=scroll], [class*=Scroll]')];for(const row of rows){let p=row.parentElement;for(let i=0;p&&i<6;i++,p=p.parentElement)nodes.push(p);}const el=nodes.find(x=>x.clientHeight>60&&x.scrollHeight>x.clientHeight+20&&/auto|scroll/.test(getComputedStyle(x).overflowY));if(!el)return false;el.scrollBy(0,Math.max(400,el.clientHeight*.8));return true;})()");
                if(result=="false")status.Text="未找到可滚动的消息列表；可在左侧手动向下滚动后再次读取。";
                await Task.Delay(900);await ReadCurrent();
            }catch(Exception ex){status.Text="加载更多未完成："+ex.Message;}
        }
        void UpdateSelectionCount()
        {
            if(status==null)return;int count=grid.Rows.Cast<DataGridViewRow>().Count(row=>Convert.ToBoolean(row.Cells["pick"].Value??false));
            status.Text="已选择 "+count+" 个会话；确认后点右下角“导入选中”。";
        }
        void RebuildGrid()
        {
            var edits=new Dictionary<string,object[]>();foreach(DataGridViewRow row in grid.Rows)if(row.Tag!=null)edits[Convert.ToString(row.Tag)]=new[]{row.Cells["pick"].Value,row.Cells["kind"].Value,row.Cells["url"].Value};
            grid.Rows.Clear();foreach(var item in candidates.Values.OrderBy(x=>x.name)){
                object[] old;bool found=edits.TryGetValue(item.id,out old);
                int i=grid.Rows.Add(found?old[0]:false,found?old[1]:item.kind=="group"?"群聊":item.kind=="private"?"私信":"待确认",item.name,found?old[2]:item.url);grid.Rows[i].Tag=item.id;
            }
            AppPalette.StyleGrid(grid);
        }
        void SaveSelection()
        {
            grid.EndEdit();var selected=new List<ConversationRecord>();
            try{
                foreach(DataGridViewRow row in grid.Rows){
                    if(!Convert.ToBoolean(row.Cells["pick"].Value??false))continue;
                    var source=candidates[Convert.ToString(row.Tag)];string kind=Convert.ToString(row.Cells["kind"].Value);
                    if(kind!="私信"&&kind!="群聊")throw new InvalidDataException("请先确认“"+source.name+"”是私信还是群聊。");
                    string url=Convert.ToString(row.Cells["url"].Value).Trim();
                    if(kind=="私信"&&url.Length>0&&!FriendRecord.ValidUrl(url))throw new InvalidDataException("个人主页格式不正确，请填写完整抖音个人主页链接。");
                    selected.Add(ConversationRecord.Normalize(new ConversationRecord {name=source.name,kind=kind=="群聊"?"group":"private",url=url,conversation_id=source.conversation_id}));
                }
                if(selected.Count==0){status.Text="请先勾选要加入名单的会话。";return;}
                // Validate both existing stores before writing either one.
                ConversationStore.Read(Path.Combine(root,"conversations.json"));SparkStorage.ReadFriends(Path.Combine(root,"friends.json"));
                int count=ConversationStore.Merge(Path.Combine(root,"conversations.json"),selected);
                var privateFriends=selected.Where(x=>x.CanSend).Select(x=>new FriendRecord {name=x.name,url=x.url}).ToList();
                if(privateFriends.Count>0)SparkStorage.AddFriends(Path.Combine(root,"friends.json"),privateFriends);
                MessageBox.Show(this,"已加入 "+count+" 个新会话。\n群聊请在消息任务中独立设置；待补主页的私信暂不发送。导入本身不会发送消息。","导入完成");Close();
            }catch(Exception ex){MessageBox.Show(this,ex.Message,"未完成导入");}
        }
    }
}
