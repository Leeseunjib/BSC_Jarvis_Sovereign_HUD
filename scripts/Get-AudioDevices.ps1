param([switch]$IncludeSessions,[ValidateSet('Render','Capture')][string]$Flow='Render')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Diagnostics;
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class EndpointEnumeratorClass {}
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointEnumerator {
 [PreserveSig] int EnumAudioEndpoints(int flow,int states,out IEndpointCollection devices);
 [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out IEndpointDevice device);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointCollection {
 [PreserveSig] int GetCount(out uint count);
 [PreserveSig] int Item(uint index,out IEndpointDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointDevice {
 [PreserveSig] int Activate(ref Guid iid,int context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)] out object result);
 [PreserveSig] int OpenPropertyStore(int access,out IntPtr store);
 [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
 [PreserveSig] int GetState(out int state);
}
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointVolume {
 [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
 [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
 [PreserveSig] int GetChannelCount(out uint count);
 [PreserveSig] int SetMasterVolumeLevel(float value,ref Guid context);
 [PreserveSig] int SetMasterVolumeLevelScalar(float value,ref Guid context);
 [PreserveSig] int GetMasterVolumeLevel(out float value);
 [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
 [PreserveSig] int SetChannelVolumeLevel(uint channel,float value,ref Guid context);
 [PreserveSig] int SetChannelVolumeLevelScalar(uint channel,float value,ref Guid context);
 [PreserveSig] int GetChannelVolumeLevel(uint channel,out float value);
 [PreserveSig] int GetChannelVolumeLevelScalar(uint channel,out float value);
 [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute,ref Guid context);
 [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
// Interface order follows the Windows SDK audiopolicy.h and audioclient.h.
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointSessionManager {
 [PreserveSig] int GetAudioSessionControl(IntPtr guid,uint flags,out IntPtr control);
 [PreserveSig] int GetSimpleAudioVolume(IntPtr guid,uint flags,out IntPtr volume);
 [PreserveSig] int GetSessionEnumerator(out IEndpointSessionEnumerator sessions);
}
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointSessionEnumerator {
 [PreserveSig] int GetCount(out int count);
 [PreserveSig] int GetSession(int index,[MarshalAs(UnmanagedType.IUnknown)] out object session);
}
[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointSessionControl {
 [PreserveSig] int GetState(out int state);
 [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
 [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name,IntPtr context);
 [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
 [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path,IntPtr context);
 [PreserveSig] int GetGroupingParam(out Guid grouping);
 [PreserveSig] int SetGroupingParam(ref Guid grouping,IntPtr context);
 [PreserveSig] int RegisterAudioSessionNotification(IntPtr callback);
 [PreserveSig] int UnregisterAudioSessionNotification(IntPtr callback);
 [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
 [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
 [PreserveSig] int GetProcessId(out uint pid);
}
[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointSessionVolume {
 [PreserveSig] int SetMasterVolume(float level,IntPtr context);
 [PreserveSig] int GetMasterVolume(out float level);
 [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute,IntPtr context);
 [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
public class EndpointSessionReport { public uint ProcessId; public string ProcessName,State; public double VolumePercent; public bool Muted; }
[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointPeakMeter {
 [PreserveSig] int GetPeakValue(out float value);
 [PreserveSig] int GetMeteringChannelCount(out uint count);
 [PreserveSig] int GetChannelsPeakValues(uint count,[Out,MarshalAs(UnmanagedType.LPArray,SizeParamIndex=0)] float[] values);
}
public class EndpointReport { public string Id,Name; public int[] DefaultRoles; public double VolumePercent,VolumeDb,Peak; public float[] ChannelPeaks; public bool Muted; public EndpointSessionReport[] Sessions; public string SessionError; }
public static class JarvisEndpointInspector {
 static EndpointSessionReport[] Sessions(IEndpointDevice device) {
  object managerObject=null; IEndpointSessionEnumerator sessions=null;
  try {
   Guid iid=typeof(IEndpointSessionManager).GUID;
   Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out managerObject));
   Marshal.ThrowExceptionForHR(((IEndpointSessionManager)managerObject).GetSessionEnumerator(out sessions));
   int count;Marshal.ThrowExceptionForHR(sessions.GetCount(out count));var reports=new List<EndpointSessionReport>();
   for(int i=0;i<count;i++) {
    object session=null;
    try {
     Marshal.ThrowExceptionForHR(sessions.GetSession(i,out session));
     var control=(IEndpointSessionControl)session;uint pid;int state;float scalar;bool muted;
     Marshal.ThrowExceptionForHR(control.GetProcessId(out pid));Marshal.ThrowExceptionForHR(control.GetState(out state));
     var volume=(IEndpointSessionVolume)session;
     Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out scalar));Marshal.ThrowExceptionForHR(volume.GetMute(out muted));
     string processName=pid==0?"System Sounds":"Unknown";
     if(pid!=0)try{using(var process=Process.GetProcessById((int)pid))processName=process.ProcessName;}catch(ArgumentException){processName="Exited";}
     reports.Add(new EndpointSessionReport{ProcessId=pid,ProcessName=processName,State=state==1?"Active":state==0?"Inactive":"Expired",VolumePercent=Math.Round(scalar*100,1),Muted=muted});
    } finally { if(session!=null)Marshal.ReleaseComObject(session); }
   }
   return reports.ToArray();
  } finally {if(sessions!=null)Marshal.ReleaseComObject(sessions);if(managerObject!=null)Marshal.ReleaseComObject(managerObject);}
 }
 public static EndpointReport[] Read(bool includeSessions,int flow) {
  var enumerator=(IEndpointEnumerator)new EndpointEnumeratorClass(); var defaults=new string[3];
  for(int role=0;role<3;role++){ IEndpointDevice device; if(enumerator.GetDefaultAudioEndpoint(flow,role,out device)==0){device.GetId(out defaults[role]);Marshal.ReleaseComObject(device);} }
  IEndpointCollection devices; Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow,1,out devices));uint count;devices.GetCount(out count);
  var reports=new List<EndpointReport>();
  for(uint i=0;i<count;i++){
   IEndpointDevice device;devices.Item(i,out device);string id;device.GetId(out id);
   Guid iid=typeof(IEndpointVolume).GUID;object obj;Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out obj));
   var volume=(IEndpointVolume)obj;float scalar,db;bool muted;volume.GetMasterVolumeLevelScalar(out scalar);volume.GetMasterVolumeLevel(out db);volume.GetMute(out muted);
   string name=id; string key=id.Substring(id.LastIndexOf('{'));
   using(var properties=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\"+(flow==1?"Capture":"Render")+@"\"+key+@"\Properties")){
    if(properties!=null){
     name=Convert.ToString(properties.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},14",""));
     if(String.IsNullOrEmpty(name)){
      string description=Convert.ToString(properties.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2",""));
      string adapter=Convert.ToString(properties.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6",""));
      name=String.IsNullOrEmpty(description)?id:description+(String.IsNullOrEmpty(adapter)?"":" ("+adapter+")");
     }
    }
   }
   var roles=new List<int>();for(int r=0;r<3;r++)if(defaults[r]==id)roles.Add(r);
   var report=new EndpointReport{Id=id,Name=name,DefaultRoles=roles.ToArray(),VolumePercent=Math.Round(scalar*100,1),VolumeDb=Math.Round(db,2),Muted=muted};
   object meterObject=null;
   try {
    Guid meterId=typeof(IEndpointPeakMeter).GUID;
    Marshal.ThrowExceptionForHR(device.Activate(ref meterId,23,IntPtr.Zero,out meterObject));
    var meter=(IEndpointPeakMeter)meterObject;float peak;uint channels;
    meter.GetPeakValue(out peak);meter.GetMeteringChannelCount(out channels);
    var peaks=new float[channels];meter.GetChannelsPeakValues(channels,peaks);
    report.Peak=peak;report.ChannelPeaks=peaks;
   } catch { } finally {if(meterObject!=null)Marshal.ReleaseComObject(meterObject);}
   if(includeSessions)try{report.Sessions=Sessions(device);}catch(Exception ex){report.SessionError=ex.Message;}
   reports.Add(report);
   Marshal.ReleaseComObject(obj);Marshal.ReleaseComObject(device);
  }
  Marshal.ReleaseComObject(devices);Marshal.ReleaseComObject(enumerator);return reports.ToArray();
 }
}
'@
[JarvisEndpointInspector]::Read([bool]$IncludeSessions, $(if($Flow -eq 'Capture'){1}else{0})) | ConvertTo-Json -Depth 5 -Compress
