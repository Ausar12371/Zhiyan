using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JevChat {
public partial class MainForm {
    CancellationTokenSource liveSession, liveRequest;
    Task<TextFeed> accessibleRead;
    ModernSelect liveMode;
    ModernButton liveToggle, liveArea;
    Rectangle liveOffset;
    Size liveWindowSize;
    IntPtr liveAreaWindow;
    uint liveAreaPid;
    TableLayoutPanel captureLayout;
    bool captureToolsVisible;
    bool awaitingUserFact;
    string liveFactIdentity, currentFeedIdentity;
    bool refreshRequested;
    string[] refreshPrevious;
    Strategy lastReplyStrategy;
    string lastStrategyText,lastStrategyFacts,lastStrategyScope;
    void ResetConversationState() {
        if(liveRequest!=null) liveRequest.Cancel();
        transcript.Clear(); context.Clear(); analysis.Clear();
        foreach(var reply in replies) reply.Clear();
        currentConversation=null; currentFeedIdentity=null; liveFactIdentity=null;
        awaitingUserFact=false; refreshRequested=false; refreshPrevious=null;
        lastReplyStrategy=null; lastStrategyText=null; lastStrategyFacts=null; lastStrategyScope=null;
    }
    void UpdateConversationLabel(ChatWindow target,string conversationName) {
        currentConversation=conversationName;
        string display=target.Platform+" · "+conversationName;
        if(selectedProfile!=null && selectedProfile.Name!=display) { selectedProfile.Name=display; RefreshProfiles(); }
    }
    void RefreshReplies() {
        if(busy || liveRequest!=null || refreshRequested) { status.Text="正在生成，请稍等"; return; }
        if(String.IsNullOrWhiteSpace(transcript.Text)) { status.Text="先读取聊天内容，再换一组"; return; }
        if(awaitingUserFact) { status.Text="请先点击补充信息，补齐当前问题所需事实"; return; }
        refreshPrevious=replies.Select(r=>r.Text).ToArray();
        if(liveSession==null) { Run(Generate); return; }
        if(String.IsNullOrWhiteSpace(cfg.JevKey) || String.IsNullOrWhiteSpace(cfg.ChatKey)) { status.Text="请先填写两组 API Key"; refreshPrevious=null; return; }
        refreshRequested=true; status.Text="正在换一组回复…";
    }
    void ToggleCaptureTools() {
        if(captureLayout==null || liveSession!=null || busy) return;
        captureToolsVisible=!captureToolsVisible;
        actions.Visible=captureToolsVisible; preview.Visible=captureToolsVisible;
        captureLayout.RowStyles[1].Height=captureToolsVisible ? 96 : 0;
        captureLayout.RowStyles[2].Height=captureToolsVisible ? 110 : 0;
    }
    void BuildLiveToolbar(Panel work) {
        var bar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=64,WrapContents=false };
        liveMode=new ModernSelect { Width=185,Margin=new Padding(0,0,8,0) };
        liveMode.Items.AddRange(new object[]{"本地视觉监测","直接读取文本"}); liveMode.SelectedIndex=0;
        liveArea=new ModernButton { Text="选择消息区域",Width=145,Height=38,Margin=new Padding(0,0,8,0) };
        liveArea.Click+=delegate { PickLiveArea(); };
        liveToggle=new ModernButton { Text="开始监测",Width=128,Height=38,Primary=true,Margin=new Padding(0,0,12,0) };
        liveToggle.Click+=delegate { if(liveSession!=null) StopLive(); else StartLive(); };
        bar.Controls.Add(liveMode); bar.Controls.Add(liveArea); bar.Controls.Add(liveToggle);
        var captureTools=new ModernButton { Text="截图工具",Width=106,Height=38,Margin=new Padding(0,0,12,0) };
        captureTools.Click+=delegate { ToggleCaptureTools(); }; bar.Controls.Add(captureTools);
        bar.Controls.Add(new Label { Text="自动决策与回复",AutoSize=true,ForeColor=Palette.Muted,Margin=new Padding(0,10,0,0),Font=new Font(Font.FontFamily,9) });
        work.Controls.Add(bar);
    }
    async void PickLiveArea() {
        if(busy || liveSession!=null) return;
        if(boundWindow==null) { tabs.SelectedIndex=0; status.Text="请先选择一个微信或 QQ 窗口。"; return; }
        busy=true; tabs.Enabled=false;
        bool compact=assistantPanel!=null && assistantPanel.Visible;
        try {
            // Bring the selected window forward once for calibration, never during polling.
            Hide(); if(compact) assistantPanel.Hide(); await ChatWindows.PrepareSelection(boundWindow);
            var before=ChatWindows.BoundsOf(boundWindow);
            using(var selector=new Selector()) {
                if(selector.ShowDialog()!=DialogResult.OK) return;
                var region=selector.RegionSelected;
                if(!before.Contains(region) || ChatWindows.BoundsOf(boundWindow)!=before) throw new InvalidOperationException("请选择所绑定窗口内部的消息区域，选择时不要移动窗口。");
                if(region.Width>2600 || region.Height>2600) throw new InvalidOperationException("区域过大，请缩小到聊天消息列表。");
                liveOffset=new Rectangle(region.X-before.X,region.Y-before.Y,region.Width,region.Height);
                liveWindowSize=before.Size; liveAreaWindow=boundWindow.Handle; liveAreaPid=boundWindow.ProcessId;
                if(selectedProfile!=null) selectedProfile.AutomaticRegion=false;
                status.Text="消息区域已选定。点击开始监测后，回到聊天窗口；不要包含输入框和联系人列表。";
            }
        } catch(Exception ex) { Show(); Error(ex); }
        finally { if(compact) { Hide(); assistantPanel.Show(); assistantPanel.Activate(); } else { Show(); Activate(); } busy=false; tabs.Enabled=true; SaveProfile(); }
    }
    void StopLive() {
        if(liveSession==null) return;
        liveSession.Cancel(); if(liveRequest!=null) liveRequest.Cancel();
        liveToggle.Text="正在停止…"; liveToggle.Enabled=false;
    }
    void LockLive(bool locked) {
        actions.Enabled=!locked; liveMode.Enabled=!locked; liveArea.Enabled=!locked;
        transcript.ReadOnly=locked; context.ReadOnly=locked; tone.Enabled=!locked;
        foreach(var nav in navButtons) nav.Enabled=!locked;
        liveToggle.Text=locked ? "停止监测" : "开始监测"; liveToggle.Enabled=true;
    }
    async void StartLive() {
        if(busy || liveSession!=null) return;
        if(boundWindow==null) { tabs.SelectedIndex=0; status.Text="请先选择微信或 QQ 窗口。"; return; }
        bool visual=liveMode.SelectedIndex==0;
        if(visual && !(autoEnabled && selectedProfile!=null && selectedProfile.AutomaticRegion) && (liveOffset.IsEmpty || liveAreaWindow!=boundWindow.Handle || liveAreaPid!=boundWindow.ProcessId)) { status.Text="首次监测，请先选择消息区域（只需选择一次）。"; return; }
        if(!visual && accessibleRead!=null && !accessibleRead.IsCompleted) { status.Text="上次文本读取尚未退出，请使用本地视觉监测。"; return; }
        try { ReadConfig(); } catch(Exception ex) { Error(ex); return; }
        bool generate=!String.IsNullOrWhiteSpace(cfg.JevKey) && !String.IsNullOrWhiteSpace(cfg.ChatKey);
        if(generate) { try { Api.Validate(cfg,true,false); Api.Validate(cfg,false,false); } catch(Exception ex) { if(autoEnabled) { autoRetryBlocked=true; status.Text="请检查模型设置："+ex.Message; } else Error(ex); return; } }
        var target=boundWindow; var gate=new ChangeGate(); var session=new CancellationTokenSource(); liveSession=session;
        var conversationBuffer=new ConversationBuffer();
        LocalOcr ocr=null; Task generation=null; string identity=null, title=null; bool failure=false;
        LockLive(true); transcript.Clear(); analysis.Clear(); foreach(var reply in replies) reply.Clear();
        try {
            title=await Task.Run(()=>ChatWindows.TitleOf(target));
            while(true) {
                session.Token.ThrowIfCancellationRequested();
                var latestTitle=await Task.Run(()=>ChatWindows.TitleOf(target));
                session.Token.ThrowIfCancellationRequested();
                if(latestTitle!=title) {
                    title=latestTitle; target.Title=latestTitle; if(liveRequest!=null) liveRequest.Cancel();
                    if(autoEnabled && selectedProfile!=null && selectedProfile.AutomaticRegion) liveOffset=Rectangle.Empty;
                    ResetConversationState(); gate=new ChangeGate(); generation=null; conversationBuffer=new ConversationBuffer();
                    status.Text="聊天标题已更新，正在重新读取当前内容…";
                }
                string text, automaticText=null;
                TextFeed direct=null;
                if(direct==null && autoEnabled && selectedProfile!=null && selectedProfile.AutomaticRegion && (target.Platform=="微信" || target.Platform=="QQ")) {
                    if(ocr==null) { ocr=new LocalOcr(); if(target.Platform!="QQ") await ocr.Start(session.Token); }
                    try { direct=await Task.Run(()=>WechatVision.Read(target,ocr,session.Token)); }
                    catch(InvalidOperationException ex) { status.Text=ex.Message; }
                    session.Token.ThrowIfCancellationRequested();
                }
                if(direct!=null) {
                    if(liveFactIdentity!=null && liveFactIdentity!=direct.Identity) { context.Clear(); liveFactIdentity=null; }
                    chatContentAvailable=true;
                    if(identity!=null && identity!=direct.Identity) {
                        ResetConversationState(); gate=new ChangeGate(); generation=null;
                        conversationBuffer=new ConversationBuffer();
                    }
                    UpdateConversationLabel(target,direct.Conversation);
                    identity=direct.Identity; currentFeedIdentity=direct.Identity; text=direct.Text;
                    status.Text=direct.Identity.StartsWith("wechat-ocr:") ? "已本地识别微信聊天内容" : "已本地识别 QQ 聊天内容";
                }
                else if(autoEnabled && selectedProfile!=null && selectedProfile.AutomaticRegion && (target.Platform=="微信" || target.Platform=="QQ")) {
                    if(target.Platform=="QQ" && QqAccessible.IsReading(target)) {
                        status.Text="正在读取 QQ 消息 · 上次已识别内容保留";
                        await Task.Delay(250,session.Token); continue;
                    }
                    // The main window survives detaching/closing a chat and may now show only contacts.
                    // Never fall back to OCR over that contact panel as if it were a conversation.
                    chatContentAvailable=false; currentConversation=null;
                    if(liveRequest!=null) liveRequest.Cancel(); gate.Observe("",DateTime.UtcNow);
                    transcript.Clear(); analysis.Clear(); foreach(var reply in replies) reply.Clear();
                    status.Text=target.Platform+" 暂未定位消息区 · 可返回工作台停止监测，再选择消息区域进行校准";
                    await Task.Delay(1500,session.Token); continue;
                }
                else if(visual) {
                    if(ocr==null) { status.Text="文本控件暂不可用，正在启动本地识别…"; ocr=new LocalOcr(); await ocr.Start(session.Token); }
                    if(autoEnabled && selectedProfile!=null && selectedProfile.AutomaticRegion) {
                        var outer=ChatWindows.BoundsOf(target); var client=ChatWindows.ClientBounds(target);
                        if(client.IsEmpty) { await Task.Delay(1500,session.Token); continue; }
                        var localClient=new Rectangle(client.X-outer.X,client.Y-outer.Y,client.Width,client.Height);
                        bool shown=assistantPanel!=null && assistantPanel.Visible && assistantPanel.Bounds.IntersectsWith(client);
                        if(shown) {
                            if(liveRequest!=null) liveRequest.Cancel(); gate.Observe("",DateTime.UtcNow);
                            status.Text="文本读取暂不可用 · 请把侧栏移到聊天窗口外以使用 OCR";
                            await Task.Delay(1500,session.Token); continue;
                        }
                        Bitmap overview=await Task.Run(()=>ChatWindows.ObserveRegion(target,localClient,outer.Size));
                        using(var whole=overview) {
                            if(whole==null) { if(liveRequest!=null) liveRequest.Cancel(); gate.Observe("",DateTime.UtcNow); status.Text="自动等待聊天界面可见…"; await Task.Delay(1500,session.Token); continue; }
                            var layout=await Task.Run(()=>ocr.ReadLayout(whole,session.Token));
                            var region=AutomaticLayout.MessageRegion(layout,whole.Size,title);
                            if(region.IsEmpty) { if(liveRequest!=null) liveRequest.Cancel(); gate.Observe("",DateTime.UtcNow); transcript.Clear(); analysis.Clear(); foreach(var reply in replies) reply.Clear(); status.Text="等待聊天会话 · 尚未识别到消息输入区"; await Task.Delay(2000,session.Token); continue; }
                            liveOffset=new Rectangle(localClient.X+region.X,localClient.Y+region.Y,region.Width,region.Height);
                            liveWindowSize=outer.Size; liveAreaWindow=target.Handle; liveAreaPid=target.ProcessId;
                            automaticText=MessageGeometry.Read(layout,region,whole);
                            status.Text="已自动定位消息区域（特殊布局可校准）";
                        }
                    }
                    if(automaticText!=null) text=automaticText;
                    else using(var bitmap=await Task.Run(()=>ChatWindows.ObserveRegion(target,liveOffset,liveWindowSize))) {
                        if(bitmap==null) {
                            if(liveRequest!=null) liveRequest.Cancel();
                            gate.Observe("",DateTime.UtcNow);
                            status.Text="等待消息区域可见 · 移开遮挡或恢复聊天窗口后自动继续";
                            await Task.Delay(1200,session.Token); continue;
                        }
                        text=await Task.Run(async ()=>MessageGeometry.Read(await ocr.ReadLayout(bitmap,session.Token),new Rectangle(Point.Empty,bitmap.Size),bitmap));
                    }
                } else {
                    accessibleRead=Task.Run(()=>AccessibleChat.Read(target));
                    if(await Task.WhenAny(accessibleRead,Task.Delay(6000,session.Token))!=accessibleRead) {
                        session.Token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("消息控件读取超时，请改用本地视觉监测。");
                    }
                    var feed=await accessibleRead;
                    if(identity!=null && identity!=feed.Identity) { ResetConversationState(); gate=new ChangeGate(); generation=null; conversationBuffer=new ConversationBuffer(); }
                    identity=feed.Identity; text=String.Join("\n",feed.Text.Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Select(line=>"【身份不明】"+line));
                }
                session.Token.ThrowIfCancellationRequested();
                text=ChatRedaction.Apply(text);
                if(identity!=null && (identity.StartsWith("wechat-ocr:") || identity.StartsWith("qq-ocr:") || identity.StartsWith("qq-uia:"))) text=conversationBuffer.Observe(identity,text);
                if(text.Length>16000) text=text.Substring(text.Length-16000);
                if(gate.Observe(text,DateTime.UtcNow)) {
                    awaitingUserFact=false; refreshRequested=false; refreshPrevious=null;
                    if(liveRequest!=null) liveRequest.Cancel();
                    transcript.Text=gate.Current; analysis.Text="已读取 "+gate.Current.Length+" 字，等待内容稳定…\r\n\r\n"+(gate.Current.Length>160 ? gate.Current.Substring(gate.Current.Length-160) : gate.Current);
                    foreach(var reply in replies) reply.Clear();
                }
                if(!generate) status.Text="本地监测中 · 已同步文字；填写两组 API Key 后重新开始，即可自动生成回复";
                else if((generation==null || generation.IsCompleted) && (refreshRequested || gate.Ready(DateTime.UtcNow))) {
                    int revision=gate.Claim(DateTime.UtcNow);
                    liveRequest=CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                    var previous=refreshRequested ? refreshPrevious : null; refreshRequested=false; refreshPrevious=null; generation=GenerateLive(gate,revision,gate.Current,liveRequest,previous);
                } else if(generation==null || generation.IsCompleted) status.Text=awaitingUserFact ? "等待补充信息 · 点击侧栏「补充信息」" : "监测中 · 等待消息变化";
                await Task.Delay(1500,session.Token);
            }
        } catch(OperationCanceledException) {}
        catch(Exception ex) { failure=true; autoRetryBlocked=autoEnabled; status.Text="监测已停止："+ex.Message; }
        {
            session.Cancel(); if(liveRequest!=null) liveRequest.Cancel();
            if(generation!=null) await Task.WhenAny(generation,Task.Delay(1500));
            if(ocr!=null) ocr.Dispose(); refreshRequested=false; refreshPrevious=null; liveSession=null; session.Dispose();
            if(IsDisposed || Disposing) return;
            LockLive(false);
            if(!failure) status.Text="监测已停止。";
            SaveProfile();
        }
    }
    async Task GenerateLive(ChangeGate gate,int revision,string text,CancellationTokenSource request,string[] previous=null) {
        bool published=false;
        try {
            status.Text="自动决策 · Jev 分析中…";
            string facts=context.Text;
            string scope=(boundWindow==null?"":boundWindow.ProcessId+":"+boundWindow.Handle)+":"+currentConversation;
            var strategy=previous!=null && lastReplyStrategy!=null && lastStrategyText==text && lastStrategyFacts==facts && lastStrategyScope==scope ? lastReplyStrategy : await api.Evaluate(cfg,text,facts+"\n采集说明：当前会话的可见消息及有连续重叠证据的近期消息；按时间顺序排列。【我】来自右侧消息块或右侧头像，【对方】来自左侧消息块或左侧头像，均为视觉识别结果，可能有误差；【身份不明】不可猜测。先判断谁发起话题、哪条消息在回应哪句话，不推测未采集历史。",request.Token);
            request.Token.ThrowIfCancellationRequested(); if(!gate.IsCurrent(revision)) return;
            lastReplyStrategy=strategy; lastStrategyText=text; lastStrategyFacts=facts; lastStrategyScope=scope;
            status.Text=previous==null ? "自动回复 · DeepSeek 生成中…" : "换一组 · DeepSeek 生成中…";
            var result=await api.Generate(cfg,text,facts+"\n采集说明：按标记和前后轮次理解话题发起者、追问对象与未解决事项；标记来自本地视觉，身份不明部分不要猜。不要把我先找对方的对话回复成“你找我有什么事”。回复要承接原来的话题，不能为未回复编造没看手机、刚忙完等原因。",Convert.ToString(tone.SelectedItem),strategy,request.Token,previous);
            request.Token.ThrowIfCancellationRequested(); if(!gate.IsCurrent(revision)) return;
            analysis.Text="建议策略："+strategy.Label+"\r\n"+result.Summary+"\r\n\r\n依据："+result.Evidence;
            for(int i=0;i<3;i++) replies[i].Text=result.Replies[i];
            awaitingUserFact=result.NeedsUserInput;
            published=true;
            status.Text=result.NeedsUserInput ? "等待补充信息 · 不猜测你的个人事实" : "已自动更新回复 · "+DateTime.Now.ToString("HH:mm:ss");
        } catch(OperationCanceledException) {}
        catch(Exception ex) {
            if(request.IsCancellationRequested) return;
            analysis.Text="自动生成失败："+ex.Message;
            autoRetryBlocked=autoEnabled;
            // Stop instead of repeatedly charging for a failing/unauthorized API.
            if(liveSession!=null) liveSession.Cancel();
        } finally {
            if(!published) gate.Abandon(text);
            if(Object.ReferenceEquals(liveRequest,request)) liveRequest=null;
            request.Dispose();
        }
    }
}
}
