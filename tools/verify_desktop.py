"""Isolated WinForms acceptance checks. Browser and sending backend are local fixtures."""
from pathlib import Path
import argparse
import csv
import datetime
import json
import os
import shutil
import subprocess
import tempfile

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--work-dir',type=Path)
args=parser.parse_args()
source=Path(__file__).resolve().parents[1]
qa=Path(tempfile.mkdtemp(prefix='spark-desktop-',dir=args.work_dir))
for name in ['抖音续火花.exe','icon.ico','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','sentences_pool.json']:
    shutil.copy2(source/name,qa/name)
shutil.copytree(source/'assets',qa/'assets')
friends=[{'name':f'测试好友{i+1:02d}','url':f'https://www.douyin.com/user/qa_{i}'} for i in range(12)]
(qa/'friends.json').write_text(json.dumps(friends,ensure_ascii=False),encoding='utf-8')
(qa/'conversations.json').write_text(json.dumps([{'name':'设计交流小组','kind':'group','conversation_id':'qa_group_A'},{'name':'朋友日常','kind':'group','conversation_id':'qa_group_B'}],ensure_ascii=False),encoding='utf-8')
(qa/'conversation_candidates.json').write_text(json.dumps([{'name':f'候选人{i}','kind':'private','conversation_id':f'qa_person_{i}','url':f'https://www.douyin.com/user/qa_{i}'} for i in range(8)]+[{'name':'测试群','kind':'group','conversation_id':'qa_group_A'}],ensure_ascii=False),encoding='utf-8')
(qa/'config.json').write_text(json.dumps({'browser':'msedge','schedule':'13:00','theme':'light','message':'今天也要照顾好自己。'}),encoding='utf-8')
(qa/'group_task.json').write_text(json.dumps({'message':'愿大家今天都有好心情。','targets':['conversation:qa_group_A','conversation:qa_group_B'],'schedule':'13:10','paused':False,'last_trigger_day':''},ensure_ascii=False),encoding='utf-8')
(qa/'logs').mkdir()
with (qa/'logs/spark_log.csv').open('w',encoding='utf-8-sig',newline='') as file:
    w=csv.writer(file);w.writerow(['time','friend','url','message','status','note'])
    for i in range(7):w.writerow([datetime.date.today().isoformat()+f' 10:{i:02d}:00',friends[i]['name'],friends[i]['url'],'测试界面消息','OK' if i<6 else 'CANCELLED','仅界面测试'])

