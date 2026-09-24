using System;
namespace JevChat {
public static class ChatBinding {
    public static bool TrackActive(bool startupDiscovery,bool paused,bool followSessionActive,ChatWindow bound) {
        return !paused && (startupDiscovery || (followSessionActive && bound!=null));
    }
    public static bool MaySwitch(ChatWindow target,IntPtr foreground) {
        return target!=null && target.Handle==foreground;
    }
    public static bool ResumeProfile(bool wasRunning,int mode,bool hasRegion,bool automaticRegion) {
        return wasRunning && (mode==1 || hasRegion || automaticRegion);
    }
    public static bool Same(ChatWindow a,ChatWindow b) { return a!=null && b!=null && a.Handle==b.Handle && a.ProcessId==b.ProcessId; }
    public static bool CanAttach(ChatWindow probed,ChatWindow current,bool messageListFound,double stableMilliseconds) {
        return messageListFound && Same(probed,current) && stableMilliseconds>=650;
    }
}
}
