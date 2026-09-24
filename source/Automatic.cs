using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JevChat {
public class OcrBox { public string Text; public Rectangle Bounds; }
public class OcrLayout {
    public string Text; public List<OcrBox> Lines=new List<OcrBox>();
    public string Within(Rectangle region) {
        var text=new System.Text.StringBuilder();
        foreach(var line in Lines) if(region.Contains(new Point(line.Bounds.Left,line.Bounds.Top+line.Bounds.Height/2))) text.AppendLine(line.Text);
        return text.ToString().Trim();
    }
}
public static class AutomaticLayout {
    public static Rectangle MessageRegion(OcrLayout layout,Size size,string title) {
        string text=(layout.Text ?? "").Replace(" ","");
        foreach(string login in new[]{"扫码登录","密码登录","二维码登录","登录QQ","登录微信","输入密码"}) if(text.Contains(login)) return Rectangle.Empty;
        if(size.Width<360 || size.Height<300) return Rectangle.Empty;
        OcrBox send=null;
        int toolbarTop=size.Height, toolCount=0, bodyLines=0;
        var tools=new HashSet<string>();
        foreach(var line in layout.Lines) {
            string name=(line.Text ?? "").Replace(" ","").Replace("\t","").ToLowerInvariant();
            bool lower=line.Bounds.Top>size.Height*.58;
            bool sendLabel=name=="发送" || name=="发送(s)" || name=="发送（s）" || name=="send" ||
                (name.Length<50 && name.Contains("发送") && (name.Contains("enter") || name.Contains("回车") || name.Contains("ctrl")));
            if(sendLabel && lower && line.Bounds.Right>size.Width*.45) send=line;
            if(lower) foreach(string tool in new[]{"表情","截图","文件","聊天记录"}) {
                if(name==tool && line.Bounds.Left>size.Width*.18 && tools.Add(tool)) { toolCount++; toolbarTop=Math.Min(toolbarTop,line.Bounds.Top); }
            }
            if(line.Bounds.Left>size.Width*.35 && line.Bounds.Top>size.Height*.15 && line.Bounds.Bottom<size.Height*.58 && name.Length>1) bodyLines++;
        }
        // A missing/disabled send label is common. Require several composer tools
        // plus visible message text instead of accepting an arbitrary QQ window.
        if(send==null && (toolCount<2 || bodyLines<2)) return Rectangle.Empty;
        bool main=title=="QQ" || title=="微信" || title=="WeChat" || title=="Weixin";
        int left=main ? (int)(size.Width*.34) : 12;
        int top=Math.Max(65,(int)(size.Height*.10));
        int bottom=send!=null ? send.Bounds.Top-Math.Max(70,(int)(size.Height*.14)) : toolbarTop-8;
        if(toolCount>=2 && toolbarTop>top+80) bottom=Math.Min(bottom,toolbarTop-8);
        if(bottom-top<80) return Rectangle.Empty;
        return new Rectangle(left,top,size.Width-left-18,bottom-top);
    }
    public static ChatWindow Choose(List<ChatWindow> windows,IntPtr foreground,ChatWindow current) {
        var active=windows.Find(w=>w.Handle==foreground); if(active!=null) return active;
        if(current!=null) { var match=windows.Find(w=>w.Handle==current.Handle && w.ProcessId==current.ProcessId); if(match!=null) return match; }
        return windows.Count==1 ? windows[0] : null;
    }
}
public partial class MainForm {
    internal EventWaitHandle WakeSignal;
    bool autoEnabled, autoPaused, autoTickBusy, autoDismissed, autoRetryBlocked, followSessionActive;
    Task<bool> chatProbe;
    readonly Dictionary<string,Task<bool>> pendingProbes=new Dictionary<string,Task<bool>>();
    ChatWindow probedWindow;
    DateTime probeStarted;
    bool chatContentAvailable;
    string currentConversation;
    string probeFailure;
    DateTime retryProbeAfter;
    int candidateCursor;
    System.Windows.Forms.Timer automaticTimer;
    NotifyIcon tray;
    public void EnableAutomatic(bool quiet) {
        autoEnabled=true; Hide();
        tray=new NotifyIcon { Icon=SystemIcons.Application,Text="知言 · 等待 QQ / 微信",Visible=true };
        var menu=new OwnedPopup(this);
        menu.Items.Add("打开工作台",null,delegate { autoDismissed=false; ShowWorkspace(); });
        menu.Items.Add("暂停 / 恢复自动监测",null,delegate { ToggleAutoPause(); });
        menu.Items.Add("模型设置",null,delegate { OpenAutomaticSettings(); });
        menu.Items.Add("停用登录自启动",null,delegate {
            try {
                string link=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"知言自动助手.lnk");
                if(File.Exists(link)) File.Delete(link);
                status.Text="已停用登录自启动；本次仍可继续使用。";
            } catch(Exception ex) { Error(ex); }
        });
        menu.Items.Add("退出知言",null,delegate { ExitAutomatic(); }); tray.ContextMenuStrip=menu;
        tray.DoubleClick+=delegate { autoDismissed=false; ShowWorkspace(); };
        automaticTimer=new System.Windows.Forms.Timer { Interval=1500 };
        automaticTimer.Tick+=delegate { AutomaticTick(); }; automaticTimer.Start();
        if(!quiet) ShowAssistant();
        AutomaticTick();
    }
    void ToggleAutoPause() {
        autoPaused=!autoPaused;
        if(autoPaused) { StopLive(); status.Text="自动监测已暂停"; }
        else {
            autoRetryBlocked=false;
            if(selectedProfile!=null && selectedProfile.Region.IsEmpty) selectedProfile.AutomaticRegion=true;
            status.Text="监测已恢复"; if(boundWindow!=null) StartLive(); else ShowWorkspace(); if(automaticSelection) AutomaticTick();
        }
    }
    void OpenAutomaticSettings() {
        autoPaused=true; StopLive(); ShowWorkspace(); tabs.SelectedIndex=2;
        status.Text="修改配置后，点击侧栏「恢复自动」继续。";
    }
    async void CalibrateAutomatic() {
        autoPaused=true; StopLive(); while(liveSession!=null) await Task.Delay(80);
        PickLiveArea();
    }
    async void ExitAutomatic() {
        if(automaticTimer!=null) automaticTimer.Stop(); autoPaused=true;
        StopLive(); while(liveSession!=null) await Task.Delay(80);
        Close();
    }
    async void AutomaticTick() {
        if(autoTickBusy || IsDisposed) return;
        autoTickBusy=true;
        try {
            if(WakeSignal!=null && WakeSignal.WaitOne(0)) { autoDismissed=false; ShowWorkspace(); }
            if(!ChatBinding.TrackActive(automaticSelection,autoPaused,followSessionActive && !autoDismissed,boundWindow)) return;
            var windows=await Task.Run(()=> { var found=ChatWindows.Find("QQ"); found.AddRange(ChatWindows.Find("微信")); found.RemoveAll(w=> { Rectangle bounds; return !ChatWindows.TryFollowBounds(w,out bounds); }); return found; });
            if(IsDisposed || Disposing) return;
            if(windows.Count==0) {
                StopLive(); if(assistantPanel!=null) assistantPanel.Hide();
                autoDismissed=false; autoRetryBlocked=false; tray.Text="知言 · 等待 QQ / 微信";
                status.Text="等待 QQ / 微信启动…"; return;
            }
            if(autoPaused || busy || switchingProfile) return;
            var target=AutomaticLayout.Choose(windows,ChatWindows.Foreground,boundWindow);
            if(target==null && boundWindow==null) {
                if(chatProbe!=null && (chatProbe.IsCompleted || (DateTime.UtcNow-probeStarted).TotalSeconds<8) && windows.Exists(w=>ChatBinding.Same(w,probedWindow))) target=probedWindow;
                else target=windows[(candidateCursor++)%windows.Count];
            }
            if(target==null) { status.Text="在 QQ / 微信中打开聊天，助手会自动跟随"; return; }
            if(boundWindow==null || target.Handle!=boundWindow.Handle || target.ProcessId!=boundWindow.ProcessId) {
                // After manual entry follow only an explicitly focused chat, never an arbitrary candidate.
                if(!automaticSelection && !ChatBinding.MaySwitch(target,ChatWindows.Foreground)) return;
                // One bounded in-flight probe, never bind from executable name or title alone.
                if(chatProbe!=null && !chatProbe.IsCompleted && ChatBinding.Same(probedWindow,target)) { status.Text=(DateTime.UtcNow-probeStarted).TotalSeconds>4 ? "窗口响应较慢 · 等待恢复或切换聊天" : "正在确认消息列表…"; return; }
                if(chatProbe==null || !ChatBinding.Same(probedWindow,target)) {
                    if(ChatBinding.Same(probedWindow,target) && DateTime.UtcNow<retryProbeAfter) return;
                    probedWindow=target; probeStarted=DateTime.UtcNow;
                    probeFailure=null;
                    string probeKey=target.ProcessId+":"+target.Handle;
                    foreach(var key in new List<string>(pendingProbes.Keys)) if(pendingProbes[key].IsCompleted) pendingProbes.Remove(key);
                    if(!pendingProbes.TryGetValue(probeKey,out chatProbe)) {
                    if(pendingProbes.Count>=8) { status.Text="聊天窗口响应超时，请关闭无响应的聊天窗口后重启助手"; return; }
                    chatProbe=Task.Run(async ()=> {
                        try { using(var local=new LocalOcr()) { if(target.Platform!="QQ") await local.Start(CancellationToken.None); return await WechatVision.Read(target,local,CancellationToken.None)!=null; } } catch(Exception ex) { probeFailure=ex.Message; return false; }
                    });
                    pendingProbes[probeKey]=chatProbe;
                    }
                    status.Text="正在确认聊天窗口…"; return;
                }
                bool verified=chatProbe.Status==TaskStatus.RanToCompletion && chatProbe.Result;
                chatProbe=null;
                if(!ChatBinding.CanAttach(probedWindow,target,verified,(DateTime.UtcNow-probeStarted).TotalMilliseconds)) {
                    retryProbeAfter=DateTime.UtcNow.AddSeconds(5);
                    status.Text=probeFailure ?? (boundWindow==null ? "等待真正的聊天会话 · 主面板和菜单不会绑定" : "保留当前聊天 · 此窗口不是消息会话"); return;
                }
                StopLive(); while(liveSession!=null) await Task.Delay(80);
                if(IsDisposed || Disposing || autoPaused || busy) return;
                if(!ChatBinding.MaySwitch(target,ChatWindows.Foreground)) { if(boundWindow!=null) StartLive(); return; }
                // Focus may change while the previous request is being cancelled.
                Rectangle confirmedBounds; if(!ChatWindows.TryFollowBounds(target,out confirmedBounds)) return;
                SaveProfile(); BindProfile(target); ResetConversationState(); selectedProfile.AutomaticRegion=true;
                chatContentAvailable=true; currentConversation=null;
                liveOffset=Rectangle.Empty; selectedProfile.Region=Rectangle.Empty; autoRetryBlocked=false;
                // Avoid holding a growing archive of all windows opened during a long session.
                if(profiles.Count>20) { profiles.RemoveAt(0); RefreshProfiles(); }
            }
            if(!autoDismissed && !Visible && ChatWindows.TryFollowBounds(target,out targetBoundsForDisplay)) {
                if(assistantPanel==null || assistantPanel.IsDisposed) assistantPanel=new AssistantPanel(this);
                if(!assistantPanel.Visible) { assistantPanel.ReloadProfiles(); assistantPanel.Show(); }
            }
            tray.Text="知言 · 自动跟随 "+target.Platform;
            string displayName=target.Platform+" · "+(String.IsNullOrWhiteSpace(currentConversation) ? target.Title : currentConversation);
            if(selectedProfile!=null && selectedProfile.Name!=displayName) {
                selectedProfile.Name=displayName; RefreshProfiles();
            }
            if(liveSession==null && !autoRetryBlocked) {
                if(selectedProfile!=null && selectedProfile.Region.IsEmpty) selectedProfile.AutomaticRegion=true;
                StartLive();
            }
        } catch(Exception ex) { autoRetryBlocked=true; status.Text="自动连接暂不可用："+ex.Message; }
        finally {
            autoTickBusy=false;
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"automatic-diagnostic.txt"), DateTime.Now.ToString("s")+" paused="+autoPaused+" busy="+busy+" switching="+switchingProfile+" bound="+(boundWindow==null?"none":boundWindow.Handle.ToString())+" probe="+(chatProbe==null?"none":chatProbe.Status.ToString())+" platform="+(probedWindow==null?"none":probedWindow.Platform)+" live="+(liveSession!=null)); } catch { }
        }
    }
    Rectangle targetBoundsForDisplay;
    void DisposeAutomatic() {
        if(automaticTimer!=null) automaticTimer.Dispose();
        if(tray!=null) { tray.Visible=false; tray.Dispose(); }
    }
}
}
