using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meitou.Husbandry;

/// <summary>Immutable assignment state from the last completed Husbandry slow-world update.</summary>
public readonly record struct HitchStatus(string PostKey, string PostName, bool AtPost);

public interface IHusbandryApi
{
    /// <summary>Returns the supplied world's assignment snapshot, or null when this animal has none.</summary>
    HitchStatus? GetHitch(World world, CharacterId animal);
}

public sealed class HusbandryMod : IMod, IHusbandryApi
{
    public const string AssignLabel = "Assign to nearby hitching post";
    public const string ReleaseLabel = "Release from hitching post";

    public HitchStatus? GetHitch(World world, CharacterId animal) =>
        world.Systems.OfType<HitchSystem>().FirstOrDefault()?.Lookup(animal);

    public void Initialize(ModContext c)
    {
        var fits = PostPacks.Load(c.Data, c.Log, c.LogError);
        c.AddSystem("hitching", ctx => new HitchSystem(PostPacks.Placements(fits, ctx.Data), c.Log), SystemAnchor.End);
        if (fits.Count == 0) return;
        c.AddDebugCommand(AssignLabel, row => Queue(row, true));
        c.AddDebugCommand(ReleaseLabel, row => Queue(row, false));
    }

    private static string Queue(DebugCommandContext row, bool assign)
    {
        if (row.Selected.Count != 1) return "Select one squad animal.";
        row.Enqueue(new HitchCommand(row.Selected[0], assign) { Tick = row.Tick });
        return assign ? "Assign queued." : "Release queued.";
    }
}

internal sealed record HitchCommand(CharacterId Animal, bool Assign) : SimCommand;
internal sealed record Post(string Key, string Name, Vector3 Base, Vector3 Stand, float Radius);
internal sealed record Fit(Vector3 Offset, float Radius);

internal static class PostPacks
{
    internal sealed record Entry(string Id, string[] BuildingRecordIds, float[] StandingOffset, float Radius);
    internal sealed record Pack(int SchemaVersion, string PackId, Entry[] Posts);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static Dictionary<string, Fit> Load(GameDatabase data, Action<string> log, Action<string> error)
    {
        var fits = new Dictionary<string, Fit>(StringComparer.Ordinal);
        var install = GameInstall.Locate();
        if (install == null) return fits;
        var paths = ModList.FindMods(install);
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in data.Files)
        {
            if (!paths.TryGetValue(Path.GetFileNameWithoutExtension(name), out var mod) || LoadOrder.BaseFiles.Contains(name)) continue;
            var file = Path.Combine(Path.GetDirectoryName(mod)!, "husbandry.json");
            if (!File.Exists(file)) continue;
            try
            {
                var pack = JsonSerializer.Deserialize<Pack>(File.ReadAllBytes(file), Json);
                if (pack is not { SchemaVersion: 1, Posts: not null } || string.IsNullOrWhiteSpace(pack.PackId) || owners.Contains(pack.PackId))
                    throw new InvalidDataException("Expected schema 1 and a unique pack ID.");
                var local = new Dictionary<string, Fit>(StringComparer.Ordinal);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in pack.Posts)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.Id) || !e.Id.StartsWith(pack.PackId + ".", StringComparison.Ordinal) ||
                        !ids.Add(e.Id) || e.BuildingRecordIds is not { Length: > 0 } || e.StandingOffset is not [var x, var y, var z] ||
                        !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(e.Radius) || e.Radius <= 0)
                        throw new InvalidDataException("Invalid post ID, building list, offset or radius.");
                    foreach (var id in e.BuildingRecordIds)
                        if (string.IsNullOrEmpty(id) || data.Find(id)?.Type != FcsRecordType.Building || !local.TryAdd(id, new(new(x, y, z), e.Radius)))
                            throw new InvalidDataException($"Unknown or repeated building {id}.");
                }
                foreach (var (id, fit) in local) fits[id] = fit;
                owners.Add(pack.PackId);
                log($"{name} defines {local.Count} hitching post building type(s).");
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            { error($"Skipped {file}: {ex.Message}"); }
        }
        return fits;
    }

    public static Post[] Placements(Dictionary<string, Fit> fits, Meitou.Simulation.Population.PopulationData data) =>
        data.Sites.SelectMany(s => s.Buildings).Where(b => !b.Destroyed && fits.ContainsKey(b.BuildingId))
            .DistinctBy(b => b.Key).OrderBy(b => b.Key, StringComparer.Ordinal).Select(b =>
            {
                var fit = fits[b.BuildingId];
                var at = b.Position + Vector3.Transform(fit.Offset, Quaternion.CreateFromAxisAngle(Vector3.UnitY, b.Yaw));
                return new Post(b.Key, b.Name, b.Position, at, fit.Radius);
            }).ToArray();
}
