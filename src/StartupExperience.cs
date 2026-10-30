using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace DouyinSpark
{
    partial class Program
    {
        static readonly string ProgramDir=AppDomain.CurrentDomain.BaseDirectory;
        static DataLocation dataLocation;
        static bool schedulePaused;
        static DateTime nextScheduled=DateTime.MaxValue;
        static string scheduleLastTrigger="";
        static DateTime privateQueueUntil=DateTime.MinValue;
        static bool startupReview;
        static bool ConfigBool(string key){return string.Equals(ConfigString(key),"true",StringComparison.OrdinalIgnoreCase);}
        static void PrepareStorageWithProgress()
        {
            bool retry;
            do{
                retry=false;Exception failure=null;
                using(var dialog=new StartupProgressForm()){
                    dialog.Shown+=async delegate{
                        try{
                            dialog.Stage(0,"正在检查数据保存位置；已有文件会保留。无需单独安装 Python。");
                            await Task.Run((Action)PrepareStorage);StartupEvent("storage_ready");
                            dialog.Stage(1,"正在启动并校验内置后端，不会访问抖音或发送消息。首次加载可能稍慢。");
                            string backend=await Task.Run((Func<string>)ValidateBackend);StartupEvent(backend);
                            dialog.Stage(2,"运行环境就绪，正在检查 WebView2 和发送浏览器；已安装的组件会跳过。");
                            string runtime=await Task.Run((Func<string>)RuntimeVersion);string browser=BrowserPath(GetBrowserChoice());
                            StartupEvent("components | webview2="+(runtime.Length>0?"ready":"missing")+" | browser="+(browser.Length>0?"ready":"missing"));
                            dialog.Stage(3,runtime.Length>0&&browser.Length>0?"准备完成，正在进入软件。":"基础环境准备完成；接下来会显示缺失组件与处理按钮。");
                            dialog.Stage(4,"环境检查完成。缺失组件可在下一页一键处理；不会自动发送消息。");await Task.Delay(250);dialog.Close();
                        }catch(Exception ex){failure=ex;try{StartupEvent("failed | "+ex.Message);}catch{}dialog.Fail(ex);}
                    };
                    dialog.ShowDialog();if(failure!=null){retry=dialog.DialogResult==DialogResult.Retry;if(!retry)throw new IOException(failure.Message,failure);}
                }
            }while(retry);
        }

        static void PrepareStorage()
        {
            dataLocation=DataLocation.Resolve(ProgramDir,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"douyin-spark"));
            BaseDir=dataLocation.Root;
            Directory.CreateDirectory(Path.Combine(BaseDir,"logs"));Directory.CreateDirectory(Path.Combine(BaseDir,"update"));
            if(!File.Exists(FriendsPath))SparkStorage.WriteJson(FriendsPath,new FriendRecord[0]);
            string configPath=Path.Combine(BaseDir,"config.json");
            if(!File.Exists(configPath))SparkStorage.WriteJson(configPath,new Dictionary<string,object>{{"browser","msedge"},{"schedule",""},{"message",""}});
            string pool=Path.Combine(BaseDir,"sentences_pool.json");
            if(!File.Exists(pool)&&File.Exists(Path.Combine(ProgramDir,"sentences_pool.json")))File.Copy(Path.Combine(ProgramDir,"sentences_pool.json"),pool);
            string statePath=Path.Combine(BaseDir,"startup_state.json"),previous="",version="";
            if(File.Exists(statePath))try{var state=SparkStorage.Json().Deserialize<Dictionary<string,string>>(File.ReadAllText(statePath));previous=state["identity"];version=state["review_version"];}catch{}
            string identity=DataLocation.Hash(DataLocation.Normalize(ProgramDir)+"|"+DataLocation.Normalize(BaseDir)+"|"+Environment.MachineName+"|"+WindowsIdentity.GetCurrent().User.Value);
            var config=ReadConfig();dataLocation.Changed=SchedulePolicy.Review(config,identity,previous);
            SparkStorage.WriteJson(configPath,config);
            if(dataLocation.Changed&&File.Exists(Path.Combine(BaseDir,"group_task.json"))){var groups=GroupTaskSettings.Read(BaseDir);groups.paused=true;groups.Save(BaseDir);}
            // The paused flag is committed before acknowledging this location.
            SparkStorage.WriteJson(statePath,new Dictionary<string,string>{{"identity",identity},{"review_version",version}});
            startupReview=dataLocation.Changed||version!=AppVersion;
            schedulePaused=ConfigBool("schedule_paused");scheduleLastTrigger=ConfigString("schedule_last_trigger_date");
            ReloadGroupTask();
        }
        static void MarkStartupReviewed()
        {
            string path=Path.Combine(BaseDir,"startup_state.json");var state=SparkStorage.Json().Deserialize<Dictionary<string,string>>(File.ReadAllText(path));state["review_version"]=AppVersion;SparkStorage.WriteJson(path,state);
        }
        static string BrowserPath(string browser)
        {
            string candidate=BrowserTarget(browser)[0];
            if(File.Exists(candidate))return Path.GetFullPath(candidate);
            foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
                try{string path=Path.Combine(folder.Trim('"'),candidate);if(File.Exists(path))return path;}catch{}
            return "";
        }
        static string RuntimeVersion(){try{return CoreWebView2Environment.GetAvailableBrowserVersionString()??"";}catch{return "";}}
        static bool DependenciesMissing(){return !DataLocation.CanWrite(BaseDir)||RuntimeVersion().Length==0||BrowserPath(GetBrowserChoice()).Length==0||FindBackend().Length==0&&FindPython().Length==0;}
        static void ShowEnvironment()
        {
            using(var dialog=new EnvironmentForm(BaseDir,ProgramDir,dataLocation!=null?dataLocation.Notice:"",GetBrowserChoice(),SaveBrowserChoice,RuntimeVersion,BrowserPath,schedulePaused))dialog.ShowDialog(mainForm);
            MarkStartupReviewed();environmentReady=!DependenciesMissing();if(mainForm!=null){RefreshDashboard();RefreshEnvironmentBanner();}
        }
        static void RepairAutostart()
        {
            try{using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){
                if(key==null||key.GetValue("DouyinSpark")==null)return;
                string expected=QuoteArg(Path.Combine(ProgramDir,"抖音续火花.exe"))+" --minimized";
                if(Convert.ToString(key.GetValue("DouyinSpark"))!=expected){key.SetValue("DouyinSpark",expected);Log("已修复开机自启到当前软件位置。");}
            }}catch(Exception ex){Log("自启路径未能更新："+ex.Message);}
        }
        static void TickSchedule(DateTime now)
        {
            if(schedulePaused||string.IsNullOrEmpty(scheduleTime)||nextScheduled==DateTime.MaxValue)return;
            if(now>=nextScheduled){bool due=SchedulePolicy.Due(now,nextScheduled,schedulePaused,scheduleLastTrigger);nextScheduled=SchedulePolicy.Next(now,scheduleTime);if(due)privateQueueUntil=now.AddMinutes(10);else Log("已跳过错过的定时，等待下一次计划时间。");}
            if(privateQueueUntil==DateTime.MinValue)return;
            if(now>privateQueueUntil){privateQueueUntil=DateTime.MinValue;Log("私信定时排队超过10分钟，本次跳过。");return;}
            if(!btnStart.Enabled||SendCooling||importingFollowing!=0||groupTaskRunning)return;
            privateQueueUntil=DateTime.MinValue;
            if(BrowserPath(GetBrowserChoice()).Length==0){Log("定时未发送：发送浏览器不可用，请打开环境与数据检查。");return;}
            // Persist before dispatch so a restart cannot repeat today's attempt.
            var config=ReadConfig();config["schedule_last_trigger_date"]=now.ToString("yyyy-MM-dd");SparkStorage.WriteJson(Path.Combine(BaseDir,"config.json"),config);scheduleLastTrigger=now.ToString("yyyy-MM-dd");
            Log("定时到点，开始发送私信。");DoStart();
        }
    }

    class EnvironmentForm:ThemedForm
    {
        readonly string root,programRoot,notice;readonly Action<string> saveBrowser;readonly Func<string> runtime;readonly Func<string,string> browserPath;
        readonly Label storageInfo,runtimeInfo,browserInfo,footer;readonly RoundButton install;readonly ComboBox browsers;readonly ProgressBar progress;
        bool installing;
        public EnvironmentForm(string dataRoot,string binaryRoot,string message,string choice,Action<string> save,Func<string> runtimeCheck,Func<string,string> browserCheck,bool paused)
        {
            root=dataRoot;programRoot=binaryRoot;notice=message;saveBrowser=save;runtime=runtimeCheck;browserPath=browserCheck;
            Text="开始使用 · 环境与数据";Size=new Size(800,670);MinimumSize=new Size(760,650);StartPosition=FormStartPosition.CenterScreen;MaximizeBox=false;MinimizeBox=false;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(26),ColumnCount=1,RowCount=6};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,82));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,116));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,106));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,116));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));Controls.Add(layout);
            var head=new Panel{Dock=DockStyle.Fill,Tag="canvas"};head.Controls.Add(new Label{Text="先准备好，再安心开始",Font=AppPalette.Font(20,true),AutoSize=true,Location=new Point(0,0)});head.Controls.Add(new Label{Text="后端已随软件提供；缺少的组件在这里处理，无需自己找安装教程。",Font=AppPalette.Font(9),AutoSize=true,Location=new Point(2,46),Tag="muted"});layout.Controls.Add(head,0,0);
            storageInfo=new Label{AutoEllipsis=true,Font=AppPalette.Font(9),Location=new Point(18,42),Size=new Size(500,54),Tag="muted"};
            var data=Card("01   数据保存",storageInfo);var open=new RoundButton{Text="打开文件夹",Primary=false,Location=new Point(548,16),Size=new Size(128,36)};open.Click+=delegate{Process.Start("explorer.exe",Program.QuoteArg(root));};data.Controls.Add(open);layout.Controls.Add(data,0,1);
            runtimeInfo=new Label{Font=AppPalette.Font(9),Location=new Point(18,44),Size=new Size(500,42),Tag="muted"};var web=Card("02   内嵌网页",runtimeInfo);install=new RoundButton{Text="一键准备",Location=new Point(548,16),Size=new Size(128,36)};install.Click+=async delegate{if(installing)return;installing=true;ControlBox=false;install.Enabled=false;progress.Visible=true;progress.Style=ProgressBarStyle.Marquee;progress.Style=ProgressBarStyle.Marquee;footer.Text="阶段2/3 · 正在联网安装 WebView2；耗时取决于网络，不会静默处理。";try{Program.StartupEvent("webview2_install_requested");await RuntimeInstaller.InstallAsync(programRoot);Program.StartupEvent("webview2_installer_completed");footer.Text=runtime().Length>0?"网页组件已准备好，可继续使用。":"安装器已结束，暂未检测到 Runtime。可重新检查或打开微软官方下载页。";}catch(Exception ex){Program.StartupEvent("webview2_install_failed | "+ex.Message);footer.Text="准备未完成："+ex.Message;MessageBox.Show(this,ex.Message+"\n请检查网络后重试，或点击微软下载页。","组件准备未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{installing=false;ControlBox=true;progress.Visible=false;RefreshChecks();}};web.Controls.Add(install);layout.Controls.Add(web,0,2);
            browserInfo=new Label{Font=AppPalette.Font(9),Location=new Point(18,72),Size=new Size(655,24),Tag="muted"};var send=Card("03   发送浏览器",browserInfo);browsers=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Font=AppPalette.Font(9),Location=new Point(18,38),Size=new Size(322,30)};browsers.Items.AddRange(new object[]{"Microsoft Edge","Google Chrome","夸克浏览器"});browsers.SelectedIndex=choice=="msedge"?0:choice=="chrome"?1:2;
            var choose=new RoundButton{Text="使用所选",Primary=false,Location=new Point(548,30),Size=new Size(128,36)};choose.Click+=delegate{string key=SelectedBrowser();if(browserPath(key).Length==0){footer.Text="没有找到所选浏览器。请改选已安装的浏览器；可先进入软件管理联系人。";return;}saveBrowser(key);footer.Text="已保存发送浏览器。首次发送前，请完成该浏览器内的软件专用登录。";RefreshChecks();};send.Controls.AddRange(new Control[]{browsers,choose});layout.Controls.Add(send,0,3);
            footer=new Label{Dock=DockStyle.Fill,Font=AppPalette.Font(9),Tag="muted",Padding=new Padding(2,10,2,0),Text=paused?"检测到首次确认或迁移，已有定时已暂停。进入软件后，在定时设置中确认启用；不会立即补发。":"登录状态由你在软件内确认。即使暂缺组件，也可先管理名单和查看历史。"};layout.Controls.Add(footer,0,4);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Tag="canvas"};var enter=new RoundButton{Text="进入软件",Size=new Size(132,42)};enter.Click+=delegate{Close();};var retry=new RoundButton{Text="重新检查",Primary=false,Size=new Size(112,42)};retry.Click+=delegate{RefreshChecks();};var download=new RoundButton{Text="微软下载页",Primary=false,Size=new Size(124,42)};download.Click+=delegate{Process.Start(RuntimeInstaller.DownloadPage);};progress=new ProgressBar{Size=new Size(100,10),Visible=false,Margin=new Padding(8,16,8,0)};bottom.Controls.AddRange(new Control[]{enter,retry,download,progress});layout.Controls.Add(bottom,0,5);
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(installing)e.Cancel=true;};RefreshChecks();
        }
        CardSurface Card(string title,Control info){var card=new CardSurface{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10)};card.Controls.Add(new Label{Text=title,Font=AppPalette.Font(11,true),AutoSize=true,Location=new Point(18,13)});card.Controls.Add(info);return card;}
        string SelectedBrowser(){return browsers.SelectedIndex==0?"msedge":browsers.SelectedIndex==1?"chrome":"quark";}
        void RefreshChecks()
        {
            storageInfo.Text=(DataLocation.CanWrite(root)?"可写 · ":"不可写 · ")+root+"\r\n"+(notice.Length>0?notice:"便携模式：数据随完整软件文件夹一起保留。");
            string version=runtime();runtimeInfo.Text=version.Length>0?"已就绪 · WebView2 "+version:"尚未安装 Runtime · 点击一键准备，软件将调用微软安装器联网安装。";install.Visible=version.Length==0;install.Enabled=!installing;
            string path=browserPath(SelectedBrowser());browserInfo.Text=path.Length>0?"已找到 · "+path:"未找到该浏览器，请选择已安装的 Edge、Chrome 或夸克。";
        }
    }
}
