using System;
using System.IO;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        internal static bool CheckSafetyResume(IWin32Window owner,bool interactive)
        {
            string path=Path.Combine(BaseDir,".spark-safety-stop.json");if(!File.Exists(path))return true;
            if(!interactive){Log("安全暂停尚未解除，定时未发送。请人工处理后确认恢复。");return false;}
            if(MessageBox.Show(owner,"此前任务遇到验证或频繁提示，所有发送已暂停。\n请先在对应浏览器中完成验证并确认可以正常使用。\n现在确认恢复发送？","恢复发送",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return false;
            try{File.Delete(path);Log("用户确认恢复发送，仍保留验证检测与单路处理。");return true;}catch(Exception ex){MessageBox.Show(owner,ex.Message,"未恢复");return false;}
        }
    }
}
