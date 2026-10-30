"""Exercise the production inline editor with isolated data and a fake sender only."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--work-dir',type=Path)
args=parser.parse_args()
source=Path(__file__).resolve().parents[1]
root=Path(tempfile.mkdtemp(prefix='message-editor-',dir=args.work_dir))
for name in ('抖音续火花.exe','icon.ico','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','sentences_pool.json'):
    shutil.copy2(source/name,root/name)
shutil.copytree(source/'assets',root/'assets')
(root/'config.json').write_text(json.dumps({'browser':'msedge','schedule':'13:00','message':'原来保存的消息','theme':'light','custom_extension':{'preserve':True}},ensure_ascii=False),encoding='utf-8')
(root/'friends.json').write_text(json.dumps([{'name':'测试好友甲','url':'https://www.douyin.com/user/qa_a'},{'name':'测试好友乙','url':'https://www.douyin.com/user/qa_b'}],ensure_ascii=False),encoding='utf-8')
(root/'conversations.json').write_text('[{"name":"测试群","kind":"group","conversation_id":"qa_group_a"}]',encoding='utf-8')
(root/'FakeSender.cs').write_text(r'''
using System;using System.IO;using System.Web.Script.Serialization;
class FakeSender{static void Main(string[] args){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"captured_args.json"),new JavaScriptSerializer().Serialize(args));Console.WriteLine("TASK|PLANNED|0");}}
''',encoding='utf-8-sig')
(root/'EditorQa.cs').write_text(r'''
using System;using System.IO;using System.Linq;using System.Reflection;using System.Drawing;using System.Windows.Forms;using System.Threading.Tasks;using System.Runtime.InteropServices;using System.Security.AccessControl;using System.Security.Principal;using System.Collections.Generic;using DouyinSpark;
class EditorQa{
 static Type P=typeof(SparkStorage).Assembly.GetType("DouyinSpark.Program");static BindingFlags F=BindingFlags.Static|BindingFlags.NonPublic;static string Root=AppDomain.CurrentDomain.BaseDirectory;static int checks;
 static object Call(string n,params object[] a){return P.GetMethod(n,F).Invoke(null,a);}static object Get(string n){return P.GetField(n,F).GetValue(null);}static void Set(string n,object v){P.GetField(n,F).SetValue(null,v);}static void Check(bool yes,string label){if(!yes)throw new Exception(label);checks++;Console.WriteLine("CHECK "+checks+" "+label);Console.Out.Flush();}
 static string Config(){return (string)Call("ConfigString","message");}static bool Dirty(){return (bool)P.GetProperty("PrivateMessageDirty",F).GetValue(null,null);}
 static void Capture(Form f,string name){f.Refresh();Application.DoEvents();using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(Root,name+".png"));}}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string klass,string title);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int message,IntPtr w,IntPtr l);
 static Timer Dismiss(string title){var timer=new Timer{Interval=40};timer.Tick+=delegate{IntPtr box=FindWindow("#32770",title);uint pid;if(box!=IntPtr.Zero){GetWindowThreadProcessId(box,out pid);if(pid==(uint)System.Diagnostics.Process.GetCurrentProcess().Id)SendMessage(box,0x10,IntPtr.Zero,IntPtr.Zero);}};timer.Start();return timer;}
 [STAThread]static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var host=new Form{ShowInTaskbar=false,WindowState=FormWindowState.Minimized};int result=0;host.Shown+=async delegate{try{await Test();Console.WriteLine("PASS: "+checks+" inline message editor assertions, production UI + captured fake backend; no login/network/messages.");}catch(Exception e){Console.WriteLine(e);result=1;}finally{host.Close();}};Application.Run(host);Environment.ExitCode=result;}
 static async Task Test(){
  Set("BaseDir",Root);Set("fixedMessage",Config());Set("environmentReady",true);Set("scheduleTime","13:00");
  string expected="固定消息 \"测试\" 😀\r\n第二行";
  using(var main=(Form)Call("BuildMainWindow")){main.Show();var box=(TextBox)Get("detailPreview");var status=(Label)Get("privateMessageStatus");var save=(Button)Get("savePrivateMessage");
   Check(!box.ReadOnly&&box.Text==Config(),"editable persisted text");Check(status.Text.StartsWith("已保存")&&!save.Enabled,"initial saved status");
   box.Text=expected;box.SelectionStart=3;Call("RefreshDashboard");Check(Dirty()&&box.Text==expected&&box.SelectionStart==3,"timer refresh preserves draft and caret");Check(status.Text.StartsWith("未保存")&&save.Enabled,"dirty reminder and save action");Check(Config()=="原来保存的消息"&&(string)Get("fixedMessage")==Config(),"typing never updates config or active value");Capture(main,"message-unsaved");
   var roster=(ListBox)Get("roster");roster.SelectedIndex=1;Call("RefreshDashboard");Check(box.Text==expected,"private selection preserves draft");
   Call("SetRosterFilter","group");Check(box.ReadOnly&&!save.Visible,"group preview isolated from private editor");Call("RefreshDashboard");Call("SetRosterFilter","private");Check(!box.ReadOnly&&box.Text==expected&&Dirty(),"private draft restored after group");
   using(var timer=Dismiss("请先保存消息")){Check(!(bool)Call("RequireSavedPrivateMessage"),"manual send guarded when dirty");}Check(!File.Exists(Path.Combine(Root,"captured_args.json")),"unsaved guard never invokes sender");
   save.PerformClick();Check(Config()==expected&&(string)Get("fixedMessage")==expected,"save persists before activation");Check(!Dirty()&&status.Text.StartsWith("已保存")&&!save.Enabled,"saved status after click");
   var config=SparkStorage.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Root,"config.json")));Check(config.ContainsKey("custom_extension")&&Convert.ToString(config["schedule"])=="13:00","unrelated configuration preserved");Capture(main,"message-saved");
   box.Text="下一条尚未保存";Set("schedulePaused",false);Set("scheduleLastTrigger","");Set("nextScheduled",DateTime.Now.AddSeconds(-1));Call("TickSchedule",DateTime.Now);for(int i=0;i<100&&(bool)Get("privateRunActive");i++)await Task.Delay(30);
   var sent=SparkStorage.Json().Deserialize<string[]>(File.ReadAllText(Path.Combine(Root,"captured_args.json")));int at=Array.IndexOf(sent,"--message");Check(at>=0&&sent[at+1]==expected,"scheduled backend uses saved multiline message, not draft");
   Set("privateRunActive",true);Call("RefreshSendButtons");Check(!save.Enabled,"cannot change active task message");Set("privateRunActive",false);Call("RefreshSendButtons");
   // Deny directory writes to force an actual atomic-save error, then retry.
   var old=Directory.GetAccessControl(Root,AccessControlSections.Access).GetSecurityDescriptorBinaryForm();var acl=new DirectorySecurity();acl.SetSecurityDescriptorBinaryForm(old,AccessControlSections.Access);acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.Write|FileSystemRights.Delete,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Deny));
   try{Directory.SetAccessControl(Root,acl);using(var timer=Dismiss("消息未保存")){save.PerformClick();}Check(status.Text.StartsWith("保存失败")&&box.Text=="下一条尚未保存","failed write preserves draft and shows failure");Check(Config()==expected&&(string)Get("fixedMessage")==expected,"failed write leaves persisted and active value unchanged");}
   finally{var restore=new DirectorySecurity();restore.SetSecurityDescriptorBinaryForm(old,AccessControlSections.Access);Directory.SetAccessControl(Root,restore);}
   typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(box,new object[]{new KeyEventArgs(Keys.Control|Keys.S)});Check(Config()=="下一条尚未保存"&&!Dirty()&&status.Text.StartsWith("已保存"),"Ctrl+S retries successfully");
   box.Text="";Check(Dirty()&&!save.Enabled,"empty draft cannot silently restore random messages");box.Text=Config();Check(!Dirty(),"reverting draft restores saved state");
   Call("PersistFixedMessage","");Check(status.Text.Contains("语录池")&&Config()=="","legacy pool mode remains available through settings");Call("PersistFixedMessage",expected);
   Call("ToggleAppearance");Capture(main,"message-dark");Check(status.ForeColor==AppPaletteColor("Success"),"saved indicator follows dark theme");main.Size=main.MinimumSize;Capture(main,"message-compact");Check(save.Bottom<save.Parent.ClientSize.Height&&save.Right<=save.Parent.ClientSize.Width,"save button inside minimum-size card");
   Set("exiting",true);main.Close();
  }
  Set("fixedMessage",Config());using(var reloaded=(Form)Call("BuildMainWindow")){reloaded.Show();Check(((TextBox)Get("detailPreview")).Text==expected&&((Label)Get("privateMessageStatus")).Text.StartsWith("已保存")&&!Dirty(),"reopening restores saved message and state");Set("exiting",true);reloaded.Close();}
 }
 static Color AppPaletteColor(string name){return (Color)P.Assembly.GetType("DouyinSpark.AppPalette").GetProperty(name).GetValue(null,null);}
}
''',encoding='utf-8-sig')
compiler='C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
subprocess.run([compiler,'/nologo','/r:System.Web.Extensions.dll','/out:'+str(root/'spark_backend.exe'),str(root/'FakeSender.cs')],check=True)
refs=['System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll',str(root/'抖音续火花.exe')]
subprocess.run([compiler,'/nologo','/platform:x64','/codepage:65001','/out:'+str(root/'EditorQa.exe')]+['/r:'+r for r in refs]+[str(root/'EditorQa.cs')],check=True)
try:
    run=subprocess.run([str(root/'EditorQa.exe')],cwd=root,env=dict(os.environ,DOUYIN_SPARK_DATA_DIR=str(root)),capture_output=True,text=True,encoding='utf-8',timeout=30)
except subprocess.TimeoutExpired as error:
    print(error.stdout.decode('utf-8',errors='replace') if isinstance(error.stdout,bytes) else error.stdout)
    print('Timeout artifacts:',root)
    raise
print(run.stdout)
print('Artifacts:',root)
assert run.returncode==0,run.stderr+run.stdout
