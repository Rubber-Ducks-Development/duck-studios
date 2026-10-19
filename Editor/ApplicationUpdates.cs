using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DuckStudio;

public sealed class UpdateRelease
{
    public string Version { get; set; } = "";
    public string InstallerUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string Notes { get; set; } = "";
    public DateTime PublishedUtc { get; set; }
}
public sealed class SignedUpdate
{
    public string Payload { get; set; } = "";
    public string Signature { get; set; } = "";
}
public sealed class UpdateChannel
{
    public string FeedUrl { get; set; } = "";
}
public sealed class ApplicationUpdates
{
    public const string PublicFeed="https://github.com/Rubber-Ducks-Development/duck-studios/releases/latest/download/latest.json";
    private const int ManifestLimit=131072;
    private static readonly HttpClient client=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(15)};
    public static Version CurrentVersion => Normalize(Assembly.GetExecutingAssembly().GetName().Version!);
    public static Version Normalize(Version version)=>new(version.Major,version.Minor,Math.Max(0,version.Build));
    public static ProcessStartInfo InstallerStartInfo(string installer, string installFolder, int parentPid)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(installer)) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() };
        foreach (var value in new[] { "--update", "--install-dir", Path.GetFullPath(installFolder), "--wait-pid", parentPid.ToString(System.Globalization.CultureInfo.InvariantCulture) }) start.ArgumentList.Add(value);
        return start;
    }
    public static string ResolveFeed(AppSettings settings)
    {
        if(!string.IsNullOrWhiteSpace(settings.UpdateFeedUrl))return settings.UpdateFeedUrl.Trim();
        var channel=Path.Combine(AppContext.BaseDirectory,"update-channel.json");
        if(File.Exists(channel))
        {
            var configured=JsonSerializer.Deserialize<UpdateChannel>(File.ReadAllText(channel),ProjectIO.Json)?.FeedUrl;
            if(!string.IsNullOrWhiteSpace(configured))return configured;
        }
        return PublicFeed;
    }
    public static Uri ValidateLocation(string location)
    {
        if(!Uri.TryCreate(location,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps&&!uri.IsFile||!string.IsNullOrEmpty(uri.UserInfo))throw new InvalidDataException("Use an HTTPS update feed URL or a local/shared signed update-feed file without embedded credentials.");
        return uri;
    }
    public static Uri ValidateRedirect(Uri current,Uri next)
    {
        var target=ValidateLocation(next.IsAbsoluteUri?next.AbsoluteUri:new Uri(current,next).AbsoluteUri);
        if(target.Scheme!=Uri.UriSchemeHttps)throw new InvalidDataException("Update redirects must stay on HTTPS.");
        return target;
    }
    private static async Task<HttpResponseMessage> OpenHttpsAsync(Uri location,CancellationToken cancellation)
    {
        for(var redirects=0;redirects<=8;redirects++)
        {
            var response=await client.GetAsync(location,HttpCompletionOption.ResponseHeadersRead,cancellation);
            if((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                try{location=ValidateRedirect(location,response.Headers.Location??throw new InvalidDataException("Update redirect is missing its location."));}
                finally{response.Dispose();}
                continue;
            }
            try{response.EnsureSuccessStatusCode();return response;}catch{response.Dispose();throw;}
        }
        throw new InvalidDataException("Too many update redirects.");
    }
    private static async Task<string> ReadManifestAsync(Stream stream,CancellationToken cancellation)
    {
        using var memory=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await stream.ReadAsync(buffer,cancellation))>0){if(memory.Length+count>ManifestLimit)throw new InvalidDataException("Update manifest is too large.");memory.Write(buffer,0,count);}
        return Encoding.UTF8.GetString(memory.ToArray());
    }
    public static UpdateRelease Verify(string json,string publicKey)
    {
        if(json.Length>ManifestLimit)throw new InvalidDataException("Update manifest is too large.");
        var envelope=JsonSerializer.Deserialize<SignedUpdate>(json,ProjectIO.Json)??throw new InvalidDataException("Update feed is empty.");
        var payload=Convert.FromBase64String(envelope.Payload);var signature=Convert.FromBase64String(envelope.Signature);
        using var rsa=RSA.Create();rsa.ImportFromPem(publicKey);
        if(!rsa.VerifyData(payload,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new InvalidDataException("The update signature does not match Duck Studio's release key.");
        var release=JsonSerializer.Deserialize<UpdateRelease>(payload,ProjectIO.Json)??throw new InvalidDataException("Invalid update release.");
        if(!Version.TryParse(release.Version,out _)||release.Size<1||release.Size>2147483648L||release.Sha256.Length!=64||!release.Sha256.All(Uri.IsHexDigit))throw new InvalidDataException("Update version, size or checksum is invalid.");
        ValidateLocation(release.InstallerUrl);return release;
    }
    private static string PublicKey()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("DuckStudio.UpdatePublicKey.pem")??throw new IOException("Update verification key is missing.");using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    public async Task<UpdateRelease?> CheckAsync(string feed,CancellationToken cancellation=default)
    {
        var location=ValidateLocation(feed);string json;
        if(location.IsFile){await using var stream=File.OpenRead(location.LocalPath);json=await ReadManifestAsync(stream,cancellation);}
        else
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response=await OpenHttpsAsync(location,timeout.Token);
            await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);json=await ReadManifestAsync(stream,timeout.Token);
        }
        var release=Verify(json,PublicKey());
        if(!location.IsFile&&ValidateLocation(release.InstallerUrl).IsFile)throw new InvalidDataException("An online release must download its installer over HTTPS.");
        return Normalize(Version.Parse(release.Version))>CurrentVersion?release:null;
    }
    public async Task<string> DownloadAsync(UpdateRelease release,string root,IProgress<double>? progress=null,CancellationToken cancellation=default)
    {
        var location=ValidateLocation(release.InstallerUrl);var folder=Path.Combine(root,"Updates",Normalize(Version.Parse(release.Version)).ToString());Directory.CreateDirectory(folder);
        var target=Path.Combine(folder,"DuckStudioSetup.exe");var temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using var response=location.IsFile?null:await OpenHttpsAsync(location,cancellation);
            await using(Stream input=location.IsFile?new FileStream(location.LocalPath,FileMode.Open,FileAccess.Read,FileShare.Read):await response!.Content.ReadAsStreamAsync(cancellation))
            await using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                var buffer=new byte[65536];long total=0;int count;
                while((count=await input.ReadAsync(buffer,cancellation))>0){total+=count;if(total>release.Size)throw new InvalidDataException("Update download exceeds its signed size.");await output.WriteAsync(buffer.AsMemory(0,count),cancellation);progress?.Report((double)total/release.Size);}
                if(total!=release.Size)throw new InvalidDataException("Update download is incomplete.");
            }
            await using(var input=File.OpenRead(temp))if(!Convert.ToHexString(await SHA256.HashDataAsync(input,cancellation)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update download checksum failed.");
            File.Move(temp,target,true);return target;
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
