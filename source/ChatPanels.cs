using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JevChat {
public class ChatProfile {
    public string Name;
    public ChatWindow Window;
    public Rectangle Region;
    public Size WindowSize;
    public string Transcript="", Analysis="", Background="";
    public string[] Replies=new string[3];
    public int Mode;
    public bool AutomaticRegion;
    public override string ToString() { return Name; }
}
public partial class MainForm {
    readonly List<ChatProfile> profiles=new List<ChatProfile>();
    ChatProfile selectedProfile;
    ModernSelect profileChoice;
    bool fillingProfiles, switchingProfile;
    AssistantPanel assistantPanel;
    bool releasingAssistant;
    void BuildProfiles(Panel work) {
        var bar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=49,WrapContents=false };
        profileChoice=new ModernSelect { Text="选择聊天栏   ▾",Width=320,Margin=new Padding(0,0,8,0) };
        profileChoice.SelectedIndexChanged+=delegate { if(!fillingProfiles && profileChoice.SelectedIndex>=0) SwitchProfile(profiles[profileChoice.SelectedIndex]); };
        bar.Controls.Add(profileChoice);
        var add=new ModernButton { Text="＋ 聊天栏",Width=115,Height=38,Margin=new Padding(0,0,8,0) };
        add.Click+=delegate { AddChatPanel(); }; bar.Controls.Add(add);
        var rename=new ModernButton { Text="重命名",Width=96,Height=38,Margin=new Padding(0,0,8,0) };
        rename.Click+=delegate { RenamePanel(); }; bar.Controls.Add(rename);
        var compact=new ModernButton { Text="进入悬浮助手",Width=145,Height=38,Primary=true };
        compact.Click+=delegate { ShowAssistant(); }; bar.Controls.Add(compact); work.Controls.Add(bar);
    }
    void SaveProfile() {
        if(selectedProfile==null) return;
        selectedProfile.Transcript=transcript.Text; selectedProfile.Analysis=analysis.Text; selectedProfile.Background=context.Text;
        for(int i=0;i<3;i++) selectedProfile.Replies[i]=replies[i].Text;
        selectedProfile.Region=liveAreaWindow==selectedProfile.Window.Handle && liveAreaPid==selectedProfile.Window.ProcessId ? liveOffset : Rectangle.Empty;
        selectedProfile.WindowSize=liveWindowSize; selectedProfile.Mode=liveMode.SelectedIndex;
    }
    void RefreshProfiles() {
        if(profileChoice==null) return;
        fillingProfiles=true; profileChoice.SelectedIndex=-1; profileChoice.Items.Clear();
        foreach(var profile in profiles) profileChoice.Items.Add(profile);
        profileChoice.SelectedIndex=selectedProfile==null ? -1 : profiles.IndexOf(selectedProfile);
        if(selectedProfile==null) profileChoice.Text="选择聊天栏   ▾";
        fillingProfiles=false;
        if(assistantPanel!=null && !assistantPanel.IsDisposed) assistantPanel.ReloadProfiles();
    }
    void LoadProfile(ChatProfile profile) {
        selectedProfile=profile; boundWindow=profile.Window; currentConversation=null; currentFeedIdentity=null; liveFactIdentity=null; awaitingUserFact=false; lastReplyStrategy=null;
        liveOffset=profile.Region; liveWindowSize=profile.WindowSize; liveAreaWindow=profile.Window.Handle; liveAreaPid=profile.Window.ProcessId;
        liveMode.SelectedIndex=profile.Mode;
        transcript.Text=profile.Transcript; context.Text=profile.Background; analysis.Text=profile.Analysis;
        for(int i=0;i<3;i++) replies[i].Text=profile.Replies[i] ?? "";
        preview.Image=null; if(capture!=null) capture.Dispose(); capture=null; lastRegion=Rectangle.Empty;
        boundLabel.Text="当前聊天栏："+profile.Name;
        RefreshProfiles(); tabs.SelectedIndex=1;
        status.Text="已切换到「"+profile.Name+"」；同窗标签请先在 QQ 中切到对应聊天。";
    }
    void BindProfile(ChatWindow window) {
        SaveProfile();
        var match=profiles.Find(p=>p.Window.Handle==window.Handle && p.Window.ProcessId==window.ProcessId);
        if(match==null) { match=new ChatProfile { Name=window.Title,Window=window }; profiles.Add(match); }
        LoadProfile(match);
    }
    async void SwitchProfile(ChatProfile next) {
        if(next==selectedProfile || switchingProfile || busy) { RefreshProfiles(); return; }
        switchingProfile=true; bool resume=liveSession!=null;
        try {
            if(resume) { StopLive(); while(liveSession!=null) await Task.Delay(80); }
            SaveProfile(); LoadProfile(next);
            if(ChatBinding.ResumeProfile(resume,next.Mode,!next.Region.IsEmpty,next.AutomaticRegion)) StartLive();
        } catch(Exception ex) { Error(ex); }
        finally { switchingProfile=false; }
    }
    string AskPanelName(string initial,string caption="聊天栏名称") {
        using(var dialog=new Form { Text=caption,Width=370,Height=178,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterScreen,MaximizeBox=false,MinimizeBox=false,BackColor=Palette.Background,ForeColor=Palette.Text,Font=Font,TopMost=assistantPanel!=null && assistantPanel.Visible }) {
            var box=new TextBox { Text=initial,Left=20,Top=24,Width=310,BackColor=Palette.Surface,ForeColor=Palette.Text,BorderStyle=BorderStyle.FixedSingle,MaxLength=500 };
            var save=new ModernButton { Text="确定",Left=222,Top=68,Width=108,Height=38,Primary=true,DialogResult=DialogResult.OK };
            dialog.Controls.Add(box); dialog.Controls.Add(save); dialog.AcceptButton=save;
            return dialog.ShowDialog()==DialogResult.OK && !String.IsNullOrWhiteSpace(box.Text) ? box.Text.Trim() : null;
        }
    }
    void AddChatPanel() {
        if(switchingProfile || busy || liveSession!=null) { status.Text="请先停止监测，再添加聊天栏。"; return; }
        if(boundWindow==null) { ShowWorkspace(); tabs.SelectedIndex=0; return; }
        string name=AskPanelName("聊天栏 "+(profiles.Count+1)); if(name==null) return;
        SaveProfile(); var profile=new ChatProfile { Name=name,Window=boundWindow,Mode=liveMode.SelectedIndex };
        // Each tab is calibrated independently; do not reuse another conversation's region or text.
        profiles.Add(profile); LoadProfile(profile);
    }
    async void SupplementFacts() {
        if(busy || switchingProfile) return;
        bool paused=autoPaused, resume=liveSession!=null; autoPaused=true;
        try {
            StopLive(); while(liveSession!=null) await Task.Delay(80);
            string fact=AskPanelName(context.Text,"补充本次聊天信息（例如：今晚吃了面）");
            if(fact!=null) {
                context.Text=fact;
                liveFactIdentity=currentFeedIdentity;
                SaveProfile(); resume=true;
            }
        } finally { autoPaused=paused; if(resume && !autoPaused && boundWindow!=null) StartLive(); }
    }
    void RenamePanel() {
        if(selectedProfile==null || switchingProfile) return;
        var name=AskPanelName(selectedProfile.Name); if(name!=null) { selectedProfile.Name=name; RefreshProfiles(); }
    }
    public void ShowAssistant() {
        if(busy) return;
        if(boundWindow==null) {ShowWorkspace();tabs.SelectedIndex=0;status.Text="请先选择聊天窗口";return;}
        autoPaused=false; autoDismissed=false; autoRetryBlocked=false; followSessionActive=true; chatContentAvailable=true;
        if(assistantPanel==null || assistantPanel.IsDisposed) assistantPanel=new AssistantPanel(this);
        assistantPanel.ReloadProfiles(); assistantPanel.Show(); Hide(); assistantPanel.Activate();
        if(liveSession==null) StartLive();
    }
    public void ShowWorkspace() { followSessionActive=false; if(assistantPanel!=null) assistantPanel.Hide(); Show(); WindowState=FormWindowState.Normal; Activate(); }

    public class AssistantPanel : Form {
        readonly MainForm host;
        readonly TextBox summary, recent;
        readonly TableLayoutPanel sidebarContent;
        readonly TextBox[] drafts=new TextBox[3];
        readonly Label state, conversation, updated;
        readonly SideButton toggle;
        readonly SideButton refresh;
        readonly CheckBox follow;
        readonly Timer timer, followTimer;
        ContextMenuStrip moreMenu;
        readonly Color ink=Palette.Text, muted=Palette.Muted;
        bool dragging;
        Rectangle lastFollowTarget;
        DateTime lastFollowChange;
        IntPtr followWindow;
        int dockSide;
        bool contentDirty=true, followLayoutDirty=true;
        string lastTranscript;
        void ContentChanged(object sender,EventArgs e) { contentDirty=true; if(Visible && timer!=null && !timer.Enabled) timer.Start(); }
        public AssistantPanel(MainForm parent,bool automaticPreview=false) {
            host=parent; if(automaticPreview) { host.autoEnabled=true; host.status.Text="等待 QQ / 微信聊天，自动连接…"; }
            if(automaticPreview) {
                host.status.Text="建议已更新，选择适合你的回复";
                host.transcript.Text="【对方】今晚吃了点什么呢"+Environment.NewLine+"【对方】很晚了，早点休息吧";
                host.analysis.Text="先回答晚饭，再接住对方的关心";
                host.replies[0].Text="吃了番茄鸡蛋面，你也早点休息";
                host.replies[1].Text="吃了碗番茄鸡蛋面，那我们早点休息";
                host.replies[2].Text="番茄鸡蛋面，晚安";
            }
            Text="知言 · 聊天侧栏"; Width=440; Height=820; MinimumSize=new Size(390,600);
            FormBorderStyle=FormBorderStyle.None; Padding=new Padding(1); DoubleBuffered=true; StartPosition=FormStartPosition.Manual; TopMost=true;
            BackColor=Palette.Background; ForeColor=ink; Font=new Font("Microsoft YaHei UI",9);
            var heading=new Panel { Dock=DockStyle.Top,Height=64,Padding=new Padding(20,16,14,14) };
            var title=new Label { Text="知言",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,14,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft };
            title.MouseDown+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { follow.Checked=false; ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } };
            var close=ActionButton("×",()=>Close(),28); close.Dock=DockStyle.Right;
            var settings=ActionButton("设置",()=>host.OpenAutomaticSettings(),48); settings.Dock=DockStyle.Right;
            toggle=ActionButton("● 采集中",()=> { if(host.autoEnabled) host.ToggleAutoPause(); else if(host.liveSession!=null) host.StopLive(); else host.StartLive(); },94); toggle.Accent=true; toggle.Dock=DockStyle.Right;
            heading.Controls.Add(title); heading.Controls.Add(toggle); heading.Controls.Add(settings); heading.Controls.Add(close);
            var scroll=new Panel { Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(20,0,20,12) };
            var content=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,RowCount=9,BackColor=BackColor };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            int[] heights={48,34,30,84,112,132,132,132,28};
            foreach(int h in heights) content.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            sidebarContent=content; scroll.Controls.Add(content); Controls.Add(scroll); Controls.Add(heading);
            scroll.SizeChanged+=delegate { content.MaximumSize=new Size(Math.Max(280,scroll.ClientSize.Width-scroll.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth),0); };
            var caption=new Panel { Dock=DockStyle.Fill };
            caption.Controls.Add(new Label { Text="想好怎么接话",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,17,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft });
            updated=new Label { Text="等待更新",Dock=DockStyle.Right,Width=105,ForeColor=muted,TextAlign=ContentAlignment.MiddleRight }; caption.Controls.Add(updated); updated.SendToBack(); content.Controls.Add(caption,0,0);
            conversation=new Label { Text="当前会话   等待聊天窗口",Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,ForeColor=muted }; content.Controls.Add(conversation,0,1);
            state=new Label { Dock=DockStyle.Fill,ForeColor=muted,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft }; content.Controls.Add(state,0,2);
            recent=Editor(BackColor); recent.ScrollBars=ScrollBars.None; var last=Card("最近消息",recent,null,BackColor); content.Controls.Add(last,0,3);
            summary=Editor(Palette.Surface); content.Controls.Add(Card("对话重点",summary,null,summary.BackColor),0,4);
            string[] names={"01  简洁一点","02  温和一点","03  直接一点"};
            for(int i=0;i<3;i++) {
                int index=i; drafts[i]=Editor(Palette.Surface);
                var copy=ActionButton("复制",()=> { if(!String.IsNullOrWhiteSpace(drafts[index].Text)) Clipboard.SetText(drafts[index].Text); },54);
                content.Controls.Add(Card(names[i],drafts[i],copy,Palette.Surface),0,5+i);
            }
            content.Controls.Add(new Label { Text="AI 建议仅供参考，由你决定如何回复",ForeColor=muted,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter },0,8);
            var footer=new Panel { Dock=DockStyle.Bottom,Height=54,Padding=new Padding(20,11,14,11) };
            follow=new CheckBox { Text="跟随聊天",Checked=true,Dock=DockStyle.Left,Width=100 };
            var more=ActionButton("更多",()=>ShowMore(),58); more.Dock=DockStyle.Right; footer.Controls.Add(follow); footer.Controls.Add(more); var facts=ActionButton("补充信息",()=>host.SupplementFacts(),80); facts.Dock=DockStyle.Right; footer.Controls.Add(facts);
            refresh=ActionButton("换一组",()=>host.RefreshReplies(),68); refresh.Dock=DockStyle.Right; footer.Controls.Add(refresh);
            Controls.Add(footer); scroll.BringToFront();
            timer=new Timer { Interval=500 }; timer.Tick+=delegate { Synchronize(); }; timer.Start();
            followTimer=new Timer { Interval=33 }; followTimer.Tick+=delegate { FollowTarget(); }; followTimer.Start();
            host.status.TextChanged+=ContentChanged; host.transcript.TextChanged+=ContentChanged; host.analysis.TextChanged+=ContentChanged;
            foreach(var reply in host.replies) reply.TextChanged+=ContentChanged;
            follow.CheckedChanged+=delegate { followLayoutDirty=true; followTimer.Enabled=Visible && follow.Checked; };
            VisibleChanged+=delegate { timer.Enabled=Visible; followTimer.Enabled=Visible && follow.Checked; contentDirty=true; followLayoutDirty=true; };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(host.autoEnabled && !host.releasingAssistant) { e.Cancel=true; host.autoDismissed=true; Hide(); } };
            FormClosed+=delegate { if(!host.releasingAssistant && !host.IsDisposed && !host.Disposing) host.ShowWorkspace(); };
            ResizeBegin+=delegate { dragging=true; }; ResizeEnd+=delegate { dragging=false; followLayoutDirty=true; contentDirty=true; };
            SizeChanged+=delegate { contentDirty=true; };
            Shown+=delegate { ReloadProfiles(); Synchronize(); FollowTarget(); };
            Location=new Point(Screen.PrimaryScreen.WorkingArea.Right-Width-20,Screen.PrimaryScreen.WorkingArea.Top+20);
        }
        Panel Card(string label,TextBox editor,Button action,Color color) {
            var card=new SurfacePanel { Dock=DockStyle.Fill,BackColor=color,Padding=new Padding(14,10,14,10),Margin=new Padding(0,0,0,10) };
            var line=new Panel { Dock=DockStyle.Top,Height=31 };
            line.Controls.Add(new Label { Text=label,Dock=DockStyle.Fill,ForeColor=muted,TextAlign=ContentAlignment.MiddleLeft });
            if(action!=null) { action.Dock=DockStyle.Right; line.Controls.Add(action); action.SendToBack(); }
            card.Controls.Add(editor); card.Controls.Add(line); return card;
        }
        void ShowMore() {
            if(moreMenu==null) {
                moreMenu=new ContextMenuStrip { BackColor=Palette.Surface,ForeColor=Palette.Text,Renderer=new DarkMenuRenderer(),ShowImageMargin=false };
                moreMenu.Items.Add("重新识别",null,delegate { host.autoRetryBlocked=false; if(host.selectedProfile!=null) host.selectedProfile.AutomaticRegion=true; host.liveOffset=Rectangle.Empty; host.StopLive(); });
                moreMenu.Items.Add("校准消息区域",null,delegate { host.CalibrateAutomatic(); });
                moreMenu.Items.Add("打开工作台",null,delegate { host.ShowWorkspace(); });
            }
            moreMenu.Show(this,new Point(Width-170,Height-140));
        }
        SideButton ActionButton(string text,Action action,int width) {
            var button=new SideButton { Text=text,Width=width,Margin=new Padding(0,0,6,0) };
            button.Click+=delegate { try { action(); } catch(Exception ex) { if(state!=null) state.Text=ex.Message; } }; return button;
        }
        TextBox Editor(Color color) { return new TextBox { Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=color,ForeColor=ink,ScrollBars=ScrollBars.None,Font=new Font("Microsoft YaHei UI",10),Margin=Padding.Empty }; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using(var pen=new Pen(Palette.Border)) e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1); }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void WndProc(ref Message message) {
            base.WndProc(ref message); if(message.Msg!=0x84) return;
            long point=message.LParam.ToInt64(); var p=PointToClient(new Point((short)(point&0xffff),(short)((point>>16)&0xffff)));
            bool left=p.X<6,right=p.X>=Width-6,top=p.Y<6,bottom=p.Y>=Height-6;
            int hit=top&&left ? 13 : top&&right ? 14 : bottom&&left ? 16 : bottom&&right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
            if(hit!=0) message.Result=new IntPtr(hit);
        }
        protected override void Dispose(bool disposing) { if(disposing) {
            host.status.TextChanged-=ContentChanged; host.transcript.TextChanged-=ContentChanged; host.analysis.TextChanged-=ContentChanged;
            foreach(var reply in host.replies) reply.TextChanged-=ContentChanged;
            if(timer!=null) timer.Dispose(); if(followTimer!=null) followTimer.Dispose(); if(moreMenu!=null) moreMenu.Dispose();
        } base.Dispose(disposing); }
        public void ReloadProfiles() {
            string name=host.selectedProfile==null ? "等待聊天窗口" : host.selectedProfile.Name;
            if(conversation.Text!="当前会话   "+name) conversation.Text="当前会话   "+name;
        }
        void Synchronize() {
            if(host.IsDisposed || !Visible || !contentDirty) return;
            contentDirty=false;
            refresh.Enabled=!host.busy && host.liveRequest==null && !host.refreshRequested;
            if(state.Text!=host.status.Text) state.Text=host.status.Text;
            string label=host.autoPaused ? "○ 已暂停" : host.liveSession!=null ? "● 采集中" : "○ 等待中";
            if(toggle.Text!=label) { toggle.Text=label; toggle.Accent=host.liveSession!=null && !host.autoPaused; toggle.Invalidate(); }
            if(summary.Text!=host.analysis.Text) summary.Text=host.analysis.Text;
            string snapshot=host.transcript.Text;
            if(snapshot!=lastTranscript) {
                lastTranscript=snapshot; string text=snapshot.Trim(); int line=text.LastIndexOf('\n'); if(line>=0) text=text.Substring(line+1).Trim();
                recent.Text=text.Length==0 ? "打开 QQ / 微信中的聊天会话" : text;
            }
            bool changed=false;
            for(int i=0;i<3;i++) if(drafts[i].Text!=host.replies[i].Text) { drafts[i].Text=host.replies[i].Text; changed=true; }
            FitCards();
            if(changed) updated.Text=String.IsNullOrWhiteSpace(drafts[0].Text) ? "等待更新" : DateTime.Now.ToString("HH:mm")+" 更新";
        }
        void FitCards() {
            sidebarContent.SuspendLayout();
            try {
                FitCard(recent,3,80,180);
                FitCard(summary,4,96,260);
                for(int i=0;i<3;i++) FitCard(drafts[i],5+i,108,300);
            } finally { sidebarContent.ResumeLayout(); }
        }
        void FitCard(TextBox box,int row,int min,int max) {
            int width=Math.Max(150,box.ClientSize.Width-4);
            int measured=TextRenderer.MeasureText(String.IsNullOrWhiteSpace(box.Text) ? "等待聊天内容" : box.Text,box.Font,new Size(width,10000),TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl).Height+64;
            int desired=Math.Max(min,Math.Min(max,measured));
            if(sidebarContent.RowStyles[row].Height!=desired) sidebarContent.RowStyles[row].Height=desired;
            var bars=measured>max ? ScrollBars.Vertical : ScrollBars.None;
            if(box.ScrollBars!=bars) box.ScrollBars=bars;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
        void FollowTarget() {
            if(!Visible || !follow.Checked || dragging || host.IsDisposed) return;
            // Follow the selected HWND independently of OCR/content availability.
            try {
            Rectangle target;
            if(!ChatWindows.TryFollowBounds(host.boundWindow,out target)) return;
            if(followWindow!=host.boundWindow.Handle) { followWindow=host.boundWindow.Handle; dockSide=0; lastFollowTarget=Rectangle.Empty; }
            bool moving=target!=lastFollowTarget;
            if(moving) { lastFollowTarget=target; lastFollowChange=DateTime.UtcNow; }
            double settled=(DateTime.UtcNow-lastFollowChange).TotalMilliseconds;
            int cadence=moving || settled<400 ? 33 : 150;
            if(followTimer.Interval!=cadence) followTimer.Interval=cadence;
            if(!moving && settled>600 && !followLayoutDirty) return;
            followLayoutDirty=false;
            var screen=Screen.FromRectangle(target).WorkingArea;
            var next=FollowLayout.Place(target,screen,Size);
            // During resizing move only; apply the new height once the chat settles.
            if(settled<300) { next.Height=Height; next.Y=Math.Max(screen.Top,Math.Min(next.Y,screen.Bottom-Height)); }
            int proposedSide=next.Left>=target.Right ? 1 : next.Right<=target.Left ? -1 : 2;
            if(dockSide==0) dockSide=proposedSide;
            // Hold the docking side through a drag; choose a new side only after it settles.
            if((DateTime.UtcNow-lastFollowChange).TotalMilliseconds>=300) dockSide=proposedSide;
            int x=dockSide==1 ? target.Right+8 : dockSide==-1 ? target.Left-next.Width-8 : target.Right-next.Width-12;
            next.X=Math.Max(screen.Left,Math.Min(x,screen.Right-next.Width));
            if(next==Bounds) return;
            // No activation, no z-order changes, no text/layout updates on the movement path.
            uint flags=0x0010|0x0004;
            if(next.Size==Size) flags|=0x0001;
            SetWindowPos(Handle,IntPtr.Zero,next.X,next.Y,next.Width,next.Height,flags);
            } catch(InvalidOperationException) { } catch(System.ComponentModel.Win32Exception) { }
        }
    }
}
}
