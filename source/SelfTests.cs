using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JevChat {
public class FakeHandler : HttpMessageHandler {
    public readonly Queue<string> Responses = new Queue<string>();
    public readonly List<Dictionary<string,object>> Bodies = new List<Dictionary<string,object>>();
    public readonly List<string> Hosts = new List<string>();
    public readonly List<string> Keys = new List<string>();
    public HttpStatusCode Status = HttpStatusCode.OK;
    public bool Wait;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
        if(Wait) await Task.Delay(10000,ct);
        if(request.Headers.Authorization == null || request.Headers.Authorization.Scheme != "Bearer") throw new Exception("Missing bearer auth");
        Bodies.Add(Json.Read(await request.Content.ReadAsStringAsync()));
        Hosts.Add(request.RequestUri.Host); Keys.Add(request.Headers.Authorization.Parameter);
        return new HttpResponseMessage(Status) { Content=new StringContent(Responses.Count>0 ? Responses.Dequeue() : "{}",Encoding.UTF8,"application/json") };
    }
}
public static class SelfTests {
    public static int InstanceTest() {
        try {
            bool created;
            using(var mutex=new Mutex(true,"Local\\Zhiyan.Automatic.Instance",out created)) {
                if(!created) { File.WriteAllText("instance-test-result.txt","SKIP: app already running; did not touch it."); return 0; }
                using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\Zhiyan.Automatic.Show"))
                using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName=System.Windows.Forms.Application.ExecutablePath,Arguments="--watch",UseShellExecute=false,CreateNoWindow=true
                })) {
                    bool notified=signal.WaitOne(5000); bool exited=child.WaitForExit(5000);
                    if(!exited) child.Kill();
                    if(!notified || !exited || child.ExitCode!=0) throw new Exception("Duplicate launch did not notify and exit");
                }
            }
            File.WriteAllText("instance-test-result.txt","PASS: duplicate --watch launch signaled existing instance and exited; no watcher or model request started."); return 0;
        } catch(Exception ex) { File.WriteAllText("instance-test-result.txt",ex.ToString()); return 1; }
    }
    public static int MenuTests() {
        try {
            using(var main=new MainForm(false)) main.TestChatProfiles(Check);
            using(var chat=new System.Windows.Forms.Form()) using(var side=new System.Windows.Forms.Form()) {
                // Keep the fixture above unrelated desktop windows, then test its own occluder.
                chat.TopMost=side.TopMost=true;
                chat.StartPosition=side.StartPosition=System.Windows.Forms.FormStartPosition.Manual;
                chat.Bounds=new System.Drawing.Rectangle(50,50,400,300); side.Bounds=new System.Drawing.Rectangle(500,50,250,300);
                chat.Show(); side.Show(); side.Activate(); System.Windows.Forms.Application.DoEvents();
                var target=new ChatWindow { Handle=chat.Handle };
                var region=new System.Drawing.Rectangle(100,120,180,100);
                Check(ChatWindows.Uncovered(target,region),"visible chat remains readable while side panel has focus");
                side.Location=new System.Drawing.Point(90,110); System.Windows.Forms.Application.DoEvents();
                Check(!ChatWindows.Uncovered(target,region),"overlapping side panel pauses capture");
                side.Hide(); System.Windows.Forms.Application.DoEvents();
                Check(ChatWindows.Uncovered(target,region),"capture resumes after obstruction hidden");
                chat.WindowState=System.Windows.Forms.FormWindowState.Minimized; System.Windows.Forms.Application.DoEvents();
                Check(!ChatWindows.Uncovered(target,region),"minimized chat remains excluded");
            }
            using(var form=new System.Windows.Forms.Form()) {
                var select=new ModernSelect { Width=200 }; select.Items.AddRange(new object[]{"本地视觉监测","直接读取文本"}); select.SelectedIndex=0;
                form.Controls.Add(select); form.Show(); System.Windows.Forms.Application.DoEvents();
                var menu=select.ContextMenuStrip; int closed=0;
                menu.Closed+=delegate { Check(!menu.IsDisposed,"menu alive during close callback"); closed++; };
                for(int i=0;i<30;i++) {
                    select.PerformClick(); System.Windows.Forms.Application.DoEvents();
                    Check(menu.Visible,"dropdown opened");
                    if(i%3==0) { menu.Items[1].PerformClick(); Check(select.SelectedIndex==1,"selection preserved"); }
                    menu.Close(i%2==0 ? System.Windows.Forms.ToolStripDropDownCloseReason.AppClicked : System.Windows.Forms.ToolStripDropDownCloseReason.Keyboard);
                    System.Windows.Forms.Application.DoEvents(); Check(!menu.IsDisposed,"closed menu reusable");
                }
                Check(closed==30,"all close paths exercised");
                select.Dispose(); Check(menu.IsDisposed,"menu released with owner");
                var owner=new System.Windows.Forms.Button(); form.Controls.Add(owner);
                var popup=new OwnedPopup(owner); popup.Items.Add("更多");
                for(int i=0;i<10;i++) { popup.Show(owner,new System.Drawing.Point(0,owner.Height)); popup.Close(System.Windows.Forms.ToolStripDropDownCloseReason.AppClicked); System.Windows.Forms.Application.DoEvents(); Check(!popup.IsDisposed,"more popup reusable"); }
                owner.Dispose(); Check(popup.IsDisposed,"more popup released");
            }
            File.WriteAllText("ui-test-result.txt","PASS: "+count+" assertions; 40 dropdown open/close cycles, selection, dismissal and owner cleanup."); return 0;
        } catch(Exception ex) { File.WriteAllText("ui-test-result.txt",ex.ToString()); return 1; }
    }
    public static int LocalOcrTest() {
        try {
            Task.Run(async ()=> {
                using(var ocr=new LocalOcr()) using(var bitmap=new System.Drawing.Bitmap(800,160)) {
                    using(var g=System.Drawing.Graphics.FromImage(bitmap)) using(var font=new System.Drawing.Font("Microsoft YaHei UI",28)) {
                        g.Clear(System.Drawing.Color.White);
                        g.DrawString("明天下午三点开会，请确认时间。",font,System.Drawing.Brushes.Black,20,50);
                    }
                    await ocr.Start(CancellationToken.None);
                    for(int i=0;i<2;i++) {
                        string text=await ocr.Read(bitmap,CancellationToken.None);
                        if(!text.Replace(" ","").Contains("明天下午三点开会")) throw new Exception("Chinese OCR fixture mismatch");
                    }
                    using(var cancelled=new CancellationTokenSource()) {
                        cancelled.Cancel(); bool rejected=false;
                        try { await ocr.Read(bitmap,cancelled.Token); } catch(OperationCanceledException) { rejected=true; }
                        if(!rejected) throw new Exception("OCR cancellation was not respected");
                    }
                }
            }).GetAwaiter().GetResult();
            File.WriteAllText("local-ocr-test-result.txt","PASS: two Chinese fixture reads through persistent OCR worker; cancellation respected. No user chat or network used."); return 0;
        } catch(Exception ex) { File.WriteAllText("local-ocr-test-result.txt",ex.ToString()); return 1; }
    }
    static int count;
    static string skipped = "";
    static void Check(bool condition,string name) { if(!condition) throw new Exception("FAIL: "+name); count++; }
    static void Reject(Action action,string name) { bool caught=false; try { action(); } catch { caught=true; } Check(caught,name); }
    static string ChatResponse(string text) { return Json.Write(new { choices=new[] { new { message=new { content=text },finish_reason="stop" } } }); }
    static Settings Config() { return new Settings { JevKey="test-secret",ChatUrl="https://example.invalid/v1/chat/completions",ChatKey="test-secret",ChatModel="test-chat",VisionModel="test-vision" }; }
    public static int Run() {
        try { FullTests().GetAwaiter().GetResult(); File.WriteAllText("self-test-result.txt","PASS: "+count+" assertions. Mock API only; no live API requests.\r\n"+skipped,Encoding.UTF8); return 0; }
        catch(Exception ex) { File.WriteAllText("self-test-result.txt",ex.ToString(),Encoding.UTF8); return 1; }
    }
    static async Task FullTests() {
        using(var startupForm=new MainForm(false)) startupForm.TestWorkspaceFlow(Check);
        using(var switchForm=new MainForm(false)) switchForm.TestConversationSwitch(Check);
        using(var refreshForm=new MainForm(false)) refreshForm.TestReplyRefresh(Check);
        SynchronizationContext.SetSynchronizationContext(null); // Console test runner has no WinForms message pump.
        var mediaArea=new System.Drawing.Rectangle(100,100,800,600);
        Check(!QqAccessible.IsMessageImage("",new System.Drawing.Rectangle(110,180,40,40),mediaArea),"left unnamed avatar excluded");
        Check(!QqAccessible.IsMessageImage("",new System.Drawing.Rectangle(850,180,40,40),mediaArea),"right unnamed avatar excluded");
        Check(QqAccessible.IsMessageImage("",new System.Drawing.Rectangle(180,180,180,180),mediaArea),"unnamed received sticker retained");
        Check(QqAccessible.IsMessageImage("",new System.Drawing.Rectangle(620,180,180,180),mediaArea),"unnamed outgoing sticker retained");
        Check(QqAccessible.IsMessageImage("图片",new System.Drawing.Rectangle(180,180,30,30),mediaArea),"named small attachment retained");
        Check(!QqAccessible.IsMessageImage("图片",new System.Drawing.Rectangle(180,900,180,180),mediaArea),"offscreen attachment excluded");
        var qqLayout=new OcrLayout {Text="聊天A 发送"}; string qqName;
        qqLayout.Lines.Add(new OcrBox {Text="聊天A",Bounds=new System.Drawing.Rectangle(280,35,100,25)});
        qqLayout.Lines.Add(new OcrBox {Text="发送",Bounds=new System.Drawing.Rectangle(930,665,50,25)});
        var qqRegion=WechatVision.QqRegion(qqLayout,new System.Drawing.Size(1000,720),"QQ",out qqName);
        Check(!qqRegion.IsEmpty && qqName=="聊天A" && qqRegion.Left<=280,"QQ embedded conversation uses its header and preserves left avatar gutter");
        qqLayout.Lines[0].Text="聊天B";
        WechatVision.QqRegion(qqLayout,new System.Drawing.Size(1000,720),"QQ",out qqName);
        Check(qqName=="聊天B","QQ same-window contact switch changes conversation identity");
        qqRegion=WechatVision.QqRegion(qqLayout,new System.Drawing.Size(1000,720),"独立聊天",out qqName);
        Check(qqName=="独立聊天" && qqRegion.Left==12,"QQ detached chat keeps left messages");
        Check(WechatVision.QqRegion(new OcrLayout {Text="图片查看器"},new System.Drawing.Size(1000,720),"图片查看器",out qqName).IsEmpty,"QQ image viewer is not a chat without composer");
        qqLayout.Text="扫码登录 发送";
        Check(WechatVision.QqRegion(qqLayout,new System.Drawing.Size(1000,720),"QQ",out qqName).IsEmpty,"QQ login excluded from binding");
        foreach(bool light in new[]{false,true}) using(var frame=new System.Drawing.Bitmap(800,620)) {
            var region=new System.Drawing.Rectangle(0,0,800,620);
            using(var g=System.Drawing.Graphics.FromImage(frame)) {
                g.Clear(light?System.Drawing.Color.White:System.Drawing.Color.FromArgb(30,30,30));
                g.FillRectangle(System.Drawing.Brushes.Blue,15,35,42,42);
                g.FillRectangle(System.Drawing.Brushes.LimeGreen,70,35,220,55);
                g.FillRectangle(System.Drawing.Brushes.Blue,742,140,42,42);
                g.FillRectangle(System.Drawing.Brushes.Gray,90,140,640,90);
                g.FillRectangle(System.Drawing.Brushes.Blue,15,290,42,42);
                g.FillRectangle(System.Drawing.Brushes.Orange,70,290,170,110);
                g.FillRectangle(System.Drawing.Brushes.Blue,742,460,42,42);
                g.FillRectangle(System.Drawing.Brushes.Gray,490,460,240,100);
            }
            var geometryLayout=new OcrLayout();
            geometryLayout.Lines.Add(new OcrBox {Text="左侧绿色内容",Bounds=new System.Drawing.Rectangle(85,48,150,25)});
            geometryLayout.Lines.Add(new OcrBox {Text="右侧长消息的第一行",Bounds=new System.Drawing.Rectangle(105,152,240,25)});
            geometryLayout.Lines.Add(new OcrBox {Text="同一条的换行",Bounds=new System.Drawing.Rectangle(105,190,180,25)});
            geometryLayout.Lines.Add(new OcrBox {Text="计划.pdf",Bounds=new System.Drawing.Rectangle(505,475,120,25)});
            string result=MessageGeometry.Read(geometryLayout,region,frame);
            Check(result.Contains("【对方】左侧绿色内容"),"left remains incoming regardless of green pixels");
            Check(result.Contains("【我】右侧长消息的第一行 / 同一条的换行"),"right avatar anchors long bubble crossing middle and wrapped text");
            Check(result.Contains("【对方】[非文字消息，内容未识别]"),"left image or sticker retained without OCR text");
            Check(result.Contains("【我】计划.pdf"),"right neutral-color attachment belongs to user");
            geometryLayout.Lines.RemoveAt(3);
            Check(MessageGeometry.Read(geometryLayout,region,frame).EndsWith("【我】[非文字消息，内容未识别]"),"right nontext message retained");
        }
        var missingMeal=Api.ParseDrafts(Json.Write(new {summary="对方问了晚饭，之后又提醒休息，晚饭问题仍未回答",evidence="今晚吃了点什么呢",needs_user_input=true,question="你今晚吃了什么？",replies=new string[0]}));
        Check(missingMeal.NeedsUserInput && missingMeal.Replies.Length==3 && Array.TrueForAll(missingMeal.Replies,x=>x==""),"unknown personal fact produces no sendable drafts");
        Check(missingMeal.Summary.Contains("你今晚吃了什么") && missingMeal.Summary.Contains("补充信息"),"missing fact question is directed to software user");
        var unsafeMeal=Api.ParseDrafts(Json.Write(new {summary="缺少事实",evidence="晚饭",needs_user_input=true,question="吃了什么？",replies=new[]{"随便吃了点","你吃的啥","还没吃"}}));
        Check(Array.TrueForAll(unsafeMeal.Replies,x=>x==""),"blocked response cannot leak invented draft even if model supplied it");
        Reject(()=>Api.ParseDrafts(Json.Write(new {summary="",evidence="",needs_user_input=true,replies=new string[0]})),"missing user question rejected");
        var delayDraft=new Drafts {Replies=new[]{"刚看到，明天一起去图书馆？","",""}};
        Check(ReplyLanguage.UnsupportedDelay(delayDraft,"【对方】刚看到",""),"other person's delay does not justify user's excuse");
        Check(!ReplyLanguage.UnsupportedDelay(delayDraft,"【对方】怎么了","刚看到消息"),"explicit user background supports delay");
        Check(!ReplyLanguage.UnsupportedDelay(delayDraft,"【我】刚看到消息",""),"user's own message supports delay");
        Check(ReplyLanguage.UnsupportedDelay(delayDraft,"【对方】怎么了",""),"unsupported delay detected");
        Check(!ReplyLanguage.UnsupportedDelay(new Drafts {Replies=new[]{"明天一起去图书馆？","",""}},"",""),"natural invitation needs no delay explanation");
        Check(ReplyLanguage.Polish("收到，谢谢。 ")=="收到，谢谢","casual reply drops terminal Chinese full stop");
        Check(ReplyLanguage.Polish("哪一页？")=="哪一页？","question tone retained");
        Check(ReplyLanguage.Polish("这也太巧了！")=="这也太巧了！","exclamation retained");
        Check(ReplyLanguage.Polish("让我想想……")=="让我想想……","ellipsis retained");
        Check(ReplyLanguage.Polish("这个嘛。。。")=="这个嘛。。。","informal trailing pause retained");
        Check(ReplyLanguage.Polish("先定范围。时间还得再看。")=="先定范围。时间还得再看","internal sentence boundary retained");
        Check(ReplyLanguage.Polish("版本3.8，文件a.txt")=="版本3.8，文件a.txt","technical punctuation retained");
        Check(ReplyLanguage.Polish("原话是“明天见。”")=="原话是“明天见。”","quoted punctuation retained");
        using(var chatImage=new System.Drawing.Bitmap(1000,700)) {
            var region=new System.Drawing.Rectangle(300,80,680,530);
            var rows=new OcrLayout();
            rows.Lines.Add(new OcrBox {Text="00 : 55",Bounds=new System.Drawing.Rectangle(620,420,70,24)});
            rows.Lines.Add(new OcrBox {Text="怎么不回我话了",Bounds=new System.Drawing.Rectangle(350,510,160,28)});
            rows.Lines.Add(new OcrBox {Text="你在做什么呢",Bounds=new System.Drawing.Rectangle(790,240,160,28)});
            rows.Lines.Add(new OcrBox {Text="怎么了",Bounds=new System.Drawing.Rectangle(350,340,90,28)});
            rows.Lines.Add(new OcrBox {Text="输入框草稿",Bounds=new System.Drawing.Rectangle(350,650,150,28)});
            using(var g=System.Drawing.Graphics.FromImage(chatImage)) { g.Clear(System.Drawing.Color.FromArgb(35,35,35)); g.FillRectangle(System.Drawing.Brushes.LimeGreen,785,235,175,40); g.FillRectangle(System.Drawing.Brushes.Gray,345,335,105,40); g.FillRectangle(System.Drawing.Brushes.Gray,345,505,180,40); }
            Check(WechatVision.Messages(rows,region,chatImage)=="【我】你在做什么呢\n【对方】怎么了\n【对方】怎么不回我话了","ordered speakers, timeline removal and composer exclusion");
            rows.Lines.Clear(); rows.Lines.Add(new OcrBox {Text="00:55",Bounds=new System.Drawing.Rectangle(350,160,70,24)});
            Check(WechatVision.Messages(rows,region,chatImage).Contains("00:55"),"time inside a message retained");
            rows.Lines.Clear(); rows.Lines.Add(new OcrBox {Text="不确定的内容",Bounds=new System.Drawing.Rectangle(700,180,180,24)});
            Check(WechatVision.Messages(rows,region,chatImage).StartsWith("【身份不明】"),"no green evidence does not claim outgoing speaker");
        }
        var memory=new ConversationBuffer();
        memory.Observe("A","【我】你在做什么呢\n【对方】怎么了\n【对方】怎么不回我话了");
        Check(memory.Observe("A","【对方】怎么了\n【对方】怎么不回我话了\n【对方】还在吗").StartsWith("【我】你在做什么呢"),"overlap preserves original initiator");
        Check(memory.Observe("B","【对方】另一位联系人")=="【对方】另一位联系人","conversation isolation");
        Check(memory.Observe("B","【对方】无重叠的另一屏")=="【对方】无重叠的另一屏","disjoint snapshots not appended");
        var wxLayout=new OcrLayout(); string wxTitle;
        wxLayout.Lines.Add(new OcrBox {Text="搜索",Bounds=new System.Drawing.Rectangle(80,50,90,20)});
        wxLayout.Lines.Add(new OcrBox {Text="测试联系人",Bounds=new System.Drawing.Rectangle(330,50,100,22)});
        Check(WechatVision.Region(wxLayout,new System.Drawing.Size(900,700),out wxTitle).IsEmpty,"wechat contact panel without composer rejected");
        wxLayout.Lines.Add(new OcrBox {Text="发 送(S)",Bounds=new System.Drawing.Rectangle(820,650,60,24)});
        var wxRegion=WechatVision.Region(wxLayout,new System.Drawing.Size(900,700),out wxTitle);
        Check(!wxRegion.IsEmpty && wxTitle=="测试联系人","wechat main conversation and send shortcut recognized");
        Check(wxRegion.Left>=300 && wxRegion.Bottom<600,"wechat contact list and composer excluded");
        var wxDetached=new OcrLayout();
        wxDetached.Lines.Add(new OcrBox {Text="测试联系人",Bounds=new System.Drawing.Rectangle(20,45,110,22)});
        wxDetached.Lines.Add(new OcrBox {Text="发送",Bounds=new System.Drawing.Rectangle(430,590,50,22)});
        Check(!WechatVision.Region(wxDetached,new System.Drawing.Size(500,640),out wxTitle).IsEmpty,"wechat detached conversation supported");
        var wxDark=new OcrLayout();
        wxDark.Lines.Add(new OcrBox {Text="联系人预览",Bounds=new System.Drawing.Rectangle(90,50,150,22)});
        wxDark.Lines.Add(new OcrBox {Text="当前会话",Bounds=new System.Drawing.Rectangle(397,50,100,25)});
        wxDark.Lines.Add(new OcrBox {Text="左侧消息预览",Bounds=new System.Drawing.Rectangle(90,150,190,22)});
        wxDark.Lines.Add(new OcrBox {Text="正文",Bounds=new System.Drawing.Rectangle(430,150,70,22)});
        wxDark.Lines.Add(new OcrBox {Text="未发送草稿",Bounds=new System.Drawing.Rectangle(430,680,140,22)});
        var darkRegion=WechatVision.Region(wxDark,new System.Drawing.Size(1100,800),out wxTitle,626,true);
        Check(wxTitle=="当前会话" && darkRegion.Left>=387,"dark main window missing search does not select contact preview as title");
        Check(wxDark.Within(darkRegion)=="正文","dark main window excludes contacts and unsent composer draft");
        var desktop=new System.Drawing.Rectangle(0,0,1920,1040); var side=new System.Drawing.Size(430,860);
        Check(FollowLayout.Place(new System.Drawing.Rectangle(100,100,800,700),desktop,side)==new System.Drawing.Rectangle(908,100,430,700),"follow docks right and matches chat height");
        Check(FollowLayout.Place(new System.Drawing.Rectangle(1000,100,800,700),desktop,side).X==562,"follow falls back left");
        Check(FollowLayout.Place(desktop,desktop,side)==new System.Drawing.Rectangle(1478,0,430,1040),"maximized chat docks inside right edge");
        var monitor=new System.Drawing.Rectangle(-1920,-200,1920,1040);
        Check(monitor.Contains(FollowLayout.Place(new System.Drawing.Rectangle(-2000,-400,1600,1400),monitor,side)),"follow remains inside negative-coordinate monitor");
        var small=new System.Drawing.Rectangle(0,0,360,600);
        Check(small.Contains(FollowLayout.Place(small,small,side)),"small screen placement clamped");
        Check(AccessibleChat.IsMessageContainer(System.Windows.Automation.ControlType.Custom),"QQ application-role message container accepted");
        Check(AccessibleChat.IsMessageContainer(System.Windows.Automation.ControlType.Group),"group message container accepted");
        Check(!AccessibleChat.IsMessageContainer(System.Windows.Automation.ControlType.Button),"navigation message button excluded");
        Check(!AccessibleChat.IsMessageContainer(System.Windows.Automation.ControlType.Edit),"draft editor excluded");
        Check(ChatRedaction.Apply("测试 sk-or-v1-"+new string('a',32)).Contains("[已隐藏 API 密钥]"),"OpenRouter-like token masked locally");
        Check(ChatRedaction.Apply("测试 sk-"+new string('b',32)).Contains("[已隐藏 API 密钥]"),"chat API token masked locally");
        Check(ChatRedaction.Apply("明天见，123")=="明天见，123","normal chat preserved");
        var wa=new ChatWindow { Handle=new IntPtr(21),ProcessId=3,Title="QQ" }; var wb=new ChatWindow { Handle=new IntPtr(22),ProcessId=4,Title="微信" };
        var autoWindows=new List<ChatWindow> {wa,wb};
        Check(ChatBinding.TrackActive(false,false,true,wa),"manual assistant tracks foreground even with startup discovery disabled");
        Check(!ChatBinding.TrackActive(false,false,false,wa),"workspace does not automatically track");
        Check(!ChatBinding.TrackActive(false,false,true,null),"unbound startup does not track");
        Check(!ChatBinding.TrackActive(true,true,true,wa),"pause disables automatic switching");
        Check(ChatBinding.TrackActive(true,false,false,null),"opt-in startup discovery remains available");
        Check(ChatBinding.MaySwitch(wb,wb.Handle),"foreground conversation may replace previous binding");
        Check(!ChatBinding.MaySwitch(wb,wa.Handle),"stale probe cannot bind after focus changes");
        Check(ChatBinding.ResumeProfile(true,0,false,true),"automatic-region profile resumes without manual rectangle");
        Check(!ChatBinding.ResumeProfile(false,0,false,true),"stopped profile remains stopped");
        Check(!ChatBinding.ResumeProfile(true,0,false,false),"uncalibrated manual profile does not start");
        Check(!ChatBinding.CanAttach(wa,wa,false,1500),"QQ main panel without message list cannot bind");
        Check(!ChatBinding.CanAttach(wa,wa,true,200),"transient foreground window cannot bind immediately");
        Check(ChatBinding.CanAttach(wa,wa,true,1500),"stable verified message window binds");
        Check(!ChatBinding.CanAttach(wa,wb,true,1500),"late probe for previous chat cannot bind new foreground");
        Check(!ChatBinding.CanAttach(wa,new ChatWindow {Handle=wa.Handle,ProcessId=999},true,1500),"reused HWND with new PID invalidates probe");
        Check(!ChatBinding.CanAttach(wa,null,true,1500),"closed target invalidates probe");
        Check(AutomaticLayout.Choose(autoWindows,wb.Handle,wa)==wb,"automatic foreground follows unregistered window");
        Check(AutomaticLayout.Choose(autoWindows,new IntPtr(99),wa)==wa,"assistant focus retains current chat");
        Check(AutomaticLayout.Choose(autoWindows,new IntPtr(99),null)==null,"multiple inactive windows not guessed");
        Check(AutomaticLayout.Choose(new List<ChatWindow>{wa},IntPtr.Zero,null)==wa,"single chat selected without user input");
        Check(AutomaticLayout.Choose(new List<ChatWindow>(),IntPtr.Zero,wa)==null,"closed chat not retained");
        var layout=new OcrLayout { Text="聊天内容\n发送" };
        layout.Lines.Add(new OcrBox { Text="发 送",Bounds=new System.Drawing.Rectangle(900,665,48,20) });
        var autoRegion=AutomaticLayout.MessageRegion(layout,new System.Drawing.Size(1000,700),"QQ");
        Check(!autoRegion.IsEmpty && autoRegion.Bottom<665,"composer detected and excluded from auto region");
        layout.Lines.Add(new OcrBox { Text="联系人",Bounds=new System.Drawing.Rectangle(100,200,50,20) });
        layout.Lines.Add(new OcrBox { Text="消息正文",Bounds=new System.Drawing.Rectangle(500,200,90,20) });
        Check(layout.Within(autoRegion)=="消息正文","only message region text forwarded");
        layout.Text="扫 码 登 录";
        Check(AutomaticLayout.MessageRegion(layout,new System.Drawing.Size(1000,700),"QQ").IsEmpty,"login page excluded despite send marker");
        Check(AutomaticLayout.MessageRegion(new OcrLayout { Text="联系人列表" },new System.Drawing.Size(1000,700),"QQ").IsEmpty,"no composer no automatic model request");
        Check(AutomaticLayout.MessageRegion(layout,new System.Drawing.Size(300,300),"QQ").IsEmpty,"small popup excluded");
        var hintLayout=new OcrLayout { Text="按 Enter 键发送" };
        hintLayout.Lines.Add(new OcrBox { Text="按 Enter 键 发 送",Bounds=new System.Drawing.Rectangle(350,400,200,20) });
        Check(!AutomaticLayout.MessageRegion(hintLayout,new System.Drawing.Size(700,600),"聊天").IsEmpty,"keyboard send hint above old eighty percent threshold");
        hintLayout.Lines[0].Text="发送（S）";
        Check(!AutomaticLayout.MessageRegion(hintLayout,new System.Drawing.Size(700,600),"聊天").IsEmpty,"fullwidth send shortcut");
        var toolbarLayout=new OcrLayout { Text="正文\n表情\n截图" };
        toolbarLayout.Lines.Add(new OcrBox { Text="表情",Bounds=new System.Drawing.Rectangle(300,400,50,20) });
        toolbarLayout.Lines.Add(new OcrBox { Text="截图",Bounds=new System.Drawing.Rectangle(370,400,50,20) });
        toolbarLayout.Lines.Add(new OcrBox { Text="第一条消息",Bounds=new System.Drawing.Rectangle(400,150,100,20) });
        toolbarLayout.Lines.Add(new OcrBox { Text="第二条消息",Bounds=new System.Drawing.Rectangle(400,220,100,20) });
        Check(!AutomaticLayout.MessageRegion(toolbarLayout,new System.Drawing.Size(700,600),"聊天").IsEmpty,"composer fallback without send label");
        toolbarLayout.Lines.RemoveAt(3); toolbarLayout.Lines.RemoveAt(2);
        Check(AutomaticLayout.MessageRegion(toolbarLayout,new System.Drawing.Size(700,600),"QQ").IsEmpty,"toolbar alone does not prove conversation");
        var offset=new System.Drawing.Rectangle(200,80,600,400); var original=new System.Drawing.Size(1000,700);
        Check(ChatWindows.FitRegion(offset,original,new System.Drawing.Rectangle(-500,100,1000,700))==new System.Drawing.Rectangle(-300,180,600,400),"region follows moved window including negative desktop coordinates");
        Check(ChatWindows.FitRegion(offset,original,new System.Drawing.Rectangle(0,0,1200,800))==new System.Drawing.Rectangle(200,80,800,500),"resize keeps four selected margins");
        Check(ChatWindows.FitRegion(offset,original,new System.Drawing.Rectangle(0,0,350,700)).IsEmpty,"too small resize pauses instead of invalid crop");
        Check(ChatWindows.FitRegion(new System.Drawing.Rectangle(-1,0,300,300),original,new System.Drawing.Rectangle(0,0,1000,700)).IsEmpty,"invalid crop rejected");
        Check(ChatWindows.FitRegion(offset,System.Drawing.Size.Empty,new System.Drawing.Rectangle(0,0,1000,700)).IsEmpty,"missing calibration rejected");
        var gate=new ChangeGate(); var now=new DateTime(2026,1,1);
        Check(!gate.Ready(now),"empty monitor does not generate");
        Check(gate.Observe("消息一",now),"new message changes revision");
        Check(!gate.Ready(now.AddSeconds(2)),"debounce message burst");
        Check(gate.Ready(now.AddSeconds(3)),"stable message ready");
        var revision=gate.Claim(now.AddSeconds(3));
        Check(gate.IsCurrent(revision),"matching response revision");
        Check(!gate.Observe("消息一\r\n",now.AddSeconds(4)),"normalize trailing whitespace");
        Check(!gate.Ready(now.AddSeconds(30)),"unchanged message no repeated billing");
        gate.Observe("消息二",now.AddSeconds(5));
        Check(!gate.IsCurrent(revision),"stale response rejected");
        Check(!gate.Ready(now.AddSeconds(9)),"cooldown caps calls");
        Check(gate.Ready(now.AddSeconds(14)),"latest stable message ready after cooldown");
        gate.Claim(now.AddSeconds(14)); gate.Abandon("消息二");
        Check(gate.Ready(now.AddSeconds(25)),"cancelled snapshot can retry");
        gate.Observe("",now.AddSeconds(26));
        Check(!gate.Ready(now.AddSeconds(40)),"unreadable state no generation");
        Check(ChatWindows.PlatformFor("WeChat")=="微信","WeChat process routing");
        Check(ChatWindows.PlatformFor("Weixin")=="微信","Weixin process routing");
        Check(ChatWindows.PlatformFor("QQ")=="QQ" && ChatWindows.PlatformFor("QQNT")=="QQ","QQ process routing");
        Check(ChatWindows.PlatformFor("QQBrowser")==null && ChatWindows.PlatformFor("wechat-helper")==null,"exclude unrelated processes");
        Check(ChatWindows.PlatformFor(null)==null,"null process handling");
        try { await ChatWindows.Capture(null); Check(false,"unbound capture expected"); }
        catch(InvalidOperationException) { Check(true,"reject unbound capture"); }
        try { await ChatWindows.Capture(new ChatWindow { Handle=IntPtr.Zero,Platform="QQ" }); Check(false,"closed capture expected"); }
        catch(InvalidOperationException) { Check(true,"reject closed window"); }
        Check(Api.ValidateUrl("https://api.typesafe.ai/v1/systemone").Host=="api.typesafe.ai","HTTPS URL");
        Check(Api.ValidateUrl("http://127.0.0.1:1234/v1/chat/completions").IsLoopback,"local API");
        Reject(()=>Api.ValidateUrl("http://remote.example/v1"),"reject remote cleartext");
        Reject(()=>Api.ValidateUrl("https://user:secret@example.com/v1"),"reject URL credentials");
        Reject(()=>Api.ValidateUrl("https://example.com/v1?key=secret"),"reject query secret");
        Reject(()=>Api.Validate(new Settings(),true,false),"empty Jev key");
        try { Api.Validate(new Settings { ChatUrl="" },false,false); Check(false,"missing chat URL expected"); }
        catch(ArgumentException ex) { Check(ex.Message.Contains("中文回复"),"field-specific missing URL error"); }
        var wrong=Config(); wrong.JevUrl="https://openrouter.ai/api/v1/chat/completions";
        Reject(()=>Api.Validate(wrong,true,false),"reject chat endpoint for Jev");
        wrong=Config(); wrong.JevModel="jev-1.13.0";
        Reject(()=>Api.Validate(wrong,true,false),"reject official model ID on OpenRouter");
        var saved=Config(); saved.ChatKey="saved-chat-key"; var edited=Config(); edited.JevKey="new-jev-key"; edited.ChatKey="unsaved-chat-key";
        var merged=Settings.MergeSection(saved,edited,true);
        Check(merged.JevKey=="new-jev-key" && merged.ChatKey=="saved-chat-key","save Jev independently");
        merged=Settings.MergeSection(saved,edited,false);
        Check(merged.ChatKey=="unsaved-chat-key" && merged.JevKey==saved.JevKey,"save reply service independently");
        var noVision=Config(); noVision.VisionModel=""; Reject(()=>Api.Validate(noVision,false,true),"empty vision model");
        string fixture="{\"summary\":\"范围尚未明确\",\"evidence\":\"做个简单的\",\"replies\":[\"先确认范围好吗？\",\"可以先说下需要哪些功能吗？\",\"我们先确认功能和时间。\"]}";
        Check(Api.ParseDrafts(fixture).Replies.Length==3,"Chinese reply parser");
        Check(Api.ParseDrafts("```json\n"+fixture+"\n```").Summary=="范围尚未明确","fenced JSON");
        Reject(()=>Api.ParseDrafts("{\"replies\":[\"one\"]}"),"reject incomplete reply list");
        Reject(()=>Api.ParseDrafts("bad json"),"reject malformed JSON");
        string path=Path.Combine(Path.GetTempPath(),"jev-test-"+Guid.NewGuid()+".bin");
        try {
            ConfigStore.Save(path,Config()); Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("test-secret"),"keys encrypted at rest");
            Check(ConfigStore.Load(path).JevKey=="test-secret","DPAPI roundtrip");
            var updated=Config(); updated.ChatModel="changed"; ConfigStore.Save(path,updated); Check(ConfigStore.Load(path).ChatModel=="changed","atomic config replacement");
        } catch(CryptographicException) { skipped="SKIP: DPAPI roundtrip unavailable in current Windows user context. No plaintext fallback.\r\n"; }
        finally { if(File.Exists(path)) File.Delete(path); }
        var handler=new FakeHandler();
        handler.Responses.Enqueue("{\"answers\":{\"strategy\":{\"type\":\"choice\",\"choice\":\"clarify\",\"confidence\":0.8,\"probabilities\":{\"clarify\":0.9}},\"needs_context\":{\"type\":\"noul\",\"noul\":0.9}}}");
        handler.Responses.Enqueue(ChatResponse(fixture));
        handler.Responses.Enqueue(ChatResponse("【对方】你好\n【我】你好呀"));
        using(var api=new Api(handler)) {
            var cfg=Config(); var strategy=await api.Evaluate(cfg,"【对方】做个简单的","职场",CancellationToken.None);
            Check(strategy.Choice=="clarify" && strategy.NeedsContext==0.9,"Jev response contract");
            var result=await api.Generate(cfg,"【对方】做个简单的","职场","自然",strategy,CancellationToken.None);
            Check(result.Replies[0]=="先确认范围好吗？","two-stage generation");
            string recognized=await api.Recognize(cfg,new byte[]{1,2,3},CancellationToken.None);
            Check(recognized.StartsWith("【对方】"),"vision response");
            var questions=Json.Map(Json.Get(handler.Bodies[0],"questions"));
            Check(Convert.ToString(Json.Get(Json.Map(Json.Get(questions,"strategy")),"type"))=="choice","Jev question schema");
            Check(Json.Write(handler.Bodies[1]).Contains("信息可能不足"),"missing-context guidance");
            Check(Json.Write(handler.Bodies[2]).Contains("data:image/png;base64,AQID"),"vision image request");
        }
        var refreshHandler=new FakeHandler(); refreshHandler.Responses.Enqueue(ChatResponse(fixture));
        using(var refreshApi=new Api(refreshHandler)) {
            var strategy=new Strategy {Choice="answer",Confidence=.9,NeedsContext=0,Answers=new {strategy="answer"}};
            await refreshApi.Generate(Config(),"【对方】明天见","","自然",strategy,CancellationToken.None,new[]{"旧草稿甲","旧草稿乙","旧草稿丙"});
            var messages=(object[])Json.Get(refreshHandler.Bodies[0],"messages");
            var payload=Json.Read((string)Json.Get(Json.Map(messages[1]),"content"));
            Check(((object[])Json.Get(payload,"previous_drafts")).Length==3,"refresh sends old drafts separately from conversation");
            Check((string)Json.Get(payload,"conversation")=="【对方】明天见","refresh does not insert generated drafts into chat history");
            Check(((string)Json.Get(payload,"rewrite")).Contains("未发送草稿"),"refresh distinguishes drafts from user facts");
        }
        string ungrounded=Json.Write(new {summary="回应邀约",evidence="怎么了",needs_user_input=false,question="",replies=new[]{"刚看到，明天去图书馆？","想约你明天去图书馆","明天一起去图书馆？"}});
        for(int repair=0;repair<2;repair++) {
            var guarded=new FakeHandler(); guarded.Responses.Enqueue(ChatResponse(ungrounded));
            guarded.Responses.Enqueue(ChatResponse(repair==0 ? fixture : ungrounded));
            using(var guardedApi=new Api(guarded)) {
                var strategy=new Strategy {Choice="answer",Confidence=.9,Answers=new {strategy="answer"}};
                var answer=await guardedApi.Generate(Config(),"【我】你在干嘛\n【对方】怎么了","想约对方去图书馆","自然",strategy,CancellationToken.None);
                Check(guarded.Bodies.Count==2,"grounding repair makes exactly one extra request");
                Check(repair==0 ? !answer.NeedsUserInput : answer.NeedsUserInput && String.IsNullOrEmpty(answer.Replies[0]),"repaired drafts returned or unsafe drafts withheld");
            }
        }
        var bad=new FakeHandler { Status=HttpStatusCode.Unauthorized };
        using(var api=new Api(bad)) {
            try { await api.Test(Config(),true,CancellationToken.None); Check(false,"401 expected"); }
            catch(InvalidOperationException ex) { Check(ex.Message.Contains("401") && !ex.Message.Contains("test-secret"),"safe authentication errors"); }
        }
        var invalid=new FakeHandler(); invalid.Responses.Enqueue("{\"answers\":{\"strategy\":{\"choice\":\"invented\"}}}");
        using(var api=new Api(invalid)) {
            try { await api.Test(Config(),true,CancellationToken.None); Check(false,"unknown choice expected"); }
            catch(InvalidDataException) { Check(true,"reject unknown strategy"); }
        }
        var delayed=new FakeHandler { Wait=true };
        using(var api=new Api(delayed)) using(var cts=new CancellationTokenSource(30)) {
            try { await api.Test(Config(),true,cts.Token); Check(false,"cancellation expected"); }
            catch(OperationCanceledException) { Check(true,"request cancellation"); }
        }
        var pipeline=new FakeHandler();
        pipeline.Responses.Enqueue("{\"answers\":{\"strategy\":{\"choice\":\"clarify\",\"confidence\":0.8},\"needs_context\":{\"noul\":0.9}}}");
        pipeline.Responses.Enqueue(ChatResponse(fixture));
        pipeline.Responses.Enqueue(ChatResponse("【对方】你好"));
        using(var api=new Api(pipeline)) {
            var cfg=new Settings { JevKey="synthetic-jev-key",ChatKey="synthetic-deepseek-key" };
            string outcome=await api.TestPipeline(cfg,CancellationToken.None);
            Check(outcome.Contains("联动测试成功"),"Jev and DeepSeek complete pipeline");
            Check(pipeline.Hosts[0]=="openrouter.ai" && pipeline.Hosts[1]=="api.deepseek.com","separate service endpoints");
            Check(pipeline.Keys[0]==cfg.JevKey && pipeline.Keys[1]==cfg.ChatKey,"isolated provider keys");
            Check(Convert.ToString(Json.Get(pipeline.Bodies[0],"model"))=="typesafe/jev-1.13","OpenRouter model preset");
            Check(Json.Write(pipeline.Bodies[1]).Contains("clarify"),"Jev decision passed to DeepSeek");
            Check(Convert.ToString(Json.Get(Json.Map(Json.Get(pipeline.Bodies[1],"response_format")),"type"))=="json_object","DeepSeek JSON mode");
            Check(Convert.ToString(Json.Get(Json.Map(Json.Get(pipeline.Bodies[1],"thinking")),"type"))=="disabled","DeepSeek latency setting");
            await api.Recognize(cfg,new byte[]{1,2,3},CancellationToken.None);
            Check(pipeline.Hosts[2]=="api.deepseek.com" && pipeline.Keys[2]==cfg.ChatKey,"vision uses DeepSeek key");
            Check(!pipeline.Bodies[2].ContainsKey("response_format"),"OCR remains plain text");
        }
        var failPipeline=new FakeHandler { Status=HttpStatusCode.Unauthorized };
        using(var api=new Api(failPipeline)) {
            try { await api.TestPipeline(new Settings { JevKey="a",ChatKey="b" },CancellationToken.None); Check(false,"Jev failure expected"); }
            catch(InvalidOperationException) { Check(failPipeline.Bodies.Count==1,"no silent bypass after Jev failure"); }
        }
    }
}
}
