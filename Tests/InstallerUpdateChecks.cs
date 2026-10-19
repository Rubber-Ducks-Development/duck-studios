using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DuckStudio;
using DuckStudio.Setup;

static class InstallerUpdateChecks
{
    public static void Run(string root,Action<bool,string> check)
    {
        var folder=Directory.CreateDirectory(Path.Combine(root,"UpdaterWorkingDirectory")).FullName;
        var target=Directory.CreateDirectory(Path.Combine(folder,"Duck Studio")).FullName;
        var start=ApplicationUpdates.InstallerStartInfo(Path.Combine(folder,"Downloads","Setup.exe"),target,1234);
        check(Path.GetFullPath(start.WorkingDirectory)==Path.GetFullPath(Path.GetTempPath())&&start.ArgumentList.SequenceEqual(new[]{"--update","--install-dir",target,"--wait-pid","1234"}),"Updater launches outside the installation and preserves target and parent PID");
        File.WriteAllBytes(Path.Combine(target,"DuckStudio.exe"),[1]);
        File.WriteAllBytes(Path.Combine(target,"DuckStudio.dll"),[1]);
        File.WriteAllText(Path.Combine(target,"notes.txt"),"Preserve local files");
        File.WriteAllText(Path.Combine(target,InstallEngine.Marker),JsonSerializer.Serialize(new InstallRecord{Version="2.1.7",Files=["DuckStudio.exe","DuckStudio.dll"]}));
        var original=Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(target);
            var blocked=false;
            try{Directory.Move(target,target+".locked-probe");}catch(IOException ex)when((ex.HResult&0xFFFF)==32){blocked=true;}
            check(blocked,"Windows reproduces the reported sharing violation when the installer inherits its target directory");
            using(var payload=Payload(out var metadata))InstallEngine.InstallAsync(".",payload,metadata,"",null,integrations:false).GetAwaiter().GetResult();
            check(File.ReadAllBytes(Path.Combine(target,"DuckStudio.dll")).SequenceEqual(new byte[]{2,3,4})&&InstallEngine.ReadRecord(target)!.Version=="2.1.8","Update succeeds from the inherited target working directory, including a relative install path");
            check(!Inside(Directory.GetCurrentDirectory(),target),"Installer releases its own directory handle before the folder swap");
            check(File.ReadAllText(Path.Combine(target,"notes.txt"))=="Preserve local files"&&Directory.GetDirectories(folder,"Duck Studio.previous-*").Any(d=>File.ReadAllBytes(Path.Combine(d,"DuckStudio.dll")).SequenceEqual(new byte[]{1})),"Working-directory fix preserves local files and the previous installation");
            var child=Directory.CreateDirectory(Path.Combine(target,"cache")).FullName;
            Directory.SetCurrentDirectory(child);
            using(var payload=Payload(out var metadata))InstallEngine.InstallAsync(target,payload,metadata,"",null,integrations:false).GetAwaiter().GetResult();
            check(!Inside(Directory.GetCurrentDirectory(),target)&&File.Exists(Path.Combine(target,"DuckStudio.dll")),"Update also releases a working directory nested inside the installation");
            Directory.SetCurrentDirectory(folder);
            using(var payload=Payload(out var metadata))InstallEngine.InstallAsync(target,payload,metadata,"",null,integrations:false).GetAwaiter().GetResult();
            check(Directory.GetCurrentDirectory()==folder,"Installer keeps an already neutral working directory unchanged");
        }
        finally{Directory.SetCurrentDirectory(original);}

        var file=Path.Combine(folder,"scanner-lock.dll");File.WriteAllBytes(file,[5,6,7]);
        var attempts=0;
        using(var held=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            var release=Task.Run(async()=>{await Task.Delay(250);held.Dispose();});
            InstallEngine.RetryFileOperationAsync(()=>{attempts++;File.ReadAllBytes(file);},file).GetAwaiter().GetResult();
            release.GetAwaiter().GetResult();
        }
        check(attempts>1&&File.ReadAllBytes(file).SequenceEqual(new byte[]{5,6,7}),"Temporary file locks are retried and recover without changing file contents");
        attempts=0;var cancelled=false;
        using(var held=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        using(var cancellation=new CancellationTokenSource(50))
        {
            try{InstallEngine.RetryFileOperationAsync(()=>{attempts++;File.ReadAllBytes(file);},file,cancellation.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
        }
        check(cancelled&&attempts==1,"Cancelling an update interrupts the sharing-lock retry delay");
        attempts=0;var missing=false;
        try{InstallEngine.RetryFileOperationAsync(()=>{attempts++;File.ReadAllBytes(Path.Combine(folder,"missing.dll"));},file).GetAwaiter().GetResult();}catch(FileNotFoundException){missing=true;}
        check(missing&&attempts==1,"Non-sharing failures are not hidden behind retry delays");
        IOException? failure=null;attempts=0;
        using(var held=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            try{InstallEngine.RetryFileOperationAsync(()=>{attempts++;File.ReadAllBytes(file);},file).GetAwaiter().GetResult();}catch(IOException ex){failure=ex;}
        }
        check(failure!=null&&failure.Message.Contains(file)&&failure.InnerException is IOException&&attempts>1&&attempts<20,"Persistent locks stop after bounded retries and identify the locked path");
        var log=InstallEngine.WriteFailureLog(failure!,target,Path.Combine(folder,"Logs"));
        check(log!=null&&File.ReadAllText(log).Contains(file)&&File.ReadAllText(log).Contains(target),"Installer diagnostic log retains the actual path, target and original exception");
        var invalidLogFolder=Path.Combine(folder,"not-a-directory");File.WriteAllText(invalidLogFolder,"fixture");
        check(InstallEngine.WriteFailureLog(failure!,target,invalidLogFolder)==null,"A diagnostic-log failure does not replace the installation error");
    }
    static bool Inside(string path,string root)=>Path.GetFullPath(path).Equals(root,StringComparison.OrdinalIgnoreCase)||Path.GetFullPath(path).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    static MemoryStream Payload(out PayloadMetadata metadata)
    {
        var bytes=new byte[]{2,3,4};metadata=new PayloadMetadata{Version="2.1.8",Files=new(){["DuckStudio.exe"]=Convert.ToHexString(SHA256.HashData(bytes)),["DuckStudio.dll"]=Convert.ToHexString(SHA256.HashData(bytes))}};
        var payload=new MemoryStream();
        using(var archive=new ZipArchive(payload,ZipArchiveMode.Create,true))foreach(var name in metadata.Files.Keys){using var entry=archive.CreateEntry(name).Open();entry.Write(bytes);}
        payload.Position=0;return payload;
    }
}
