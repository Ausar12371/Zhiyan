using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
namespace JevChat {
public partial class MainForm {
    bool automaticSelection;
    CheckBox automaticOption,loginOption;
    bool fillingStartupOptions;
    string OptionsPath {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-options.json");}}
    string StartupLink {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"知言自动助手.lnk");}}
    public void InitializeWorkspace() {
        autoPaused=true;
        EnableAutomatic(true);
        ShowWorkspace(); tabs.SelectedIndex=0;
        status.Text="先选择 QQ / 微信及聊天窗口，再点击「进入悬浮助手」";
        if(automaticSelection) {autoPaused=false;AutomaticTick();}
    }
    void BuildStartupOptions(TableLayoutPanel grid) {
        if(loadConfig && File.Exists(OptionsPath)) try {
            object value; var settings=Json.Read(File.ReadAllText(OptionsPath));
            automaticSelection=settings.TryGetValue("automaticDiscovery",out value) && value is bool && (bool)value;
        } catch { automaticSelection=false; }
        var options=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(0,14,0,8)};
        automaticOption=new CheckBox {AutoSize=true,Text="启动后自动寻找聊天（可选，默认关闭）",Checked=automaticSelection};
        loginOption=new CheckBox {AutoSize=true,Text="登录 Windows 时启动知言工作台（可选）",Checked=loadConfig && File.Exists(StartupLink)};
        options.Controls.Add(automaticOption);options.Controls.Add(loginOption);
        grid.RowCount=4; foreach(Control existing in grid.Controls) grid.SetRow(existing,grid.GetRow(existing)+1); grid.Controls.Add(options,0,0);grid.SetColumnSpan(options,2);
        automaticOption.CheckedChanged+=delegate {
            if(fillingStartupOptions) return;
            try {
                File.WriteAllText(OptionsPath,Json.Write(new {automaticDiscovery=automaticOption.Checked}));
                automaticSelection=automaticOption.Checked;
                if(automaticSelection) {autoPaused=false;AutomaticTick();}
                else {status.Text="自动寻找已关闭；手动进入悬浮助手后仍会跟随当前聊天";}
            } catch(Exception ex) {fillingStartupOptions=true;automaticOption.Checked=automaticSelection;fillingStartupOptions=false;Error(ex);}
        };
        loginOption.CheckedChanged+=delegate {
            if(fillingStartupOptions) return;
            try {SetLoginStartup(loginOption.Checked);status.Text=loginOption.Checked?"已开启登录启动工作台":"已关闭登录自启动";}
            catch(Exception ex) {fillingStartupOptions=true;loginOption.Checked=File.Exists(StartupLink);fillingStartupOptions=false;Error(ex);}
        };
    }
    void SetLoginStartup(bool enabled) {
        if(!enabled) {if(File.Exists(StartupLink)) File.Delete(StartupLink);return;}
        object shell=null,shortcut=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{StartupLink});
            var type=shortcut.GetType();
            type.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{Application.ExecutablePath});
            type.InvokeMember("Arguments",BindingFlags.SetProperty,null,shortcut,new object[]{"--workspace"});
            type.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{AppDomain.CurrentDomain.BaseDirectory});
            type.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
        } finally {
            if(shortcut!=null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            if(shell!=null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
}