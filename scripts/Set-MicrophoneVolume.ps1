param([Parameter(Mandatory=$true)][string]$DeviceId,[ValidateRange(-60,0)][float]$Decibels=0)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class CaptureVolumeEnumeratorClass{}
[ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ICaptureVolumeEnumerator {
 [PreserveSig] int EnumAudioEndpoints(int flow,int state,out ICaptureVolumeCollection collection);
}
[ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ICaptureVolumeCollection {
 [PreserveSig] int GetCount(out uint count);
 [PreserveSig] int Item(uint index,out ICaptureVolumeDevice device);
}
[ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ICaptureVolumeDevice {
 [PreserveSig] int Activate(ref Guid iid,int context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object result);
 [PreserveSig] int OpenPropertyStore(int access,out IntPtr store);
 [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
}
[ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ICaptureVolumeEndpoint {
 [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
 [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
 [PreserveSig] int GetChannelCount(out uint count);
 [PreserveSig] int SetMasterVolumeLevel(float value,ref Guid context);
 [PreserveSig] int SetMasterVolumeLevelScalar(float value,ref Guid context);
 [PreserveSig] int GetMasterVolumeLevel(out float value);
}
public static class JarvisCaptureVolume {
 public static float[] Set(string requestedId,float db) {
  if(float.IsNaN(db)||db>0||db< -60)throw new ArgumentOutOfRangeException("db");
  var enumerator=(ICaptureVolumeEnumerator)new CaptureVolumeEnumeratorClass();ICaptureVolumeCollection devices=null;
  try {
   // Only active CAPTURE endpoints are eligible. Playback volume is untouched.
   Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(1,1,out devices));uint count;devices.GetCount(out count);
   for(uint i=0;i<count;i++){
    ICaptureVolumeDevice device;devices.Item(i,out device);object control=null;
    try {
     string id;device.GetId(out id);if(id!=requestedId)continue;
     Guid iid=typeof(ICaptureVolumeEndpoint).GUID,context=Guid.Empty;
     Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out control));var volume=(ICaptureVolumeEndpoint)control;
     float before,after;Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevel(out before));
     Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevel(db,ref context));Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevel(out after));return new float[]{before,after};
    }finally{if(control!=null)Marshal.ReleaseComObject(control);Marshal.ReleaseComObject(device);}
   }
   throw new InvalidOperationException("The specified active capture device was not found.");
  }finally{if(devices!=null)Marshal.ReleaseComObject(devices);Marshal.ReleaseComObject(enumerator);}
 }
}
'@
$levels=[JarvisCaptureVolume]::Set($DeviceId,$Decibels)
[pscustomobject]@{DeviceId=$DeviceId;BeforeDb=$levels[0];AfterDb=$levels[1]}|ConvertTo-Json -Compress
