using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        static OverviewCard overview;
        static EmptyHistory emptyHistory;
        static RoundButton themeButton;
        static System.Windows.Forms.Timer overviewTimer;
        static ContextMenuStrip actionsMenu;
        static bool groupOverview;
        static RoundButton btnGroup,todayPrivate,todayGroups;
        static void SetOverview(bool groups)
        {
            groupOverview=groups;todayPrivate.Selected=!groups;todayGroups.Selected=groups;todayPrivate.Invalidate();todayGroups.Invalidate();
            overview.GroupMode=groups;btnStart.Visible=!groups;btnGroup.Visible=groups;RefreshDashboard();
        }

        static Label ViewLabel(string text,float size=10,bool bold=false,bool muted=false)
        {
            return new Label {Text=text,Font=AppPalette.Font(size,bold),ForeColor=muted?AppPalette.Muted:AppPalette.Text,Tag=muted?"muted":"text",BackColor=Color.Transparent,AutoSize=false,AutoEllipsis=true};
        }
        static RoundButton SmallAction(string text,string icon,EventHandler click)
        {
            var button=new RoundButton {Text=text,IconName=icon,Primary=false,Size=new Size(84,32),Font=AppPalette.Font(9),Margin=new Padding(6,0,0,0)};
            button.Click+=click;return button;
        }
        static Form BuildMainWindow() { return BuildChatWorkspace(); }
        static void ToggleAppearance()
        {
            AppPalette.Dark=!AppPalette.Dark;
            var config=ReadConfig();config["theme"]=AppPalette.Dark?"dark":"light";SparkStorage.WriteJson(Path.Combine(BaseDir,"config.json"),config);
            foreach(Form form in Application.OpenForms)AppPalette.Apply(form);
            if(themeButton!=null)themeButton.Text=AppPalette.Dark?"浅色模式":"深色模式";
            if(actionsMenu!=null){actionsMenu.BackColor=AppPalette.Surface;actionsMenu.ForeColor=AppPalette.Text;}
            FormatHistory();
        }
        static void RefreshDashboard()
        {
            if(overview==null || overview.IsDisposed)return;
            try
            {
                overview.Summary=TodaySummary.Build(SparkStorage.ReadFriends(FriendsPath),SparkStorage.ReadCsv(Path.Combine(BaseDir,"logs","spark_log.csv")),DateTime.Now);
                DateTime when;
                overview.NextText=schedulePaused&&!string.IsNullOrEmpty(scheduleTime)?"定时已暂停 · 等待确认":DateTime.TryParseExact(scheduleTime,"HH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out when)
                    ? "下次定时 · "+(DateTime.Now.TimeOfDay<when.TimeOfDay?"今天 ":"明天 ")+scheduleTime : "尚未设置定时";
                overview.ModeText=fixedMessage.Length>0?"固定消息":"分类语录";
                if(groupOverview){var attempts=GroupJournal.Read(BaseDir,DateTime.Now);overview.Summary=GroupSummary.Build(groupPlan.targets.Union(attempts.Select(x=>x.group_id)),attempts,DateTime.Now);overview.NextText=string.IsNullOrEmpty(groupPlan.schedule)?"尚未设置群聊定时":groupPlan.paused?"群聊定时已暂停":"群聊每日 · "+groupPlan.schedule;overview.ModeText="独立群聊消息";}
                overview.AccessibleName="今日已处理 "+overview.Summary.Processed+" / "+overview.Summary.Total+"，成功 "+overview.Summary.Succeeded+"，失败 "+overview.Summary.Failed+"，待发 "+overview.Summary.Pending+"，待确认 "+overview.Summary.Unconfirmed+"。"+overview.NextText;
                overview.StateText=privateRunActive&&groupTaskRunning?"单路发送 · 按会话交替":privateRunActive?(privateStopRequested?"等待当前联系人结束":"私信发送中"):groupTaskRunning?"群聊发送中":SendCooling?"停止冷却中":"准备就绪";overview.Busy=privateRunActive||groupTaskRunning;overview.Invalidate();RefreshRoster();RefreshTaskWorkspace();RefreshEnvironmentBanner();
            }
            catch(Exception){overview.NextText="记录正在更新…";overview.Invalidate();}
        }
        static void FormatHistory()
        {
            if(dgv==null || dgv.IsDisposed)return;
            AppPalette.StyleGrid(dgv);
            FilterRosterHistory();
            if(dgv.Columns.Contains("好友"))dgv.Columns["好友"].Visible=false;
            if(dgv.Columns.Contains("主页"))dgv.Columns["主页"].Visible=false;
            if(dgv.Columns.Contains("说明"))dgv.Columns["说明"].Visible=false;
            if(dgv.Columns.Contains("时间")){dgv.Columns["时间"].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;dgv.Columns["时间"].Width=72;}
            if(dgv.Columns.Contains("好友"))dgv.Columns["好友"].FillWeight=16;
            if(dgv.Columns.Contains("消息"))dgv.Columns["消息"].FillWeight=44;
            if(dgv.Columns.Contains("状态")){dgv.Columns["状态"].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;dgv.Columns["状态"].Width=76;dgv.Columns["状态"].DisplayIndex=5;}
            foreach(DataGridViewRow row in dgv.Rows)if(row.Cells.Count>=6)row.Cells["状态"].ToolTipText=Convert.ToString(row.Cells["说明"].Value);
            if(emptyHistory!=null){emptyHistory.Visible=dgv.Rows.Count==0;if(emptyHistory.Visible)emptyHistory.BringToFront();emptyHistory.Invalidate();}
        }
        static void PaintHistoryStatus(object sender,DataGridViewCellPaintingEventArgs e)
        {
            if(e.RowIndex<0 || e.ColumnIndex<0 || dgv.Columns[e.ColumnIndex].Name!="状态")return;
            string raw=Convert.ToString(e.Value),label="失败";Color foreground=AppPalette.Error,background=AppPalette.ErrorSoft;
            if(raw=="OK"){label="成功";foreground=AppPalette.Success;background=AppPalette.SuccessSoft;}
            else if(raw=="UNCONFIRMED" || raw=="CAPTCHA" || raw=="LOGIN_EXPIRED") {label=raw=="UNCONFIRMED"?"待确认":raw=="CAPTCHA"?"待验证":"需登录";foreground=AppPalette.Warning;background=AppPalette.WarningSoft;}
            else if(raw=="CANCELLED" || raw=="STOPPED"){label="已取消";foreground=AppPalette.Muted;background=AppPalette.Soft;}
            e.PaintBackground(e.CellBounds,true);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            var box=new RectangleF(e.CellBounds.X+4,e.CellBounds.Y+6,Math.Min(88,e.CellBounds.Width-8),22);
            using(var path=AppPalette.Round(box,11))using(var brush=new SolidBrush(background))e.Graphics.FillPath(brush,path);
            using(var brush=new SolidBrush(foreground))using(var font=AppPalette.Font(8.5f))
            {e.Graphics.FillEllipse(brush,box.X+8,box.Y+8,6,6);e.Graphics.DrawString(label,font,brush,box.X+20,box.Y+3);}
            e.Handled=true;
        }
    }
}
