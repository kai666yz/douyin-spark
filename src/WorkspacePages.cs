using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        static Panel workspaceDeck;
        static Control contactsWorkspace,tasksWorkspace,settingsWorkspace;
        static Dictionary<string,RoundButton> workspaceNavigation=new Dictionary<string,RoundButton>();
        static Label privateTaskInfo,groupTaskInfo;
        static void ShowWorkspace(string key)
        {
            contactsWorkspace.Visible=key=="联系人";tasksWorkspace.Visible=key=="消息任务";settingsWorkspace.Visible=key=="设置";
            foreach(var item in workspaceNavigation){item.Value.Selected=item.Key==key;item.Value.Invalidate();}
            if(key=="消息任务")RefreshTaskWorkspace();
        }
        static void RefreshTaskWorkspace()
        {
            if(privateTaskInfo!=null)privateTaskInfo.Text=(fixedMessage.Length>0?"统一固定消息":"分类语录")+"  ·  "+(string.IsNullOrEmpty(scheduleTime)?"未设置定时":schedulePaused?"定时已暂停":"每天 "+scheduleTime)+"\r\n私信通过发送浏览器执行，今日去重规则保留。";
            if(groupTaskInfo!=null)groupTaskInfo.Text="已选 "+groupPlan.targets.Count+" 个群  ·  "+(string.IsNullOrEmpty(groupPlan.schedule)?"未设置定时":groupPlan.paused?"定时已暂停":"每天 "+groupPlan.schedule)+"\r\n复用软件内登录；按群标识核对，已提交与送达分开记录。";
        }
        static TableLayoutPanel Page(string title,string description)
        {
            var page=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(30,26,30,24),Tag="canvas"};
            page.RowStyles.Add(new RowStyle(SizeType.Absolute,86));page.RowStyles.Add(new RowStyle(SizeType.Absolute,204));page.RowStyles.Add(new RowStyle(SizeType.Absolute,204));page.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var head=new Panel{Dock=DockStyle.Fill,Tag="canvas"};var t=ViewLabel(title,22,true);t.SetBounds(0,0,500,38);var d=ViewLabel(description,10,false,true);d.SetBounds(1,45,760,28);head.Controls.AddRange(new Control[]{t,d});page.Controls.Add(head,0,0);return page;
        }
        static CardSurface FeatureCard(string title,string description,out Label info,params RoundButton[] buttons)
        {
            var card=new CardSurface{Dock=DockStyle.Fill,Margin=new Padding(0,0,0,16),Padding=new Padding(22,16,22,16)};
            var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};body.RowStyles.Add(new RowStyle(SizeType.Absolute,32));body.RowStyles.Add(new RowStyle(SizeType.Percent,100));body.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
            var label=ViewLabel(title,13,true);label.Dock=DockStyle.Fill;body.Controls.Add(label,0,0);info=ViewLabel(description,9,false,true);info.Dock=DockStyle.Fill;info.Padding=new Padding(0,8,0,0);body.Controls.Add(info,0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};foreach(var button in buttons){button.Margin=new Padding(0,0,10,4);actions.Controls.Add(button);}body.Controls.Add(actions,0,2);card.Controls.Add(body);return card;
        }
        static RoundButton PageAction(string text,EventHandler click,bool primary=false)
        {var b=new RoundButton{Text=text,Primary=primary,Size=new Size(142,38),Font=AppPalette.Font(9)};b.Click+=click;return b;}
        static Control BuildTasksWorkspace()
        {
            var page=Page("消息任务","私信与群聊各自设置，消息、时间和执行记录清晰分开。");
            page.Controls.Add(FeatureCard("私信问候","",out privateTaskInfo,
                PageAction("消息内容",delegate{ShowFixedMessage();RefreshTaskWorkspace();}),PageAction("分类语录",delegate{ShowSentenceManager();}),PageAction("每日定时",delegate{ShowScheduleDialog();RefreshTaskWorkspace();}),PageAction("预览消息",delegate{DoDry();}),PageAction("发送私信",delegate{DoStart();},true)),0,1);
            page.Controls.Add(FeatureCard("群聊问候","",out groupTaskInfo,
                PageAction("群聊消息与定时",delegate{OpenGroupTask();},true),PageAction("群聊发送记录",delegate{GroupHistoryForm.Open(mainForm,BaseDir);}),PageAction("导入目标群",delegate{ShowWorkspace("联系人");StartConversationImport();})),0,2);
            var hint=ViewLabel("电脑休眠、软件退出或迁移暂停时，不追发错过的任务。同一时间的任务会排队处理。",9,false,true);hint.Dock=DockStyle.Top;hint.Height=48;page.Controls.Add(hint,0,3);RefreshTaskWorkspace();return page;
        }
        static Control BuildSettingsWorkspace()
        {
            var page=Page("设置","账号环境、数据和外观，按用途集中收纳。");page.RowStyles[1].Height=168;page.RowStyles[2].Height=168;
            Label info;page.Controls.Add(FeatureCard("账号与运行环境","检查内嵌网页、发送浏览器和保存位置；软件内登录与发送浏览器登录各自保留。",out info,
                PageAction("环境与数据",delegate{ShowEnvironment();}),PageAction("发送浏览器",delegate{ShowBrowserDialog();}),PageAction("发送登录自检",delegate{DoLogin();})),0,1);
            page.Controls.Add(FeatureCard("数据与记录","好友和任务保存在本机，私信与群聊记录分开查看。",out info,
                PageAction("打开数据目录",delegate{Process.Start("explorer.exe",QuoteArg(BaseDir));}),PageAction("导出私信历史",delegate{ExportHistory();}),PageAction("清理旧私信历史",delegate{ClearHistory();}),PageAction("群聊记录",delegate{GroupHistoryForm.Open(mainForm,BaseDir);}),PageAction("好友状态",delegate{ShowFriendStatus();})),0,2);
            page.RowStyles[3].SizeType=SizeType.Absolute;page.RowStyles[3].Height=168;
            page.Controls.Add(FeatureCard("外观与软件","浅色 / 深色跟随你的选择；开机自启按你保存的位置运行。",out info,
                PageAction("切换浅色 / 深色",delegate{ToggleAppearance();}),PageAction("开机自启",delegate{ToggleAutostart();}),PageAction("检查更新",delegate{DoCheckUpdate();})),0,3);return page;
        }
    }
}
