using System.Numerics;
using Meitou.Content;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.World;

const string town = "Stoat Hitching Post";
const string provider = "Meitou Husbandry";
const string visit = "Husbandry Field Visit";
const string stockPart = "18914-small_changes_otto.mod";
const string garru = "42201-rebirth.mod";

var install = GameInstall.Locate() ?? throw new Exception("Set KENSHI_PATH.");
var db = GameDatabase.Load(LoadOrder.FromInstall(install));
var part = db.Find(stockPart) ?? throw new Exception("Stock pole part missing.");
var wanderer = db.Find("1533662-rebirth.mod");
Console.WriteLine($"Stock part {part.Name}: {string.Join(", ", part.Filenames)}. Human template: {wanderer?.Name} {wanderer?.Type}");
if (args is ["verify", var folder])
{
    var header = FcsReader.ReadFile(Path.Combine(folder, town + ".mod"));
    var zone = LevelFile.ReadFile(Path.Combine(folder, "leveldata", "zone.39.16.zone")).Data;
    if (!header.Dependencies.SequenceEqual([provider + ".mod"]) || header.Records.Single().StringId != "1-" + town + ".mod" ||
        zone.Records.Single().Instances.Single().Target != header.Records.Single().StringId) throw new InvalidDataException();
    Console.WriteLine("Verified sole dependency, town building and placed instance."); return;
}
if (args is ["second-post"])
{
    const string overlay = "Husbandry Second Post QA";
    var f = FileFor("Second physical copy of the Stoat building for independent occupancy checks.", 2);
    f.Dependencies.Add(town + ".mod");
    WriteNew($"qa/fixtures/{overlay}/{overlay}.mod", f);
    var zone = FileFor("Additional placement of the original town building", 2);
    var listTwo = Add(zone, FcsRecordType.InstanceCollection, "0", WorldLevelData.BuildingListId);
    listTwo.Flags = 1;
    listTwo.Instances.Add(new FcsInstance
    {
        Id = "1-" + overlay + ".mod",
        Target = "1-" + town + ".mod",
        Position = new(35154, 0, -70668),
        Rotation = Quaternion.Identity
    });
    WriteNew($"qa/fixtures/{overlay}/leveldata/zone.39.16.zone", zone);
    return;
}
if (args.Length != 0) throw new ArgumentException("Use no arguments to seed or verify <town-folder>.");
static FcsFile FileFor(string description, int next) => new() { Author = "Meitou Husbandry contributors", Description = description, NextId = next };
static FcsRecord Add(FcsFile file, FcsRecordType type, string name, string id)
{
    var record = new FcsRecord { ByteSize = 1, RecordType = type, Name = name, StringId = id };
    file.Records.Add(record); return record;
}
static void WriteNew(string path, FcsFile file)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    if (!File.Exists(path)) FcsWriter.WriteFile(file, path);
}
WriteNew($"mods/{provider}/{provider}.mod", FileFor("Optional individual animal hitching framework.", 1));
var buildingFile = FileFor("An inert hitching post outside the gate of Stoat.", 3);
var pole = Add(buildingFile, FcsRecordType.Building, town, "1-" + town + ".mod");
pole.References["parts"] = [new(stockPart, 0, 100, 0)];
WriteNew($"projects/{town}/{town}.mod", buildingFile);
var zoneFile = FileFor("One placed post", 3);
var list = Add(zoneFile, FcsRecordType.InstanceCollection, "0", WorldLevelData.BuildingListId);
list.Flags = 1;
list.Instances.Add(new() { Id = "2-" + town + ".mod", Target = pole.StringId, Position = new(35071, 0, -70668), Rotation = Quaternion.Identity });
WriteNew($"projects/{town}/leveldata/zone.39.16.zone", zoneFile);
var startFile = FileFor("QA start for making a portable two-Garru save at Stoat.", 3);
var squad = Add(startFile, FcsRecordType.SquadTemplate, "Husbandry field party", "1-" + visit + ".mod");
squad.Bools["dont multiply"] = true; squad.Floats["animal age random"] = 0;
if (wanderer?.Type != FcsRecordType.Character) throw new Exception("Wrong vanilla handler.");
squad.References["leader"] = [new(wanderer.StringId, 1, 0, 0)];
squad.References["animals"] = [new(garru, 2, 2, 110)];
var start = Add(startFile, FcsRecordType.NewGameStartoff, visit, "2-" + visit + ".mod");
start.Bools["force start pos"] = true; start.Ints["start pos X"] = 35098; start.Ints["start pos Z"] = -70650;
start.Strings["description"] = "One handler and two stock Garru outside Stoat."; start.Strings["style"] = "Field trial";
start.References["squad"] = [new(squad.StringId, 1, 0, 0)];
WriteNew($"qa/starter/{visit}/{visit}.mod", startFile);
