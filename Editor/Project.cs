using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace DuckStudio;

public sealed class ClothingGroupState : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private bool isExpanded = true;
    public bool IsExpanded
    {
        get => isExpanded;
        set { if (isExpanded == value) return; isExpanded = value; PropertyChanged?.Invoke(this, new(nameof(IsExpanded))); }
    }
}

public sealed class ClothingAsset : System.ComponentModel.INotifyPropertyChanged
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ClothingGroupState GroupState { get; set; } = new();
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private bool isNew;
    private long addedAt;
    private double newHighlightOpacity;
    [System.Text.Json.Serialization.JsonIgnore]
    public double NewHighlightOpacity => newHighlightOpacity;
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Brush NewHighlightForeground => new System.Windows.Media.SolidColorBrush(
        System.Windows.Media.Color.FromRgb(Blend(0x29), Blend(0x26), Blend(0x11)));
    private byte Blend(int channel) => (byte)Math.Round(0xEE + (channel - 0xEE) * newHighlightOpacity);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNew
    {
        get => isNew;
        set
        {
            if (isNew == value) return;
            isNew = value;
            if (value) addedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            newHighlightOpacity = value ? 1 : 0;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsNew)));
            NotifyNewHighlight();
        }
    }
    public void RefreshNewHighlight() => UpdateNewHighlight(System.Diagnostics.Stopwatch.GetElapsedTime(addedAt));
    public void UpdateNewHighlight(TimeSpan elapsed)
    {
        if (!IsNew) return;
        var opacity = Math.Clamp(1 - (elapsed.TotalSeconds - 2) / .7, 0, 1);
        if (opacity == 0) { IsNew = false; return; }
        if (newHighlightOpacity == opacity) return;
        newHighlightOpacity = opacity;
        NotifyNewHighlight();
    }
    private void NotifyNewHighlight()
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(NewHighlightOpacity)));
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(NewHighlightForeground)));
    }
    private bool statusOk = true;
    private string statusTooltip = "No issues found for this drawable.";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled item";
    public string Ped { get; set; } = "mp_m_freemode_01";
    public string Slot { get; set; } = "jbib";
    public int Drawable { get; set; }
    public string ModelPath { get; set; } = "";
    public bool GeneratedLods { get; set; }
    public List<string> TexturePaths { get; set; } = [];
    public Dictionary<string, TextureSourceLink> TextureSources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public event EventHandler? TexturesChanged;
    public void NotifyTexturesChanged() => TexturesChanged?.Invoke(this, EventArgs.Empty);
    public Dictionary<string, string> TextureLabels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string PreviewPath { get; set; } = "";
    public string Notes { get; set; } = "";
    public string FirstPersonPath { get; set; } = "";
    public string PhysicsPath { get; set; } = "";
    public string Audio { get; set; } = "none";
    public uint BehaviourFlags { get; set; }
    public string RestrictionTags { get; set; } = "";
    public string Shop { get; set; } = "CLO_SHOP_NONE";
    public string CreatorName { get; set; } = "";
    public string Website { get; set; } = "";
    public string SupportUrl { get; set; } = "";
    public bool IsProp => Slot.StartsWith("p_");
    public string GenderLabel => Ped == "mp_f_freemode_01" ? "F" : "M";
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.ImageSource GenderIcon => IconAssets.Get(Ped == "mp_f_freemode_01" ? "female-pink" : "male-blue");
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Visibility PropIconVisibility => IsProp ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.ImageSource StatusIcon => IconAssets.StatusBulb(statusOk);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasValidationIssues => !statusOk;
    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusTooltip => statusTooltip;
    public void SetValidationStatus(IReadOnlyList<string> issues)
    {
        var ok = issues.Count == 0;
        var tip = ok ? "No issues found for this drawable." : string.Join("\n", issues);
        if (ok == statusOk && tip == statusTooltip) return;
        statusOk = ok;
        statusTooltip = tip;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusIcon)));
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasValidationIssues)));
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusTooltip)));
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Geometry GenderGeometry => SlotIcons.GenderFor(Ped == "mp_f_freemode_01");
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Brush GenderBrush => Ped == "mp_f_freemode_01" ? System.Windows.Media.Brushes.DeepPink : System.Windows.Media.Brushes.DeepSkyBlue;
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Geometry IconGeometry => SlotIcons.For(Slot);
    [System.Text.Json.Serialization.JsonIgnore]
    public string GroupTitle { get; set; } = "";
    public string Group => ProjectIO.SlotLabel(Slot);
    public string RowText => $"{Drawable} {Name} [R{Drawable:000}]  {FileSize(ModelPath)}";
    public static string FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length >= 1048576 ? $"{new FileInfo(path).Length / 1048576d:0.00} MB" : $"{new FileInfo(path).Length / 1024d:0.00} KB" : "Missing file";
    public string Display => Name.Equals($"{Slot.ToUpperInvariant()} {Drawable:000}", StringComparison.OrdinalIgnoreCase)
        ? Name : $"{Name}  /  {Slot.ToUpperInvariant()} {Drawable:000}";
    public override string ToString() => Display;
}

