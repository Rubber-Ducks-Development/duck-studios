using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DuckStudio;
using DuckStudio.Setup;
using ImageMagick;

static class SecurityChecks
{
    static bool Rejected(Action action)
    {
        try{action();return false;}
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or FormatException or JsonException){return true;}
    }
    public static void Run(string root,Action<bool,string> check)
    {
        ImageSafety.Initialize();
        using(var image=new MagickImage(MagickColors.Red,4,4))
        {
            foreach(var format in new[]{MagickFormat.Png,MagickFormat.Jpeg,MagickFormat.Psd})
            {
                using var decoded=new MagickImage(image.ToByteArray(format));
                check(decoded.Width==4&&decoded.Height==4,"Image policy preserves "+format+" imports");
            }
        }
        var svg=System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4\" height=\"4\"><rect width=\"4\" height=\"4\" fill=\"red\"/></svg>");
        var blocked=false;try{using var decoded=new MagickImage(svg);}catch(MagickException){blocked=true;}
        check(blocked,"Image policy rejects script-capable SVG decoding");
        var projectPath=Path.Combine(root,"untrusted.weftproj");
        File.WriteAllText(projectPath,JsonSerializer.Serialize(new ClothingProject{CodeWalkerCorePath=Path.Combine(root,"attacker.dll"),CodeWalkerPath=Path.Combine(root,"attacker.exe"),GtaFolderPath="attacker-game"},ProjectIO.Json));
        var loaded=ProjectIO.Load(projectPath);
        check(loaded.CodeWalkerCorePath==""&&loaded.CodeWalkerPath==""&&loaded.GtaFolderPath=="","Imported project cannot choose executable decoder or game paths");
        check(ApplicationUpdates.ResolveFeed(new AppSettings())==ApplicationUpdates.PublicFeed,"Unconfigured copies use the public signed GitHub feed");
        var https=new Uri("https://github.com/example/feed.json");
        check(Rejected(()=>ApplicationUpdates.ValidateRedirect(https,new Uri("http://example.com/feed.json"))),"Update redirect cannot downgrade HTTPS to HTTP");
        check(Rejected(()=>ApplicationUpdates.ValidateRedirect(https,new Uri(Path.Combine(root,"feed.json")))),"Online update redirect cannot switch to a local file");
        check(ApplicationUpdates.ValidateRedirect(https,new Uri("/other/feed.json",UriKind.Relative)).AbsoluteUri=="https://github.com/other/feed.json","Relative HTTPS redirects remain supported");
        check(Rejected(()=>ApplicationUpdates.ValidateLocation("https://name:password@example.invalid/feed")),"Update addresses cannot embed credentials");
        var oversized=Path.Combine(root,"oversized.json");File.WriteAllText(oversized,new string('x',131073));
        check(Rejected(()=>new ApplicationUpdates().CheckAsync(new Uri(oversized).AbsoluteUri).GetAwaiter().GetResult()),"Oversized local feed is rejected before deserialization");
        var bytes=new byte[]{1,2,3,4};var installer=Path.Combine(root,"fixture-installer.exe");File.WriteAllBytes(installer,bytes);
        var release=new UpdateRelease{Version="9.0.0",InstallerUrl=new Uri(installer).AbsoluteUri,Size=bytes.Length,Sha256=Convert.ToHexString(SHA256.HashData(bytes))};
        var downloaded=new ApplicationUpdates().DownloadAsync(release,root).GetAwaiter().GetResult();
        check(File.ReadAllBytes(downloaded).SequenceEqual(bytes),"Installer download accepts exact size and SHA256");
        File.WriteAllBytes(installer,[4,3,2,1]);
        check(Rejected(()=>new ApplicationUpdates().DownloadAsync(release,root).GetAwaiter().GetResult()),"Modified installer bytes are rejected");
        check(File.ReadAllBytes(downloaded).SequenceEqual(bytes),"Rejected download preserves the previously verified installer");
        File.WriteAllBytes(installer,[1,2,3,4,5]);
        check(Rejected(()=>new ApplicationUpdates().DownloadAsync(release,root).GetAwaiter().GetResult()),"Installer exceeding signed size is rejected");
        var target=Path.Combine(root,"installed");Directory.CreateDirectory(target);
        foreach(var relative in new[]{"../outside.txt","folder/../../outside.txt","C:/outside.txt","file.txt:stream","folder./file.txt"})
            check(Rejected(()=>InstallEngine.SafePath(target,relative)),"Installer rejects unsafe path: "+relative);
        var keep=Path.Combine(target,"keep.txt");File.WriteAllText(keep,"keep me");
        File.WriteAllText(Path.Combine(target,InstallEngine.Marker),JsonSerializer.Serialize(new InstallRecord{Files=["keep.txt","../outside.txt"]}));
        check(Rejected(()=>InstallEngine.Uninstall(target,false))&&File.ReadAllText(keep)=="keep me","Uninstall rejects unsafe records before deleting any file");
        var outside=Path.Combine(root,"outside");Directory.CreateDirectory(outside);var witness=Path.Combine(outside,"witness.txt");File.WriteAllText(witness,"outside unchanged");
        var junction=Path.Combine(target,"linked");var script=Path.Combine(root,"junction.ps1");
        File.WriteAllText(script,"param($JunctionPath,$TargetPath)\n$ErrorActionPreference='Stop'\nNew-Item -ItemType Junction -Path $JunctionPath -Value $TargetPath | Out-Null\n");
        var start=new ProcessStartInfo("pwsh"){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"-NoProfile","-File",script,junction,outside})start.ArgumentList.Add(arg);
        using(var process=Process.Start(start)!){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Could not create junction security fixture");}
        File.WriteAllText(Path.Combine(target,InstallEngine.Marker),JsonSerializer.Serialize(new InstallRecord{Files=["keep.txt","linked/witness.txt"]}));
        check(Rejected(()=>InstallEngine.Uninstall(target,false))&&File.ReadAllText(witness)=="outside unchanged"&&File.Exists(keep),"Uninstall cannot delete files through a child junction");
        var installTarget=Path.Combine(root,"package-target");
        using var payload=new MemoryStream();
        using(var zip=new ZipArchive(payload,ZipArchiveMode.Create,true))
        {
            using var stream=zip.CreateEntry("../escaped.dll").Open();stream.Write(bytes);
        }
        payload.Position=0;
        var metadata=new PayloadMetadata{Version="2.1.2",Files=new(){["../escaped.dll"]=Convert.ToHexString(SHA256.HashData(bytes))}};
        check(Rejected(()=>InstallEngine.InstallAsync(installTarget,payload,metadata,"",null,integrations:false).GetAwaiter().GetResult())&&!File.Exists(Path.Combine(root,"escaped.dll")),"Malicious ZIP cannot write beyond installer staging folder");
    }
}
