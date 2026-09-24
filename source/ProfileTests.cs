using System;
using System.Drawing;
namespace JevChat {
public partial class MainForm {
    internal void TestConversationSwitch(Action<bool,string> check) {
        transcript.Text="A 的消息"; context.Text="A 的私人信息"; analysis.Text="A 的分析"; replies[0].Text="给 A 的回复";
        currentConversation="A";currentFeedIdentity="qq-uia:1:A"; liveFactIdentity=currentFeedIdentity;
        awaitingUserFact=true;refreshRequested=true;refreshPrevious=new[]{"旧回复"};
        liveRequest=new System.Threading.CancellationTokenSource();
        ResetConversationState();
        check(liveRequest.IsCancellationRequested,"conversation switch cancels old model request");
        check(transcript.Text=="" && context.Text=="" && analysis.Text=="" && replies[0].Text=="","conversation switch clears old messages facts and suggestions");
        check(!awaitingUserFact && !refreshRequested && refreshPrevious==null,"conversation switch releases old question and refresh state");
        check(currentFeedIdentity==null && liveFactIdentity==null && currentConversation==null,"conversation identity is reset");
        liveRequest.Dispose();liveRequest=null;
        var window=new ChatWindow {Handle=new IntPtr(211),ProcessId=3,Platform="QQ",Title="QQ"};
        BindProfile(window);
        UpdateConversationLabel(window,"联系人 B");
        check(currentConversation=="联系人 B" && selectedProfile.Name=="QQ · 联系人 B","same-window contact switch refreshes displayed conversation");
    }
    internal void TestWorkspaceFlow(Action<bool,string> check) {
        check(!automaticSelection && liveSession==null && assistantPanel==null,"startup defaults to manual selection without hidden monitoring");
        var window=new ChatWindow {Handle=new IntPtr(201),ProcessId=2,Platform="QQ",Title="QQ"};
        windowList.Items.Add(window);windowList.SelectedIndex=0;BindWindow();
        check(boundWindow==window && selectedProfile.AutomaticRegion,"manual QQ selection does not require OCR probe");
        check(liveSession==null && assistantPanel==null,"selecting window waits for explicit panel action");
        check(!automaticOption.Checked && !loginOption.Checked,"new configuration does not opt in to discovery or login startup");
    }
    internal void TestReplyRefresh(Action<bool,string> check) {
        RefreshReplies(); check(!refreshRequested,"empty conversation does not queue refresh");
        liveSession=new System.Threading.CancellationTokenSource(); cfg.JevKey="fixture";cfg.ChatKey="fixture";
        transcript.Text="【对方】明天见"; replies[0].Text="好，明天见";
        RefreshReplies(); check(refreshRequested && refreshPrevious[0]=="好，明天见","refresh snapshots previous draft");
        var snapshot=refreshPrevious; RefreshReplies(); check(Object.ReferenceEquals(snapshot,refreshPrevious),"repeated click does not enqueue another request");
        refreshRequested=false; refreshPrevious=null; awaitingUserFact=true;
        RefreshReplies(); check(!refreshRequested,"refresh cannot bypass missing personal facts");
        awaitingUserFact=false; liveRequest=new System.Threading.CancellationTokenSource();
        RefreshReplies(); check(!refreshRequested,"in-flight generation blocks repeat click");
        liveRequest.Dispose(); liveRequest=null;liveSession.Dispose();liveSession=null;
    }
    internal void TestChatProfiles(Action<bool,string> check) {
        var a=new ChatWindow { Handle=new IntPtr(101),ProcessId=1,Platform="QQ",Title="聊天 A" };
        var b=new ChatWindow { Handle=new IntPtr(102),ProcessId=1,Platform="QQ",Title="聊天 B" };
        BindProfile(a); transcript.Text="A 的消息"; replies[0].Text="给 A 的回复";
        liveOffset=new Rectangle(20,30,200,300); liveWindowSize=new Size(600,600);
        BindProfile(b); check(transcript.Text=="" && replies[0].Text=="","new window starts without previous chat");
        transcript.Text="B 的消息"; replies[0].Text="给 B 的回复";
        BindProfile(a); check(transcript.Text=="A 的消息" && replies[0].Text=="给 A 的回复","switch restores only matching chat");
        check(liveOffset==new Rectangle(20,30,200,300),"chat region restored");
        check(profiles.Count==2,"rebinding does not duplicate window");
        var tab=new ChatProfile { Name="A 的另一个标签",Window=a }; profiles.Add(tab); SwitchProfile(tab);
        check(transcript.Text=="" && liveOffset.IsEmpty,"same hwnd tab has independent content and calibration");
        transcript.Text="第二个标签"; liveOffset=new Rectangle(50,60,100,200); liveWindowSize=new Size(600,600);
        SwitchProfile(profiles[0]); check(transcript.Text=="A 的消息" && liveOffset.X==20,"same hwnd profiles isolated");
        SwitchProfile(tab); check(transcript.Text=="第二个标签" && liveOffset.X==50,"second tab retains own region");
        Clear(); check(selectedProfile==null && boundWindow==null && profiles.Count==2,"clear removes only active profile");
    }
}
}
