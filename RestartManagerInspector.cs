using MhwModManager.Core;
using System.Runtime.InteropServices;

namespace MhwModManager.Filesystem;

public sealed record LockingProcessInfo(int ProcessId,string ApplicationName,string ServiceName,bool Restartable);

public static class RestartManagerInspector
{
    private const int ErrorMoreData=234;
    private const int CchRmSessionKey=32;
    private const int CchRmMaxAppName=255;
    private const int CchRmMaxSvcName=63;

    public static IReadOnlyList<LockingProcessInfo> GetLockingProcesses(IEnumerable<string> resources)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!OperatingSystem.IsWindows())return [];
        var files=resources.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToArray();
        if(files.Length==0)return [];
        var key=new char[CchRmSessionKey+1];
        var result=RmStartSession(out var handle,0,key);
        if(result!=0)return [];
        try
        {
            result=RmRegisterResources(handle,(uint)files.Length,files,0,null,0,null);
            if(result!=0)return [];
            uint needed=0,count=0,reasons=0;
            result=RmGetList(handle,out needed,ref count,null,ref reasons);
            if(result==0)return [];
            if(result!=ErrorMoreData||needed==0)return [];
            var info=new RM_PROCESS_INFO[needed];
            count=needed;
            result=RmGetList(handle,out needed,ref count,info,ref reasons);
            if(result!=0)return [];
            return info.Take((int)count).Select(x=>new LockingProcessInfo(
                x.Process.dwProcessId,
                x.strAppName??string.Empty,
                x.strServiceShortName??string.Empty,
                x.bRestartable)).ToArray();
        }
        finally
        {
            var endResult = RmEndSession(handle);
            if (endResult != 0)
                System.Diagnostics.Debug.WriteLine($"Restart Manager RmEndSession failed with code {endResult}.");
        }
    }

    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle,int dwSessionFlags,[Out] char[] strSessionKey);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle,uint nFiles,string[] rgsFilenames,uint nApplications,RM_UNIQUE_PROCESS[]? rgApplications,uint nServices,string[]? rgsServiceNames);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle,out uint pnProcInfoNeeded,ref uint pnProcInfo,[In,Out] RM_PROCESS_INFO[]? rgAffectedApps,ref uint lpdwRebootReasons);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp=0,RmMainWindow=1,RmOtherWindow=2,RmService=3,RmExplorer=4,RmConsole=5,RmCritical=1000
    }

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=CchRmMaxAppName+1)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=CchRmMaxSvcName+1)] public string strServiceShortName;
        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }
}