(qa/'FakeBackend.cs').write_text(r'''
using System;using System.IO;using System.Threading;
class FakeBackend{static void Main(string[] a){Console.OutputEncoding=new System.Text.UTF8Encoding(false);string root=AppDomain.CurrentDomain.BaseDirectory;
 if(Array.IndexOf(a,"--environment-check")>=0){string v=File.Exists(Path.Combine(root,"bad_version"))?"0.0.0":"2.3.1";Console.WriteLine("{\"app_version\":\""+v+"\",\"python_version\":\"fixture\",\"playwright_version\":\"fixture\"}");return;}
 int index=Array.IndexOf(a,"--stop-after-current");if(index<0)throw new Exception("fixture requires stop flag");string flag=a[index+1];Console.WriteLine("TASK|PLANNED|4");Console.WriteLine("RESULT|https://www.douyin.com/user/fixture|OK");Console.WriteLine("PROGRESS|1|4|本地测试|OK");
 for(int i=0;i<300&&!File.Exists(flag);i++)Thread.Sleep(50);if(!File.Exists(flag))throw new Exception("no stop flag");Console.WriteLine("TASK|CANCELLED|3");
}}
''',encoding='utf-8-sig')
(qa/'DesktopQa.cs').write_text(r'''
using System;using System.IO;using System.Linq;using System.Drawing;using System.Reflection;using System.Windows.Forms;using System.Collections.Generic;using System.Threading.Tasks;using System.Runtime.InteropServices;using DouyinSpark;
class DesktopQa{
 static Type P=typeof(SparkStorage).Assembly.GetType("DouyinSpark.Program");static BindingFlags F=BindingFlags.Static|BindingFlags.NonPublic;static string Root=AppDomain.CurrentDomain.BaseDirectory;
 static object Call(string n,params object[] a){return P.GetMethod(n,F).Invoke(null,a);}static object Get(string n){return P.GetField(n,F).GetValue(null);}static void Set(string n,object v){P.GetField(n,F).SetValue(null,v);}static void Check(bool v,string n){if(!v)throw new Exception(n);}
 static IEnumerable<Control> All(Control c){foreach(Control k in c.Controls){yield return k;foreach(var j in All(k))yield return j;}}
 static void Click(Form f,string label){((Button)All(f).First(x=>x is Button&&x.Text==label)).PerformClick();Application.DoEvents();}
 static Form Create(string name,params object[] a){return (Form)Activator.CreateInstance(P.Assembly.GetType("DouyinSpark."+name),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,a,null);}
 static void Capture(Form f,string name){f.Refresh();Application.DoEvents();using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(Root,name+".png"));}}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string klass,string title);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int message,IntPtr w,IntPtr l);
 [STAThread]static int Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var host=new Form{ShowInTaskbar=false,WindowState=FormWindowState.Minimized};int result=0;
 host.Shown+=async delegate{try{await Test();Console.WriteLine("PASS: startup stages + diagnostics, both selection dialogs, real UI stop/process lifecycle + cooldown, grouped tasks, light/dark/compact layout. Fake backend only; no network/messages.");}catch(Exception e){Console.WriteLine(e);result=1;}finally{host.Close();}};Application.Run(host);return result;}
 static async Task Test(){
  Call("PrepareStorageWithProgress");Check(File.ReadAllText(Path.Combine(Root,"logs/startup.log")).Contains("components |"),"startup stages logged");
  File.WriteAllText(Path.Combine(Root,"bad_version"),"fixture");bool rejected=false;try{Call("ValidateBackend");}catch(TargetInvocationException e){rejected=e.InnerException is IOException;}finally{File.Delete(Path.Combine(Root,"bad_version"));}Check(rejected,"mismatched backend gives explicit error");
  using(var stages=Create("StartupProgressForm")){stages.Show();stages.GetType().GetMethod("Stage").Invoke(stages,new object[]{2,"环境已校验，正在检查浏览器组件。"});Capture(stages,"startup-stages");stages.Close();}
  using(var failure=Create("StartupProgressForm")){failure.Show();failure.GetType().GetMethod("Fail").Invoke(failure,new object[]{new IOException("测试：后端组件不完整")});Check(All(failure).OfType<Button>().Any(x=>x.Text=="重试"),"startup failure retry action");Capture(failure,"startup-failure");failure.Close();}
  using(var missing=Create("EnvironmentForm",Root,Root,"模拟缺组件；不执行安装","msedge",(Action<string>)(x=>{}),(Func<string>)(()=>""),(Func<string,string>)(x=>""),true)){missing.Show();Check(All(missing).OfType<Button>().Any(x=>x.Text=="一键准备"&&x.Visible),"missing runtime install action");Check(All(missing).OfType<Label>().Any(x=>x.Text.Contains("未找到该浏览器")),"missing browser guidance");Capture(missing,"environment-missing");missing.Close();}
  Set("fixedMessage","今天也要照顾好自己。");Set("scheduleTime","13:00");Set("environmentReady",true);
  using(var main=(Form)Call("BuildMainWindow")){main.Show();Call("LoadLogs");Call("RefreshDashboard");Capture(main,"ui-light");
   var overview=(Control)Get("overview");Check(((Control)Get("btnStart")).Height>=52,"primary action height");Check(((Control)Get("btnStop")).Bounds.Right<=overview.Width&&((Control)Get("btnStop")).Left>((Control)Get("btnStart")).Right,"stop beside start, no overlap");
   Check(GroupTaskSettings.Read(Root).paused,"migration pauses group schedule too");Call("SetOverview",true);Capture(main,"ui-groups");Call("SetOverview",false);
   Call("ShowWorkspace","消息任务");Capture(main,"ui-tasks");Call("ShowWorkspace","设置");Capture(main,"ui-settings");
   using(var dialog=Create("ConversationImportForm",Root)){dialog.Show();var grid=All(dialog).OfType<DataGridView>().Single();Check(grid.Rows.Count==9&&grid.Rows[0].Height>=36,"conversation row size");Click(dialog,"全选");Check(grid.Rows.Cast<DataGridViewRow>().All(x=>Convert.ToBoolean(x.Cells["pick"].Value)),"conversation select all");Click(dialog,"全不选");Check(grid.Rows.Cast<DataGridViewRow>().All(x=>!Convert.ToBoolean(x.Cells["pick"].Value)),"conversation clear all");Click(dialog,"仅选今日未发送");Check(grid.Rows.Cast<DataGridViewRow>().Count(x=>Convert.ToBoolean(x.Cells["pick"].Value))==3,"conversation unsent excludes OK and accepts cancelled");Capture(dialog,"selection-conversations");dialog.Close();}
   var items=new List<string[]>{new[]{"已发好友","https://www.douyin.com/user/qa_0"},new[]{"未发好友","https://www.douyin.com/user/qa_10"}};
   using(var dialog=Create("FollowingPickForm",Root,items,new HashSet<string>())){dialog.Show();var grid=All(dialog).OfType<DataGridView>().Single();Click(dialog,"仅选今日未发送");Check(!Convert.ToBoolean(grid.Rows[0].Cells[0].Value)&&Convert.ToBoolean(grid.Rows[1].Cells[0].Value),"following unsent");Click(dialog,"全选");Check(All(dialog).OfType<Label>().Any(x=>x.Text=="已选 2 / 2 人"),"live selection count");Click(dialog,"全不选");typeof(DataGridView).GetMethod("OnCellClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,new object[]{new DataGridViewCellEventArgs(1,0)});Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value),"whole row toggle");typeof(Form).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,new object[]{new KeyEventArgs(Keys.Control|Keys.A)});Check(grid.Rows.Cast<DataGridViewRow>().All(x=>Convert.ToBoolean(x.Cells[0].Value)),"Ctrl+A");Capture(dialog,"selection-following");dialog.Close();}
   Call("OpenGroupTask",false,null);var group=(Form)Get("groupWindow");Check(main.Enabled&&group.Visible,"modeless group window");var interval=All(group).OfType<NumericUpDown>().Single();Check(interval.Value==60&&interval.Minimum==15,"group slower default + configurable interval");Capture(group,"ui-group-task");group.Close();
   Call("ShowWorkspace","联系人");Call("StartPrivateRun");for(int i=0;i<80&&(int)Get("privateDone")<1;i++)await Task.Delay(50);Check((bool)Get("privateRunActive")&&((Control)Get("btnStop")).Enabled,"stop active while processing");Capture(main,"ui-sending");
   using(var timer=new Timer{Interval=50}){timer.Tick+=delegate{IntPtr box=FindWindow("#32770","停止发送");if(box!=IntPtr.Zero)SendMessage(box,0x111,new IntPtr(6),IntPtr.Zero);};timer.Start();Call("RequestStopSending");timer.Stop();}
   for(int i=0;i<100&&(bool)Get("privateRunActive");i++)await Task.Delay(50);Check(!(bool)Get("privateRunActive")&&(bool)P.GetProperty("SendCooling",F).GetValue(null,null),"stop finished and cooldown active");Check(!((Control)Get("btnStart")).Enabled,"start disabled for cooldown");Capture(main,"ui-stopped");await Task.Delay(10100);Call("RefreshSendButtons");Check(((Control)Get("btnStart")).Enabled,"start returns after 10 seconds");
   Call("ToggleAppearance");Capture(main,"ui-dark");main.Size=main.MinimumSize;Call("ShowWorkspace","消息任务");Capture(main,"ui-compact-tasks");Set("exiting",true);main.Close();
  }
 }
}
''',encoding='utf-8-sig')
compiler=Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe')
subprocess.run([str(compiler),'/nologo','/out:'+str(qa/'spark_backend.exe'),str(qa/'FakeBackend.cs')],check=True)
refs=['System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll',str(qa/'抖音续火花.exe')]
subprocess.run([str(compiler),'/nologo','/platform:x64','/codepage:65001','/out:'+str(qa/'DesktopQa.exe')]+['/r:'+r for r in refs]+[str(qa/'DesktopQa.cs')],check=True)
env=dict(os.environ,DOUYIN_SPARK_DATA_DIR=str(qa))
run=subprocess.run([str(qa/'DesktopQa.exe')],cwd=qa,env=env,capture_output=True,text=True,encoding='utf-8',timeout=90)
print(run.stdout)
assert run.returncode==0,run.stderr+run.stdout
print('Artifacts:',qa)
