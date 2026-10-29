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

        static Label ViewLabel(string text,float size=10,bool bold=false,bool muted=false)
        {
            return new Label {Text=text,Font=AppPalette.Font(size,bold),ForeColor=muted?AppPalette.Muted:AppPalette.Text,Tag=muted?"muted":"text",BackColor=Color.Transparent,AutoSize=false,AutoEllipsis=true};
        }
        static RoundButton SmallAction(string text,string icon,EventHandler click)
        {
            var button=new RoundButton {Text=text,IconName=icon,Primary=false,Size=new Size(84,32),Font=AppPalette.Font(9),Margin=new Padding(6,0,0,0)};
            button.Click+=click;return button;
        }
        static Form BuildMainWindow()
        {
            try{AppPalette.Dark=ConfigString("theme")=="dark";}catch{AppPalette.Dark=false;}
            var form=new CardForm {Text="抖音续火花",Font=AppPalette.Font(),StartPosition=FormStartPosition.CenterScreen,MinimumSize=new Size(980,700)};
            var screen=Screen.PrimaryScreen.WorkingArea;
            form.Size=new Size(Math.Min(1180,screen.Width-40),Math.Min(850,screen.Height-40));
            mainForm=form;form.SuspendLayout();
            var body=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(28,20,28,22),ColumnCount=1,RowCount=5,Tag="canvas"};
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute,188));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute,72));
            body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute,132));
            var sidebar=new Panel {Dock=DockStyle.Left,Width=196,Padding=new Padding(16),Tag="sidebar"};
            form.Controls.Add(body);form.Controls.Add(sidebar);

            var badge=new KittyBadge {Location=new Point(18,26),Size=new Size(44,44)};
            var logo=ViewLabel("抖音续火花",12,true);logo.SetBounds(68,27,120,24);
            var version=ViewLabel("v"+AppVersion+"  ·  每日问候",8,false,true);version.SetBounds(68,55,120,22);
            var nav=new FlowLayoutPanel {Location=new Point(12,118),Size=new Size(172,300),FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.Transparent,Tag="sidebar"};
            Action<string,string,EventHandler,bool> addNav=delegate(string text,string icon,EventHandler action,bool selected)
            {
                var button=new RoundButton {Text=text,IconName=icon,Navigation=true,Primary=false,Selected=selected,Size=new Size(172,44),Margin=new Padding(0,0,0,8),Font=AppPalette.Font(10)};
                button.Click+=action;nav.Controls.Add(button);
            };
            addNav("工作台","home",delegate{RefreshDashboard();},true);
            addNav("添加好友","people",delegate{ImportFollowing();},false);
            addNav("好友状态","stats",delegate{ShowFriendStatus();},false);
            addNav("语录管理","chat",delegate{ShowSentenceManager();RefreshDashboard();},false);
            actionsMenu=new ContextMenuStrip {Font=AppPalette.Font(10)};
            actionsMenu.Items.Add("预览句子",null,delegate{DoDry();});
            actionsMenu.Items.Add("固定消息设置",null,delegate{ShowFixedMessage();RefreshDashboard();});
            actionsMenu.Items.Add("管理好友",null,delegate{AddFriend();LoadLogs();});
            actionsMenu.Items.Add("登录自检",null,delegate{DoLogin();});
            actionsMenu.Items.Add("打开抖音私信",null,delegate{OpenMessages();});
            actionsMenu.Items.Add(new ToolStripSeparator());
            actionsMenu.Items.Add("定时设置",null,delegate{ShowScheduleDialog();RefreshDashboard();});
            actionsMenu.Items.Add("开机自启",null,delegate{ToggleAutostart();});
            actionsMenu.Items.Add("浏览器选择",null,delegate{ShowBrowserDialog();});
            actionsMenu.Items.Add("切换浅色 / 深色",null,delegate{ToggleAppearance();});
            actionsMenu.Items.Add(new ToolStripSeparator());
            actionsMenu.Items.Add("导出历史 CSV",null,delegate{ExportHistory();});
            actionsMenu.Items.Add("清理旧历史",null,delegate{ClearHistory();});
            actionsMenu.Items.Add("启动 Steam 游戏",null,delegate{try{Process.Start("steam://rungameid/431960");}catch(Exception ex){Log(ex.Message);}});
            addNav("更多功能","more",delegate(object sender,EventArgs e){var c=(Control)sender;actionsMenu.Show(c,new Point(c.Width+6,0));},false);
            themeButton=new RoundButton {Text=AppPalette.Dark?"浅色模式":"深色模式",IconName="moon",Navigation=true,Primary=false,Size=new Size(164,40),Margin=new Padding(0,0,0,2),Font=AppPalette.Font(9)};
            themeButton.Click+=delegate{ToggleAppearance();};
            var update=new RoundButton {Text="检查更新",IconName="refresh",Navigation=true,Primary=false,Size=new Size(164,40),Margin=new Padding(0,0,0,2),Font=AppPalette.Font(9)};
            update.Click+=delegate{DoCheckUpdate();};
            lblStatus=ViewLabel("就绪 · 数据保存在本机",8,false,true);lblStatus.Size=new Size(152,40);lblStatus.Margin=new Padding(8,8,0,0);
            var footer=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=146,FlowDirection=FlowDirection.TopDown,WrapContents=false,Tag="sidebar"};
            footer.Controls.AddRange(new Control[]{themeButton,update,lblStatus});
            sidebar.Controls.AddRange(new Control[]{badge,logo,version,nav,footer});

            var heading=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty,Tag="canvas"};
            var title=ViewLabel("把问候，留给重要的人。",18,true);title.SetBounds(0,0,560,34);
            var subtitle=ViewLabel("今天的进度、发送结果和下一次安排，都在这里。",9,false,true);subtitle.SetBounds(1,37,580,21);
            heading.Controls.AddRange(new Control[]{title,subtitle});body.Controls.Add(heading,0,0);

            overview=new OverviewCard {Dock=DockStyle.Fill,Margin=new Padding(0,0,0,8)};
            btnStart=new RoundButton {Text="开始发送",IconName="play",Primary=true,CornerRadius=28,Font=AppPalette.Font(11,true)};
            btnStart.EnabledChanged+=delegate{btnStart.Text=btnStart.Enabled?"开始发送":"正在发送…";};
            btnStart.Click+=delegate{DoStart();};overview.StartButton=btnStart;overview.Controls.Add(btnStart);
            body.Controls.Add(overview,0,1);
            progress=new SparkProgress {Dock=DockStyle.Fill,Margin=new Padding(2,6,2,8)};body.Controls.Add(progress,0,2);

            var history=new CardSurface {Dock=DockStyle.Fill,Margin=new Padding(0,0,0,12)};
            var tableLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,Padding=Padding.Empty};
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,43));tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var historyHead=new Panel {Dock=DockStyle.Fill};
            var historyTitle=ViewLabel("发送记录",11,true);historyTitle.SetBounds(0,2,180,30);
            var tools=new FlowLayoutPanel {Dock=DockStyle.Right,Width=184,FlowDirection=FlowDirection.LeftToRight,WrapContents=false};
            tools.Controls.Add(SmallAction("导出","download",delegate{ExportHistory();}));tools.Controls.Add(SmallAction("刷新","refresh",delegate{LoadLogs();}));
            historyHead.Controls.Add(historyTitle);historyHead.Controls.Add(tools);
            var tableBody=new Panel {Dock=DockStyle.Fill};
            dgv=new DataGridView {Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,ShowCellToolTips=true};
            dgv.DataBindingComplete+=delegate{FormatHistory();};dgv.CellPainting+=PaintHistoryStatus;
            emptyHistory=new EmptyHistory();tableBody.Controls.Add(dgv);tableBody.Controls.Add(emptyHistory);
            tableLayout.Controls.Add(historyHead,0,0);tableLayout.Controls.Add(tableBody,0,1);history.Controls.Add(tableLayout);body.Controls.Add(history,0,3);

            var runCard=new CardSurface {Dock=DockStyle.Fill,Margin=Padding.Empty};
            var runTitle=ViewLabel("运行动态",10,true);runTitle.Dock=DockStyle.Top;runTitle.Height=30;
            txtLog=new RichTextBox {Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.None,Font=AppPalette.Font(9),ScrollBars=RichTextBoxScrollBars.Vertical,Text="就绪。先预览消息，确认后再开始发送。",Tag="placeholder"};
            runCard.Controls.Add(txtLog);runCard.Controls.Add(runTitle);body.Controls.Add(runCard,0,4);
            overviewTimer=new System.Windows.Forms.Timer {Interval=2000};
            overviewTimer.Tick+=delegate{if(form.Visible || !btnStart.Enabled){RefreshDashboard();if(!btnStart.Enabled)LoadLogs();}};overviewTimer.Start();
            form.Activated+=delegate{RefreshDashboard();};form.FormClosed+=delegate{overviewTimer.Dispose();actionsMenu.Dispose();};
            Action responsive=delegate
            {
                bool compact=form.ClientSize.Height<730;
                body.RowStyles[0].Height=compact?58:64;body.RowStyles[1].Height=compact?180:188;
                body.RowStyles[2].Height=compact?60:72;body.RowStyles[4].Height=compact?88:132;
                runTitle.Height=compact?24:30;
            };
            form.SizeChanged+=delegate{responsive();};responsive();
            form.ResumeLayout(true);AppPalette.Apply(form);RefreshDashboard();return form;
        }
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
                overview.NextText=DateTime.TryParseExact(scheduleTime,"HH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out when)
                    ? "下次定时 · "+(DateTime.Now.TimeOfDay<when.TimeOfDay?"今天 ":"明天 ")+scheduleTime : "尚未设置定时";
                overview.ModeText=fixedMessage.Length>0?"固定消息":"分类语录";
                overview.AccessibleName="今日已处理 "+overview.Summary.Processed+" / "+overview.Summary.Total+"，成功 "+overview.Summary.Succeeded+"，失败 "+overview.Summary.Failed+"，待发 "+overview.Summary.Pending+"，待确认 "+overview.Summary.Unconfirmed+"。"+overview.NextText;
                overview.Invalidate();
            }
            catch(Exception){overview.NextText="记录正在更新…";overview.Invalidate();}
        }
        static void FormatHistory()
        {
            if(dgv==null || dgv.IsDisposed)return;
            AppPalette.StyleGrid(dgv);
            if(dgv.Columns.Contains("主页"))dgv.Columns["主页"].Visible=false;
            if(dgv.Columns.Contains("说明"))dgv.Columns["说明"].Visible=false;
            if(dgv.Columns.Contains("时间"))dgv.Columns["时间"].FillWeight=23;
            if(dgv.Columns.Contains("好友"))dgv.Columns["好友"].FillWeight=16;
            if(dgv.Columns.Contains("消息"))dgv.Columns["消息"].FillWeight=44;
            if(dgv.Columns.Contains("状态")){dgv.Columns["状态"].FillWeight=17;dgv.Columns["状态"].DisplayIndex=5;}
            foreach(DataGridViewRow row in dgv.Rows)if(row.Cells.Count>=6)row.Cells["状态"].ToolTipText=Convert.ToString(row.Cells["说明"].Value);
            if(emptyHistory!=null){emptyHistory.Visible=dgv.Rows.Count==0;if(emptyHistory.Visible)emptyHistory.BringToFront();emptyHistory.Invalidate();}
        }
        static void PaintHistoryStatus(object sender,DataGridViewCellPaintingEventArgs e)
        {
            if(e.RowIndex<0 || e.ColumnIndex<0 || dgv.Columns[e.ColumnIndex].Name!="状态")return;
            string raw=Convert.ToString(e.Value),label="失败";Color foreground=AppPalette.Error,background=AppPalette.ErrorSoft;
            if(raw=="OK"){label="成功";foreground=AppPalette.Success;background=AppPalette.SuccessSoft;}
            else if(raw=="UNCONFIRMED" || raw=="CAPTCHA" || raw=="LOGIN_EXPIRED") {label=raw=="UNCONFIRMED"?"待确认":raw=="CAPTCHA"?"待验证":"需登录";foreground=AppPalette.Warning;background=AppPalette.WarningSoft;}
            else if(raw=="CANCELLED" || raw=="STOPPED"){label="已停止";foreground=AppPalette.Muted;background=AppPalette.Soft;}
            e.PaintBackground(e.CellBounds,true);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            var box=new RectangleF(e.CellBounds.X+10,e.CellBounds.Y+6,Math.Min(88,e.CellBounds.Width-18),22);
            using(var path=AppPalette.Round(box,11))using(var brush=new SolidBrush(background))e.Graphics.FillPath(brush,path);
            using(var brush=new SolidBrush(foreground))using(var font=AppPalette.Font(8.5f))
            {e.Graphics.FillEllipse(brush,box.X+10,box.Y+8,6,6);e.Graphics.DrawString(label,font,brush,box.X+24,box.Y+3);}
            e.Handled=true;
        }
    }
}
