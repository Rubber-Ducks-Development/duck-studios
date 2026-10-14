using ImageMagick;
using ImageMagick.Configuration;

namespace DuckStudio;

public static class ImageSafety
{
    private static readonly Lazy<bool> initialized=new(()=>
    {
        var configuration=ConfigurationFiles.Default;
        configuration.Policy.Data="""
            <policymap>
              <policy domain="delegate" rights="none" pattern="*" />
              <policy domain="filter" rights="none" pattern="*" />
              <policy domain="module" rights="none" pattern="*" />
              <policy domain="module" rights="read|write" pattern="{JPEG,PNG,PSD,WEBP,BMP,TIFF,GIF,TGA,DDS,HEIC,JP2,PCX,PNM,QOI,EXR,HDR,ICON,JXL,BGR,RGB,XC}" />
              <policy domain="path" rights="none" pattern="@*" />
              <policy domain="path" rights="none" pattern="|*" />
              <policy domain="resource" name="width" value="16384" />
              <policy domain="resource" name="height" value="16384" />
              <policy domain="resource" name="memory" value="512MiB" />
              <policy domain="resource" name="map" value="1GiB" />
              <policy domain="resource" name="disk" value="4GiB" />
            </policymap>
            """;
        MagickNET.Initialize(configuration);
        return true;
    });
    public static void Initialize()=>_ = initialized.Value;
}
