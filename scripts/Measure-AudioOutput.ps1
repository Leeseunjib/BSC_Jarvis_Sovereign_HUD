param([int]$Seconds = 12)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class AudioEnumerator { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEnumerator {
    [PreserveSig] int EnumAudioEndpoints(int flow, int state, out IntPtr collection);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IAudioDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioDevice { [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result); }
[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeter { [PreserveSig] int GetPeakValue(out float value); }
public class JarvisAudioMeter {
    private IAudioMeter meter;
    public JarvisAudioMeter() {
        var enumerator = (IAudioEnumerator)new AudioEnumerator(); IAudioDevice device;
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0,1,out device));
        Guid iid = typeof(IAudioMeter).GUID; object result;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out result));
        meter = (IAudioMeter)result;
        Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator);
    }
    public float Peak() { float value; Marshal.ThrowExceptionForHR(meter.GetPeakValue(out value)); return value; }
}
'@
$meter = New-Object JarvisAudioMeter
$clock = [Diagnostics.Stopwatch]::StartNew()
$samples = @()
while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
    $samples += [pscustomobject]@{ Milliseconds = $clock.ElapsedMilliseconds; Peak = [math]::Round($meter.Peak(),5) }
    Start-Sleep -Milliseconds 100
}
[pscustomobject]@{ Samples = $samples.Count; BaselinePeak = (($samples | Where-Object {$_.Milliseconds -lt 5000}).Peak | Measure-Object -Maximum).Maximum; Peak = ($samples.Peak | Measure-Object -Maximum).Maximum; NonSilentSamples = @($samples | Where-Object {$_.Peak -gt 0.001}).Count; FirstSignalMs = ($samples | Where-Object {$_.Peak -gt 0.001} | Select-Object -First 1).Milliseconds; LastSignalMs = ($samples | Where-Object {$_.Peak -gt 0.001} | Select-Object -Last 1).Milliseconds } | ConvertTo-Json -Compress
