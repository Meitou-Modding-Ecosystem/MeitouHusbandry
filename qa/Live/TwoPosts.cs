using System.Numerics;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Simulation;

internal static partial class StartupHook
{
    static void TwoPosts(object host, WorldSession session, object system, object[] posts, CharacterSnapshot a, CharacterSnapshot b)
    {
        Must(posts.Length == 2, "Expected two physical instances of the same building.");
        var sorted = posts.OrderBy(p => ((Vector3)Property(p, "Stand")!).X).ToArray();
        var firstKey = (string)Property(sorted[0], "Key")!; var secondKey = (string)Property(sorted[1], "Key")!;
        Must(firstKey != secondKey && Equals(Property(sorted[0], "Name"), Property(sorted[1], "Name")), "distinct test placements missing");
        var target = (Vector3)Property(sorted[1], "Stand")!;
        var world = session.World;
        var current = session.CurrentSnapshot;
        object? Status(CharacterSnapshot who) => AccessTools.Method(system.GetType(), "Lookup").Invoke(system, [who.Id]);
        string? Assigned(CharacterSnapshot who) => Status(who) is { } s ? (string)Property(s, "PostKey")! : null;
        void Select(CharacterSnapshot who) => world.Commands.Enqueue(new SelectCommand([who.Id]) { Tick = world.Tick });
        void Move(CharacterSnapshot who, Vector3 at) => world.Commands.Enqueue(new MoveOrder([who.Id], at) { Tick = world.Tick });
        switch (stage)
        {
            case "start":
                var basePoint = (Vector3)Property(sorted[0], "Base")!;
                session.Camera.Place(new(basePoint.X + 42, basePoint.Z), 1.25f, .43f, 185);
                Select(a); Change("assign first instance", current.Tick); break;
            case "assign first instance":
                if (!a.Selected) break;
                Action(host, "Assign to nearby hitching post"); Change("send sibling to second", current.Tick); break;
            case "send sibling to second":
                if (Assigned(a) != firstKey) break;
                Select(b); Move(b, target); Change("at second", current.Tick); break;
            case "at second":
                if (!b.Selected || Flat(b.Position, target) > 8) break;
                Action(host, "Assign to nearby hitching post"); Change("both assigned", current.Tick); break;
            case "both assigned":
                if (Assigned(b) != secondKey) break;
                Must(Assigned(a) == firstKey, "assigning B displaced A");
                Log($"Two instances: {a.PortraitSerial} at {firstKey}; {b.PortraitSerial} at {secondKey}");
                Shot(host, "two-occupied-posts"); Select(a); Change("release only first", current.Tick); break;
            case "release only first":
                if (!a.Selected) break;
                Action(host, "Release from hitching post"); Change("independent release", current.Tick); break;
            case "independent release":
                if (Assigned(a) != null) break;
                Must(Assigned(b) == secondKey, "releasing A evicted B");
                End(true, "two FCS placements of one BUILDING supported independent occupancies and independent release"); break;
        }
    }
}
