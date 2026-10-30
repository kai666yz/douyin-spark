using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        static bool privateRunActive,privateStopRequested;
        static string privateStopFile="";
        static int privateTotal,privateDone,privateSucceeded,privateUnconfirmed;
        static Dictionary<string,string> privateResults=new Dictionary<string,string>();
        static DateTime sendCooldown=DateTime.MinValue;
        static RoundButton btnStop,taskStart,taskStop;
        static bool SendCooling {get{return DateTime.UtcNow<sendCooldown;}}
        static void RefreshSendButtons()
        {
            int seconds=Math.Max(0,(int)Math.Ceiling((sendCooldown-DateTime.UtcNow).TotalSeconds));
            if(btnStart!=null){btnStart.Enabled=!privateRunActive&&seconds==0;btnStart.Text=privateRunActive?(privateStopRequested?"正在停止…":"正在发送…"):seconds>0?"稍候 "+seconds+" 秒":"开始发送";}
            if(taskStart!=null){taskStart.Enabled=btnStart.Enabled;taskStart.Text=btnStart.Text;}
            bool stoppable=(privateRunActive&&!privateStopRequested)||groupTaskRunning;
            if(btnStop!=null){btnStop.Enabled=stoppable;btnStop.Text=privateRunActive&&privateStopRequested?"停止已请求":"停止发送";}
            if(taskStop!=null){taskStop.Enabled=stoppable;taskStop.Text=btnStop!=null?btnStop.Text:"停止发送";}
            RefreshPrivateMessageStatus();
        }
        static void RequestStopSending()
        {
            if(!privateRunActive){if(groupTaskRunning&&groupWindow!=null)groupWindow.RequestStop();return;}
            if(privateStopRequested)return;
            int pending=Math.Max(0,privateTotal-privateDone)+(groupTaskRunning&&groupWindow!=null?groupWindow.PendingCount:0);
            string counts=privateTotal>0?"已发（页面确认） "+(privateSucceeded+(groupTaskRunning&&groupWindow!=null?groupWindow.SentCount:0))+" / 未处理 "+pending+"\n待核对 "+(privateUnconfirmed+(groupTaskRunning&&groupWindow!=null?groupWindow.AwaitingCount:0))+" / 已处理 "+privateDone:"正在核对本轮名单";
            if(MessageBox.Show(mainForm,counts+"\n当前联系人处理结束后停止，其余标记为已取消。\n已发送的消息不会撤回；停止后需等待10秒才可再次开始。","停止发送",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            try{File.WriteAllText(privateStopFile,"stop");privateStopRequested=true;if(groupTaskRunning&&groupWindow!=null)groupWindow.StopWithoutConfirmation();Log("已请求停止："+counts+"，等待当前联系人结束。");RefreshSendButtons();}
            catch(Exception ex){MessageBox.Show(mainForm,"停止请求未能保存，请重试。\n"+ex.Message,"未停止");}
        }
        static async void StartPrivateRun()
        {
            if(privateRunActive||SendCooling)return;
            privateRunActive=true;privateStopRequested=false;privateDone=privateTotal=privateSucceeded=privateUnconfirmed=0;privateResults.Clear();
            privateStopFile=Path.Combine(BaseDir,"logs","private_stop_"+Guid.NewGuid().ToString("N")+".flag");RefreshSendButtons();
            progress.Reset();progress.Done=progress.Total=0;progress.Info="检查发送名单…";
            Log("开始私信任务 · 支持当前联系人结束后停止。");
            int exit=-1;
            try{
                var psi=BackendInfo("--stop-after-current "+QuoteArg(privateStopFile)+(fixedMessage.Length>0?" --message "+QuoteArg(fixedMessage):""));
                await Task.Run(delegate{
                    using(var process=new Process{StartInfo=psi}){
                        process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null)return;string line=e.Data;
                            if(line.StartsWith("TASK|PLANNED|")){int count;if(int.TryParse(line.Substring(13),out count))mainForm.BeginInvoke((Action)delegate{privateTotal=count;progress.Total=count;});}
                            else if(line.StartsWith("RESULT|")){var parts=line.Split('|');if(parts.Length==3)mainForm.BeginInvoke((Action)delegate{privateResults[parts[1]]=parts[2];privateSucceeded=privateResults.Values.Count(x=>x=="OK");privateUnconfirmed=privateResults.Values.Count(x=>x=="UNCONFIRMED");});}
                            else if(line.StartsWith("PROGRESS|")){var parts=line.Split('|');int done,total;if(parts.Length>=5&&int.TryParse(parts[1],out done)&&int.TryParse(parts[2],out total))mainForm.BeginInvoke((Action)delegate{privateDone=done;privateTotal=total;progress.Done=done;progress.Total=total;progress.Info=parts[3]+" · "+parts[4];});}
                            else {Log(line);if(line.StartsWith("ATTENTION|"))mainForm.BeginInvoke((Action)delegate{if(tray!=null)tray.ShowBalloonTip(10000,"需要人工处理",line.Split('|')[line.Split('|').Length-1],ToolTipIcon.Warning);});}
                            try{SparkStorage.AppendLog(Path.Combine(BaseDir,"logs","run_out.txt"),line+Environment.NewLine);}catch{}
                        };
                        process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null){Log("[!] "+e.Data);try{SparkStorage.AppendLog(Path.Combine(BaseDir,"logs","run_err.txt"),e.Data+Environment.NewLine);}catch{}}};
                        process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();process.WaitForExit();exit=process.ExitCode;
                    }
                });
            }catch(Exception ex){Log("发送任务未完成："+ex.Message);MessageBox.Show(mainForm,"发送未完成，已保存的记录保留。\n"+ex.Message,"发送任务");}
            finally{
                privateRunActive=false;if(privateStopRequested)sendCooldown=DateTime.UtcNow.AddSeconds(10);
                progress.MarkFinished(exit!=0||privateStopRequested);progress.Info=privateStopRequested?"已停止 · 未处理联系人已取消":exit==0?"本轮结束，请核对记录":"运行异常，请查看日志";
                try{File.Delete(privateStopFile);}catch{}Log(privateStopRequested?"本轮已停止，10秒后可再次开始。":"私信任务退出码 "+exit);
                RefreshSendButtons();LoadLogs();RefreshDashboard();if(exiting&&mainForm!=null)mainForm.Close();
            }
        }
    }
}
