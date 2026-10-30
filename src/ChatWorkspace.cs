using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        static RosterList roster;
        static TextBox rosterSearch,detailPreview;
        static Label rosterCount,rosterEmpty,detailName,detailKind,detailState,detailHint;
        static RoundButton completeProfile,filterAll,filterPrivate,filterGroup;
        static string rosterFilter="all",rosterSignature="";
        static bool refreshingRoster;
        static Form BuildChatWorkspace()
        {
            AppPalette.Dark=ConfigString("theme")=="dark";
            var form=new CardForm {Text="抖音续火花",Font=AppPalette.Font(),StartPosition=FormStartPosition.CenterScreen,MinimumSize=new Size(1120,740)};
            form.FormClosing+=delegate(object sender,FormClosingEventArgs e){if(exiting&&groupTaskRunning&&groupWindow!=null){e.Cancel=true;groupWindow.RequestStopAndClose();}else if(exiting&&groupWindow!=null)groupWindow.Close();if(exiting&&privateRunActive){e.Cancel=true;RequestStopSending();if(!privateStopRequested)exiting=false;}};
            try{form.Icon=new Icon(Path.Combine(ProgramDir,"icon.ico"));}catch{}
            var screen=Screen.PrimaryScreen.WorkingArea;form.Size=new Size(Math.Min(1320,screen.Width-30),Math.Min(880,screen.Height-30));mainForm=form;
            var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Tag="canvas",Padding=new Padding(0),Margin=Padding.Empty};
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,172));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            form.Controls.Add(shell);
            workspaceDeck=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty,Tag="canvas"};shell.Controls.Add(workspaceDeck,1,0);
            var contactColumns=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Tag="canvas"};contactColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));contactColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,350));contactsWorkspace=contactColumns;workspaceDeck.Controls.Add(contactColumns);
            var navPanel=new Panel {Dock=DockStyle.Fill,Padding=new Padding(12,24,12,12),Tag="sidebar",Margin=Padding.Empty};
            var logo=ViewLabel("Spark",18,true);logo.SetBounds(60,29,96,36);var brand=new SparkBrandBadge{Location=new Point(18,25),Size=new Size(34,34)};
            var subtitle=ViewLabel("每日问候 · v"+AppVersion,8,false,true);subtitle.SetBounds(21,69,145,22);
            var nav=new FlowLayoutPanel {Location=new Point(12,124),Size=new Size(150,350),FlowDirection=FlowDirection.TopDown,WrapContents=false,Tag="sidebar"};
            workspaceNavigation.Clear();
            Action<string,string,EventHandler,bool> add=delegate(string text,string icon,EventHandler action,bool selected){var b=new RoundButton {Text=text,IconName=icon,Navigation=true,Primary=false,Selected=selected,Size=new Size(148,44),Margin=new Padding(0,0,0,7)};b.Click+=action;nav.Controls.Add(b);workspaceNavigation[text]=b;};
            add("联系人","people",delegate{ShowWorkspace("联系人");RefreshRoster();},true);
            add("消息任务","chat",delegate{ShowWorkspace("消息任务");},false);
            add("群聊消息","people",delegate{OpenGroupTask();},false);
            actionsMenu=new ContextMenuStrip {Font=AppPalette.Font(10)};
            actionsMenu.Items.Add("手动添加 / 删除好友",null,delegate{AddFriend();LoadLogs();RefreshRoster();});
            actionsMenu.Items.Add("统一固定消息",null,delegate{ShowFixedMessage();RefreshDashboard();ShowRosterDetail();});
            actionsMenu.Items.Add("预览本轮消息",null,delegate{DoDry();});
            actionsMenu.Items.Add("定时设置",null,delegate{ShowScheduleDialog();RefreshDashboard();});
            actionsMenu.Items.Add("开机自启",null,delegate{ToggleAutostart();});
            actionsMenu.Items.Add("环境检查 / 数据位置",null,delegate{ShowEnvironment();});
            actionsMenu.Items.Add(new ToolStripSeparator());
            actionsMenu.Items.Add("传统发送浏览器",null,delegate{ShowBrowserDialog();});
            actionsMenu.Items.Add("传统浏览器登录自检",null,delegate{DoLogin();});
            actionsMenu.Items.Add("导出历史 CSV",null,delegate{ExportHistory();});actionsMenu.Items.Add("清理旧历史",null,delegate{ClearHistory();});
            add("设置","settings",delegate{ShowWorkspace("设置");},false);
            themeButton=new RoundButton {Text=AppPalette.Dark?"浅色模式":"深色模式",IconName="moon",Navigation=true,Primary=false,Size=new Size(148,38),Margin=new Padding(0,0,0,6)};themeButton.Click+=delegate{ToggleAppearance();roster.Invalidate();};
            var update=new RoundButton {Text="检查更新",IconName="refresh",Navigation=true,Primary=false,Size=new Size(148,38)};update.Click+=delegate{DoCheckUpdate();};
            lblStatus=ViewLabel("就绪 · 数据保存在本机",8,false,true);lblStatus.Size=new Size(144,40);lblStatus.Margin=new Padding(4,8,0,0);
            var footer=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=112,FlowDirection=FlowDirection.TopDown,WrapContents=false,Tag="sidebar"};
            footer.Controls.AddRange(new Control[]{themeButton,lblStatus});navPanel.Controls.AddRange(new Control[]{brand,logo,subtitle,nav,footer});shell.Controls.Add(navPanel,0,0);
            var middle=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(22,22,16,20),Tag="canvas",Margin=Padding.Empty};
            middle.RowStyles.Add(new RowStyle(SizeType.Absolute,68));middle.RowStyles.Add(new RowStyle(SizeType.Absolute,46));middle.RowStyles.Add(new RowStyle(SizeType.Absolute,158));middle.RowStyles.Add(new RowStyle(SizeType.Absolute,48));middle.RowStyles.Add(new RowStyle(SizeType.Percent,100));contactColumns.Controls.Add(middle,0,0);
            var heading=new Panel {Dock=DockStyle.Fill,Tag="canvas"};var title=ViewLabel("联系人",19,true);title.SetBounds(0,0,210,32);var desc=ViewLabel("私信与群聊，一处管理",9,false,true);desc.SetBounds(1,36,280,22);heading.Controls.AddRange(new Control[]{title,desc});middle.Controls.Add(heading,0,0);
            var todayTabs=new FlowLayoutPanel{Dock=DockStyle.Right,Width=206,WrapContents=false,Padding=new Padding(0,10,0,0),Tag="canvas"};todayPrivate=new RoundButton{Text="今日私信",Primary=false,Selected=true,Size=new Size(96,32),Font=AppPalette.Font(9)};todayGroups=new RoundButton{Text="今日群聊",Primary=false,Size=new Size(96,32),Font=AppPalette.Font(9)};todayPrivate.Click+=delegate{SetOverview(false);};todayGroups.Click+=delegate{SetOverview(true);};todayTabs.Controls.AddRange(new Control[]{todayPrivate,todayGroups});heading.Controls.Add(todayTabs);
            readiness=new ReadinessCard{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,6)};readiness.Click+=delegate{if(File.Exists(Path.Combine(BaseDir,".spark-safety-stop.json")))CheckSafetyResume(mainForm,true);else if(schedulePaused)ShowScheduleDialog();else if(GroupPaused)OpenGroupTask();else ShowEnvironment();RefreshEnvironmentBanner();};middle.Controls.Add(readiness,0,1);
            overview=new OverviewCard {Compact=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10)};
            btnStart=new RoundButton {Text="开始发送",IconName="play",Primary=true,CornerRadius=24,Font=AppPalette.Font(10,true)};btnStart.Click+=delegate{DoStart();};overview.StartButton=btnStart;overview.Controls.Add(btnStart);middle.Controls.Add(overview,0,2);
            btnGroup=new RoundButton{Text="群聊消息任务",IconName="chat",Primary=true,Font=AppPalette.Font(10,true),Visible=false,Bounds=btnStart.Bounds};btnGroup.Click+=delegate{OpenGroupTask();};overview.GroupButton=btnGroup;overview.Controls.Add(btnGroup);btnStop=new RoundButton{Text="停止发送",Danger=true,IconName="",Primary=true,Enabled=false,CornerRadius=12,Font=AppPalette.Font(9,true)};btnStop.Click+=delegate{RequestStopSending();};overview.StopButton=btnStop;overview.Controls.Add(btnStop);overview.PerformLayout();
            progress=new SparkProgress {Dock=DockStyle.Fill,Margin=new Padding(0,2,0,8)};middle.Controls.Add(progress,0,3);
            var listCard=new CardSurface {Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(8,12,8,8)};
            var listLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
            listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));listLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var listHead=new Panel {Dock=DockStyle.Fill};rosterCount=ViewLabel("我的名单",10,true);rosterCount.SetBounds(10,1,240,28);var import=new RoundButton {Text="＋ 导入",Size=new Size(88,30),Dock=DockStyle.Right,Primary=false};var importMenu=new ContextMenuStrip();importMenu.Items.Add("从私信 / 群聊导入",null,delegate{StartConversationImport();});importMenu.Items.Add("从关注列表导入",null,delegate{StartFollowingImport();});importMenu.Items.Add("手动添加 / 删除",null,delegate{AddFriend();LoadLogs();RefreshRoster();});import.Click+=delegate{importMenu.Show(import,new Point(0,import.Height));};listHead.Controls.Add(rosterCount);listHead.Controls.Add(import);listLayout.Controls.Add(listHead,0,0);
            var searchSurface=new Panel {Dock=DockStyle.Fill,Padding=new Padding(12,6,12,4),Margin=new Padding(12,3,12,7),Tag="soft"};
            rosterSearch=new SearchInput {Dock=DockStyle.Fill,Font=AppPalette.Font(10),BorderStyle=BorderStyle.None};rosterSearch.AccessibleName="搜索联系人或群聊";rosterSearch.TextChanged+=delegate{RefreshRoster();};searchSurface.Controls.Add(rosterSearch);listLayout.Controls.Add(searchSurface,0,1);
            var tabs=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(6,0,0,0)};
            filterAll=new RoundButton {Text="全部",Primary=false,Selected=true,Size=new Size(72,28),Margin=new Padding(3,0,4,0),Font=AppPalette.Font(9)};
            filterPrivate=new RoundButton {Text="私信",Primary=false,Size=new Size(72,28),Margin=new Padding(0,0,4,0),Font=AppPalette.Font(9)};
            filterGroup=new RoundButton {Text="群聊",Primary=false,Size=new Size(72,28),Font=AppPalette.Font(9)};
            filterAll.Click+=delegate{SetRosterFilter("all");};filterPrivate.Click+=delegate{SetRosterFilter("private");};filterGroup.Click+=delegate{SetRosterFilter("group");};tabs.Controls.AddRange(new Control[]{filterAll,filterPrivate,filterGroup});listLayout.Controls.Add(tabs,0,2);
            roster=new RosterList {Dock=DockStyle.Fill,Margin=Padding.Empty};roster.SelectedIndexChanged+=delegate{if(!refreshingRoster)ShowRosterDetail();};
            var rosterMenu=new ContextMenuStrip();rosterMenu.Items.Add("移出名单",null,delegate{RemoveRosterItem();});roster.ContextMenuStrip=rosterMenu;
            roster.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Right){int i=roster.IndexFromPoint(e.Location);if(i>=0)roster.SelectedIndex=i;}};
            var listHost=new Panel {Dock=DockStyle.Fill};rosterEmpty=ViewLabel("还没有联系人\r\n点击右上角“导入”选择私信或群聊",10,false,true);rosterEmpty.Dock=DockStyle.Fill;rosterEmpty.TextAlign=ContentAlignment.MiddleCenter;listHost.Controls.Add(roster);listHost.Controls.Add(rosterEmpty);listLayout.Controls.Add(listHost,0,3);listCard.Controls.Add(listLayout);middle.Controls.Add(listCard,0,4);
            var right=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(6,22,18,20),Margin=Padding.Empty,Tag="canvas"};
            right.RowStyles.Add(new RowStyle(SizeType.Absolute,138));right.RowStyles.Add(new RowStyle(SizeType.Absolute,292));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,108));contactColumns.Controls.Add(right,1,0);
            var identity=new CardSurface {Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10),Padding=new Padding(16)};
            detailName=ViewLabel("选择一个联系人",14,true);detailName.SetBounds(18,20,280,30);detailKind=ViewLabel("查看详情和消息预览",9,false,true);detailKind.SetBounds(19,59,275,24);detailState=ViewLabel("",9,false,true);detailState.SetBounds(19,91,275,24);identity.Controls.AddRange(new Control[]{detailName,detailKind,detailState});right.Controls.Add(identity,0,0);
            var preview=new CardSurface {Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10),Padding=new Padding(16)};
            privateMessageTitle=ViewLabel("私信固定消息",11,true);privateMessageTitle.SetBounds(18,16,270,28);
            privateMessageDraft=fixedMessage;privateMessageDraftLoaded=true;privateMessageSaveError=false;
            detailPreview=new TextBox {Text=privateMessageDraft,Multiline=true,AcceptsReturn=true,BorderStyle=BorderStyle.FixedSingle,Location=new Point(18,54),Size=new Size(274,104),Font=AppPalette.Font(10),ScrollBars=ScrollBars.Vertical,AccessibleName="私信固定消息编辑框",AccessibleDescription="输入消息后点击保存消息，手动和定时私信共用已保存内容"};
            detailPreview.TextChanged+=delegate{if(!syncingPrivateMessage&&!detailPreview.ReadOnly){privateMessageDraft=detailPreview.Text;privateMessageSaveError=false;RefreshPrivateMessageStatus();}};
            detailPreview.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.S&&!detailPreview.ReadOnly){e.SuppressKeyPress=true;SaveInlinePrivateMessage();}};
            detailHint=ViewLabel("保存后，手动和定时私信均固定发送此消息。",8,false,true);detailHint.SetBounds(18,167,274,31);
            privateMessageStatus=ViewLabel("",8,true);privateMessageStatus.SetBounds(18,203,274,24);privateMessageStatus.AccessibleName="消息保存状态";
            savePrivateMessage=new RoundButton {Text="保存消息",Primary=true,Location=new Point(18,236),Size=new Size(110,32),Font=AppPalette.Font(9)};savePrivateMessage.Click+=delegate{SaveInlinePrivateMessage();};
            completeProfile=new RoundButton {Text="更多消息设置",Primary=false,Location=new Point(136,236),Size=new Size(156,32),Font=AppPalette.Font(9)};completeProfile.Click+=delegate{var item=roster.SelectedItem as RosterItem;if(item!=null&&item.Kind=="group")OpenGroupTask(false,item.ConversationId);else if(item!=null&&!item.Sendable&&item.Kind=="private")CompleteRosterProfile(item);else{ShowFixedMessage();RefreshDashboard();ShowRosterDetail();}};
            preview.Controls.AddRange(new Control[]{privateMessageTitle,detailPreview,detailHint,privateMessageStatus,savePrivateMessage,completeProfile});right.Controls.Add(preview,0,1);
            var history=new CardSurface {Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10),Padding=new Padding(10)};var hHead=new Panel {Dock=DockStyle.Top,Height=32};var hTitle=ViewLabel("最近发送记录",10,true);hTitle.SetBounds(6,0,180,28);var export=SmallAction("导出","download",delegate{ExportHistory();});export.Dock=DockStyle.Right;hHead.Controls.Add(hTitle);hHead.Controls.Add(export);
            var tableBody=new Panel {Dock=DockStyle.Fill};dgv=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};dgv.DataBindingComplete+=delegate{FormatHistory();};dgv.CellPainting+=PaintHistoryStatus;
            dgv.CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e){if(e.RowIndex>=0&&e.ColumnIndex>=0&&dgv.Columns[e.ColumnIndex].Name=="时间"){DateTime stamp;if(DateTime.TryParse(Convert.ToString(e.Value),out stamp)){e.Value=stamp.Date==DateTime.Today?stamp.ToString("HH:mm"):stamp.ToString("MM-dd");e.FormattingApplied=true;}}};
            emptyHistory=new EmptyHistory();tableBody.Controls.Add(dgv);tableBody.Controls.Add(emptyHistory);history.Controls.Add(tableBody);history.Controls.Add(hHead);right.Controls.Add(history,0,2);
            var run=new CardSurface {Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(12)};var runTitle=ViewLabel("运行动态",9,true);runTitle.Dock=DockStyle.Top;runTitle.Height=26;txtLog=new RichTextBox {Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.None,Font=AppPalette.Font(8),Tag="placeholder",Text="先导入会话，再核对消息。"};run.Controls.Add(txtLog);run.Controls.Add(runTitle);right.Controls.Add(run,0,3);
            overviewTimer=new System.Windows.Forms.Timer {Interval=1000};overviewTimer.Tick+=delegate{RefreshSendButtons();if(form.Visible||!btnStart.Enabled){RefreshDashboard();if(!btnStart.Enabled)LoadLogs();}};overviewTimer.Start();form.Activated+=delegate{RefreshDashboard();};form.FormClosed+=delegate{overviewTimer.Dispose();actionsMenu.Dispose();};
            tasksWorkspace=BuildTasksWorkspace();settingsWorkspace=BuildSettingsWorkspace();workspaceDeck.Controls.Add(tasksWorkspace);workspaceDeck.Controls.Add(settingsWorkspace);ShowWorkspace("联系人");
            AppPalette.Apply(form);RefreshDashboard();RefreshRoster();RefreshEnvironmentBanner();return form;
        }
        static void SetRosterFilter(string value){rosterFilter=value;filterAll.Selected=value=="all";filterPrivate.Selected=value=="private";filterGroup.Selected=value=="group";filterAll.Invalidate();filterPrivate.Invalidate();filterGroup.Invalidate();RefreshRoster();}
        static void RefreshRoster()
        {
            if(roster==null||roster.IsDisposed)return;
            try{
                var all=RosterModel.Build(SparkStorage.ReadFriends(FriendsPath),ConversationStore.Read(ConversationsPath),SparkStorage.ReadCsv(Path.Combine(BaseDir,"logs","spark_log.csv")),DateTime.Now);
                string search=rosterSearch.Text.Trim();var items=all.Where(x=>(rosterFilter=="all"||x.Kind==rosterFilter)&&(search.Length==0||(x.Name??"").IndexOf(search,StringComparison.CurrentCultureIgnoreCase)>=0)).ToList();
                rosterCount.Text=search.Length>0?"搜索结果 · "+items.Count:"我的名单 · "+all.Count+"";
                string signature=string.Join("\n",items.Select(x=>x.Key+"|"+x.Name+"|"+x.Subtitle).ToArray());
                if(signature!=rosterSignature||roster.Items.Count!=items.Count){
                    var current=roster.SelectedItem as RosterItem;string selected=current==null?null:current.Key;refreshingRoster=true;roster.BeginUpdate();roster.Items.Clear();foreach(var item in items)roster.Items.Add(item);int i=items.FindIndex(x=>x.Key==selected);if(items.Count>0)roster.SelectedIndex=i>=0?i:0;roster.EndUpdate();refreshingRoster=false;rosterSignature=signature;
                }
                rosterEmpty.Visible=items.Count==0;rosterEmpty.Text=search.Length>0?"未找到匹配的联系人":rosterFilter=="group"?"还没有群聊\r\n导入会话后可在此管理":"还没有联系人\r\n点击右上角“导入”选择私信或群聊";if(rosterEmpty.Visible)rosterEmpty.BringToFront();ShowRosterDetail();
            }catch(Exception ex){rosterCount.Text="名单读取异常，原文件已保留";detailHint.Text=ex.Message;}
        }
        static string PreviewMessage()
        {
            if(fixedMessage.Length>0)return fixedMessage;
            try{
                var pool=SparkStorage.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(BaseDir,"sentences_pool.json"),System.Text.Encoding.UTF8));
                object raw;var cats=pool.TryGetValue("categories",out raw)?raw as Dictionary<string,object>:null;
                if(cats!=null){var active=pool.TryGetValue("active_categories",out raw)?raw as System.Collections.IEnumerable:null;if(active!=null)foreach(var key in active){object values;if(cats.TryGetValue(Convert.ToString(key),out values))foreach(object message in (System.Collections.IEnumerable)values)if(!string.IsNullOrWhiteSpace(Convert.ToString(message)))return Convert.ToString(message);}}
                if(pool.TryGetValue("sentences",out raw))foreach(var message in (System.Collections.IEnumerable)raw)if(!string.IsNullOrWhiteSpace(Convert.ToString(message)))return Convert.ToString(message);
            }catch{}
            return "还没有可预览的语录，请先在语录管理中选择分类。";
        }
        static void ShowRosterDetail()
        {
            if(detailName==null)return;var item=roster.SelectedItem as RosterItem;
            detailName.Text=item==null?"选择一个联系人":item.Name;
            detailKind.Text=item==null?"导入私信好友和群聊后，在此查看详情":item.Kind=="group"?"群聊 · 独立任务":"私信好友";
            detailState.Text=item==null?"":item.Kind=="group"?"可单独设置消息及每日定时":item.Subtitle;
            RefreshPrivateMessageEditor(item!=null&&item.Kind=="group");
            completeProfile.Enabled=true;completeProfile.Text=item!=null&&item.Kind=="group"?"打开群聊消息任务":item!=null&&!item.Sendable?"补全主页并加入私信":"更多消息设置";
            FilterRosterHistory();
        }
        static void FilterRosterHistory()
        {
            if(dgv==null)return;var table=dgv.DataSource as System.Data.DataTable;if(table==null)return;var item=roster==null?null:roster.SelectedItem as RosterItem;
            string filter=item==null?"":item.Kind=="group"||!FriendRecord.ValidUrl(item.Url)?"1=0":"主页 = '"+item.Url.Replace("'","''")+"'";
            if(table.DefaultView.RowFilter!=filter)table.DefaultView.RowFilter=filter;
            if(emptyHistory!=null){emptyHistory.Visible=dgv.Rows.Count==0;if(emptyHistory.Visible)emptyHistory.BringToFront();}
        }
        static void CompleteRosterProfile(RosterItem item)
        {
            using(var form=new ThemedForm {Text="补全个人主页",ClientSize=new Size(540,180),StartPosition=FormStartPosition.CenterParent}){
                var label=new Label {Text="请粘贴“"+item.Name+"”的完整抖音个人主页链接：",Location=new Point(18,18),Size=new Size(500,35)};
                var box=new TextBox {Location=new Point(18,62),Size=new Size(500,28),Text=item.Url??""};var save=new RoundButton {Text="保存并加入私信",Location=new Point(340,115),Size=new Size(178,38)};
                save.Click+=delegate{try{
                    string url=box.Text.Trim();if(!FriendRecord.ValidUrl(url))throw new InvalidDataException("请输入完整的 https://www.douyin.com/user/… 主页链接。");
                    var stored=ConversationStore.Read(ConversationsPath).FirstOrDefault(x=>x.id==item.ConversationId);
                    if(stored!=null){stored.url=url;ConversationStore.Merge(ConversationsPath,new[]{stored});}
                    SparkStorage.AddFriends(FriendsPath,new[]{new FriendRecord {name=item.Name,url=url}});form.Close();LoadLogs();RefreshRoster();
                }catch(Exception ex){MessageBox.Show(form,ex.Message,"未保存");}};form.Controls.AddRange(new Control[]{label,box,save});form.ShowDialog(mainForm);
            }
        }
        static void RemoveRosterItem()
        {
            if(!btnStart.Enabled||groupTaskRunning){MessageBox.Show(mainForm,"发送过程中请先停止任务，再调整名单。");return;}
            var item=roster.SelectedItem as RosterItem;if(item==null)return;
            if(MessageBox.Show(mainForm,"将“"+item.Name+"”移出本软件名单？不会删除抖音里的好友或退出群聊。","移出名单",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            try{
                var conversations=ConversationStore.Read(ConversationsPath).Where(x=>x.id!=item.ConversationId && !(item.Kind=="private"&&item.Url.Length>0&&x.url==item.Url)).ToList();
                SparkStorage.WriteJson(ConversationsPath,conversations);
                if(item.Kind=="private")SparkStorage.DeleteFriend(FriendsPath,new FriendRecord {name=item.Name,url=item.Url});
                LoadLogs();RefreshRoster();
            }catch(Exception ex){MessageBox.Show(mainForm,ex.Message,"移出未完成");}
        }
    }
}
