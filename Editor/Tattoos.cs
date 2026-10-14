using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DuckStudio;

public sealed class TattooAsset
{
    public event EventHandler? Changed;
    public void NotifyChanged()=>Changed?.Invoke(this,EventArgs.Empty);
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New tattoo";
    public string TextureName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string TexturePath { get; set; } = "";
    public string Gender { get; set; } = "GENDER_MALE";
    public string Zone { get; set; } = "ZONE_TORSO";
    public string Facing { get; set; } = "TATTOO_CHEST";
    public string UpdateGroup { get; set; } = "";
    public double U { get; set; } = .5;
    public double V { get; set; } = .5;
    public double ScaleX { get; set; } = .1;
    public double ScaleY { get; set; } = .1;
    public double Rotation { get; set; }
    public int Cost { get; set; }
    public override string ToString() => Name + "   /   " + Zone.Replace("ZONE_", "").Replace('_', ' ') + "   /   " + (Gender == "GENDER_FEMALE" ? "F" : Gender == "GENDER_DONTCARE" ? "M/F" : "M");
}

public static class TattooBuilder
{
    public static readonly string[] Zones = ["ZONE_TORSO", "ZONE_HEAD", "ZONE_LEFT_ARM", "ZONE_RIGHT_ARM", "ZONE_LEFT_LEG", "ZONE_RIGHT_LEG"];
    public static readonly string[] Genders = ["GENDER_MALE", "GENDER_FEMALE", "GENDER_DONTCARE"];
    public static readonly string[] Facings = ["TATTOO_BACK", "TATTOO_CHEST", "TATTOO_FRONT", "TATTOO_FRONT_LEFT", "TATTOO_FRONT_RIGHT", "TATTOO_LEFT", "TATTOO_RIGHT", "TATTOO_STOMACH"];
    public static string Collection(ClothingProject project) => project.Collection + "_overlays";
    public static string Preset(TattooAsset tattoo, bool female) => tattoo.TextureName + (female ? "_F" : "_M");
    public static string Slug(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9_]", "_").Trim('_');
    public static IEnumerable<bool> Sexes(TattooAsset tattoo) => tattoo.Gender == "GENDER_DONTCARE" ? [false, true] : [tattoo.Gender == "GENDER_FEMALE"];
    public static List<string> Validate(ClothingProject project)
    {
        var issues = new List<string>();
        foreach (var tattoo in project.Tattoos)
        {
            var prefix = "Tattoo " + tattoo.Name + ": ";
            if (!Regex.IsMatch(tattoo.TextureName, "^[a-z][a-z0-9_]{1,62}$")) issues.Add(prefix + "texture name must contain 2-63 lowercase letters, digits or underscores.");
            if (!File.Exists(tattoo.SourcePath) && !File.Exists(tattoo.TexturePath)) issues.Add(prefix + "image or YTD is missing.");
            if (!Zones.Contains(tattoo.Zone) || !Genders.Contains(tattoo.Gender) || !Facings.Contains(tattoo.Facing)) issues.Add(prefix + "invalid body zone, gender or facing.");
            if (!double.IsFinite(tattoo.U) || !double.IsFinite(tattoo.V) || !double.IsFinite(tattoo.ScaleX) || !double.IsFinite(tattoo.ScaleY) || !double.IsFinite(tattoo.Rotation) || tattoo.ScaleX <= 0 || tattoo.ScaleY <= 0 || tattoo.ScaleX > 2 || tattoo.ScaleY > 2 || Math.Abs(tattoo.U) > 2 || Math.Abs(tattoo.V) > 2) issues.Add(prefix + "check placement values and positive scale.");
            if (tattoo.Cost < 0) issues.Add(prefix + "price cannot be negative.");
        }
        foreach (var group in project.Tattoos.GroupBy(t=>t.TextureName,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1)) issues.Add("Duplicate tattoo texture name: " + group.Key);
        return issues;
    }
    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    public static XDocument Overlays(ClothingProject project) => new(new XElement("PedDecorationCollection", new XElement("bRequiredForSync",new XAttribute("value",true)),new XElement("presets",
        project.Tattoos.SelectMany(t=>Sexes(t).Select(f=>new XElement("Item",
            new XElement("uvPos",new XAttribute("x",Number(t.U)),new XAttribute("y",Number(t.V))),
            new XElement("scale",new XAttribute("x",Number(t.ScaleX)),new XAttribute("y",Number(t.ScaleY))),
            new XElement("rotation",new XAttribute("value",Number(t.Rotation))),new XElement("nameHash",Preset(t,f)),
            new XElement("txdHash",t.TextureName),new XElement("txtHash",t.TextureName),new XElement("zone",t.Zone),new XElement("type","TYPE_TATTOO"),new XElement("faction","FM"),new XElement("garment","All"),new XElement("gender",f?"GENDER_FEMALE":"GENDER_MALE"),new XElement("award"),new XElement("awardLevel")))))));
    public static XDocument Shop(ClothingProject project) => new(new XElement("TattooShopItemArray",new XElement("TattooShopItems",project.Tattoos.SelectMany((t,index)=>Sexes(t).Select(f=>new XElement("Item",
        new XElement("id",new XAttribute("value",100000+index*2+(f?1:0))),new XElement("cost",new XAttribute("value",t.Cost)),new XElement("textLabel",t.TextureName.ToUpperInvariant()),new XElement("collection",Collection(project)),new XElement("preset",Preset(t,f)),new XElement("zone",t.Zone.Replace("ZONE_","PDZ_")),new XElement("eFacing",t.Facing),new XElement("updateGroup",t.UpdateGroup),new XElement("eFaction",f?"TATTOO_MP_FM_F":"TATTOO_MP_FM"),new XElement("lockHash")))))));
    public static void Prepare(ClothingProject project, string core)
    {
        foreach (var tattoo in project.Tattoos)
        {
            if (!File.Exists(tattoo.SourcePath)) continue;
            if (string.IsNullOrWhiteSpace(tattoo.TexturePath)) tattoo.TexturePath = TextureImport.NewDestination(tattoo.TextureName + ".png");
            if (Path.GetExtension(tattoo.SourcePath).Equals(".ytd",StringComparison.OrdinalIgnoreCase))
                CodeWalkerPreview.RenameSingleTexture(tattoo.SourcePath,tattoo.TexturePath,tattoo.TextureName,core);
            else TextureImport.Convert(tattoo.SourcePath,tattoo.TexturePath,core,"",null,tattoo.TextureName,512,true,512);
        }
    }
    public static void Export(ClothingProject project, string resource, string stream)
    {
        if (project.Tattoos.Count == 0) return;
        foreach (var tattoo in project.Tattoos) File.Copy(tattoo.TexturePath,Path.Combine(stream,tattoo.TextureName+".ytd"),true);
        Overlays(project).Save(Path.Combine(resource,Collection(project)+".xml"));
        Shop(project).Save(Path.Combine(resource,"shop_tattoo.meta"));
        File.WriteAllText(Path.Combine(resource,"tattoos.json"),JsonSerializer.Serialize(project.Tattoos.Select(t=>new{t.Name,collection=Collection(project),male=Sexes(t).Contains(false)?Preset(t,false):null,female=Sexes(t).Contains(true)?Preset(t,true):null,t.Zone,t.Cost}),ProjectIO.Json));
    }
}
