param([Parameter(Mandatory=$true)][string]$ClientDll)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot=Join-Path $repoRoot ('.local/gui-tests/'+(Get-Date -Format 'yyyyMMdd-HHmmssfff'))
$client=Join-Path $testRoot 'client'
$game=Join-Path $testRoot 'game'
New-Item -ItemType Directory -Force -Path $client,(Join-Path $game 'Phoenix/Binaries/Win64'),(Join-Path $game 'Phoenix/Content/Paks') | Out-Null
Copy-Item -LiteralPath $ClientDll -Destination (Join-Path $client 'ArcaneMPClient.dll')
Copy-Item -LiteralPath $ClientDll -Destination (Join-Path $game 'Phoenix/Binaries/Win64/HogwartsLegacy.exe')
@{mod_dll_name='ArcaneMPClient.dll';game_path=$game;custom='keep'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $client 'ArcaneMP_launcher.json') -Encoding utf8
$text=@'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
public static class UiSmoke {
 static readonly List<string> checks=new List<string>();
 static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static T Field<T>(object value,string name){return (T)value.GetType().GetField(name,flags).GetValue(value);}
 static bool Busy(object page){return (bool)page.GetType().GetProperty("IsBusy").GetValue(page,null);}
 static void Check(bool value,string title){if(!value)throw new Exception(title);checks.Add(title);}
 static void Wait(Func<bool> done){var timer=Stopwatch.StartNew();while(!done()){Application.DoEvents();Thread.Sleep(15);if(timer.ElapsedMilliseconds>15000)throw new Exception("UI timed out");}Application.DoEvents();}
 static CheckBox Choice(object page,string name){return Field<CheckBox>(Field<object>(page,name),"Choice");}
 [STAThread] public static int Main(string[] args){
  AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
  try{
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   var assembly=Assembly.LoadFrom(args[0]);
   using(var form=(Form)Activator.CreateInstance(assembly.GetType("ArcaneThai.InstallerForm"),true)){
    form.GetType().GetField("rendering",flags).SetValue(form,true);
    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);
    Exception failure=null;
    form.Shown+=(sender,eventArgs)=>form.BeginInvoke((Action)(()=>{try{
    object page=Field<object>(form,"ClientPage");var folder=Field<TextBox>(page,"FolderInput");var install=Field<Button>(page,"Install");var remove=Field<Button>(page,"Remove");
    Check(!install.Enabled&&!remove.Enabled,"Empty state disables write actions");
    folder.Text=args[1];Wait(()=>install.Enabled);
    Check(Choice(page,"MenuRow").Checked&&Choice(page,"GameRow").Checked,"One folder readies both components");
    Choice(page,"GameRow").Checked=false;install.PerformClick();
    var navigation=Field<Array>(form,"navigation");((Button)navigation.GetValue(1)).PerformClick();
    Check(((Control)page).Visible,"Navigation stays on the active page during writes");
    form.Close();Check(!form.IsDisposed,"Closing during a write is deferred");
    Wait(()=>!Busy(page));
    string menu=Path.Combine(args[1],"mods/ArcaneThai/ArcaneMPClient.Thai.dll"),pak=Path.Combine(args[1],"mods/ArcaneThai/zArcaneThai_Community_P.pak"),link=Path.Combine(args[1],"files/paks/zArcaneThai_Community_P.pak");
    Check(File.Exists(menu)&&!File.Exists(pak),"Menu-only click leaves the game pack untouched");
    Choice(page,"GameRow").Checked=true;Wait(()=>install.Enabled);install.PerformClick();Wait(()=>!Busy(page));
    Check(File.Exists(menu)&&File.Exists(pak)&&File.Exists(link),"Combined selection adds the remaining game component");
    Check(!install.Enabled&&remove.Enabled,"Installed state offers removal instead of redundant installation");
    Choice(page,"MenuRow").Checked=false;remove.PerformClick();Wait(()=>!Busy(page));
    Check(File.Exists(menu)&&!File.Exists(pak)&&!File.Exists(link),"Game-only removal preserves the translated menu");
    Choice(page,"MenuRow").Checked=true;Choice(page,"GameRow").Checked=false;remove.PerformClick();Wait(()=>!Busy(page));
    Check(!File.Exists(menu),"Menu removal uses the same selected folder");
    Wait(()=>install.Enabled);
    using(var running=Process.Start(new ProcessStartInfo(Path.Combine(args[2],"Phoenix/Binaries/Win64/HogwartsLegacy.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){
     try{Wait(()=>!install.Enabled&&Field<Label>(page,"processNotice").Text.StartsWith("เกมหรือ"));Check(folder.Enabled,"A running target disables writes while keeping folder inspection available");}
     finally{if(!running.HasExited){running.Kill();running.WaitForExit();}}
    }
    Wait(()=>install.Enabled);Check(install.Enabled,"Write actions become available automatically when the target closes");
    File.WriteAllText(pak,"foreign pack");Choice(page,"GameRow").Checked=true;Field<Button>(page,"scan").PerformClick();
    Wait(()=>Field<Label>(page,"statusTitle").Text=="ตรวจตำแหน่งหรือไฟล์ก่อนติดตั้ง");
    Check(!install.Enabled&&File.ReadAllText(pak)=="foreign pack","Conflicting selected components block the batch without overwriting files");
    Choice(page,"GameRow").Checked=false;Check(install.Enabled,"An unselected conflicting game pack does not block menu-only work");File.Delete(pak);
    folder.Text=Path.Combine(args[1],"missing");Wait(()=>Field<Label>(page,"statusTitle").Text=="ตรวจตำแหน่งหรือไฟล์ก่อนติดตั้ง");
    Check(!install.Enabled&&!remove.Enabled,"Invalid paths cannot trigger writes");
    ((Button)navigation.GetValue(2)).PerformClick();var image=Field<PictureBox>(form,"galleryImage");var choices=Field<ComboBox>(form,"galleryChoice");choices.SelectedIndex=5;
    Check(image.Image!=null&&image.Image.Width==1600&&choices.Items.Count==6,"All six embedded previews are available offline");
    ((Button)navigation.GetValue(1)).PerformClick();object direct=Field<object>(form,"DirectPage");Field<TextBox>(direct,"FolderInput").Text=args[2];var directInstall=Field<Button>(direct,"Install");var directRemove=Field<Button>(direct,"Remove");Wait(()=>directInstall.Enabled);directInstall.PerformClick();Wait(()=>!Busy(direct));
    string gamePak=Path.Combine(args[2],"Phoenix/Content/Paks/~mods/zArcaneThai_Community_P.pak");Check(File.Exists(gamePak),"Direct-game page installs into its own game folder");directRemove.PerformClick();Wait(()=>!Busy(direct));Check(!File.Exists(gamePak),"Direct-game page also uninstalls its pack");
    ((Button)navigation.GetValue(0)).PerformClick();form.ClientSize=new Size(920,620);Application.DoEvents();
    var browse=Field<Button>(page,"browse");Rectangle bounds=form.RectangleToClient(browse.RectangleToScreen(browse.ClientRectangle));
    Check(bounds.Bottom<form.ClientSize.Height&&browse.Height<=48&&browse.Height>=35,"Folder button remains fully visible at minimum size");
    }catch(Exception error){failure=error;}finally{if(!form.IsDisposed)form.Close();}}));
    Application.Run(form);
    if(failure!=null)throw failure;
   }
   File.WriteAllText(args[3],new JavaScriptSerializer().Serialize(new{Passed=checks.Count,Checks=checks}));return 0;
  }catch(Exception error){File.WriteAllText(args[3],new JavaScriptSerializer().Serialize(new{Error=error.ToString(),Checks=checks}));return 1;}
 }
}
'@
$source=Join-Path $testRoot 'UiSmoke.cs'
[IO.File]::WriteAllText($source,$text)
$runner=Join-Path $testRoot 'UiSmoke.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$fakeSource=Join-Path $testRoot 'FakeGame.cs'
[IO.File]::WriteAllText($fakeSource,'public static class FakeGame { public static void Main(){System.Threading.Thread.Sleep(60000);} }')
& $compiler /nologo /target:winexe ('/out:'+(Join-Path $game 'Phoenix/Binaries/Win64/HogwartsLegacy.exe')) $fakeSource
if($LASTEXITCODE -ne 0){throw 'Fake game compilation failed'}
& $compiler /nologo /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll ('/out:'+$runner) $source
if($LASTEXITCODE -ne 0){throw 'UI test compilation failed'}
$report=Join-Path $repoRoot 'dist/gui-tests.json'
& $runner (Join-Path $repoRoot 'dist/ArcaneThaiInstaller.exe') $client $game $report
if($LASTEXITCODE -ne 0){throw (Get-Content -Raw -LiteralPath $report)}
Get-Content -Raw -LiteralPath $report
