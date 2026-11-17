using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class ValidateMsi
{
    [DllImport("msi.dll", CharSet=CharSet.Unicode)] private static extern uint MsiOpenDatabaseW(string path, IntPtr mode, out uint db);
    [DllImport("msi.dll", CharSet=CharSet.Unicode)] private static extern uint MsiDatabaseOpenViewW(uint db, string sql, out uint view);
    [DllImport("msi.dll")] private static extern uint MsiViewExecute(uint view, uint record);
    [DllImport("msi.dll")] private static extern uint MsiViewFetch(uint view, out uint record);
    [DllImport("msi.dll", CharSet=CharSet.Unicode)] private static extern uint MsiRecordGetStringW(uint record, uint field, StringBuilder value, ref uint length);
    [DllImport("msi.dll")] private static extern uint MsiRecordReadStream(uint record, uint field, byte[] bytes, ref uint length);
    [DllImport("msi.dll", CharSet=CharSet.Unicode)] private static extern uint MsiDatabaseMergeW(uint db, uint other, string errors);
    [DllImport("msi.dll")] private static extern uint MsiDatabaseCommit(uint db);
    [DllImport("msi.dll", CharSet=CharSet.Unicode)] private static extern uint MsiOpenPackageExW(string path, uint flags, out uint package);
    [DllImport("msi.dll", CharSet=CharSet.Unicode)] private static extern uint MsiDoActionW(uint package, string action);
    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(uint handle);
    [DllImport("msi.dll")] private static extern uint MsiGetLastErrorRecord();
    [DllImport("msi.dll", CharSet=CharSet.Unicode)] private static extern uint MsiFormatRecordW(uint session,uint record,StringBuilder text,ref uint length);
    [DllImport("msi.dll")] private static extern uint MsiSetInternalUI(uint level, IntPtr parent);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr LoadLibraryW(string file);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint IceAction(uint session);
    private delegate int UiHandler(IntPtr context, uint message, [MarshalAs(UnmanagedType.LPWStr)] string text);
    [DllImport("msi.dll", CharSet=CharSet.Unicode)] private static extern IntPtr MsiSetExternalUIW(UiHandler handler, uint filter, IntPtr context);
    private static readonly List<string> log = new List<string>();
    private static int findings, warnings;
    private static readonly UiHandler handler = OnMessage;
    private static int OnMessage(IntPtr context, uint message, string text) {
        log.Add(message.ToString("X8") + " " + text);
        if ((message & 0xff000000) == 0x03000000) {
            string[] fields = text.Replace("\\t","\t").Split('\t');
            if(fields.Length > 1 && fields[0].StartsWith("ICE",StringComparison.Ordinal)) {
                if(fields[1] == "0" || fields[1] == "1") findings++;
                else if(fields[1] == "2") warnings++;
            }
        }
        return 1;
    }
    private static void Check(uint result, string operation) {
        if(result == 0) return;
        uint error = MsiGetLastErrorRecord(); string details = "";
        if(error != 0) { try { var text = new StringBuilder(4096); uint length = 4096; MsiFormatRecordW(0,error,text,ref length); details = text.ToString(); } finally { MsiCloseHandle(error); } }
        throw new Exception(operation + " failed: MSI " + result + " " + details);
    }
    private static List<string> Column(uint db, string sql)
    {
        uint view; Check(MsiDatabaseOpenViewW(db,sql,out view),sql); var values = new List<string>();
        try { Check(MsiViewExecute(view,0),"Execute view"); uint row; while(MsiViewFetch(view,out row) == 0)
            { try { var text = new StringBuilder(1024); uint length = 1024; Check(MsiRecordGetStringW(row,1,text,ref length),"Read row"); values.Add(text.ToString()); } finally { MsiCloseHandle(row); } } }
        finally { MsiCloseHandle(view); }
        return values;
    }
    private static void Extract(uint db, string stream, string destination)
    {
        uint view; Check(MsiDatabaseOpenViewW(db,"SELECT `Data` FROM `_Streams` WHERE `Name`='"+stream.Replace("'","''")+"'",out view),"Open stream view");
        try { Check(MsiViewExecute(view,0),"Execute stream view"); uint row; Check(MsiViewFetch(view,out row),"Read stream record");
            try { using(var file = File.Create(destination)) { byte[] buffer = new byte[65536]; while(true) { uint length = (uint)buffer.Length; Check(MsiRecordReadStream(row,1,buffer,ref length),"Read stream"); if(length == 0) break; file.Write(buffer,0,(int)length); } } }
            finally { MsiCloseHandle(row); } }
        finally { MsiCloseHandle(view); }
    }
    private static string[] ActionInfo(uint db,string action)
    {
        uint view; Check(MsiDatabaseOpenViewW(db,"SELECT `Source`,`Target` FROM `CustomAction` WHERE `Action`='"+action+"'",out view),"Read SDK action metadata");
        try { Check(MsiViewExecute(view,0),"Execute SDK metadata view"); uint row; Check(MsiViewFetch(view,out row),"Read SDK action row");
            try { var result = new string[2]; for(uint field=1;field<=2;field++) { var text=new StringBuilder(1024); uint length=1024; Check(MsiRecordGetStringW(row,field,text,ref length),"Read SDK action field"); result[field-1]=text.ToString(); } return result; }
            finally { MsiCloseHandle(row); } }
        finally { MsiCloseHandle(view); }
    }
    private static int Main(string[] args)
    {
        try
        {
            string source = Path.GetFullPath(args[0]), cube = Path.GetFullPath(args[1]), folder = Path.GetFullPath(args[2]); Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder,"validation.msi"); File.Copy(source,copy,true);
            string cubeCopy = Path.Combine(folder,"sdk-validation.cub"); File.Copy(cube,cubeCopy,true);
            uint preparedCube; Check(MsiOpenDatabaseW(cubeCopy,new IntPtr(1),out preparedCube),"Open validation cube copy");
            try {
                // The SDK cube has its own dummy ProductCode. Preserve the package's real code
                // when merging validation actions; never alter the installed SDK source cube.
                uint cleanup; Check(MsiDatabaseOpenViewW(preparedCube,"DELETE FROM `Property` WHERE `Property`='ProductCode'",out cleanup),"Prepare cube metadata");
                try { Check(MsiViewExecute(cleanup,0),"Remove cube-only product identity"); } finally { MsiCloseHandle(cleanup); }
                uint schemaDb; Check(MsiOpenDatabaseW(source,IntPtr.Zero,out schemaDb),"Read package schema");
                try {
                    // The package carries standard SDK metadata already. Preserve its own
                    // column rules rather than merging differing duplicate cube definitions.
                    var tables = Column(schemaDb,"SELECT `Table` FROM `_Validation` ORDER BY `Table`,`Column`");
                    var columns = Column(schemaDb,"SELECT `Column` FROM `_Validation` ORDER BY `Table`,`Column`");
                    for(int index=0;index<tables.Count;index++) {
                        uint duplicate; Check(MsiDatabaseOpenViewW(preparedCube,"DELETE FROM `_Validation` WHERE `Table`='"+tables[index].Replace("'","''")+"' AND `Column`='"+columns[index].Replace("'","''")+"'",out duplicate),"Prepare validation schema");
                        try { Check(MsiViewExecute(duplicate,0),"Preserve package validation metadata"); } finally { MsiCloseHandle(duplicate); }
                    }
                } finally { MsiCloseHandle(schemaDb); }
                Check(MsiDatabaseCommit(preparedCube),"Commit cube copy");
            } finally { MsiCloseHandle(preparedCube); }
            cube = cubeCopy;
            uint db; Check(MsiOpenDatabaseW(copy,new IntPtr(1),out db),"Open validation copy");
            List<string> actions;
            try { Extract(db,"payload.cab",Path.Combine(folder,"payload.cab")); uint cubeDb; Check(MsiOpenDatabaseW(cube,IntPtr.Zero,out cubeDb),"Open SDK cube"); try { uint merged = MsiDatabaseMergeW(db,cubeDb,"_MergeErrors"); if (merged != 0) { foreach(string table in Column(db,"SELECT `Table` FROM `_MergeErrors`")) Console.WriteLine("Merge conflict table: " + table); } Check(merged,"Merge SDK validation cube"); } finally { MsiCloseHandle(cubeDb); } actions = Column(db,"SELECT `Action` FROM `_ICESequence` ORDER BY `Sequence`"); Check(MsiDatabaseCommit(db),"Commit validation copy"); }
            finally { MsiCloseHandle(db); }
            MsiSetInternalUI(2,IntPtr.Zero); MsiSetExternalUIW(handler,0x7fffffff,IntPtr.Zero);
            // ICEs are SDK DLL actions. Open an ordinary validation session and invoke only
            // those explicit ICE actions; never invoke INSTALL or an installation sequence.
            uint package; Check(MsiOpenPackageExW(copy,0,out package),"Open validation session");
            var modules = new Dictionary<string,IntPtr>(); uint streamDb = 0;
            bool direct = args.Length > 3 && args[3] == "--in-process";
            if (direct) {
                Check(MsiOpenDatabaseW(copy,IntPtr.Zero,out streamDb),"Open validation streams");
            }
            int failed = 0, skipped = 0;
            try
            {
                foreach(string action in actions)
                {
                    uint result;
                    if (direct) {
                        string[] info = ActionInfo(streamDb,action); IntPtr module;
                        if (!info[0].EndsWith(".dll",StringComparison.OrdinalIgnoreCase)) { skipped++; log.Add("SKIPPED "+action+" (SDK script action requires the unavailable Windows Installer service host)"); continue; }
                        if(!modules.TryGetValue(info[0],out module)) {
                            string dllPath=Path.Combine(folder,Path.GetFileName(info[0])); Extract(streamDb,"Binary."+info[0],dllPath);
                            module=LoadLibraryW(dllPath); if(module==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Load SDK DLL "+info[0]); modules.Add(info[0],module);
                        }
                        IntPtr entry = GetProcAddress(module,info[1]);
                        if(entry == IntPtr.Zero) throw new Exception("Missing SDK ICE export: " + action);
                        result = ((IceAction)Marshal.GetDelegateForFunctionPointer(entry,typeof(IceAction)))(package);
                    } else result = MsiDoActionW(package,action);
                    log.Add("ACTION " + action + " => " + result);
                    if(result != 0) failed++;
                }
            }
            finally { MsiCloseHandle(package); MsiSetExternalUIW(null,0,IntPtr.Zero); if(streamDb!=0) MsiCloseHandle(streamDb); foreach(IntPtr module in modules.Values) FreeLibrary(module); File.WriteAllLines(Path.Combine(folder,"ice.log"),log); }
            Console.WriteLine("SDK ICE actions: {0} total; {1} unavailable script checks; failed native action returns: {2}. Log: {3}",actions.Count,skipped,failed,Path.Combine(folder,"ice.log"));
            Console.WriteLine("Native ICE findings: {0} errors, {1} warnings.",findings,warnings);
            return failed == 0 && findings == 0 ? 0 : 1;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
