using System.IO;
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
    private static readonly HttpClient client=new(){Timeout=TimeSpan.FromMinutes(15)};
    public static Version CurrentVersion => Normalize(Assembly.GetExecutingAssembly().GetName().Version!);
    public static Version Normalize(Version version)=>new(version.Major,version.Minor,Math.Max(0,version.Build));
    public static string ResolveFeed(AppSettings settings)
    {
        if(!string.IsNullOrWhiteSpace(settings.UpdateFeedUrl))return settings.UpdateFeedUrl.Trim();
        var channel=Path.Combine(AppContext.BaseDirectory,"update-channel.json");
        if(File.Exists(channel))return JsonSerializer.Deserialize<UpdateChannel>(File.ReadAllText(channel),ProjectIO.Json)?.FeedUrl??"";
        var local=Path.Combine(AppContext.BaseDirectory,"UpdateFeed","latest.json");return File.Exists(local)?new Uri(local).AbsoluteUri:"";
    }
    public static Uri ValidateLocation(string location)
    {
        if(!Uri.TryCreate(location,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps&&!uri.IsFile)throw new InvalidDataException("Use an HTTPS update feed URL or a local/shared signed update-feed file.");
        return uri;
    }
    public static UpdateRelease Verify(string json,string publicKey)
    {
        if(json.Length>131072)throw new InvalidDataException("Update manifest is too large.");
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
        if(location.IsFile)json=await File.ReadAllTextAsync(location.LocalPath,cancellation);
        else
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response=await client.GetAsync(location,HttpCompletionOption.ResponseHeadersRead,timeout.Token);response.EnsureSuccessStatusCode();
            await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var memory=new MemoryStream();var buffer=new byte[8192];int count;
            while((count=await stream.ReadAsync(buffer,timeout.Token))>0){if(memory.Length+count>131072)throw new InvalidDataException("Update manifest is too large.");memory.Write(buffer,0,count);}json=Encoding.UTF8.GetString(memory.ToArray());
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
            await using(Stream input=location.IsFile?new FileStream(location.LocalPath,FileMode.Open,FileAccess.Read,FileShare.Read):await client.GetStreamAsync(location,cancellation))
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
