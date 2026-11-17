using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Opens a restricted authoring session, evaluates the launch check, and previews dialogs.
// It never runs ExecuteAction/INSTALL or any action that registers or installs the product.
internal static class InstallerRuntimeChecks
{
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiOpenDatabaseW(string path,IntPtr mode,out uint db);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiDatabaseOpenViewW(uint db,string sql,out uint view);
    [DllImport("msi.dll")] private static extern uint MsiViewExecute(uint view,uint record);
    [DllImport("msi.dll")] private static extern uint MsiViewFetch(uint view,out uint record);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiRecordGetStringW(uint record,uint field,StringBuilder text,ref uint length);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiOpenPackageExW(string path,uint flags,out uint package);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiGetPropertyW(uint package,string name,StringBuilder text,ref uint length);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiSetPropertyW(uint package,string name,string value);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiDoActionW(uint package,string action);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern int MsiEvaluateConditionW(uint package,string condition);
    [DllImport("msi.dll")] private static extern uint MsiEnableUIPreview(uint db,out uint preview);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern uint MsiPreviewDialogW(uint preview,string name);
    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(uint handle);
    private delegate int UiHandler(IntPtr context,uint type,[MarshalAs(UnmanagedType.LPWStr)] string text);
    [DllImport("msi.dll",CharSet=CharSet.Unicode)] private static extern IntPtr MsiSetExternalUIW(UiHandler callback,uint filter,IntPtr context);
    [DllImport("msi.dll")] private static extern uint MsiSetInternalUI(uint level,IntPtr parent);
    private static readonly List<string> log=new List<string>();
    private static readonly UiHandler handler=delegate(IntPtr context,uint type,string text){log.Add(type.ToString("X8")+" "+text);return (type & 0xff000000)==0x0e000000 ? 0 : 1;};
    private static int assertions;
    private static void Assert(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    private static void Check(uint result,string operation){if(result!=0)throw new Exception(operation+" returned MSI "+result);}
    private static List<string[]> Rows(uint db,string sql,int fields)
    {
        uint view;Check(MsiDatabaseOpenViewW(db,sql,out view),sql);var result=new List<string[]>();
        try{Check(MsiViewExecute(view,0),"Execute view");uint row;while(MsiViewFetch(view,out row)==0){
            try{var data=new string[fields];for(uint field=1;field<=fields;field++){var text=new StringBuilder(4096);uint length=4096;Check(MsiRecordGetStringW(row,field,text,ref length),"Read field");data[field-1]=text.ToString();}result.Add(data);}
            finally{MsiCloseHandle(row);}}}finally{MsiCloseHandle(view);}return result;
    }
    private static string Property(uint package,string name)
    {var text=new StringBuilder(4096);uint length=4096;Check(MsiGetPropertyW(package,name,text,ref length),"Read property "+name);return text.ToString();}
    [STAThread]
    private static int Main(string[] args)
    {
        string destination=args.Length>1?Path.GetFullPath(args[1]):Path.GetFullPath("artifacts\\installer-validation");Directory.CreateDirectory(destination);
        try
        {
            bool oldExpected=args.Length>2&&args[2]=="--expect-original-failure";
            string path=Path.GetFullPath(args[0]);uint db;Check(MsiOpenDatabaseW(path,IntPtr.Zero,out db),"Open package metadata");
            try
            {
                var dialogs=Rows(db,"SELECT `Dialog`,`Attributes`,`Control_First` FROM `Dialog`",3);
                var controls=Rows(db,"SELECT `Dialog_`,`Control`,`Control_Next` FROM `Control`",3);
                var next=new Dictionary<string,string>();foreach(var control in controls)next[control[0]+"/"+control[1]]=control[2];
                bool brokenSubset=false;
                foreach(var subset in new[]{new[]{"O"},new[]{"O","C"},new[]{"A","R","I"},new[]{"Y","N"},new[]{"Y","N","C"},new[]{"R","C"}})
                {
                    var present=new HashSet<string>(subset){"ErrorText","ErrorIcon"};
                    foreach(string control in present){string target;if(next.TryGetValue("MessageDlg/"+control,out target)&&target.Length>0&&!present.Contains(target))brokenSubset=true;}
                }
                if(oldExpected)Assert(brokenSubset,"The original package must reproduce a dangling error-dialog focus link.");
                else
                {
                    Assert(!brokenSubset,"Error-dialog focus must remain valid for every Windows message-button combination.");
                    foreach(var control in controls)if(control[0]=="MessageDlg")Assert(control[2].Length==0,"Windows owns error-dialog focus; Control_Next must be empty.");
                    foreach(var dialog in dialogs)Assert(next.ContainsKey(dialog[0]+"/"+dialog[2]),"Dialog first control must exist: "+dialog[0]);
                }
                MsiSetInternalUI(2,IntPtr.Zero);MsiSetExternalUIW(handler,0x7fffffff,IntPtr.Zero);
                uint package;Check(MsiOpenPackageExW(path,1,out package),"Open restricted non-installing authoring session");
                try
                {
                    string compatibility=Property(package,"VersionNT"),platform=Property(package,"VersionNT64");
                    string condition=Rows(db,"SELECT `Condition` FROM `LaunchCondition` WHERE `Description`='Seamless Mice requires a 64-bit Windows 10 or Windows 11 installation.'",1)[0][0];
                    if(!oldExpected)Check(MsiDoActionW(package,"AppSearch"),"Search actual OS registry version");
                    int accepted=MsiEvaluateConditionW(package,condition);
                    Console.WriteLine("MSI VersionNT={0}, VersionNT64={1}, OS registry major={2}, OS condition accepted={3}",compatibility,platform,Property(package,"SM_WINDOWS_MAJOR"),accepted==1);
                    if(oldExpected)Assert(accepted==0,"The original OS check must reproduce rejection on this supported machine.");
                    else
                    {
                        Assert(accepted==1,"The actual Windows Installer runtime must accept this supported x64 machine.");
                        Check(MsiDoActionW(package,"LaunchConditions"),"Actual launch-condition action");
                        string actual=Property(package,"SM_WINDOWS_MAJOR");
                        Check(MsiSetPropertyW(package,"SM_WINDOWS_MAJOR","#6"),"Simulate unsupported OS");
                        Assert(MsiEvaluateConditionW(package,condition)==0,"Older OS versions must remain rejected.");
                        Check(MsiSetPropertyW(package,"SM_WINDOWS_MAJOR",actual),"Restore authoring-session OS property");
                    }
                }finally{MsiCloseHandle(package);}
                if(!oldExpected)
                {
                    foreach(var dialog in dialogs){
                        uint preview;Check(MsiEnableUIPreview(db,out preview),"Enable Windows Installer authoring preview");
                        try{Check(MsiPreviewDialogW(preview,dialog[0]),"Create actual installer dialog "+dialog[0]);}
                        finally{MsiCloseHandle(preview);}
                    }
                    bool cleanFocus = true;
                    foreach(string entry in log)
                        cleanFocus &= !entry.Contains("Error 2814:") && !entry.Contains("Error 2809:") && !entry.Contains("Error 2810:") && !entry.Contains("Error 2835:");
                    Assert(cleanFocus,"The MSI dialog runtime reported a missing control or invalid focus chain.");
                }
            }finally{MsiCloseHandle(db);MsiSetExternalUIW(null,0,IntPtr.Zero);}
            File.WriteAllLines(Path.Combine(destination,"runtime-dialog-checks.log"),log);
            Console.WriteLine("PASS: {0} installer-runtime and message-button regression assertions. No installation performed.",assertions);return 0;
        }
        catch(Exception error){File.WriteAllLines(Path.Combine(destination,"runtime-dialog-checks.log"),log);Console.Error.WriteLine(error);return 1;}
    }
}
