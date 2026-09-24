using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JevChat {
static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static int Main(string[] args) {
        SetProcessDPIAware();
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length > 0 && args[0] == "--self-test") return SelfTests.Run();
        if (args.Length > 0 && args[0] == "--local-ocr-test") return SelfTests.LocalOcrTest();
        if (args.Length > 0 && args[0] == "--ui-self-test") return SelfTests.MenuTests();
        if (args.Length > 0 && args[0] == "--instance-test") return SelfTests.InstanceTest();
        if(args.Length>1 && args[0]=="--render-panel") {
            using(var main=new MainForm(false)) using(var panel=new MainForm.AssistantPanel(main,true)) {
                panel.Show(); Application.DoEvents();
                using(var bitmap=new Bitmap(panel.Width,panel.Height)) { panel.DrawToBitmap(bitmap,new Rectangle(Point.Empty,panel.Size)); bitmap.Save(args[1]); }
            }
            return 0;
        }
        if (args.Length > 1 && (args[0] == "--render" || args[0] == "--render-settings" || args[0] == "--render-work")) {
            using (var form = new MainForm(false)) {
                if(args[0] == "--render-settings") form.ShowSettings();
                if(args[0] == "--render-work") form.ShowWork();
                form.Show(); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[1]);
                }
            }
            return 0;
        }
        bool first;
        using(var mutex=new Mutex(true,"Local\\Zhiyan.Automatic.Instance",out first)) {
            if(!first) { try { using(var wake=EventWaitHandle.OpenExisting("Local\\Zhiyan.Automatic.Show")) wake.Set(); } catch(WaitHandleCannotBeOpenedException) {} return 0; }
            using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\Zhiyan.Automatic.Show")) {
                var app=new MainForm(true); app.WakeSignal=signal;
                {
                    app.Shown+=delegate { app.InitializeWorkspace(); };
                }
                Application.Run(app);
            }
        }
        return 0;
    }
}
public class Selector : Form {
    Point anchor; Rectangle selection; bool drawing;
    public Rectangle RegionSelected;
    public Selector() {
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen; BackColor = Color.Black; Opacity = .28;
        TopMost = true; ShowInTaskbar = false; DoubleBuffered = true; Cursor = Cursors.Cross; KeyPreview = true;
        KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { anchor = e.Location; drawing = true; } };
        MouseMove += delegate(object s, MouseEventArgs e) {
            if (!drawing) return;
            selection = new Rectangle(Math.Min(anchor.X, e.X), Math.Min(anchor.Y, e.Y), Math.Abs(e.X-anchor.X), Math.Abs(e.Y-anchor.Y)); Invalidate();
        };
        MouseUp += delegate(object s, MouseEventArgs e) {
            if (!drawing) return; drawing = false;
            if (selection.Width < 20 || selection.Height < 20) { DialogResult = DialogResult.Cancel; Close(); return; }
            RegionSelected = new Rectangle(PointToScreen(selection.Location), selection.Size);
            DialogResult = DialogResult.OK; Close();
        };
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        using (var pen = new Pen(Color.White, 3)) e.Graphics.DrawRectangle(pen, selection);
        using (var font = new Font("Microsoft YaHei UI", 16)) e.Graphics.DrawString("拖动框选聊天区域 · Esc 取消", font, Brushes.White, 35, 35);
    }
}
public partial class MainForm : Form {
    readonly Api api = new Api(); Settings cfg = new Settings();
    readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.bin");
    readonly Color ink = Palette.Text, teal = Palette.Text;
    TextBox transcript, context, analysis; TextBox[] replies = new TextBox[3];
    TextBox jevUrl, jevKey, jevModel, chatUrl, chatKey, chatModel, visionModel;
    PictureBox preview; Label status; ModernSelect tone, jevProvider, chatProvider; Button cancel;
    bool filling;
    ListBox windowList; Label windowHint, boundLabel, emptyWindows, pageTitle; ChatWindow boundWindow;
    ModernButton[] navButtons=new ModernButton[3];
    string platform=""; Button wechatChoice, qqChoice;
    PageHost tabs; FlowLayoutPanel actions; TableLayoutPanel root;
    CancellationTokenSource pending; Bitmap capture; Rectangle lastRegion;
    bool busy; readonly bool loadConfig;
    public MainForm(bool readConfig) {
        loadConfig = readConfig; Text = "知言 4.3.0 · 自动聊天助手"; Width = 1360; Height = 920; MinimumSize = new Size(1180,860);
        FormBorderStyle=FormBorderStyle.None; Padding=new Padding(1); DoubleBuffered=true;
        MaximizedBounds=Screen.FromControl(this).WorkingArea;
        StartPosition = FormStartPosition.CenterScreen; BackColor = Palette.Background;
        Font = new Font("Microsoft YaHei UI", 10); ForeColor = ink; AutoScaleMode = AutoScaleMode.Dpi;
        Build();
        if (loadConfig && File.Exists(configPath)) {
            try { cfg = ConfigStore.Load(configPath); FillConfig(); status.Text = "已加载配置 · 选择聊天窗口后开始监测"; }
            catch { status.Text = "无法读取加密配置，请在 设置中重新填写（配置仅限原 Windows 用户使用）。"; }
        }
        else if(loadConfig) { status.Text="首次使用，请在设置中填写 API Key。"; }
    }
    Label Label(string text, int size, bool bold) {
        return new Label { Text = text, AutoSize = true, Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0,5,0,6) };
    }
    Button Button(string text, Action action, bool primary = false) {
        var button = new ModernButton { Text = text, AutoSize = true, Height = 38, MinimumSize = new Size(92,38), Primary=primary,
            Margin = new Padding(0,0,8,7), Padding = new Padding(8,0,8,0) };
        button.Click += delegate { try { if(liveSession==null) action(); } catch (Exception ex) { Error(ex); } }; return button;
    }
    TextBox Multi(bool readOnly = false) {
        return new PaddedTextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.None, ReadOnly = readOnly,
            BorderStyle = BorderStyle.None, BackColor = Palette.Surface, ForeColor = ink, Font = new Font("Microsoft YaHei UI", 11), Margin = new Padding(0,6,0,14) };
    }
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);
    void DragTitle(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } }
    void ToggleMaximize() { MaximizedBounds=Screen.FromControl(this).WorkingArea; WindowState=WindowState==FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; }
    void BuildChrome() {
        var sidebar=new Panel { Dock=DockStyle.Left,Width=192,BackColor=Palette.Sidebar,Padding=new Padding(16,18,16,18) };
        var navigation=new FlowLayoutPanel { Dock=DockStyle.Top,Height=410,FlowDirection=FlowDirection.TopDown,WrapContents=false };
        var brand=Label("知言",21,true); brand.Margin=new Padding(8,6,0,4); navigation.Controls.Add(brand);
        brand.Margin=new Padding(8,6,0,38);
        
        string[] names={"连接聊天","回复工作台","模型设置"};
        for(int i=0;i<3;i++) { int index=i; var nav=new ModernButton { Text=names[i],Width=160,Height=44,TextAlign=ContentAlignment.MiddleLeft,BackColor=Palette.Sidebar,Margin=new Padding(0,0,0,8) }; nav.Click+=delegate { if(!busy) tabs.SelectedIndex=index; }; navButtons[i]=nav; navigation.Controls.Add(nav); }
        sidebar.Controls.Add(navigation);
        var bottom=new Label { Dock=DockStyle.Bottom,Height=28,ForeColor=Palette.Muted,Font=new Font(Font.FontFamily,9),Text="知言  4.3.0",Padding=new Padding(8,0,0,0) }; sidebar.Controls.Add(bottom); Controls.Add(sidebar);
        var caption=new Panel { Dock=DockStyle.Top,Height=42,BackColor=Palette.Sidebar };
        var title=new Label { Text="知言",Dock=DockStyle.Fill,Padding=new Padding(20,0,0,0),TextAlign=ContentAlignment.MiddleLeft,ForeColor=Palette.Muted,Font=new Font("Segoe UI",9) };
        title.MouseDown+=DragTitle; title.DoubleClick+=delegate { ToggleMaximize(); }; caption.Controls.Add(title);
        var controls=new Panel { Dock=DockStyle.Right,Width=144 };
        var minimize=new ModernButton { Text="—",Bounds=new Rectangle(0,4,42,34),BackColor=Palette.Sidebar,AccessibleName="最小化" }; minimize.Click+=delegate { WindowState=FormWindowState.Minimized; };
        var maximize=new ModernButton { Text="□",Bounds=new Rectangle(47,4,42,34),BackColor=Palette.Sidebar,AccessibleName="最大化或还原" }; maximize.Click+=delegate { ToggleMaximize(); };
        var close=new ModernButton { Text="×",Bounds=new Rectangle(94,4,42,34),BackColor=Palette.Sidebar,AccessibleName="关闭" }; close.Click+=delegate { Close(); };
        controls.Controls.Add(minimize); controls.Controls.Add(maximize); controls.Controls.Add(close); caption.Controls.Add(controls); title.BringToFront(); Controls.Add(caption);
        tabs.SelectedIndexChanged+=delegate { string[] titles={"连接聊天","回复","设置"}; pageTitle.Text=titles[tabs.SelectedIndex]; for(int i=0;i<3;i++) { navButtons[i].Selected=i==tabs.SelectedIndex; navButtons[i].Invalidate(); } };
        tabs.SelectedIndex=0;
    }
    protected override void WndProc(ref Message message) {
        base.WndProc(ref message);
        if(message.Msg!=0x84 || WindowState!=FormWindowState.Normal) return;
        long point=message.LParam.ToInt64(); var p=PointToClient(new Point((short)(point&0xffff),(short)((point>>16)&0xffff)));
        bool left=p.X<6,right=p.X>=Width-6,top=p.Y<6,bottom=p.Y>=Height-6;
        int hit=top&&left ? 13 : top&&right ? 14 : bottom&&left ? 16 : bottom&&right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
        if(hit!=0) message.Result=new IntPtr(hit);
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using(var pen=new Pen(Palette.Border)) e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1); }
    void Build() {
        root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(32,18,32,12), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,32)); Controls.Add(root);
        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        pageTitle=Label("连接聊天",20,true); header.Controls.Add(pageTitle);
        var subtitle=Label("Jev 判断策略   →   DeepSeek 识别与回复",9,false); subtitle.ForeColor=Palette.Muted;  root.Controls.Add(header,0,0);
        tabs = new PageHost { Dock = DockStyle.Fill }; root.Controls.Add(tabs,0,1);
        var launcher = new Panel { BackColor=Palette.Background,Padding=new Padding(0,18,0,0) };
        var work = new Panel { BackColor = Palette.Background, Padding = new Padding(0,14,0,0) };
        var settings = new Panel { BackColor = Palette.Background, Padding = new Padding(0,10,0,0), AutoScroll = true };
        tabs.Add(launcher); tabs.Add(work); tabs.Add(settings); BuildLauncher(launcher);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,49)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,51)); work.Controls.Add(columns);
        BuildLiveToolbar(work); BuildProfiles(work); columns.BringToFront();
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(0,0,16,0) };
        foreach (var height in new float[]{36,96,110,30}) left.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        left.RowStyles.Add(new RowStyle(SizeType.Percent,100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute,30)); left.RowStyles.Add(new RowStyle(SizeType.Absolute,67));
        columns.Controls.Add(left,0,0); left.Controls.Add(Label("聊天内容",12,true),0,0);
        actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.Add(Button("读取所选窗口",CaptureBound,true));
        actions.Controls.Add(Button("框选聊天", () => CaptureScreen(false)));
        
        
        actions.Controls.Add(Button("识别截图", () => Run(async ct => { RequireCapture(); ReadConfig(); transcript.Text = await api.Recognize(cfg, ImageBytes(), ct); status.Text = "识别完成：请核对【我】和【对方】，再点击生成回复。"; }), true));
        
        var more=Button("更多",()=>{}); var moreMenu=new OwnedPopup(more); more.ContextMenuStrip=moreMenu;
        Action<Action> menuAction=action=> { if(busy || liveSession!=null) return; try { action(); } catch(Exception ex) { Error(ex); } };
        moreMenu.Items.Add("导入截图",null,delegate { menuAction(ImportImage); });
        moreMenu.Items.Add("粘贴文字",null,delegate { menuAction(()=>transcript.Text=Clipboard.GetText()); });
        moreMenu.Items.Add("刷新原区域",null,delegate { menuAction(()=>CaptureScreen(true)); });
        moreMenu.Items.Add("清空会话",null,delegate { menuAction(Clear); });
        more.Click+=delegate { if(!busy && liveSession==null) moreMenu.Show(more,new Point(0,more.Height)); }; actions.Controls.Add(more);
        boundLabel=new Label { Text="尚未绑定窗口 · 可框选或导入截图",AutoSize=true,MaximumSize=new Size(430,0),Margin=new Padding(0,2,0,0),Font=new Font(Font.FontFamily,9) };
        actions.Controls.Add(boundLabel); left.Controls.Add(actions,0,1);
        preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Palette.Surface, Margin = new Padding(0,0,0,7) };
        preview.Paint+=delegate(object sender,PaintEventArgs e) { if(preview.Image==null) TextRenderer.DrawText(e.Graphics,"截图预览",Font,preview.ClientRectangle,Palette.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter); };
        left.Controls.Add(preview,0,2); left.Controls.Add(Label("聊天文字",10,true),0,3);
        transcript = Multi(); left.Controls.Add(transcript,0,4);
        left.Controls.Add(Label("补充背景（可选）",10,true),0,5);
        context = Multi(); left.Controls.Add(context,0,6);
        captureLayout=left; actions.Visible=false; preview.Visible=false; left.RowStyles[1].Height=0; left.RowStyles[2].Height=0;
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(10,0,0,0) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        right.RowStyles.Add(new RowStyle(SizeType.Percent,25));
        for(int i=0;i<3;i++) right.RowStyles.Add(new RowStyle(SizeType.Percent,25));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,25)); columns.Controls.Add(right,1,0);
        right.Controls.Add(Label("建议回复",12,true),0,0);
        var generateBar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        tone = new ModernSelect { Width = 148, Margin = new Padding(0,0,10,0) };
        tone.Items.AddRange(new object[]{"自然得体","职场专业","温和亲近","简洁直接"}); tone.SelectedIndex=0;
        generateBar.Controls.Add(tone); generateBar.Controls.Add(Button("生成回复", () => Run(Generate), true)); right.Controls.Add(generateBar,0,1);
        analysis=Multi(true); analysis.Text="读到聊天内容后，这里会显示对话重点";
        right.Controls.Add(WorkCard("对话重点",analysis,null),0,2);
        string[] names={"01  简洁一点","02  温和一点","03  直接一点"};
        for(int i=0;i<3;i++) {
            int index=i; replies[i]=Multi();
            var copy = new ModernButton { Text="复制",Width=78,Height=28,Margin=Padding.Empty };
            copy.Click += delegate { try { if(!String.IsNullOrWhiteSpace(replies[index].Text)) { Clipboard.SetText(replies[index].Text); status.Text="已复制，可返回聊天软件粘贴并发送。"; } } catch(Exception ex) { Error(ex); } };
            right.Controls.Add(WorkCard(names[i],replies[i],copy),0,3+i);
        }
        var footnote=Label("回复可修改 · 由你决定是否发送",9,false); footnote.ForeColor=Palette.Muted;
        right.Controls.Add(footnote,0,6);
        BuildSettings(settings);
        var footer=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(0,6,0,0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));
        status=new Label { Text="选择聊天窗口后开始监测",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,ForeColor=Palette.Muted,Font=new Font(Font.FontFamily,9) };
        cancel=Button("取消请求",()=> { if(pending!=null) pending.Cancel(); }); cancel.Enabled=false; cancel.Visible=false; footer.Controls.Add(status,0,0); footer.Controls.Add(cancel,1,0); root.Controls.Add(footer,0,2);
        var privacy=Label("监测在本机进行 · 自动决策会上传变化后的聊天文字 · 不自动发送",9,false); privacy.ForeColor=Palette.Muted; root.Controls.Add(privacy,0,3);
        BuildChrome();
    }
    Panel WorkCard(string title,TextBox editor,Button action) {
        var card=new SurfacePanel { Dock=DockStyle.Fill,BackColor=Palette.Surface,Padding=new Padding(14,8,14,10),Margin=new Padding(0,0,0,10) };
        var header=new Panel {Dock=DockStyle.Top,Height=32};
        var label=new Label {Text=title,Dock=DockStyle.Fill,ForeColor=Palette.Muted,TextAlign=ContentAlignment.MiddleLeft,Font=new Font(Font.FontFamily,9)};
        header.Controls.Add(label);
        if(action!=null) {action.Dock=DockStyle.Right;header.Controls.Add(action);action.SendToBack();}
        editor.Margin=Padding.Empty;
        card.Controls.Add(editor);card.Controls.Add(header);
        return card;
    }
    void BuildLauncher(Panel page) {
        var grid=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=7 };
        foreach(int h in new[]{0,62,108,44}) grid.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent,100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,55)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        page.Controls.Add(grid);
        
        var introduction=Label("选一个聊天窗口，开始自然接话",14,false); introduction.ForeColor=Palette.Muted; grid.Controls.Add(introduction,0,1); 
        var choices=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1 };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        wechatChoice=new ModernButton { Text="微信",Subtitle="个人聊天 · 群聊",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,15,FontStyle.Bold),Margin=new Padding(0,8,10,8) };
        qqChoice=new ModernButton { Text="QQ",Subtitle="主窗口 · 独立会话",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,15,FontStyle.Bold),Margin=new Padding(10,8,0,8) };
        wechatChoice.Click+=delegate { SelectPlatform("微信"); }; qqChoice.Click+=delegate { SelectPlatform("QQ"); };
        choices.Controls.Add(wechatChoice,0,0); choices.Controls.Add(qqChoice,1,0); grid.Controls.Add(choices,0,2);
        windowHint=Label("可连接的窗口",11,true); grid.Controls.Add(windowHint,0,3);
        var listArea=new SurfacePanel { Dock=DockStyle.Fill,BackColor=Palette.Surface,Padding=new Padding(12) };
        windowList=new ListBox { Dock=DockStyle.Fill,IntegralHeight=false,BorderStyle=BorderStyle.None,Font=new Font(Font.FontFamily,11),HorizontalScrollbar=true,BackColor=Palette.Surface,ForeColor=Palette.Text,DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=60,Visible=false };
        windowList.DrawItem+=delegate(object sender,DrawItemEventArgs e) { if(e.Index<0) return; using(var brush=new SolidBrush((e.State&DrawItemState.Selected)!=0 ? Palette.Hover : Palette.Surface)) e.Graphics.FillRectangle(brush,e.Bounds); TextRenderer.DrawText(e.Graphics,Convert.ToString(windowList.Items[e.Index]),windowList.Font,new Rectangle(e.Bounds.X+12,e.Bounds.Y,e.Bounds.Width-24,e.Bounds.Height),Palette.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis); e.DrawFocusRectangle(); };
        emptyWindows=new Label { Text="先选择上方的微信或 QQ"+Environment.NewLine+Environment.NewLine+"已打开的聊天窗口会出现在这里",Dock=DockStyle.Fill,ForeColor=Palette.Muted,TextAlign=ContentAlignment.MiddleCenter };
        listArea.Controls.Add(windowList); listArea.Controls.Add(emptyWindows);
        windowList.DoubleClick+=delegate { try { BindWindow(); } catch(Exception ex) { Error(ex); } }; grid.Controls.Add(listArea,0,4);
        var bar=new FlowLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(0,10,0,0) };
        bar.Controls.Add(Button("选择此窗口",BindWindow,true)); bar.Controls.Add(Button("刷新窗口列表",RefreshWindows));
         grid.Controls.Add(bar,0,5);
        grid.Controls.Add(new Label { AutoSize=true,Dock=DockStyle.Fill,Text="选择后进入工作台，可随时切换到跟随聊天的侧栏。",ForeColor=Palette.Muted,Font=new Font(Font.FontFamily,9),Margin=new Padding(0,12,0,0) },0,6);
    }
    void SelectPlatform(string value) {
        platform=value; ((ModernButton)wechatChoice).Selected=value=="微信"; ((ModernButton)qqChoice).Selected=value=="QQ"; wechatChoice.Invalidate(); qqChoice.Invalidate();
        RefreshWindows();
    }
    void RefreshWindows() {
        if(String.IsNullOrEmpty(platform)) { windowHint.Text="请先选择微信或 QQ。"; return; }
        try {
            windowList.Items.Clear(); var found=ChatWindows.Find(platform); foreach(var window in found) windowList.Items.Add(window);
            windowList.Visible=found.Count>0; emptyWindows.Visible=found.Count==0; emptyWindows.Text="未发现 "+platform+" 窗口\r\n\r\n打开并登录 "+platform+" 后，点击刷新窗口列表。";
            windowHint.Text=found.Count==0 ? "未发现 "+platform+" 窗口，请打开并登录后点击刷新。" : platform+" · 找到 "+found.Count+" 个窗口，请选择实际聊天会话。";
        } catch(Exception ex) { Error(ex); }
    }
    void BindWindow() {
        var choice=windowList.SelectedItem as ChatWindow;
        if(choice==null) { windowHint.Text="请先在列表中选中一个聊天窗口。"; return; }
        BindProfile(choice); selectedProfile.AutomaticRegion=true; autoRetryBlocked=false;
        status.Text="窗口已选中，点击「进入悬浮助手」开始；识别失败时可校准消息区域";
    }
    async void CaptureBound() {
        if(busy) return;
        if(boundWindow==null) { tabs.SelectedIndex=0; status.Text="请先选择微信或 QQ 的聊天窗口。"; return; }
        busy=true; tabs.Enabled=false; Exception failure=null;
        try { Hide(); await Task.Delay(200); using(var bitmap=await ChatWindows.Capture(boundWindow)) SetCapture(bitmap); }
        catch(Exception ex) { failure=ex; }
        finally { Show(); Activate(); busy=false; tabs.Enabled=true; }
        if(failure!=null) Error(failure); else status.Text="已读取所选窗口。确认预览后点击「识别截图」，可见区域中的消息将发送给视觉模型。";
    }
    void BuildSettings(Panel page) {
        var grid=new TableLayoutPanel { Dock=DockStyle.Top, AutoSize=true, ColumnCount=2, Padding=new Padding(4), RowCount=3 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); page.Controls.Add(grid);
        var jev=ServicePanel("Jev"); var chat=ServicePanel("DeepSeek");
        grid.Controls.Add(jev,0,0); grid.Controls.Add(chat,1,0);
        jevProvider=Provider(jev,new object[]{"OpenRouter · Jev","TypeSafe 官方 · Jev","自定义 Jev 决策接口"});
        ServiceField(jev,"API Key（所选 Jev 服务商的密钥）",out jevKey,true);
        ServiceField(jev,"接口地址 · 预设自动填写",out jevUrl,false);
        ServiceField(jev,"Jev 模型",out jevModel,false);
        var jb=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,14,0,4) };
        jb.Controls.Add(Button("保存 Jev",()=>SaveSection(true),true));
        jb.Controls.Add(Button("测试 Jev",()=>Run(async ct=> { ReadConfig(); status.Text=await api.Test(cfg,true,ct); })));
        jev.Controls.Add(jb);
        jev.Controls.Add(new Label { AutoSize=true,MaximumSize=new Size(420,0),Text="OpenRouter 使用 OpenRouter Key；TypeSafe 使用官方 Key。\r\n切换服务商会清空该组密钥，避免误发给其他服务。",Margin=new Padding(0,8,0,0) });
        chatProvider=Provider(chat,new object[]{"DeepSeek 官方（推荐）","自定义 Chat Completions"});
        ServiceField(chat,"API Key（DeepSeek 官方密钥）",out chatKey,true);
        ServiceField(chat,"回复接口地址 · 预设自动填写",out chatUrl,false);
        ServiceField(chat,"中文回复模型",out chatModel,false);
        ServiceField(chat,"截图识别模型",out visionModel,false);
        var cb=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,14,0,4) };
        cb.Controls.Add(Button("保存回复服务",()=>SaveSection(false),true));
        cb.Controls.Add(Button("测试回复",()=>Run(async ct=> { ReadConfig(); status.Text=await api.Test(cfg,false,ct); })));
        chat.Controls.Add(cb);
        var all=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,20,0,8) };
        all.Controls.Add(Button("保存全部配置",()=> { ReadConfig(); ValidateAddresses(); ConfigStore.Save(configPath,cfg); status.Text="两组配置已加密保存，可稍后补齐缺失密钥。"; },true));
        all.Controls.Add(Button("测试双模型联动",()=>Run(async ct=> { ReadConfig(); status.Text=await api.TestPipeline(cfg,ct); })));
        grid.Controls.Add(all,0,1); grid.SetColumnSpan(all,2);
        var notes=new Label { AutoSize=true,MaximumSize=new Size(940,0),ForeColor=Palette.Muted,Font=new Font(Font.FontFamily,9),Text="分别填写两组密钥即可开始；接口与模型由预设自动填写。\r\n联动测试使用固定示例，可能产生少量 API 费用。密钥仅在本机加密保存。",Margin=new Padding(0,8,0,0) };
        grid.Controls.Add(notes,0,2); grid.SetColumnSpan(notes,2); BuildStartupOptions(grid);
        jevProvider.SelectedIndexChanged += delegate { if(filling) return; jevKey.Clear(); if(jevProvider.SelectedIndex==0) { jevUrl.Text="https://openrouter.ai/api/alpha/decisions"; jevModel.Text="typesafe/jev-1.13"; } else if(jevProvider.SelectedIndex==1) { jevUrl.Text="https://api.typesafe.ai/v1/systemone"; jevModel.Text="jev-1.13.0"; } jevUrl.ReadOnly=jevProvider.SelectedIndex!=2; };
        chatProvider.SelectedIndexChanged += delegate { if(filling) return; chatKey.Clear(); if(chatProvider.SelectedIndex==0) { chatUrl.Text="https://api.deepseek.com/chat/completions"; chatModel.Text="deepseek-flash"; visionModel.Text="deepseek-flash"; } chatUrl.ReadOnly=chatProvider.SelectedIndex==0; };
        FillConfig();
    }
    TableLayoutPanel ServicePanel(string title) {
        var panel=new TableLayoutPanel { AutoSize=true,Dock=DockStyle.Top,ColumnCount=1,Padding=new Padding(18),Margin=new Padding(0,0,16,0),BackColor=Palette.Surface };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); panel.Controls.Add(Label(title,12,true)); return panel;
    }
    ModernSelect Provider(TableLayoutPanel panel,object[] values) {
        var select=new ModernSelect { Dock=DockStyle.Top,Margin=new Padding(0,8,0,12) }; select.Items.AddRange(values); panel.Controls.Add(select); return select;
    }
    void ServiceField(TableLayoutPanel panel,string label,out TextBox box,bool secret) {
        var caption=Label(label,9,false); caption.ForeColor=Palette.Muted; panel.Controls.Add(caption);
        var frame=new Panel { Dock=DockStyle.Top,Height=36,Padding=new Padding(10,7,10,5),BackColor=Palette.Background,Margin=new Padding(0,2,0,8) };
        box=new TextBox { Dock=DockStyle.Fill,UseSystemPasswordChar=secret,BorderStyle=BorderStyle.None,BackColor=Palette.Background,ForeColor=Palette.Text,Font=new Font("Segoe UI",10) }; frame.Controls.Add(box); panel.Controls.Add(frame);
    }
    void ValidateAddresses() {
        try { Api.ValidateUrl(cfg.JevUrl); } catch(ArgumentException) { throw new ArgumentException("Jev 地址无效，请选择服务商预设。"); }
        try { Api.ValidateUrl(cfg.ChatUrl); } catch(ArgumentException) { throw new ArgumentException("回复服务地址无效，请选择 DeepSeek 官方预设。"); }
    }
    void SaveSection(bool jev) {
        ReadConfig();
        try { Api.ValidateUrl(jev ? cfg.JevUrl : cfg.ChatUrl); } catch(ArgumentException) { throw new ArgumentException((jev ? "Jev" : "回复服务")+"地址无效，请选择服务商预设。"); }
        var saved=File.Exists(configPath) ? ConfigStore.Load(configPath) : new Settings();
        ConfigStore.Save(configPath,Settings.MergeSection(saved,cfg,jev));
        status.Text=(jev ? "Jev" : "回复服务")+"配置已加密保存，另一组已保存的配置保持不变。";
    }
    void AddField(TableLayoutPanel grid,int row,string label,out TextBox box,bool secret) {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        grid.Controls.Add(Label(label,10,false),0,row);
        box=new TextBox { Dock=DockStyle.Top,UseSystemPasswordChar=secret,Margin=new Padding(0,5,0,8) }; grid.Controls.Add(box,1,row);
    }
    public void ShowSettings() { tabs.SelectedIndex=2; }
    public void ShowWork() { tabs.SelectedIndex=1; }
    void FillConfig() {
        filling=true;
        jevProvider.SelectedIndex=cfg.JevUrl=="https://openrouter.ai/api/alpha/decisions" ? 0 : cfg.JevUrl=="https://api.typesafe.ai/v1/systemone" ? 1 : 2;
        chatProvider.SelectedIndex=cfg.ChatUrl=="https://api.deepseek.com/chat/completions" ? 0 : 1;
        jevUrl.Text=cfg.JevUrl; jevKey.Text=cfg.JevKey; jevModel.Text=cfg.JevModel; chatUrl.Text=cfg.ChatUrl; chatKey.Text=cfg.ChatKey; chatModel.Text=cfg.ChatModel; visionModel.Text=cfg.VisionModel;
        jevUrl.ReadOnly=jevProvider.SelectedIndex!=2; chatUrl.ReadOnly=chatProvider.SelectedIndex==0; filling=false;
    }
    void ReadConfig() { cfg=new Settings { JevUrl=jevUrl.Text.Trim(),JevKey=jevKey.Text.Trim(),JevModel=jevModel.Text.Trim(),ChatUrl=chatUrl.Text.Trim(),ChatKey=chatKey.Text.Trim(),ChatModel=chatModel.Text.Trim(),VisionModel=visionModel.Text.Trim() }; }
    void Error(Exception ex) {
        string message = ex is HttpRequestException ? "连接失败，请检查网络、接口地址和服务商可用性。" : ex.Message;
        if(ex is ArgumentException) ShowSettings();
        status.Text="操作未完成："+message; MessageBox.Show(this,message,"知言",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    async void Run(Func<CancellationToken,Task> task) {
        if(busy) return; busy=true; tabs.Enabled=false; cancel.Enabled=true; cancel.Visible=true; pending=new CancellationTokenSource(); status.Text="正在请求，请稍候…";
        Exception failure=null;
        try { await task(pending.Token); }
        catch(OperationCanceledException) { status.Text=pending.IsCancellationRequested ? "已取消请求。" : "请求超时，请稍后重试。"; }
        catch(Exception ex) { failure=ex; }
        finally { pending.Dispose(); pending=null; busy=false; tabs.Enabled=true; cancel.Enabled=false; cancel.Visible=false; }
        if(failure!=null) Error(failure);
    }
    async Task Generate(CancellationToken ct) {
        var previous=refreshPrevious; refreshPrevious=null;
        ReadConfig(); Api.Validate(cfg,true,false); Api.Validate(cfg,false,false);
        string text=ChatRedaction.Apply(transcript.Text.Trim()); if(text.Length==0) throw new ArgumentException("请先识别截图或粘贴聊天文字。");
        if(text.Length+context.Text.Length>18000) throw new ArgumentException("聊天和背景合计请控制在18000字符以内，仅保留相关上下文。");
        foreach(var reply in replies) reply.Clear(); analysis.Text="Jev 正在分析回复策略…"; status.Text="1/2 · Jev 分析";
        Strategy strategy;
        try { strategy=await api.Evaluate(cfg,text,ChatRedaction.Apply(context.Text),ct); }
        catch(OperationCanceledException) { throw; }
        catch(Exception ex) { throw new InvalidOperationException("Jev 策略阶段失败："+ex.Message+"\r\n回复模型尚未调用。"); }
        analysis.Text="建议策略："+strategy.Label+"\r\n模型置信指标："+strategy.Confidence.ToString("0.00")+"（非准确率或心理概率）\r\n"+
            (strategy.NeedsContext>0.5 || strategy.Confidence<0.65 ? "上下文或判断可能不充分，优先核实。" : "请结合实际情况判断。")+"\r\n\r\n正在生成回复…";
        status.Text="2/2 · 生成中文回复";
        Drafts result;
        try { result=await api.Generate(cfg,text,ChatRedaction.Apply(context.Text),Convert.ToString(tone.SelectedItem),strategy,ct,previous); }
        catch(OperationCanceledException) { throw; }
        catch(Exception ex) { throw new InvalidOperationException("Jev 判断已完成，但中文回复阶段失败："+ex.Message); }
        analysis.Text="建议策略："+strategy.Label+"\r\n"+result.Summary+"\r\n\r\n原文依据："+result.Evidence;
        awaitingUserFact=result.NeedsUserInput;
        for(int i=0;i<3;i++) replies[i].Text=result.Replies[i]; status.Text=result.NeedsUserInput ? "请补充本次聊天信息后重新生成" : "已生成三条回复，核对事实后可编辑、复制。";
    }
    async void CaptureScreen(bool reuse) {
        if(busy) return;
        if(reuse && lastRegion.IsEmpty) { Error(new ArgumentException("请先框选一次聊天区域。")); return; }
        busy=true; tabs.Enabled=false;
        try {
            Hide(); await Task.Delay(250);
            Rectangle region=lastRegion;
            if(!reuse) using(var selector=new Selector()) { if(selector.ShowDialog()!=DialogResult.OK) return; region=selector.RegionSelected; }
            if(!SystemInformation.VirtualScreen.Contains(region)) throw new InvalidOperationException("屏幕布局已变化，请重新框选。");
            await Task.Delay(150);
            using(var bitmap=new Bitmap(region.Width,region.Height)) {
                using(var g=Graphics.FromImage(bitmap)) g.CopyFromScreen(region.Location,Point.Empty,region.Size);
                SetCapture(bitmap);
            }
            lastRegion=region; status.Text="已捕获区域。点击识别截图会将预览图片发送到所配视觉接口。";
        } catch(Exception ex) { Show(); Error(ex); }
        finally { Show(); Activate(); busy=false; tabs.Enabled=true; }
    }
    void SetCapture(Image image) {
        double scale=Math.Min(1.0,2600.0/Math.Max(image.Width,image.Height));
        var next=new Bitmap(Math.Max(1,(int)(image.Width*scale)),Math.Max(1,(int)(image.Height*scale)));
        using(var g=Graphics.FromImage(next)) { g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(image,0,0,next.Width,next.Height); }
        preview.Image=null; if(capture!=null) capture.Dispose(); capture=next; preview.Image=capture;
        transcript.Clear(); analysis.Clear(); foreach(var reply in replies) reply.Clear();
    }
    void ImportImage() {
        using(var dialog=new OpenFileDialog { Filter="聊天截图|*.png;*.jpg;*.jpeg;*.bmp",Title="选择聊天截图" }) {
            if(dialog.ShowDialog()!=DialogResult.OK) return;
            if(new FileInfo(dialog.FileName).Length>20000000) throw new ArgumentException("请选择小于20MB的截图。");
            using(var image=Image.FromFile(dialog.FileName)) {
                if((long)image.Width*image.Height>40000000) throw new ArgumentException("图片像素过大，请先裁剪。");
                SetCapture(image);
            }
            status.Text="已导入截图，点击识别截图开始转写。";
        }
    }
    void RequireCapture() { if(capture==null) throw new ArgumentException("请先框选聊天区域或导入截图。"); }
    byte[] ImageBytes() { using(var stream=new MemoryStream()) { capture.Save(stream,ImageFormat.Png); if(stream.Length>12000000) throw new ArgumentException("截图较大，请框选更小区域。"); return stream.ToArray(); } }
    void Clear() { if(selectedProfile!=null) profiles.Remove(selectedProfile); selectedProfile=null; RefreshProfiles(); transcript.Clear(); context.Clear(); analysis.Clear(); foreach(var reply in replies) reply.Clear(); preview.Image=null; if(capture!=null) capture.Dispose(); capture=null; lastRegion=Rectangle.Empty; boundWindow=null; if(boundLabel!=null) boundLabel.Text="尚未绑定窗口 · 可框选或导入截图"; status.Text="会话已清空。"; }
    protected override void OnFormClosing(FormClosingEventArgs e) {
        if(liveSession!=null) { StopLive(); e.Cancel=true; status.Text="正在停止监测，请稍后关闭。"; base.OnFormClosing(e); return; }
        if(busy) { e.Cancel=true; if(pending!=null) pending.Cancel(); status.Text="请等待当前操作结束后关闭。"; }
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing) { if(disposing) { DisposeAutomatic(); releasingAssistant=true; if(assistantPanel!=null) assistantPanel.Dispose(); api.Dispose(); if(capture!=null) capture.Dispose(); } base.Dispose(disposing); }
}
}
