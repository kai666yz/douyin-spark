using System;
using System.Drawing;
using System.Windows.Forms;

namespace DouyinSpark
{
    partial class Program
    {
        static Label privateMessageTitle,privateMessageStatus;
        static RoundButton savePrivateMessage;
        static string privateMessageDraft="";
        static bool privateMessageDraftLoaded,syncingPrivateMessage,privateMessageSaveError;

        static string ComparableMessage(string text)
        {return (text??"").Trim().Replace("\r\n","\n").Replace("\r","\n");}

        static bool PrivateMessageDirty
        {get{return privateMessageDraftLoaded&&ComparableMessage(privateMessageDraft)!=ComparableMessage(fixedMessage);}}

        static void RefreshPrivateMessageEditor(bool group)
        {
            if(detailPreview==null)return;
            if(!privateMessageDraftLoaded){privateMessageDraft=fixedMessage;privateMessageDraftLoaded=true;}
            string text=group?(string.IsNullOrWhiteSpace(groupPlan.message)?"请在群聊消息任务中选择消息模式并保存。":groupPlan.message):privateMessageDraft;
            syncingPrivateMessage=true;
            try{detailPreview.ReadOnly=group;if(detailPreview.Text!=text)detailPreview.Text=text;}
            finally{syncingPrivateMessage=false;}
            privateMessageTitle.Text=group?"群聊消息预览":"私信固定消息";
            detailHint.Text=group?"群聊消息独立设置，不使用这里的私信内容。":"保存后，手动和定时私信均固定发送此消息。";
            savePrivateMessage.Visible=!group;
            RefreshPrivateMessageStatus();
        }

        static void RefreshPrivateMessageStatus()
        {
            if(privateMessageStatus==null)return;
            bool group=detailPreview.ReadOnly;
            savePrivateMessage.Enabled=!group&&!privateRunActive&&PrivateMessageDirty&&!string.IsNullOrWhiteSpace(privateMessageDraft);
            if(group){privateMessageStatus.Text="群聊请到独立任务中保存";privateMessageStatus.ForeColor=AppPalette.Muted;return;}
            if(privateMessageSaveError){privateMessageStatus.Text="保存失败 · 修改仍保留，请重试";privateMessageStatus.ForeColor=AppPalette.Error;return;}
            if(PrivateMessageDirty){privateMessageStatus.Text=privateRunActive?"未保存 · 当前任务仍用原消息":"未保存 · 请点击“保存消息”";privateMessageStatus.ForeColor=AppPalette.Warning;return;}
            privateMessageStatus.Text=fixedMessage.Length>0?"已保存 · 私信固定发送此消息":"未设置固定消息 · 当前使用语录池";
            privateMessageStatus.ForeColor=fixedMessage.Length>0?AppPalette.Success:AppPalette.Muted;
        }

        // Publish the in-memory value only after the atomic config write succeeds.
        static void PersistFixedMessage(string text)
        {
            string value=(text??"").Trim();
            var config=ReadConfig();config["message"]=value;
            SparkStorage.WriteJson(System.IO.Path.Combine(BaseDir,"config.json"),config);
            fixedMessage=value;privateMessageDraft=value;privateMessageDraftLoaded=true;privateMessageSaveError=false;
            Log(value.Length>0?"私信固定消息已保存（手动与定时共用）":"已保存，私信恢复语录池模式");
            RefreshDashboard();RefreshTaskWorkspace();
            if(detailPreview!=null)RefreshPrivateMessageEditor(detailPreview.ReadOnly);
        }

        static void SaveInlinePrivateMessage()
        {
            if(detailPreview==null||detailPreview.ReadOnly||privateRunActive)return;
            if(string.IsNullOrWhiteSpace(privateMessageDraft)){detailPreview.Focus();return;}
            try{PersistFixedMessage(privateMessageDraft);}
            catch(Exception ex){privateMessageSaveError=true;RefreshPrivateMessageStatus();MessageBox.Show(mainForm,"未能保存，已输入的内容仍保留，发送设置没有改变。\n"+ex.Message,"消息未保存",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }

        static bool RequireSavedPrivateMessage()
        {
            if(!PrivateMessageDirty)return true;
            ShowWorkspace("联系人");SetRosterFilter("private");RefreshPrivateMessageEditor(false);detailPreview.Focus();
            MessageBox.Show(mainForm,"消息还有未保存的修改。请先点击右侧“保存消息”，再开始发送。\n定时任务始终使用上次保存的消息。","请先保存消息",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return false;
        }
    }
}
