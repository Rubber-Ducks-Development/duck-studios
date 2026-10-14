using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using N=System.Numerics;

namespace DuckStudio;

public sealed record RigBone(string Name,int Index,int Parent,N.Matrix4x4 Local);
public sealed class SkinMesh
{
    public MeshGeometry3D Mesh { get; }
    public Point3D[] Positions { get; }
    public Vector3D[] Normals { get; }
    public RigBone[] Bones { get; }
    public int[] Palette { get; }
    public int[] Indices { get; }
    public float[] Weights { get; }
    public SkinMesh(MeshGeometry3D mesh,RigBone[] bones,int[] palette,int[] indices,float[] weights)
    {Mesh=mesh;Positions=mesh.Positions.ToArray();Normals=mesh.Normals.ToArray();Bones=bones;Palette=palette;Indices=indices;Weights=weights;}
    public static RigBone[] ReadBones(JsonElement element)
    {
        if(element.ValueKind!=JsonValueKind.Array)return [];
        return element.EnumerateArray().Select(b=>
        {
            float[] Values(string field)=>b.GetProperty(field).EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            var t=Values("translation");var r=Values("rotation");var s=Values("scale");
            var q=new N.Quaternion(r[0],r[1],r[2],r[3]);if(q.LengthSquared()<.001f)q=N.Quaternion.Identity;else q=N.Quaternion.Normalize(q);
            var local=N.Matrix4x4.CreateScale(s[0],s[1],s[2])*N.Matrix4x4.CreateFromQuaternion(q)*N.Matrix4x4.CreateTranslation(t[0],t[1],t[2]);
            return new RigBone(b.GetProperty("name").GetString()??"",b.GetProperty("index").GetInt32(),b.GetProperty("parent").GetInt32(),local);
        }).ToArray();
    }
}
public static class PreviewPoses
{
    public static readonly DependencyProperty SkinProperty=DependencyProperty.RegisterAttached("Skin",typeof(SkinMesh),typeof(PreviewPoses));
    public static readonly string[] Names=["Standing","Walking","Running","Sitting","Aiming"];
    public static IEnumerable<GeometryModel3D> Geometry(Model3D model)
    {
        if(model is GeometryModel3D geometry)yield return geometry;
        if(model is Model3DGroup group)foreach(var child in group.Children)foreach(var item in Geometry(child))yield return item;
    }
    public static bool CanAnimate(Model3D model)=>Geometry(model).Any(g=>g.GetValue(SkinProperty) is SkinMesh {Bones.Length:>0,Weights.Length:>0});
    public static void Apply(Model3D model,string pose,double seconds)
    {
        // Fitting poses use the actual rig and skin weights, with procedural joint
        // rotations. Exact game animations and cloth simulation are tested in FiveM.
        var transforms=new Dictionary<RigBone[],N.Matrix4x4[]>();
        foreach(var geometry in Geometry(model))
        {
            if(geometry.GetValue(SkinProperty) is not SkinMesh skin)continue;
            if(pose=="Standing"){skin.Mesh.Positions=new Point3DCollection(skin.Positions);skin.Mesh.Normals=new Vector3DCollection(skin.Normals);continue;}
            if(skin.Bones.Length==0||skin.Indices.Length!=skin.Positions.Length*4||skin.Weights.Length!=skin.Indices.Length)continue;
            if(!transforms.TryGetValue(skin.Bones,out var matrices)){matrices=Transforms(skin.Bones,pose,seconds);transforms[skin.Bones]=matrices;}
            var positions=new Point3DCollection(skin.Positions.Length);var normals=new Vector3DCollection(skin.Normals.Length);
            for(var i=0;i<skin.Positions.Length;i++)
            {
                var p=skin.Positions[i];var source=new N.Vector3((float)p.X,(float)-p.Z,(float)p.Y);var result=N.Vector3.Zero;var normal=N.Vector3.Zero;float total=0;
                var n=i<skin.Normals.Length?skin.Normals[i]:new Vector3D(0,1,0);var sourceNormal=new N.Vector3((float)n.X,(float)-n.Z,(float)n.Y);
                for(var j=0;j<4;j++)
                {
                    var weight=skin.Weights[i*4+j];if(weight<=0)continue;var local=skin.Indices[i*4+j];var index=skin.Palette.Length>0?skin.Palette.ElementAtOrDefault(local):local;
                    if(index<0||index>=matrices.Length)continue;result+=N.Vector3.Transform(source,matrices[index])*weight;normal+=N.Vector3.TransformNormal(sourceNormal,matrices[index])*weight;total+=weight;
                }
                if(total<=.001f){result=source;normal=sourceNormal;}else{result/=total;if(normal.LengthSquared()>.00001f)normal=N.Vector3.Normalize(normal);}
                positions.Add(new Point3D(result.X,result.Z,-result.Y));if(skin.Normals.Length>0)normals.Add(new Vector3D(normal.X,normal.Z,-normal.Y));
            }
            skin.Mesh.Positions=positions;if(normals.Count==positions.Count)skin.Mesh.Normals=normals;
        }
    }
    public static N.Matrix4x4[] BindTransforms(RigBone[] bones)
    {
        var size=bones.Length==0?0:bones.Max(b=>b.Index)+1;var result=Enumerable.Repeat(N.Matrix4x4.Identity,size).ToArray();var done=new HashSet<int>();var visiting=new HashSet<int>();var lookup=bones.ToDictionary(b=>b.Index);
        N.Matrix4x4 Resolve(int index)
        {
            if(done.Contains(index))return result[index];if(!lookup.TryGetValue(index,out var bone)||!visiting.Add(index))return N.Matrix4x4.Identity;
            var world=bone.Local;if(bone.Parent>=0&&bone.Parent!=index)world*=Resolve(bone.Parent);visiting.Remove(index);done.Add(index);return result[index]=world;
        }
        foreach(var bone in bones)Resolve(bone.Index);return result;
    }
    public static N.Matrix4x4[] Transforms(RigBone[] bones,string pose,double seconds)
    {
        var bind=BindTransforms(bones);var skins=Enumerable.Repeat(N.Matrix4x4.Identity,bind.Length).ToArray();var done=new HashSet<int>();var lookup=bones.ToDictionary(b=>b.Index);var visiting=new HashSet<int>();
        N.Matrix4x4 Resolve(int index)
        {
            if(done.Contains(index))return skins[index];if(!lookup.TryGetValue(index,out var bone)||!visiting.Add(index))return N.Matrix4x4.Identity;
            var name=bone.Name.ToLowerInvariant();var left=name.Contains("_l_");var right=name.Contains("_r_");var side=left?1:right?-1:0;var phase=Math.Sin(seconds*(pose=="Running"?10:6))*side;
            double x=0,z=0;
            if(pose is "Walking" or "Running")
            {
                if(name.EndsWith("thigh"))x=phase*(pose=="Running"?48:28);
                if(name.EndsWith("calf"))x=Math.Max(0,-phase)*(pose=="Running"?70:35);
                if(name.EndsWith("upperarm")){z=side*30;x=-phase*(pose=="Running"?45:24);}
                if(name.EndsWith("forearm"))x=pose=="Running"?-65:-15;
            }
            if(pose=="Sitting"){if(name.EndsWith("thigh"))x=-85;if(name.EndsWith("calf"))x=85;if(name.EndsWith("upperarm"))z=side*40;if(name.EndsWith("forearm"))x=-35;}
            if(pose=="Aiming"){if(name.EndsWith("upperarm")){x=-65;z=side*18;}if(name.EndsWith("forearm"))x=-25;}
            var pivot=new N.Vector3(bind[index].M41,bind[index].M42,bind[index].M43);
            var rotation=N.Matrix4x4.CreateRotationX((float)(x*Math.PI/180))*N.Matrix4x4.CreateRotationZ((float)(z*Math.PI/180));
            if(name.EndsWith("upperarm"))
            {
                var forearm=bones.FirstOrDefault(b=>b.Name.Equals(left?"SKEL_L_Forearm":"SKEL_R_Forearm",StringComparison.OrdinalIgnoreCase));
                if(forearm!=null)
                {
                    var start=new N.Vector3(bind[forearm.Index].M41,bind[forearm.Index].M42,bind[forearm.Index].M43)-pivot;
                    var goal=pose=="Aiming"?new N.Vector3(-side*.08f,-1,0):pose=="Sitting"?new N.Vector3(side*.12f,-.2f,-1):new N.Vector3(side*.12f,(float)(phase*(pose=="Running"?.9:.5)),-1);
                    if(start.LengthSquared()>.000001f){start=N.Vector3.Normalize(start);goal=N.Vector3.Normalize(goal);var axis=N.Vector3.Cross(start,goal);var dot=Math.Clamp(N.Vector3.Dot(start,goal),-1,1);if(axis.LengthSquared()>.000001f)rotation=N.Matrix4x4.CreateFromAxisAngle(N.Vector3.Normalize(axis),(float)Math.Acos(dot));}
                }
            }
            if(pose=="Aiming"&&name.EndsWith("forearm"))rotation=N.Matrix4x4.Identity;
            var matrix=N.Matrix4x4.CreateTranslation(-pivot)*rotation*N.Matrix4x4.CreateTranslation(pivot);
            if(bone.Parent>=0&&bone.Parent!=index&&bone.Parent<skins.Length)matrix*=Resolve(bone.Parent);visiting.Remove(index);done.Add(index);return skins[index]=matrix;
        }
        foreach(var bone in bones)Resolve(bone.Index);return skins;
    }
}