public sealed class ClothingProject
{
    public List<OutfitPreset> Outfits { get; set; } = [];
    public string Name { get; set; } = "New clothing pack";
    public string HistoryId { get; set; } = "";
    public BuildTarget BuildTarget { get; set; } = BuildTarget.FiveM;
    public List<TattooAsset> Tattoos { get; set; } = [];
    public string Collection { get; set; } = "my_clothes";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public List<ClothingAsset> Items { get; set; } = [];
    public string MaleYmtPath { get; set; } = "";
    public string FemaleYmtPath { get; set; } = "";
    public string MaleMetaPath { get; set; } = "";
    public string FemaleMetaPath { get; set; } = "";
    public List<string> SupplementalYmtPaths { get; set; } = [];
    public string SourceResourcePath { get; set; } = "";
    public string CodeWalkerPath { get; set; } = "";
    public string CodeWalkerCorePath { get; set; } = "";
    public string GtaFolderPath { get; set; } = "";
}

public static class ProjectIO
{
    public static readonly string[] Slots = ["head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib", "p_head", "p_eyes", "p_ears", "p_lwrist", "p_rwrist"];
    public static readonly string[] SlotLabels = ["0 Face [head]", "1 Mask [berd]", "2 Hair [hair]", "3 Arms / torso [uppr]", "4 Pants / legs [lowr]", "5 Bags / parachutes [hand]", "6 Shoes [feet]", "7 Neckwear [teef]", "8 Undershirt [accs]", "9 Armor / vest [task]", "10 Decals [decl]", "11 Tops / jackets [jbib]", "0 Hats / helmets [p_head]", "1 Glasses [p_eyes]", "2 Ear accessories [p_ears]", "6 Watches [p_lwrist]", "7 Bracelets [p_rwrist]"];
    public static string SlotLabel(string slot) => Array.IndexOf(Slots, slot) is var index && index >= 0 ? SlotLabels[index] : slot;
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly Regex DrawableName = new(@"(?:^|\^)(?<slot>p_(?:head|eyes|ears|lwrist|rwrist)|head|berd|hair|uppr|lowr|hand|feet|teef|accs|task|decl|jbib)_(?<number>\d{3,4})(?:_[urm])?(?:_\d+)?\.ydd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TextureName = new(@"(?:^|\^)(?<slot>p_(?:head|eyes|ears|lwrist|rwrist)|head|berd|hair|uppr|lowr|hand|feet|teef|accs|task|decl|jbib)_diff_(?<number>\d{3,4})_[a-z](?:_[a-z]{3})?\.ytd$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParseDrawable(string path, out string slot, out int number)
    {
        var match = DrawableName.Match(Path.GetFileName(path));
        slot = match.Groups["slot"].Value.ToLowerInvariant();
        return int.TryParse(match.Groups["number"].Value, out number) && match.Success;
    }

    public static bool TryParseTexture(string path, out string slot, out int number)
    {
        var match = TextureName.Match(Path.GetFileName(path));
        slot = match.Groups["slot"].Value.ToLowerInvariant();
        return int.TryParse(match.Groups["number"].Value, out number) && match.Success;
    }

    public static string SourceCollection(string path)
    {
        var file = Path.GetFileName(path);
        return file.Contains('^') ? file[..file.LastIndexOf('^')].ToLowerInvariant() : Path.GetDirectoryName(Path.GetFullPath(path))!.ToLowerInvariant();
    }
    public static bool IsAlternative(string path) => Regex.IsMatch(path,@"_[urm]_\d+\.ydd$",RegexOptions.IgnoreCase);
    public static string InferPed(string path) => Path.GetFileName(path).Contains("mp_f_freemode_01", StringComparison.OrdinalIgnoreCase) ? "mp_f_freemode_01" : "mp_m_freemode_01";

    public static ClothingProject Load(string path)
    {
        var project=JsonSerializer.Deserialize<ClothingProject>(File.ReadAllText(path),Json) ?? throw new InvalidDataException("Invalid project file.");
        // Project files carry assets, never permission to load executable code.
        // A missing bundled decoder must be selected locally through the picker.
        project.CodeWalkerCorePath="";
        project.CodeWalkerPath="";
        project.GtaFolderPath="";
        var baseDir=Path.GetDirectoryName(Path.GetFullPath(path))!;
        string Resolve(string value) => string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(baseDir,value));
        foreach(var item in project.Items)
        {
            item.TextureLabels=item.TextureLabels.ToDictionary(x=>Resolve(x.Key),x=>x.Value,StringComparer.OrdinalIgnoreCase);
            item.TextureSources=item.TextureSources.ToDictionary(x=>Resolve(x.Key),x=>new TextureSourceLink { SourcePath=Resolve(x.Value.SourcePath), TextureName=x.Value.TextureName, MaxDimension=x.Value.MaxDimension, CompressDiffuse=x.Value.CompressDiffuse },StringComparer.OrdinalIgnoreCase);
            item.ModelPath=Resolve(item.ModelPath);
            item.TexturePaths=item.TexturePaths.Select(Resolve).ToList();
            item.FirstPersonPath=Resolve(item.FirstPersonPath);
            item.PhysicsPath=Resolve(item.PhysicsPath);
            item.PreviewPath=Resolve(item.PreviewPath);
        }
        foreach(var tattoo in project.Tattoos) { tattoo.SourcePath=Resolve(tattoo.SourcePath); tattoo.TexturePath=Resolve(tattoo.TexturePath); }
        if(string.IsNullOrWhiteSpace(project.HistoryId)) project.HistoryId=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant())))[..32];
        project.OutputFolder=Resolve(project.OutputFolder);
        project.MaleYmtPath=Resolve(project.MaleYmtPath);
        project.FemaleYmtPath=Resolve(project.FemaleYmtPath);
        project.MaleMetaPath=Resolve(project.MaleMetaPath);
        project.FemaleMetaPath=Resolve(project.FemaleMetaPath);
        project.SupplementalYmtPaths=project.SupplementalYmtPaths.Select(Resolve).ToList();
        return project;
    }

    public static void Save(ClothingProject project, string path)
    {
        var baseDir=Path.GetDirectoryName(Path.GetFullPath(path))!;
        var portable=Path.GetFileName(baseDir).Equals("Project",StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(Path.Combine(baseDir,"..","Assets"));
        if(!portable) { WriteAtomic(path,JsonSerializer.Serialize(project,Json)); return; }
        var copy=JsonSerializer.Deserialize<ClothingProject>(JsonSerializer.Serialize(project,Json),Json)!;
        string Relative(string value)=>string.IsNullOrWhiteSpace(value)||!Path.IsPathRooted(value)
            ? value : Path.GetRelativePath(baseDir,value);
        foreach(var item in copy.Items)
        {
            item.TextureLabels=item.TextureLabels.ToDictionary(x=>Relative(x.Key),x=>x.Value,StringComparer.OrdinalIgnoreCase);
            item.TextureSources=item.TextureSources.ToDictionary(x=>Relative(x.Key),x=>new TextureSourceLink { SourcePath=Relative(x.Value.SourcePath), TextureName=x.Value.TextureName, MaxDimension=x.Value.MaxDimension, CompressDiffuse=x.Value.CompressDiffuse },StringComparer.OrdinalIgnoreCase);
            item.ModelPath=Relative(item.ModelPath);
            item.TexturePaths=item.TexturePaths.Select(Relative).ToList();
            item.FirstPersonPath=Relative(item.FirstPersonPath);
            item.PhysicsPath=Relative(item.PhysicsPath);
            item.PreviewPath=Relative(item.PreviewPath);
        }
        foreach(var tattoo in copy.Tattoos) { tattoo.SourcePath=Relative(tattoo.SourcePath); tattoo.TexturePath=Relative(tattoo.TexturePath); }
        copy.OutputFolder=Relative(copy.OutputFolder);
        copy.MaleYmtPath=Relative(copy.MaleYmtPath);
        copy.FemaleYmtPath=Relative(copy.FemaleYmtPath);
        copy.MaleMetaPath=Relative(copy.MaleMetaPath);
        copy.FemaleMetaPath=Relative(copy.FemaleMetaPath);
        copy.SupplementalYmtPaths=copy.SupplementalYmtPaths.Select(Relative).ToList();
        WriteAtomic(path,JsonSerializer.Serialize(copy,Json));
    }

    private static void WriteAtomic(string path, string text)
    {
        path = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, 1024, leaveOpen: true)) { writer.Write(text); writer.Flush(); }
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
