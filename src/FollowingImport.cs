using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DouyinSpark
{
    public class FollowingReport
    {
        public string status { get; set; }
        public string message { get; set; }
        public string reason { get; set; }
        public List<FriendRecord> items { get; set; }
        public int count { get; set; }
        public static FollowingReport Read(string path)
        {
            var report=SparkStorage.Json().Deserialize<FollowingReport>(File.ReadAllText(path,System.Text.Encoding.UTF8));
            if(report==null || !new[]{"success","partial","empty","login_required","failed","cancelled"}.Contains(report.status))
                throw new InvalidDataException("读取结果无效，请重试。原名单未修改。");
            report.items=report.items??new List<FriendRecord>();
            if(report.count!=report.items.Count || report.items.Any(f=>f==null||!FriendRecord.ValidUrl(f.url)))
                throw new InvalidDataException("读取结果校验失败，原名单未修改。");
            if((report.status=="success" || report.status=="partial") && report.items.Count==0)
                throw new InvalidDataException("未读取到可导入对象，原名单未修改。");
            return report;
        }
    }

    partial class Program
    {
        static int importingFollowing;
        static void StartFollowingImport()
        {
            if(!btnStart.Enabled) { MessageBox.Show(mainForm,"发送任务正在运行，请等结束后再读取关注列表。","添加好友"); return; }
            if(Interlocked.CompareExchange(ref importingFollowing,1,0)!=0) return;
            string id=Guid.NewGuid().ToString("N");
            string folder=Path.Combine(BaseDir,"logs");
            try {Directory.CreateDirectory(folder);} catch(Exception ex) {Interlocked.Exchange(ref importingFollowing,0);MessageBox.Show(mainForm,"无法创建读取任务："+ex.Message,"添加好友");return;}
            string resultPath=Path.Combine(folder,"following-"+id+".json");
            string cancelPath=Path.Combine(folder,"following-"+id+".cancel");
            FollowingReport report=null;
            bool finished=false, cancelRequested=false, retry=false, manual=false;
            var dialog=new ThemedForm {Text="读取关注列表",ClientSize=new Size(600,300),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false};
            var title=new Label {Text="登录后，选择要添加的好友",Font=AppPalette.Font(15,true),Location=new Point(24,24),AutoSize=true};
            var guide=new Label {Text="请在弹出的软件专用浏览器中扫码登录。\n它与日常 Edge / 夸克的登录状态相互独立。\n可在浏览器中手动打开自己的关注列表。",Location=new Point(24,65),Size=new Size(550,72),Font=AppPalette.Font(10)};
            var state=new Label {Text="正在启动浏览器…",Location=new Point(24,145),Size=new Size(550,55),Font=AppPalette.Font(10)};
            var bar=new ProgressBar {Location=new Point(24,205),Size=new Size(550,8),Style=ProgressBarStyle.Marquee};
            var cancel=new RoundButton {Text="取消读取",Location=new Point(430,235),Size=new Size(144,40),Primary=false};
            Action requestCancel=delegate {
                if(cancelRequested) return;
                try { File.WriteAllText(cancelPath,"cancel"); cancelRequested=true; cancel.Enabled=false; state.Text="正在结束读取，请稍候；原名单不会修改。"; }
                catch(Exception ex) { MessageBox.Show(dialog,ex.Message,"取消失败"); }
            };
            cancel.Click+=delegate {requestCancel();};
            dialog.FormClosing+=delegate(object sender,FormClosingEventArgs e) {if(!finished){e.Cancel=true;requestCancel();}};
            dialog.Controls.AddRange(new Control[]{title,guide,state,bar,cancel});
            dialog.Shown+=delegate {
                new Thread(delegate() {
                    try {
                        RunBackend("--fetch-following --following-result "+QuoteArg(resultPath)+" --cancel-file "+QuoteArg(cancelPath),delegate(string line) {
                            if(!line.StartsWith("FOLLOWING|")) return;
                            var data=SparkStorage.Json().Deserialize<Dictionary<string,object>>(line.Substring(10));
                            object value; if(!data.TryGetValue("message",out value)) return;
                            string message=Convert.ToString(value);
                            dialog.BeginInvoke((Action)delegate {if(!cancelRequested)state.Text=message;});
                        });
                        report=FollowingReport.Read(resultPath);
                        // Diagnostic summary contains no recipient list or login credentials.
                        SparkStorage.AppendLog(Path.Combine(folder,"following_import.log"),DateTime.Now.ToString("s")+" | "+report.status+" | count="+report.count+" | "+report.reason+Environment.NewLine);
                    }
                    catch(Exception ex) {
                        report=new FollowingReport {status="failed",message="读取未完成。请检查所选浏览器、网络，以及是否完整解压了便携包。\n原名单已保留，可重试或手动添加；详细原因见“查看诊断”。",reason=ex.ToString(),items=new List<FriendRecord>()};
                        Log("关注列表导入失败："+ex.Message);
                    }
                    finally {
                        try {dialog.BeginInvoke((Action)delegate {finished=true;dialog.Close();});} catch { }
                    }
                }) {IsBackground=true}.Start();
            };
            try {
                dialog.ShowDialog(mainForm);
                if(cancelRequested || report==null || report.status=="cancelled") { Log("已取消读取关注列表，原名单未修改。"); return; }
                Log(report.message);
                if(report.status=="partial") {
                    if(MessageBox.Show(mainForm,report.message+"\n\n已读取 "+report.count+" 人。是否继续勾选这些对象？","关注列表不完整",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes) return;
                }
                if(report.status=="success" || report.status=="partial") {
                    ShowFollowingPick(report.items.Select(f=>new[]{f.name,f.url}).ToList(),new HashSet<string>());
                    LoadLogs();
                } else if(report.status=="empty") MessageBox.Show(mainForm,report.message,"读取关注列表",MessageBoxButtons.OK,MessageBoxIcon.Information);
                else {
                    using(var problem=new ThemedForm {Text="关注列表未能读取",ClientSize=new Size(610,300),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false}) {
                        var info=new TextBox {Text=report.message,Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,ScrollBars=ScrollBars.Vertical,Location=new Point(24,24),Size=new Size(560,175)};
                        var again=new RoundButton {Text="重试读取",Location=new Point(24,225),Size=new Size(150,42),Primary=true};
                        var add=new RoundButton {Text="手动添加",Location=new Point(190,225),Size=new Size(150,42),Primary=false};
                        var diagnostic=new RoundButton {Text="查看诊断",Location=new Point(356,225),Size=new Size(150,42),Primary=false};
                        again.Click+=delegate {retry=true;problem.Close();}; add.Click+=delegate {manual=true;problem.Close();};
                        diagnostic.Click+=delegate {
                            using(var detail=new ThemedForm {Text="本次读取诊断（可截图反馈）",ClientSize=new Size(680,370),StartPosition=FormStartPosition.CenterParent}) {
                                detail.Controls.Add(new TextBox {Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Both,Text="状态："+report.status+"\r\n"+report.message+"\r\n\r\n"+(report.reason??"未获得后端诊断，请确认完整解压了便携包。")});
                                detail.ShowDialog(problem);
                            }
                        };
                        problem.Controls.AddRange(new Control[]{info,again,add,diagnostic});problem.ShowDialog(mainForm);
                    }
                }
            }
            finally {
                dialog.Dispose(); Interlocked.Exchange(ref importingFollowing,0);
                foreach(string path in new[]{resultPath,cancelPath}) {try {File.Delete(path);}catch{}}
            }
            if(retry) StartFollowingImport();
            else if(manual) {AddFriend();LoadLogs();}
        }
    }
}
