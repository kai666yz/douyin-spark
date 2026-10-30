using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DouyinSpark
{
    class StartupProgressForm:ThemedForm
    {
        readonly Label detail,title;
        readonly ListBox stages;
        readonly PhaseProgress progress;
        readonly string[] names={"检查环境与数据位置","校验内置运行环境","检查浏览器组件","准备完成"};
        public StartupProgressForm()
        {
            Text="抖音续火花 · 启动检查";ClientSize=new Size(620,365);FormBorderStyle=FormBorderStyle.FixedDialog;ControlBox=false;StartPosition=FormStartPosition.CenterScreen;
            title=new Label{Text="正在准备你的工作空间",Font=AppPalette.Font(18,true),AutoSize=true,Location=new Point(26,22)};
            detail=new Label{Text="首次加载可能需要一些时间，请保留完整软件文件夹。",Font=AppPalette.Font(10),Location=new Point(28,64),Size=new Size(562,64),Tag="muted"};
            stages=new ListBox{Location=new Point(28,136),Size=new Size(562,140),BorderStyle=BorderStyle.None,Font=AppPalette.Font(11),ItemHeight=31,DrawMode=DrawMode.OwnerDrawFixed};stages.DrawItem+=delegate(object sender,DrawItemEventArgs e){if(e.Index<0)return;using(var b=new SolidBrush(AppPalette.Surface))e.Graphics.FillRectangle(b,e.Bounds);TextRenderer.DrawText(e.Graphics,Convert.ToString(stages.Items[e.Index]),stages.Font,e.Bounds,AppPalette.Text,TextFormatFlags.VerticalCenter);};
            foreach(var name in names)stages.Items.Add("○  "+name);
            progress=new PhaseProgress{Location=new Point(28,298),Size=new Size(562,14)};
            Controls.AddRange(new Control[]{title,detail,stages,progress});
        }
        public void Stage(int index,string text)
        {
            progress.Value=index;for(int i=0;i<names.Length;i++)stages.Items[i]=(i<index?"✓  ":i==index?"●  ":"○  ")+names[i];detail.Text=text;detail.AccessibleName=text;Refresh();
        }
        public void Fail(Exception ex)
        {
            detail.Height=88;stages.Top=160;stages.Height=116;stages.ItemHeight=28;
            title.Text="准备未完成，原数据已保留";detail.Text=ex.Message+"\r\n请完整解压便携包，保留 spark_backend 文件夹；可重试或检查安全软件隔离记录。";
            var retry=new RoundButton{Text="重试",Location=new Point(342,322),Size=new Size(116,36)};retry.Click+=delegate{DialogResult=DialogResult.Retry;Close();};
            var exit=new RoundButton{Text="退出",Primary=false,Location=new Point(474,322),Size=new Size(116,36)};exit.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};Controls.AddRange(new Control[]{retry,exit});
        }
    }
    class PhaseProgress:Control
    {
        public int Value;
        public PhaseProgress(){DoubleBuffered=true;}
        protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(var p=AppPalette.Round(new RectangleF(0,0,Width,Height),7))using(var b=new SolidBrush(AppPalette.Soft))e.Graphics.FillPath(b,p);float width=Width*Math.Max(0,Math.Min(4,Value))/4f;if(width>0)using(var p=AppPalette.Round(new RectangleF(0,0,width,Height),7))using(var b=new SolidBrush(AppPalette.Accent))e.Graphics.FillPath(b,p);}
    }
    partial class Program
    {
        static string ValidateBackend()
        {
            var output=new StringBuilder();var errors=new StringBuilder();
            using(var process=new Process{StartInfo=BackendInfo("--environment-check")}){
                process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)lock(output)output.AppendLine(e.Data);};process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)lock(errors)errors.AppendLine(e.Data);};
                process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
                if(!process.WaitForExit(60000)){try{process.Kill();}catch{}throw new IOException("运行环境检查超过60秒。请检查磁盘与安全软件，然后重试。");}process.WaitForExit();
                if(process.ExitCode!=0)throw new IOException("后端无法启动（退出码 "+process.ExitCode+"）。请重新完整解压便携包。详情见 logs/startup.log。\n"+errors.ToString().Substring(0,Math.Min(240,errors.Length)));
            }
            var check=SparkStorage.Json().Deserialize<System.Collections.Generic.Dictionary<string,object>>(output.ToString());
            if(check==null||!check.ContainsKey("app_version")||Convert.ToString(check["app_version"])!=AppVersion)throw new IOException("前后端版本不一致，请使用同一版本的完整便携包。");
            return "运行环境已就绪 · Python "+Convert.ToString(check["python_version"])+" / Playwright "+Convert.ToString(check["playwright_version"]);
        }
        internal static void StartupEvent(string message){SparkStorage.AppendLog(Path.Combine(BaseDir,"logs","startup.log"),DateTime.Now.ToString("s")+" | "+message+Environment.NewLine);}
    }
}
