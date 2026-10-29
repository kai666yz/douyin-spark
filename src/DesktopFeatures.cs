using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        static NotifyIcon tray;
        static bool exiting;
        static string fixedMessage="";
        static Dictionary<string,object> ReadConfig()
        {
            string path=Path.Combine(BaseDir,"config.json");
            return File.Exists(path) ? SparkStorage.Json().Deserialize<Dictionary<string,object>>(SparkStorage.CleanJson(File.ReadAllText(path))) : new Dictionary<string,object>();
        }
        static void SaveAllConfig(string browser,string schedule)
        {
            var config=ReadConfig(); config["browser"]=browser; config["schedule"]=schedule; config["message"]=fixedMessage;
            SparkStorage.WriteJson(Path.Combine(BaseDir,"config.json"),config);
        }
        static string ConfigString(string key)
        {
            object value; return ReadConfig().TryGetValue(key,out value) ? Convert.ToString(value) : "";
        }
        static void ShowFixedMessage()
        {
            var form=new Form {Text="固定消息（手动与定时发送共用）",Size=new Size(510,280),StartPosition=FormStartPosition.CenterParent};
            var label=new Label {Text="留空使用语录池；固定消息可重复使用，但仍保留当天好友去重。",AutoSize=true,Location=new Point(15,15)};
            var box=new TextBox {Text=fixedMessage,Multiline=true,Location=new Point(15,50),Size=new Size(460,120)};
            var save=new Button {Text="保存",Location=new Point(365,185),Size=new Size(110,32)};
            save.Click+=delegate { fixedMessage=box.Text.Trim(); SaveAllConfig(GetBrowserChoice(),scheduleTime); form.Close(); Log(fixedMessage.Length>0?"已启用固定消息":"已恢复语录池模式"); };
            form.Controls.AddRange(new Control[]{label,box,save});form.ShowDialog(mainForm);
        }
        static void SetupTray(Form form,bool minimized)
        {
            tray=new NotifyIcon {Text="抖音续火花",Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath),Visible=true};
            var menu=new ContextMenuStrip();
            Action restore=delegate {form.Show();form.WindowState=FormWindowState.Normal;form.Activate();};
            menu.Items.Add("显示窗口",null,delegate {restore();});
            menu.Items.Add("退出",null,delegate {exiting=true;form.Close();});
            tray.ContextMenuStrip=menu; tray.DoubleClick+=delegate {restore();};
            form.Resize+=delegate {if(form.WindowState==FormWindowState.Minimized) form.Hide();};
            form.FormClosing+=delegate(object sender,FormClosingEventArgs e)
            {
                if(!exiting && e.CloseReason==CloseReason.UserClosing) {e.Cancel=true;form.Hide();tray.ShowBalloonTip(2000,"抖音续火花","已在托盘运行。右键托盘图标可退出。",ToolTipIcon.Info);}
            };
            form.FormClosed+=delegate {tray.Visible=false;tray.Dispose();};
            if(minimized) form.Shown+=delegate {form.Hide();};
        }
        static void ExportHistory()
        {
            if(!btnStart.Enabled) {MessageBox.Show("请等待当前发送任务结束。");return;}
            using(var dialog=new SaveFileDialog {Filter="CSV 文件|*.csv",FileName="发送历史_"+DateTime.Now.ToString("yyyyMMdd")+".csv"})
                if(dialog.ShowDialog(mainForm)==DialogResult.OK) BackendAction("--export-history "+QuoteArg(dialog.FileName));
        }
        static void ClearHistory()
        {
            if(!btnStart.Enabled) {MessageBox.Show("请等待当前发送任务结束。");return;}
            if(MessageBox.Show("清理今日之前的历史？会先备份到 logs/history_backup；保留今日记录，防止重复发送。","清理历史",MessageBoxButtons.YesNo)==DialogResult.Yes) BackendAction("--clear-history");
        }
        static void DoCheckUpdate()
        {
            Log("正在查询 GitHub Releases...");
            new System.Threading.Thread(delegate()
            {
                try
                {
                    ServicePointManager.SecurityProtocol=(SecurityProtocolType)3072;
                    string json;
                    using(var web=new WebClient()) {web.Encoding=System.Text.Encoding.UTF8;web.Headers["User-Agent"]="douyin-spark/"+AppVersion;json=web.DownloadString("https://api.github.com/repos/kai666yz/douyin-spark/releases/latest");}
                    var release=SparkStorage.Json().Deserialize<Dictionary<string,object>>(json);
                    string version=Convert.ToString(release["tag_name"]).TrimStart('v');
                    bool newer=CompareVersion(version,AppVersion)>0;
                    mainForm.BeginInvoke((Action)delegate
                    {
                        if(!newer) {MessageBox.Show("当前版本 v"+AppVersion+"，已是最新正式版。","检查更新");return;}
                        if(MessageBox.Show("发现 v"+version+"。打开官方下载页？\n更新时请保留自己的好友、配置与登录状态。","检查更新",MessageBoxButtons.YesNo)==DialogResult.Yes)
                            Process.Start("https://github.com/kai666yz/douyin-spark/releases/latest");
                    });
                }
                catch(WebException ex) {Log("在线检查失败："+ex.Message+"。可手动访问 GitHub Releases。");}
                catch(Exception ex) {Log("检查更新失败："+ex.Message);}
            }) {IsBackground=true}.Start();
        }
    }
}
