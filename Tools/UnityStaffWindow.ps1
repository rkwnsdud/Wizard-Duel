param([switch]$Refresh, [switch]$TogglePlay)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StaffWindowNative {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int cmd);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 public struct Rect { public int Left,Top,Right,Bottom; }
}
'@
$editor=Get-Process Unity | Where-Object {$_.MainWindowTitle -like 'Wizard-Duel*'} | Select-Object -First 1
if(!$editor){throw 'Wizard-Duel editor not found'}
$handle=$editor.MainWindowHandle
[StaffWindowNative]::ShowWindow($handle,9) | Out-Null
$shell=New-Object -ComObject WScript.Shell
$shell.SendKeys('%')
[StaffWindowNative]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 1000
if($Refresh -and [StaffWindowNative]::GetForegroundWindow() -eq $handle){
 $shell=New-Object -ComObject WScript.Shell
 $shell.SendKeys('^r')
 Start-Sleep -Milliseconds 1500
}
if($TogglePlay -and [StaffWindowNative]::GetForegroundWindow() -eq $handle){
 $shell.SendKeys('^p')
 Start-Sleep -Milliseconds 1500
}
$rect=New-Object StaffWindowNative+Rect
[StaffWindowNative]::GetWindowRect($handle,[ref]$rect) | Out-Null
Write-Output "Unity pid=$($editor.Id) handle=$handle foreground=$([StaffWindowNative]::GetForegroundWindow()) rectangle=$rect"
if([StaffWindowNative]::GetForegroundWindow() -ne $handle){throw 'Unity is not foreground; no screenshot taken'}
$bmp=New-Object System.Drawing.Bitmap ($rect.Right-$rect.Left),($rect.Bottom-$rect.Top)
$g=[System.Drawing.Graphics]::FromImage($bmp)
$dc=$g.GetHdc()
[StaffWindowNative]::PrintWindow($handle,$dc,2) | Out-Null
$g.ReleaseHdc($dc)
$bmp.Save((Join-Path (Get-Location) 'Logs\UnityWindow.png'))
$g.Dispose()
$bmp.Dispose()
