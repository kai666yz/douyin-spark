using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        static GroupTaskSettings groupPlan=new GroupTaskSettings();
        static DateTime nextGroupScheduled=DateTime.MaxValue,groupQueueUntil=DateTime.MinValue;
        static bool groupTaskRunning;
        static GroupTaskForm groupWindow;
        static bool GroupPaused {get{return groupPlan.paused&&!string.IsNullOrEmpty(groupPlan.schedule);}}
        static void ReloadGroupTask(bool resetQueue=true)
        {
            groupPlan=GroupTaskSettings.Read(BaseDir);nextGroupScheduled=groupPlan.paused?DateTime.MaxValue:SchedulePolicy.Next(DateTime.Now,groupPlan.schedule);if(resetQueue)groupQueueUntil=DateTime.MinValue;
        }
        static void OpenGroupTask(bool scheduled=false,string initialId=null)
        {
            if(groupWindow!=null&&!groupWindow.IsDisposed){if(scheduled)mainForm.BeginInvoke((Action)async delegate{await groupWindow.RunScheduled();});else ShowWorkspace("群聊消息");return;}
            if(importingFollowing!=0){MessageBox.Show(mainForm,"请先结束当前会话导入，再打开群聊网页。","群聊消息");return;}

            try{
                groupWindow=new GroupTaskForm(BaseDir,ProgramDir,false,initialId);
                groupWindow.ReadyForRun=delegate{return !SendCooling&&importingFollowing==0&&!groupTaskRunning;};
                groupWindow.RunningChanged=delegate(bool value){groupTaskRunning=value;if(!value&&groupWindow!=null&&groupWindow.RestartAfter>DateTime.UtcNow)sendCooldown=groupWindow.RestartAfter;RefreshSendButtons();if(btnGroup!=null)btnGroup.Text=value?"查看群聊进度":"群聊消息任务";RefreshDashboard();if(!value&&exiting&&mainForm!=null)mainForm.BeginInvoke((Action)delegate{mainForm.Close();});};
                groupWindow.SettingsSaved=delegate{ReloadGroupTask();RefreshTaskWorkspace();RefreshDashboard();};
                groupWindow.FormClosed+=delegate{groupWindow=null;groupTaskRunning=false;try{ReloadGroupTask(false);RefreshEnvironmentBanner();RefreshTaskWorkspace();ShowRosterDetail();}catch(Exception ex){Log("群聊设置无法读取："+ex.Message);}};
                groupWindow.TopLevel=false;groupWindow.FormBorderStyle=FormBorderStyle.None;groupWindow.Dock=DockStyle.Fill;workspaceDeck.Controls.Add(groupWindow);groupWindow.Show();if(!scheduled)ShowWorkspace("群聊消息");else{groupWindow.Visible=false;mainForm.BeginInvoke((Action)async delegate{await groupWindow.RunScheduled();});}
            }
            catch(Exception ex){groupTaskRunning=false;if(groupWindow!=null)groupWindow.Dispose();groupWindow=null;MessageBox.Show(mainForm,ex.Message,"群聊任务未完成");}
        }
        static void TickGroupSchedule(DateTime now)
        {
            if(groupPlan.paused||string.IsNullOrEmpty(groupPlan.schedule))return;
            if(now>=nextGroupScheduled){
                bool due=SchedulePolicy.Due(now,nextGroupScheduled,false,groupPlan.last_trigger_day);nextGroupScheduled=SchedulePolicy.Next(now,groupPlan.schedule);
                if(due){groupQueueUntil=now.AddMinutes(10);Log("群聊定时已到，将与私信共用单路发送名额。");}
                else Log("已跳过错过的群聊定时，等待下一次时间。");
            }
            if(groupQueueUntil==DateTime.MinValue)return;
            if(now>groupQueueUntil){groupQueueUntil=DateTime.MinValue;Log("群聊定时等待超过10分钟，本次跳过。");return;}
            if(SendCooling||importingFollowing!=0||groupTaskRunning)return;
            groupQueueUntil=DateTime.MinValue;
            try{
                if(!CheckSafetyResume(mainForm,false))return;GroupTaskSettings.Resolve(ConversationStore.Read(ConversationsPath),groupPlan.targets);
                groupPlan.last_trigger_day=now.ToString("yyyy-MM-dd");groupPlan.Save(BaseDir);
                OpenGroupTask(true);
            }catch(Exception ex){Log("群聊定时未执行："+ex.Message);}
        }
    }
}
