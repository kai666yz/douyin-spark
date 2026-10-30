using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
    class GroupTaskForm:ThemedForm
    {
        readonly string root,programRoot;readonly bool automatic;
        readonly DataGridView grid;readonly Label status,selectionCount,example;
        readonly ComboBox mode;readonly CheckedListBox categories;
        readonly TextBox message,time;readonly CheckBox enabled;readonly NumericUpDown interval;
        readonly RoundButton save,send,cancel,login;readonly SplitContainer split;
        readonly Timer cooldownTimer;readonly List<ConversationRecord> groups;
        readonly Dictionary<string,List<string>> pool=new Dictionary<string,List<string>>();
        GroupTaskSettings settings;WebView2 web;bool running,cancelled,closeAfter,initializing;
        int processed,total,active,succeeded,uncertain;DateTime restartAfter=DateTime.MinValue;
        string stopPath="",requestPath="",lastMode="",fixedDraft="",rotationDraft="";
        public Func<bool> ReadyForRun;public Action<bool> RunningChanged;public Action SettingsSaved;
        public DateTime RestartAfter {get{return restartAfter;}}
        public int ActiveCount {get{return active;}}
        public int SentCount {get{return succeeded;}}
        public int AwaitingCount {get{return uncertain;}}
        public int PendingCount {get{return Math.Max(0,total-processed);}}
        public void RequestStopAndClose(){closeAfter=true;StopWithoutConfirmation();if(!running)Close();}
        public void RequestStop(){if(!running||cancelled)return;if(MessageBox.Show(this,"已发（页面确认） "+succeeded+" / 未处理 "+PendingCount+"\n待核对 "+uncertain+"\n当前会话结束后停止，剩余标为已取消；停止后冷却10秒。","停止群聊任务",MessageBoxButtons.YesNo)==DialogResult.Yes)StopWithoutConfirmation();}
        public void StopWithoutConfirmation(){if(!running)return;cancelled=true;if(stopPath.Length>0)File.WriteAllText(stopPath,"stop");status.Text="已请求停止，等待当前会话结束。";WriteEvent("stop_requested");}
        void WriteEvent(string text){SparkStorage.AppendLog(Path.Combine(root,"logs","group_task.log"),DateTime.Now.ToString("s")+" | "+text+Environment.NewLine);}
        public GroupTaskForm(string dataRoot,string binaryRoot,bool autoStart=false,string initialId=null)
        {
            root=dataRoot;programRoot=binaryRoot;automatic=autoStart;Directory.CreateDirectory(Path.Combine(root,"logs"));settings=GroupTaskSettings.Read(root);
            groups=ConversationStore.Read(Path.Combine(root,"conversations.json")).Where(x=>x.kind=="group").OrderBy(x=>x.name).ToList();
            Text="群聊消息";Size=new Size(1220,820);MinimumSize=new Size(880,650);StartPosition=FormStartPosition.CenterParent;
            var outer=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=3,Tag="canvas"};outer.RowStyles.Add(new RowStyle(SizeType.Absolute,64));outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));outer.RowStyles.Add(new RowStyle(SizeType.Absolute,62));Controls.Add(outer);
            var title=new Panel{Dock=DockStyle.Fill,Tag="canvas"};title.Controls.Add(new Label{Text="群聊消息",Font=AppPalette.Font(18,true),AutoSize=true});title.Controls.Add(new Label{Text="选目标、选消息、设定时，一屏完成；按会话ID核对，单路发送。",Font=AppPalette.Font(9),AutoSize=true,Location=new Point(1,35),Tag="muted"});outer.Controls.Add(title,0,0);
            split=new SplitContainer{Dock=DockStyle.Fill,Panel2Collapsed=true,SplitterWidth=8};outer.Controls.Add(split,0,1);
            var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Tag="canvas"};foreach(float height in new[]{30f,44f,0f,42f,124f,46f,98f})left.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));split.Panel1.Controls.Add(left);
            selectionCount=new Label{Dock=DockStyle.Fill,Font=AppPalette.Font(10,true)};left.Controls.Add(selectionCount,0,0);
            grid=new DataGridView{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,EditMode=DataGridViewEditMode.EditOnEnter};
            grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="pick",HeaderText="选择",Width=58,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="name",HeaderText="群聊",ReadOnly=true});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="identity",HeaderText="会话标识尾段",ReadOnly=true,FillWeight=55});
            foreach(var group in groups){int i=grid.Rows.Add(settings.targets.Contains(group.id)||group.id==initialId,group.name,group.conversation_id.Length>10?group.conversation_id.Substring(group.conversation_id.Length-10):group.conversation_id);grid.Rows[i].Tag=group.id;}
            left.Controls.Add(ContactSelection.Toolbar(grid,UpdateSelection,row=>!GroupJournal.Blocked(GroupJournal.Read(root,DateTime.Now),Convert.ToString(row.Tag),DateTime.Now)),0,1);left.Controls.Add(grid,0,2);
            grid.CurrentCellDirtyStateChanged+=delegate{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};grid.CellValueChanged+=delegate{UpdateSelection();};
            var modes=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,6,0,0)};modes.Controls.Add(new Label{Text="消息来源",AutoSize=true,Margin=new Padding(0,5,12,0)});mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=180,Font=AppPalette.Font(10)};mode.Items.AddRange(new object[]{"随机语录","固定消息","多条语录轮换"});modes.Controls.Add(mode);var preview=new RoundButton{Text="预览示例",Primary=false,Size=new Size(112,32)};preview.Click+=delegate{RefreshExample();string text=Mode=="random"?example.Text:message.Text.Trim();MessageBox.Show(this,string.IsNullOrWhiteSpace(text)?"请先填写消息或勾选分类。":text,"消息示例（预览不发送）");};modes.Controls.Add(preview);left.Controls.Add(modes,0,3);
            var source=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,0,0,4)};categories=new CheckedListBox{Dock=DockStyle.Left,Width=235,CheckOnClick=true,BorderStyle=BorderStyle.None,Font=AppPalette.Font(10)};message=new TextBox{Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Vertical,MaxLength=20000,Font=AppPalette.Font(10)};example=new Label{Dock=DockStyle.Fill,Font=AppPalette.Font(10),Padding=new Padding(14,5,6,0),Tag="muted"};source.Controls.Add(message);source.Controls.Add(example);source.Controls.Add(categories);left.Controls.Add(source,0,4);
            try{var data=SparkStorage.Json().Deserialize<Dictionary<string,object>>(SparkStorage.CleanJson(File.ReadAllText(Path.Combine(root,"sentences_pool.json"))));object raw;if(data.TryGetValue("categories",out raw)){foreach(var item in (Dictionary<string,object>)raw){var values=new List<string>();foreach(var text in (IEnumerable)item.Value)if(text is string)values.Add((string)text);pool[item.Key]=values;categories.Items.Add(item.Key,settings.categories.Contains(item.Key));}}}catch(Exception ex){example.Text="语录库无法读取："+ex.Message;}
            fixedDraft=settings.message;rotationDraft=string.Join(Environment.NewLine,settings.custom_messages.ToArray());mode.SelectedIndex=settings.message_mode=="fixed"?1:settings.message_mode=="rotation"?2:0;mode.SelectedIndexChanged+=delegate{ChangeMode();};categories.ItemCheck+=delegate{BeginInvoke((Action)RefreshExample);};message.TextChanged+=delegate{RefreshExample();};ChangeMode();
            var schedule=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,8,0,0)};enabled=new CheckBox{Text="每日定时",AutoSize=true,Checked=!settings.paused&&!string.IsNullOrEmpty(settings.schedule),Margin=new Padding(0,4,10,0)};time=new TextBox{Text=string.IsNullOrEmpty(settings.schedule)?"13:10":settings.schedule,Width=72,Font=AppPalette.Font(10)};interval=new NumericUpDown{Minimum=15,Maximum=600,Value=Math.Max(15,Math.Min(600,settings.interval_seconds)),Width=70,Font=AppPalette.Font(10)};schedule.Controls.AddRange(new Control[]{enabled,time,new Label{Text="群间间隔（秒）",AutoSize=true,Margin=new Padding(14,5,4,0)},interval});left.Controls.Add(schedule,0,5);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};send=new RoundButton{Text="预览并发送",IconName="play",Size=new Size(154,52),CornerRadius=24};cancel=new RoundButton{Text="停止发送",Danger=true,Enabled=false,Size=new Size(124,52)};save=new RoundButton{Text="保存任务",Primary=false,Size=new Size(116,40)};login=new RoundButton{Text="登录 / 查看网页",Primary=false,Size=new Size(160,40)};var records=new RoundButton{Text="发送记录",Primary=false,Size=new Size(116,36)};actions.Controls.AddRange(new Control[]{send,cancel,save,login,records});left.Controls.Add(actions,0,6);
            send.Click+=async delegate{await RunAsync(true);};cancel.Click+=delegate{RequestStop();};save.Click+=delegate{try{var plan=Collect();if(!plan.paused&&MessageBox.Show(this,"每天 "+plan.schedule+" 将向 "+plan.targets.Count+" 个选中群发送当前来源的消息。\n确认启用？软件运行且电脑唤醒时执行，错过不补发。","保存群聊定时",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;plan.Save(root);settings=plan;if(SettingsSaved!=null)SettingsSaved();WriteEvent("saved | mode="+plan.message_mode+" | targets="+plan.targets.Count);status.Text=plan.paused?"已保存，定时暂停。":"已保存，将从下一次时间执行，不补发。";}catch(Exception ex){MessageBox.Show(this,ex.Message,"未保存");}};
            login.Click+=async delegate{try{if(web==null)await OpenWeb();else split.Panel2Collapsed=!split.Panel2Collapsed;}catch(Exception ex){status.Text=ex.Message;}};records.Click+=delegate{GroupHistoryForm.Open(this,root);};
            status=new Label{Dock=DockStyle.Fill,Font=AppPalette.Font(9),Padding=new Padding(0,9,0,0),Tag="muted",Text=groups.Count==0?"还没有群聊，请从联系人页导入。":"随机/轮换语录当天不重句，跨天重置。固定消息可重复；同一群当天不重复提交。"};outer.Controls.Add(status,0,2);
            cooldownTimer=new Timer{Interval=500};cooldownTimer.Tick+=delegate{if(!running){int n=Math.Max(0,(int)Math.Ceiling((restartAfter-DateTime.UtcNow).TotalSeconds));send.Enabled=n==0;send.Text=n==0?"预览并发送":"稍候 "+n+" 秒";}};cooldownTimer.Start();FormClosed+=delegate{cooldownTimer.Dispose();if(web!=null)web.Dispose();};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running){closeAfter=true;StopWithoutConfirmation();e.Cancel=true;}};
            UpdateSelection();if(automatic)Shown+=async delegate{await RunAsync(false);};
        }
        public Task RunScheduled(){return RunAsync(false);}
        string Mode {get{return mode.SelectedIndex==1?"fixed":mode.SelectedIndex==2?"rotation":"random";}}
        void ChangeMode(){if(lastMode=="fixed")fixedDraft=message.Text;if(lastMode=="rotation")rotationDraft=message.Text;lastMode=Mode;categories.Visible=example.Visible=Mode=="random";message.Visible=Mode!="random";message.Text=Mode=="fixed"?fixedDraft:rotationDraft;RefreshExample();}
        void RefreshExample(){if(mode==null||categories==null||example==null)return;var values=categories.CheckedItems.Cast<string>().Where(pool.ContainsKey).SelectMany(x=>pool[x]).ToList();var used=new HashSet<string>();string ledger=Path.Combine(root,"logs","phrase_usage_"+DateTime.Today.ToString("yyyy-MM-dd")+".json");try{if(File.Exists(ledger))foreach(var key in SparkStorage.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(ledger)).Keys)used.Add(key);foreach(var row in SparkStorage.ReadCsv(Path.Combine(root,"logs","spark_log.csv")))if(row.Length>=5&&row[0].StartsWith(DateTime.Today.ToString("yyyy-MM-dd"))&&(row[4]=="OK"||row[4]=="UNCONFIRMED"))used.Add(DataLocation.Hash(row[3]));values=values.Where(x=>!used.Contains(DataLocation.Hash(x))).Distinct().ToList();}catch{example.Text="今日语录记录暂无法读取，请查看本机数据文件。";return;}example.Text=Mode=="random"?(values.Count==0?"勾选分类后可预览。":"示例 · "+values[0]+"\r\n\r\n实际发送按当天未用语录抽取，共 "+values.Count+" 条候选。"):(Mode=="rotation"?"每行一条语录":"固定消息");}
        void UpdateSelection(){selectionCount.Text="已选 "+grid.Rows.Cast<DataGridViewRow>().Count(x=>Convert.ToBoolean(x.Cells["pick"].Value??false))+" / "+grid.Rows.Count+" 个群 · 按标识区分同名群";}
        GroupTaskSettings Collect(){grid.EndEdit();var plan=new GroupTaskSettings{targets=grid.Rows.Cast<DataGridViewRow>().Where(x=>Convert.ToBoolean(x.Cells["pick"].Value??false)).Select(x=>Convert.ToString(x.Tag)).ToList(),message_mode=Mode,message=Mode=="fixed"?message.Text.Trim():fixedDraft,categories=categories.CheckedItems.Cast<string>().ToList(),custom_messages=Mode=="rotation"?message.Lines.Select(x=>x.Trim()).Where(x=>x.Length>0).Distinct().ToList():rotationDraft.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).ToList(),schedule=time.Text.Trim(),paused=!enabled.Checked,last_trigger_day=settings.last_trigger_day,interval_seconds=(int)interval.Value};GroupTaskSettings.Resolve(groups,plan.targets);return plan;}
        async Task OpenWeb()
        {
            if(initializing)return;initializing=true;login.Enabled=false;split.Panel2Collapsed=false;split.SplitterDistance=Math.Max(440,split.Width/2);
            try{if(web==null){web=new WebView2{Dock=DockStyle.Fill};split.Panel2.Controls.Add(web);var environment=await BrowserSession.EnvironmentAsync(root);await web.EnsureCoreWebView2Async(environment);try{await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled","{\"enabled\":true}");}catch{}web.CoreWebView2.Settings.AreDevToolsEnabled=false;web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;web.CoreWebView2.Settings.IsStatusBarEnabled=false;web.CoreWebView2.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e){if(!Trusted(e.Uri))e.Cancel=true;};web.CoreWebView2.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;if(Trusted(e.Uri))web.CoreWebView2.Navigate(e.Uri);};web.CoreWebView2.DownloadStarting+=delegate(object s,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};web.CoreWebView2.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};}
                var loaded=new TaskCompletionSource<bool>();EventHandler<CoreWebView2NavigationCompletedEventArgs> handler=delegate(object s,CoreWebView2NavigationCompletedEventArgs e){loaded.TrySetResult(e.IsSuccess);};web.NavigationCompleted+=handler;try{web.CoreWebView2.Navigate("https://www.douyin.com/");if(await Task.WhenAny(loaded.Task,Task.Delay(30000))!=loaded.Task||!await loaded.Task)throw new IOException("网页未加载，请检查网络后重试。");}finally{web.NavigationCompleted-=handler;}
            }catch{if(web!=null){web.Dispose();web=null;}throw;}finally{initializing=false;login.Enabled=!running;}
        }
        static bool Trusted(string address){Uri uri;return Uri.TryCreate(address,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&(uri.Host=="douyin.com"||uri.Host.EndsWith(".douyin.com",StringComparison.OrdinalIgnoreCase));}
        async Task RunAsync(bool confirm)
        {
            if(running||initializing||DateTime.UtcNow<restartAfter)return;if(ReadyForRun!=null&&!ReadyForRun()){status.Text="正在导入或停止冷却中，请稍后开始。";return;}
            GroupTaskSettings plan;try{plan=!confirm?GroupTaskSettings.Read(root):Collect();GroupTaskSettings.Resolve(groups,plan.targets);if(plan.targets.Count==0)throw new IOException("请先勾选目标群。");if(plan.message_mode=="fixed"&&string.IsNullOrWhiteSpace(plan.message)||plan.message_mode=="rotation"&&plan.custom_messages.Count==0||plan.message_mode=="random"&&plan.categories.Count==0)throw new IOException("请填写消息或选择可用语录。");}catch(Exception ex){status.Text=ex.Message;return;}
            if(confirm&&MessageBox.Show(this,"目标群："+plan.targets.Count+" 个\n消息模式："+mode.Text+"\n"+(Mode=="fixed"?plan.message:Mode=="rotation"?"按多条语录依次轮换，当天不重句":"按勾选分类抽取当天未用语录")+"\n\n今日已提交或待确认的群跳过。确认开始？","群聊发送预览",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            if(!Program.CheckSafetyResume(this,confirm))return;
            running=true;cancelled=false;processed=active=succeeded=uncertain=0;total=plan.targets.Count;save.Enabled=send.Enabled=login.Enabled=false;cancel.Enabled=true;grid.Enabled=mode.Enabled=message.Enabled=categories.Enabled=enabled.Enabled=time.Enabled=interval.Enabled=false;
            string token=Guid.NewGuid().ToString("N");stopPath=Path.Combine(root,"logs","group_stop_"+token+".flag");requestPath=Path.Combine(root,"logs","group_run_"+token+".json");if(RunningChanged!=null)RunningChanged(true);int exit=-1;
            try{
                if(web==null)await OpenWeb();
                var target=SparkStorage.Json().Deserialize<Dictionary<string,object>>(await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Target.getTargetInfo","{}"));var info=(Dictionary<string,object>)target["targetInfo"];
                SparkStorage.WriteJson(requestPath,new{plan=plan,endpoint="http://127.0.0.1:"+BrowserSession.Port(root),target_id=Convert.ToString(info["targetId"])});
                var psi=Program.BackendInfo("--group-run "+Program.QuoteArg(requestPath)+" --stop-after-current "+Program.QuoteArg(stopPath));WriteEvent("started | mode="+plan.message_mode+" | targets="+total);
                await Task.Run(delegate{using(var process=new Process{StartInfo=psi}){process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null)return;string line=e.Data;BeginInvoke((Action)delegate{if(line=="UI|LOCK")web.Enabled=false;else if(line=="UI|UNLOCK")web.Enabled=true;else if(line.StartsWith("TASK|ACTIVE|"))active=line.EndsWith("1")?1:0;else if(line.StartsWith("RESULT|")){string state=line.Split('|').Last();if(state=="OK")succeeded++;else if(state=="SUBMITTED"||state=="UNCONFIRMED")uncertain++;}else if(line.StartsWith("PROGRESS|")){var parts=line.Split('|');int n;if(parts.Length>=5&&int.TryParse(parts[1],out n)){processed=n;status.Text="群聊进度 "+processed+" / "+total+" · "+parts.Last();}}else if(line.StartsWith("ATTENTION|")){split.Panel2Collapsed=false;web.Enabled=true;status.Text=line.Split('|').Last();}else status.Text=line;});WriteEvent(line);};process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null){string error=e.Data;BeginInvoke((Action)delegate{status.Text="后端异常："+error;});WriteEvent("error | "+error);}};process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();process.WaitForExit();exit=process.ExitCode;}});
                status.Text=cancelled?"已停止，剩余目标记为已取消。":exit==0?"本轮结束，请查看群聊记录核对页面确认或待确认状态。":"后端未完成，请查看运行日志；已有记录保留。";
            }catch(Exception ex){status.Text="群聊未完成："+ex.Message;WriteEvent("failed | "+ex.Message);MessageBox.Show(this,ex.Message,"群聊未完成");}
            finally{running=false;active=0;if(web!=null)web.Enabled=true;if(cancelled)restartAfter=DateTime.UtcNow.AddSeconds(10);save.Enabled=login.Enabled=true;send.Enabled=!cancelled;cancel.Enabled=false;grid.Enabled=mode.Enabled=message.Enabled=categories.Enabled=enabled.Enabled=time.Enabled=interval.Enabled=true;try{File.Delete(requestPath);File.Delete(stopPath);}catch{}if(RunningChanged!=null)RunningChanged(false);if(closeAfter)Close();}
        }
    }
}
