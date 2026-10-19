using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace DuckStudio.Setup;

public sealed class PayloadMetadata
{
    public string Version { get; set; } = "2.0.0";
    public Dictionary<string,string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed class InstallRecord
{
    public string AppId { get; set; } = "DuckStudio";
    public string Version { get; set; } = "";
    public List<string> Files { get; set; } = [];
    public string FeedUrl { get; set; } = "";
}
public static class InstallEngine
{
    public const string Marker="duck-install.json";
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    public static string DefaultFolder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Duck Studio");
    public static string ResolveInstallFolder(string path)
    {
        var full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        var root=Path.GetPathRoot(full)!.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(full==root||Directory.GetParent(full)==null)throw new IOException("Choose an application folder, not the root of a drive.");
        for(var info=new DirectoryInfo(full);info!=null;info=info.Parent)if(info.Exists&&(info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Choose a normal folder rather than a linked or junction folder.");
        if(Directory.Exists(full)&&Directory.EnumerateFileSystemEntries(full).Any()&&ReadRecord(full)==null)throw new IOException("This folder contains unrelated files. Choose an empty folder or an existing Duck Studio installation.");
        return full;
    }
    public static string SafePath(string root,string relative)
    {
        var parts=relative.Replace('\\','/').Split('/');
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(':')||parts.Any(p=>p is "." or ".." or "" || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0))throw new InvalidDataException("Unsafe file path in installation package.");
        var full=Path.GetFullPath(Path.Combine(root,relative));var prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Installation file escapes its application folder.");
        for(var current=full;current!=null;current=Path.GetDirectoryName(current))
        {
            try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Installation files must not use symbolic links or junctions.");}
            catch(FileNotFoundException){}
            catch(DirectoryNotFoundException){}
        }
        return full;
    }
    public static InstallRecord? ReadRecord(string path)
    {
        var file=Path.Combine(path,Marker);if(!File.Exists(file))return null;
        try{var record=JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(file),Json);return record?.AppId=="DuckStudio"?record:null;}
        catch(JsonException){return null;}
    }
    public static void CheckRunning(string target)
    {
        foreach(var process in Process.GetProcessesByName("DuckStudio"))using(process)
        {
            try{if(process.MainModule?.FileName is string path&&Path.GetDirectoryName(path)!.Equals(target,StringComparison.OrdinalIgnoreCase))throw new IOException("Close Duck Studio before installing or uninstalling this copy.");}
            catch(System.ComponentModel.Win32Exception){throw new IOException("Duck Studio is running. Close it before continuing.");}
            catch(InvalidOperationException){}
        }
    }
    public static async Task InstallAsync(string target,Stream payload,PayloadMetadata metadata,string feed,string? installerPath,IProgress<double>? progress=null,bool integrations=true,CancellationToken cancellation=default)
    {
        target=ResolveInstallFolder(target);ReleaseWorkingDirectory(target);CheckRunning(target);
        metadata.Files=metadata.Files.ToDictionary(p=>p.Key.Replace('/',Path.DirectorySeparatorChar),p=>p.Value,StringComparer.OrdinalIgnoreCase);
        if(!Version.TryParse(metadata.Version,out _)||metadata.Files.Count==0)throw new InvalidDataException("Installation package metadata is invalid.");
        var old=ReadRecord(target);var parent=Directory.GetParent(target)!.FullName;Directory.CreateDirectory(parent);
        var stage=Path.Combine(parent,".duck-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        var backup=target+".previous-"+Guid.NewGuid().ToString("N");bool swapped=false;
        try
        {
            using(var archive=new ZipArchive(payload,ZipArchiveMode.Read,leaveOpen:true))
            {
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;int count=0;
                foreach(var entry in archive.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(entry.FullName.EndsWith('/'))continue;
                    if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Installation package contains a symbolic link.");
                    var relative=entry.FullName.Replace('/',Path.DirectorySeparatorChar);
                    if(!seen.Add(relative)||!metadata.Files.TryGetValue(relative,out var expected))throw new InvalidDataException("Package file is duplicated or absent from its manifest: "+entry.FullName);
                    total+=entry.Length;if(total>2147483648L)throw new InvalidDataException("Installation package is too large.");
                    var destination=SafePath(stage,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using(var input=entry.Open())await using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))await input.CopyToAsync(output,cancellation);
                    string? actualHash=null;
                    await RetryFileOperationAsync(async()=>{await using var input=File.OpenRead(destination);actualHash=Convert.ToHexString(await SHA256.HashDataAsync(input,cancellation));},destination,cancellation);
                    if(!string.Equals(actualHash,expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Installation file checksum failed: "+entry.FullName);
                    progress?.Report((double)++count/metadata.Files.Count*.8);
                }
                if(seen.Count!=metadata.Files.Count)throw new InvalidDataException("Installation package is incomplete.");
            }
            if(!File.Exists(Path.Combine(stage,"DuckStudio.exe"))||!File.Exists(Path.Combine(stage,"DuckStudio.dll")))throw new InvalidDataException("Installation package is missing Duck Studio.");
            var installedFiles=metadata.Files.Keys.ToList();
            if(installerPath!=null){var copy=Path.Combine(stage,"DuckStudioSetup.exe");await RetryFileOperationAsync(()=>File.Copy(installerPath,copy,true),copy,cancellation);installedFiles.Add("DuckStudioSetup.exe");}
            if(old!=null&&Directory.Exists(target))
            {
                var owned=old.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);owned.Add(Marker);owned.Add("update-channel.json");
                void Preserve(string directory)
                {
                    foreach(var file in Directory.GetFiles(directory)){var relative=Path.GetRelativePath(target,file);if(owned.Contains(relative)||metadata.Files.ContainsKey(relative))continue;if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Move linked files out of the application folder before updating.");var destination=SafePath(stage,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination,false);}
                    foreach(var directoryPath in Directory.GetDirectories(directory))if((File.GetAttributes(directoryPath)&FileAttributes.ReparsePoint)==0)Preserve(directoryPath);
                }
                Preserve(target);
            }
            if(string.IsNullOrWhiteSpace(feed))feed=old?.FeedUrl??"";
            File.WriteAllText(Path.Combine(stage,"update-channel.json"),JsonSerializer.Serialize(new{FeedUrl=feed},Json));installedFiles.Add("update-channel.json");
            File.WriteAllText(Path.Combine(stage,Marker),JsonSerializer.Serialize(new InstallRecord{Version=metadata.Version,Files=installedFiles,FeedUrl=feed},Json));
            // Both paths are validated absolute children of the selected application's
            // parent, and an existing target is identified by its Duck Studio marker.
            CheckRunning(target);
            if(Directory.Exists(target))await RetryFileOperationAsync(()=>Directory.Move(target,backup),target,cancellation);
            try{await RetryFileOperationAsync(()=>Directory.Move(stage,target),stage,cancellation);swapped=true;}
            catch{if(Directory.Exists(backup))await RetryFileOperationAsync(()=>Directory.Move(backup,target),backup);throw;}
            progress?.Report(.95);
            if(integrations)Register(target,metadata.Version);
            if(Directory.Exists(backup))
            {
                // Keep the most recent install intact for recovery. Do not recursively
                // delete backups, user files, settings or projects during installation.
                File.WriteAllText(Path.Combine(backup,"ROLLBACK.txt"),"Previous Duck Studio installation. Current copy: "+target);
            }
            progress?.Report(1);
        }
        catch
        {
            if(swapped&&Directory.Exists(backup))
            {
                var failed=target+".failed-"+Guid.NewGuid().ToString("N");await RetryFileOperationAsync(()=>Directory.Move(target,failed),target);await RetryFileOperationAsync(()=>Directory.Move(backup,target),backup);
            }
            throw;
        }
        finally
        {
            // A scanner's cleanup lock must not replace the original install error.
            if(Directory.Exists(stage))try{await RetryFileOperationAsync(()=>Directory.Delete(stage,true),stage);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}
        }
    }
    private static void ReleaseWorkingDirectory(string target)
    {
        var current=Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(!current.Equals(target,StringComparison.OrdinalIgnoreCase)&&!current.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
        var neutral=Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if(!Directory.Exists(neutral)||neutral.Equals(target,StringComparison.OrdinalIgnoreCase)||neutral.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))neutral=Directory.GetParent(target)!.FullName;
        // The installer owns its process. Release the inherited directory handle
        // before replacing the installation, including launches by older editors.
        Directory.SetCurrentDirectory(neutral);
    }
    internal static Task RetryFileOperationAsync(Action operation,string path,CancellationToken cancellation=default)
        =>RetryFileOperationAsync(()=>{operation();return Task.CompletedTask;},path,cancellation);
    internal static async Task RetryFileOperationAsync(Func<Task> operation,string path,CancellationToken cancellation=default)
    {
        for(var attempt=0;;attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            try{await operation().ConfigureAwait(false);return;}
            catch(IOException ex)when((ex.HResult&0xFFFF) is 32 or 33)
            {
                if(attempt>=7)throw new IOException($"Windows kept '{path}' in use after retrying. Close Duck Studio and any other installer windows, then try again.",ex);
                await Task.Delay(Math.Min(100*(1<<attempt),1000),cancellation).ConfigureAwait(false);
            }
        }
    }
    public static string? WriteFailureLog(Exception error,string target,string? logFolder=null)
    {
        try
        {
            logFolder??=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DuckStudio","Logs");
            Directory.CreateDirectory(logFolder);
            var path=Path.Combine(logFolder,"installer-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".log");
            File.WriteAllText(path,$"Installation target: {target}\nWorking directory: {Directory.GetCurrentDirectory()}\n\n{error}");
            return path;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){return null;}
    }
    private static void Register(string target,string version)
    {
        var shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Duck Studio.lnk");
        var shellType=Type.GetTypeFromProgID("WScript.Shell");if(shellType!=null){dynamic shell=Activator.CreateInstance(shellType)!;dynamic link=shell.CreateShortcut(shortcut);link.TargetPath=Path.Combine(target,"DuckStudio.exe");link.WorkingDirectory=target;link.IconLocation=Path.Combine(target,"DuckStudio.exe")+",0";link.Save();}
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DuckStudio")){key.SetValue("DisplayName","Duck Studio");key.SetValue("DisplayVersion",version);key.SetValue("Publisher","Duck Studio");key.SetValue("InstallLocation",target);key.SetValue("DisplayIcon",Path.Combine(target,"DuckStudio.exe"));key.SetValue("UninstallString","\""+Path.Combine(target,"DuckStudioSetup.exe")+"\" --uninstall --install-dir \""+target+"\"");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}
        using var classes=Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        RegisterProjectFiles(classes,target);
    }
    public static void RegisterProjectFiles(RegistryKey classes,string target)
    {
        using(var type=classes.CreateSubKey("DuckStudio.Project")){type.SetValue("","Duck Studio project");using var icon=type.CreateSubKey("DefaultIcon");icon.SetValue("",Path.Combine(target,"DuckStudio.exe")+",0");using var command=type.CreateSubKey(@"shell\open\command");command.SetValue("","\""+Path.Combine(target,"DuckStudio.exe")+"\" \"%1\"");}
        foreach(var suffix in ProjectFiles.OpenWithExtensions)
        {
            using var extension=classes.CreateSubKey(suffix+@"\OpenWithProgids");
            extension.SetValue("DuckStudio.Project",Array.Empty<byte>(),RegistryValueKind.None);
        }
    }
    public static void Uninstall(string target,bool integrations=true)
    {
        target=ResolveInstallFolder(target);ReleaseWorkingDirectory(target);var record=ReadRecord(target)??throw new IOException("This folder is not an installed Duck Studio copy.");CheckRunning(target);
        // Validate every owned path before deleting any file, including linked children.
        var files=record.Files.Distinct(StringComparer.OrdinalIgnoreCase).Select(relative=>SafePath(target,relative)).ToArray();
        foreach(var file in files)
        {
            if(File.Exists(file))File.Delete(file);
        }
        File.Delete(Path.Combine(target,Marker));
        void EmptyDirectories(string folder)
        {
            foreach(var child in Directory.GetDirectories(folder))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0){EmptyDirectories(child);if(!Directory.EnumerateFileSystemEntries(child).Any())Directory.Delete(child);}
        }
        EmptyDirectories(target);if(!Directory.EnumerateFileSystemEntries(target).Any())Directory.Delete(target);
        if(!integrations)return;
        var shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Duck Studio.lnk");if(File.Exists(shortcut))File.Delete(shortcut);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DuckStudio",false);
        using var classes=Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        UnregisterProjectFiles(classes);
    }
    public static void UnregisterProjectFiles(RegistryKey classes)
    {
        classes.DeleteSubKeyTree("DuckStudio.Project",false);
        foreach(var suffix in ProjectFiles.OpenWithExtensions)
        {
            using var extension=classes.OpenSubKey(suffix+@"\OpenWithProgids",true);
            extension?.DeleteValue("DuckStudio.Project",false);
        }
    }
}
