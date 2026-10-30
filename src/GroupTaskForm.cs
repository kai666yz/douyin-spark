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
    class GroupTaskForm:ThemedForm
    {
        readonly string root,programRoot;readonly bool automatic;
        readonly DataGridView grid;readonly TextBox message,time;readonly CheckBox enabled;readonly Label status;
        readonly Label selectionCount;readonly NumericUpDown interval;readonly Timer cooldownTimer;DateTime restartAfter=DateTime.MinValue;int processed,total;
        readonly RoundButton save,send,cancel,login;readonly Panel webHost;
        readonly List<ConversationRecord> groups;
        GroupTaskSettings settings;WebView2 web;bool running,cancelled,closeAfter,initializing;
        public Func<bool> ReadyForRun;public Action<bool> RunningChanged;public Action SettingsSaved;
        public DateTime RestartAfter {get{return restartAfter;}}
        public void RequestStopAndClose(){cancelled=true;closeAfter=true;if(!running)Close();}
        public void RequestStop(){if(!running||cancelled)return;if(MessageBox.Show(this,"已处理 "+processed+" / 未处理 "+Math.Max(0,total-processed)+"\n停止后保留已提交记录，未开始的群标为已取消；10秒后可再次开始。","停止群聊任务",MessageBoxButtons.YesNo)==DialogResult.Yes){cancelled=true;status.Text="已请求停止，正在保存当前结果。";WriteEvent("stop_requested");}}
        void WriteEvent(string text){SparkStorage.AppendLog(Path.Combine(root,"logs","group_task.log"),DateTime.Now.ToString("s")+" | "+text+Environment.NewLine);}
        public GroupTaskForm(string dataRoot,string binaryRoot,bool autoStart=false,string initialId=null)
        {
            root=dataRoot;programRoot=binaryRoot;automatic=autoStart;settings=GroupTaskSettings.Read(root);
            groups=ConversationStore.Read(Path.Combine(root,"conversations.json")).Where(x=>x.kind=="group").OrderBy(x=>x.name).ToList();
            Text=autoStart?"群聊定时任务":"群聊消息与定时";StartPosition=FormStartPosition.CenterParent;MinimumSize=new Size(1050,760);Size=new Size(1240,820);
            var outer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(22),Tag="canvas"};outer.RowStyles.Add(new RowStyle(SizeType.Absolute,76));outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));outer.RowStyles.Add(new RowStyle(SizeType.Absolute,56));Controls.Add(outer);
            var header=new Panel{Dock=DockStyle.Fill,Tag="canvas"};header.Controls.Add(new Label{Text="把问候送到选中的群",Font=AppPalette.Font(18,true),AutoSize=true});header.Controls.Add(new Label{Text="独立消息 · 独立定时 · 按群标识核对 · 不确定的提交不会自动重发",Font=AppPalette.Font(9),AutoSize=true,Location=new Point(1,42),Tag="muted"});outer.Controls.Add(header,0,0);
            var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,470));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));outer.Controls.Add(body,0,1);
            var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(0,0,14,0)};left.RowStyles.Add(new RowStyle(SizeType.Absolute,88));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,32));left.RowStyles.Add(new RowStyle(SizeType.Absolute,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,62));left.RowStyles.Add(new RowStyle(SizeType.Absolute,100));body.Controls.Add(left,0,0);

            grid=new DataGridView{Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,EditMode=DataGridViewEditMode.EditOnEnter,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
            grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="pick",HeaderText="选择",Width=52,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="name",HeaderText="群聊",ReadOnly=true});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="identity",HeaderText="标识尾段",ReadOnly=true,FillWeight=55});
            grid.CurrentCellDirtyStateChanged+=delegate{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            foreach(var group in groups){int i=grid.Rows.Add(settings.targets.Contains(group.id)||group.id==initialId,group.name,group.conversation_id.Length>10?group.conversation_id.Substring(group.conversation_id.Length-10):group.conversation_id);grid.Rows[i].Tag=group.id;grid.Rows[i].Cells["identity"].ToolTipText=group.conversation_id;}
            selectionCount=new Label{Dock=DockStyle.Top,Height=24,Font=AppPalette.Font(9,true)};
            var selectionHead=new Panel{Dock=DockStyle.Fill};var batch=ContactSelection.Toolbar(grid,UpdateGroupSelection,row=>!GroupJournal.Blocked(GroupJournal.Read(root,DateTime.Now),Convert.ToString(row.Tag),DateTime.Now));batch.Dock=DockStyle.Bottom;batch.Height=42;selectionHead.Controls.Add(batch);selectionHead.Controls.Add(selectionCount);left.Controls.Add(selectionHead,0,0);
            grid.CellValueChanged+=delegate{UpdateGroupSelection();};UpdateGroupSelection();
            left.Controls.Add(grid,0,1);left.Controls.Add(new Label{Text="本轮统一群聊消息",Font=AppPalette.Font(10,true),Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0)},0,2);
            message=new TextBox{Multiline=true,Dock=DockStyle.Fill,Font=AppPalette.Font(10),MaxLength=2000,ScrollBars=ScrollBars.Vertical,Text=settings.message};left.Controls.Add(message,0,3);
            var schedule=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,12,0,0)};enabled=new CheckBox{Text="启用群聊每日定时",AutoSize=true,Checked=!settings.paused&&!string.IsNullOrEmpty(settings.schedule),Margin=new Padding(0,5,12,0)};time=new TextBox{Width=72,Text=string.IsNullOrEmpty(settings.schedule)?"13:10":settings.schedule,Font=AppPalette.Font(10)};schedule.Controls.AddRange(new Control[]{enabled,time,new Label{Text="HH:mm",AutoSize=true,Margin=new Padding(8,6,0,0),Tag="muted"}});left.Controls.Add(schedule,0,4);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};save=new RoundButton{Text="保存任务",Size=new Size(112,38),Primary=false};send=new RoundButton{Text="预览并发送",Size=new Size(140,38),IconName="play"};cancel=new RoundButton{Text="停止本轮",Size=new Size(112,38),Danger=true,Enabled=false};var history=new RoundButton{Text="群聊记录",Size=new Size(112,34),Primary=false};
            save.Click+=delegate{try{var plan=Collect();if(!plan.paused&&MessageBox.Show(this,"每天 "+plan.schedule+" 将向 "+plan.targets.Count+" 个选中群发送当前消息。软件运行且电脑唤醒时执行。\n仅等待下一次时间，错过不补发。确认保存？","启用群聊定时",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;plan.Save(root);settings=plan;if(SettingsSaved!=null)SettingsSaved();WriteEvent("settings_saved | targets="+plan.targets.Count+" | interval="+plan.interval_seconds);status.Text=plan.paused?"任务已保存，群聊定时保持暂停。":"群聊定时已保存，将从下一次时间开始。";}catch(Exception ex){status.Text=ex.Message;}};
            send.Click+=async delegate{await RunAsync(true);};cancel.Click+=delegate{RequestStop();};history.Click+=delegate{GroupHistoryForm.Open(this,root);};interval=new NumericUpDown{Minimum=15,Maximum=600,Value=Math.Max(15,Math.Min(600,settings.interval_seconds)),Width=72,Font=AppPalette.Font(10)};actions.Controls.AddRange(new Control[]{send,save,cancel,history,new Label{Text="群间间隔（秒）",AutoSize=true,Margin=new Padding(2,8,3,0)},interval});left.Controls.Add(actions,0,5);
            webHost=new Panel{Dock=DockStyle.Fill,Tag="soft"};var webTools=new FlowLayoutPanel{Dock=DockStyle.Top,Height=46,WrapContents=false};login=new RoundButton{Text="软件内登录 / 刷新",Size=new Size(180,36),Primary=false};login.Click+=async delegate{try{await OpenWeb();}catch(Exception ex){status.Text=ex.Message;}};webTools.Controls.Add(login);webTools.Controls.Add(new Label{Text="复用会话导入的登录",AutoSize=true,Margin=new Padding(8,10,0,0),Tag="muted"});var empty=new Label{Text="网页在需要时才打开\r\n首次请登录并展开抖音消息列表\r\n自动发送前会核对当前群的稳定标识",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=AppPalette.Font(10),Tag="muted"};webHost.Controls.Add(empty);webHost.Controls.Add(webTools);body.Controls.Add(webHost,1,0);
            status=new Label{Dock=DockStyle.Fill,Font=AppPalette.Font(9),Padding=new Padding(0,10,0,0),Tag="muted",Text=groups.Count==0?"还没有群聊。请返回联系人页，从会话导入中勾选群聊。":"手动发送前会显示目标和消息预览。已提交但未确认的群当天不会自动重发。"};outer.Controls.Add(status,0,2);
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running){cancelled=true;closeAfter=true;e.Cancel=true;status.Text="正在停止本轮，结束后自动关闭。";}};FormClosed+=delegate{if(web!=null)web.Dispose();};
            cooldownTimer=new Timer{Interval=500};cooldownTimer.Tick+=delegate{if(!running&&DateTime.UtcNow>=restartAfter){send.Enabled=true;send.Text="预览并发送";}else if(!running)send.Text="稍候 "+Math.Ceiling((restartAfter-DateTime.UtcNow).TotalSeconds)+" 秒";};cooldownTimer.Start();FormClosed+=delegate{cooldownTimer.Dispose();};
            if(automatic)Shown+=async delegate{await RunAsync(false);};
        }
        void UpdateGroupSelection(){if(selectionCount!=null)selectionCount.Text="已选 "+grid.Rows.Cast<DataGridViewRow>().Count(x=>Convert.ToBoolean(x.Cells["pick"].Value??false))+" / "+grid.Rows.Count+" 个群 · 同名群按标识区分";}
        GroupTaskSettings Collect()
        {
            grid.EndEdit();var ids=grid.Rows.Cast<DataGridViewRow>().Where(x=>Convert.ToBoolean(x.Cells["pick"].Value??false)).Select(x=>Convert.ToString(x.Tag)).ToList();GroupTaskSettings.Resolve(groups,ids);
            var value=new GroupTaskSettings{message=message.Text.Trim(),targets=ids,schedule=time.Text.Trim(),paused=!enabled.Checked,last_trigger_day=settings.last_trigger_day,interval_seconds=(int)interval.Value};
            if(value.message.Length>2000)throw new InvalidDataException("消息不超过2000字。");return value;
        }
        async Task OpenWeb()
        {
            if(initializing)return;initializing=true;login.Enabled=false;
            try{
                if(web==null){web=new WebView2{Dock=DockStyle.Fill};webHost.Controls.Add(web);web.BringToFront();webHost.Controls[webHost.Controls.Count-1].BringToFront();
                    var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"conversation_profile"));await web.EnsureCoreWebView2Async(environment);
                    try{await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled","{\"enabled\":true}");}catch{}
                    web.CoreWebView2.Settings.AreDevToolsEnabled=false;web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;web.CoreWebView2.Settings.IsStatusBarEnabled=false;
                    web.CoreWebView2.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e){if(!Trusted(e.Uri))e.Cancel=true;};
                    web.CoreWebView2.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;if(Trusted(e.Uri))web.CoreWebView2.Navigate(e.Uri);};web.CoreWebView2.DownloadStarting+=delegate(object s,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};web.CoreWebView2.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
                }
                var loaded=new TaskCompletionSource<bool>();EventHandler<CoreWebView2NavigationCompletedEventArgs> handler=delegate(object s,CoreWebView2NavigationCompletedEventArgs e){loaded.TrySetResult(e.IsSuccess);};web.NavigationCompleted+=handler;
                try{web.CoreWebView2.Navigate("https://www.douyin.com/");if(await Task.WhenAny(loaded.Task,Task.Delay(30000))!=loaded.Task||!await loaded.Task)throw new IOException("网页加载未完成，请检查网络；不会自动提交消息。");}finally{web.NavigationCompleted-=handler;}
                status.Text="请在软件内完成登录，并展开消息列表。";
            }catch{if(web!=null){web.Dispose();web=null;}throw;}finally{initializing=false;login.Enabled=!running;}
        }
        static bool Trusted(string address){Uri uri;return Uri.TryCreate(address,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&(uri.Host=="douyin.com"||uri.Host.EndsWith(".douyin.com",StringComparison.OrdinalIgnoreCase));}
        static string Explain(string code)
        {
            var labels=new Dictionary<string,string>{{"GROUP_NOT_LOADED","消息列表尚未加载该群，请展开消息或重新导入"},{"AMBIGUOUS_GROUP","发现重复群标识，未猜测目标"},{"WRONG_ACTIVE_GROUP","当前输入框属于其他会话，未提交"},{"UNVERIFIED_ACTIVE_GROUP","无法核实当前群标识，未提交"},{"AMBIGUOUS_EDITOR","输入框尚未就绪或存在多个输入框"},{"DRAFT_PRESENT","检测到已有草稿，已保留"},{"INPUT_UNSUPPORTED","当前输入框尚不支持自动填写"},{"SEND_BUTTON_UNAVAILABLE","未找到可验证的网页发送入口"},{"DRAFT_CHANGED","草稿内容发生变化，未提交"},{"LOGIN_REQUIRED","登录未完成，请在软件内扫码"},{"VERIFICATION_REQUIRED","需要手动完成网页安全验证"}};
            string text;return labels.TryGetValue(code,out text)?text:"未提交："+code;
        }
        async Task<string> Action(string phase,ConversationRecord group,string text)
        {
            if(web==null||web.CoreWebView2==null||web.Source==null||!Trusted(web.Source.AbsoluteUri))throw new IOException("网页未就绪或尚未登录。");
            string script=File.ReadAllText(Path.Combine(programRoot,"assets","group_actions.js"),Encoding.UTF8);
            string json=SparkStorage.Json().Serialize(new {phase=phase,id=group.conversation_id,message=text});
            var result=SparkStorage.Json().Deserialize<Dictionary<string,object>>(await web.ExecuteScriptAsync(script+"("+json+")"));return Convert.ToString(result["status"]);
        }
        async Task RunAsync(bool confirm)
        {
            if(running||initializing||DateTime.UtcNow<restartAfter)return;if(ReadyForRun!=null&&!ReadyForRun()){status.Text="私信或导入任务正在执行。你可以先编辑群聊设置，发送任务稍后进行；定时会排队。";return;}GroupTaskSettings plan;
            try{plan=automatic?settings:Collect();if(plan.targets.Count==0||string.IsNullOrWhiteSpace(plan.message))throw new InvalidDataException("请先选择群聊并填写消息。");GroupTaskSettings.Resolve(groups,plan.targets);}
            catch(Exception ex){status.Text=ex.Message;return;}
            var selected=GroupTaskSettings.Resolve(ConversationStore.Read(Path.Combine(root,"conversations.json")),plan.targets);
            if(confirm&&MessageBox.Show(this,"向以下 "+selected.Count+" 个群发送：\n"+string.Join("、",selected.Select(x=>x.name).ToArray())+"\n\n消息：\n"+plan.message+"\n\n今日已提交或待确认的群会跳过。","群聊发送预览",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            processed=0;total=selected.Count;WriteEvent("started | targets="+total+" | interval="+plan.interval_seconds);running=true;cancelled=false;save.Enabled=send.Enabled=login.Enabled=false;cancel.Enabled=true;grid.Enabled=message.Enabled=enabled.Enabled=time.Enabled=interval.Enabled=false;
            if(RunningChanged!=null)RunningChanged(true);
            int submitted=0,skipped=0,failed=0;string runStarted=DateTime.Now.ToString("s");
            try{using(var taskLock=GroupJournal.Acquire(root)){
                if(web==null)await OpenWeb();
                string open=File.ReadAllText(Path.Combine(programRoot,"assets","open_messages.js"),Encoding.UTF8);
                foreach(var group in selected){
                    processed=submitted+skipped+failed;
                    if(cancelled)break;
                    if(GroupJournal.Blocked(GroupJournal.Read(root,DateTime.Now),group.id,DateTime.Now)){skipped++;continue;}
                    if(submitted+failed>0){status.Text="群间等待 · "+plan.interval_seconds+"秒，可随时停止";for(int tick=0;tick<plan.interval_seconds*4&&!cancelled;tick++)await Task.Delay(250);}
                    if(cancelled)break;
                    status.Text="正在定位选中群 "+(submitted+skipped+failed+1)+" / "+selected.Count+"…";
                    string result="GROUP_NOT_LOADED";
                    for(int attempt=0;attempt<180&&!cancelled;attempt++){
                        result=await Action("select",group,plan.message);if(result=="OPENED")break;
                        if(result=="LOGIN_REQUIRED"||result=="VERIFICATION_REQUIRED"){status.Text="请在右侧完成扫码或安全验证，软件正在等待；不会自动解验证码。";await Task.Delay(1000);continue;}
                        if(result!="GROUP_NOT_LOADED")break;
                        await web.ExecuteScriptAsync(open);if(attempt>0)await Action("scroll",group,plan.message);await Task.Delay(1000);
                    }
                    if(cancelled)break;if(result!="OPENED"){failed++;status.Text=Explain(result);GroupJournal.Failure(root,group,Explain(result));continue;}
                    for(int attempt=0;attempt<12&&!cancelled;attempt++){result=await Action("inspect",group,plan.message);if(result=="READY")break;await Task.Delay(400);}
                    if(cancelled)break;
                    if(result!="READY"){failed++;status.Text=Explain(result);GroupJournal.Failure(root,group,Explain(result));continue;}
                    // Lock native mouse/keyboard input only during fill/submit, so switching
                    // other windows cannot redirect the scoped browser submission.
                    web.Enabled=false;
                    try{
                        result=await Action("fill",group,plan.message);
                        if(result!="FILLED"){failed++;status.Text=Explain(result);GroupJournal.Failure(root,group,Explain(result));continue;}
                        await Task.Delay(350);if(cancelled)break;
                        var entry=GroupJournal.Begin(root,group,DateTime.Now);
                        try{
                            result=await Action("submit",group,plan.message);
                            if(result=="KEYBOARD_READY"){
                                result=await Action("keyboard-verify",group,plan.message);
                                if(result=="KEYBOARD_READY"){
                                    await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"keyDown\",\"key\":\"Enter\",\"code\":\"Enter\",\"windowsVirtualKeyCode\":13,\"nativeVirtualKeyCode\":13,\"text\":\"\\r\"}");
                                    await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"keyUp\",\"key\":\"Enter\",\"code\":\"Enter\",\"windowsVirtualKeyCode\":13,\"nativeVirtualKeyCode\":13}");result="SUBMITTED";
                                }
                            }
                            if(result=="SUBMITTED"){entry.status="SUBMITTED";entry.note="已向核对后的网页提交，等待用户核对送达";submitted++;}else{entry.status="FAILED";entry.note=Explain(result);failed++;}GroupJournal.Write(root,entry);
                        }catch{entry.status="UNCONFIRMED";entry.note="提交阶段中断，需核对网页后再决定是否重发";GroupJournal.Write(root,entry);throw;}
                    }finally{if(web!=null&&!web.IsDisposed)web.Enabled=true;}

                }
            }}catch(Exception ex){status.Text="本轮已停止："+ex.Message;return;}
            finally{if(cancelled){restartAfter=DateTime.UtcNow.AddSeconds(10);foreach(var target in selected)if(!GroupJournal.Blocked(GroupJournal.Read(root,DateTime.Now),target.id,DateTime.Now)&&!GroupJournal.Read(root,DateTime.Now).Any(x=>x.group_id==target.id&&x.time.CompareTo(runStarted)>=0))GroupJournal.Cancel(root,target);WriteEvent("cancelled");}running=false;save.Enabled=login.Enabled=true;send.Enabled=!cancelled;cancel.Enabled=false;grid.Enabled=message.Enabled=enabled.Enabled=time.Enabled=interval.Enabled=true;if(RunningChanged!=null)RunningChanged(false);if(closeAfter)Close();}
            if(!IsDisposed)status.Text=(cancelled?"本轮已停止。":"本轮结束。")+"已提交 "+submitted+"，跳过 "+skipped+"，未提交 "+failed+"。提交不等于送达，请在群聊记录中核对。";
        }
    }
    class GroupHistoryForm
    {
        public static void Open(IWin32Window owner,string root)
        {
            using(var form=new ThemedForm{Text="群聊发送记录",Size=new Size(850,530),StartPosition=FormStartPosition.CenterParent}){
                var grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AutoGenerateColumns=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};foreach(string name in new[]{"时间","群聊","状态","说明"})grid.Columns.Add(name,name);
                grid.CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e){if(e.RowIndex<0||e.ColumnIndex!=2)return;var record=grid.Rows[e.RowIndex].Tag as GroupAttempt;if(record==null)return;e.Value="●  "+Convert.ToString(e.Value);e.CellStyle.ForeColor=record.status=="CONFIRMED"||record.status=="OK"?AppPalette.Success:record.status=="SUBMITTED"||record.status=="UNCONFIRMED"?AppPalette.Warning:record.status=="CANCELLED"?AppPalette.Muted:AppPalette.Error;e.FormattingApplied=true;};
                Action reload=delegate{grid.Rows.Clear();foreach(var item in GroupJournal.Read(root).Take(300)){int i=grid.Rows.Add(item.time,item.name,item.status=="SUBMITTED"?"已提交待核对":item.status=="CONFIRMED"?"已核对送达":item.status=="UNCONFIRMED"?"结果不确定":item.status=="CANCELLED"?"已取消":"未提交",item.note);grid.Rows[i].Tag=item;}};
                var tools=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=62,Padding=new Padding(12)};var ok=new RoundButton{Text="已核对送达",Size=new Size(140,36)};var no=new RoundButton{Text="已核对未送达",Primary=false,Size=new Size(150,36)};var export=new RoundButton{Text="导出CSV",Primary=false,Size=new Size(120,36)};
                Action<bool> mark=delegate(bool delivered){if(grid.CurrentRow==null)return;var entry=grid.CurrentRow.Tag as GroupAttempt;if(entry==null)return;if(MessageBox.Show(form,"请先在抖音网页核对该条消息。确认标为“"+(delivered?"已送达":"未送达，可手动重试")+"”？","核对发送结果",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;entry.status=delivered?"CONFIRMED":"FAILED";entry.note="用户在网页核对后手动标记";GroupJournal.Write(root,entry);reload();};ok.Click+=delegate{mark(true);};no.Click+=delegate{mark(false);};
                export.Click+=delegate{using(var dialog=new SaveFileDialog{Filter="CSV文件|*.csv",FileName="群聊记录.csv"})if(dialog.ShowDialog(form)==DialogResult.OK){Func<string,string> cell=delegate(string s){s=s??"";if(s.Length>0&&"=+-@\t\r".IndexOf(s[0])>=0)s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";};var lines=new List<string>{"时间,群聊,状态,说明"};lines.AddRange(GroupJournal.Read(root).Select(x=>string.Join(",",new[]{x.time,x.name,x.status,x.note}.Select(cell).ToArray())));File.WriteAllLines(dialog.FileName,lines,new UTF8Encoding(true));}};
                tools.Controls.AddRange(new Control[]{ok,no,export});form.Controls.Add(grid);form.Controls.Add(tools);reload();form.ShowDialog(owner);
            }
        }
    }
}
