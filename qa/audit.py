"""Audit player archives against source, isolated client installs and native fixture saves."""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path, PurePosixPath
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
CORE = "Meitou Husbandry"
TOWN = "Stoat Hitching Post"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def tree(folder):
    return {p.relative_to(folder).as_posix(): digest(p.read_bytes()) for p in folder.rglob("*") if p.is_file()}


def payload(folder):
    data = (folder / "meitou-mods/meitou.husbandry.dat").read_bytes()
    assert struct.unpack_from("<4sii", data) == (b"MTMD", 1, 1) and data[12:21] == b"\x08hitching"
    size = struct.unpack_from("<i", data, 21)[0]
    assert len(data) == 25 + size
    doc = json.loads(data[25:])
    assert doc["Version"] == 1
    return doc["Assignments"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for arg in ("core", "play", "second", "prepare"):
        parser.add_argument(arg, type=Path)
    args = parser.parse_args()
    logs = {name: (path / "client.log").read_text(encoding="utf-8", errors="replace") for name, path in vars(args).items()}
    for name, log in logs.items():
        assert re.search(r"^HITCH QA PASS ", log, re.M), name
    assert "zero post instances" in logs["core"]
    assert "2 enabled, 2 loaded, 0 problems" in logs["play"]
    assert "two FCS placements" in logs["second"]
    members = {
        CORE: {name: ROOT / "mods" / CORE / name for name in (CORE + ".mod", "meitou.json", "maestro.json", "Meitou.Husbandry.dll")},
        TOWN: {name: ROOT / "projects" / TOWN / name for name in (TOWN + ".mod", "husbandry.json", "leveldata/zone.39.16.zone", "LICENSE.txt", "NOTICE.txt")},
    }
    members[CORE].update({"LICENSE.txt": ROOT / "LICENSE", "LICENSING.md": ROOT / "LICENSING.md"})
    packs = {}
    for name, sources in members.items():
        path = ROOT / "dist" / (name + ".zip")
        with ZipFile(path) as archive:
            assert sorted(archive.namelist()) == sorted(name + "/" + n for n in sources)
            assert archive.testzip() is None
            assert len({n.casefold() for n in archive.namelist()}) == len(archive.namelist())
            for member, source in sources.items():
                item = archive.read(str(PurePosixPath(name, member)))
                assert item == source.read_bytes(), member
                profiles = [args.play, args.second, args.prepare] + ([args.core] if name == CORE else [])
                for profile in profiles:
                    assert item == (profile / "install/mods" / name / member).read_bytes(), (profile.name, member)
                for encoding in ("utf-8", "utf-16le"):
                    text = item.decode(encoding, errors="ignore")
                    assert not re.search(r"[A-Za-z]:\\(?:Users|Meitou|NeoEcosystem)", text), (name, member)
            if name == CORE:
                manifest = json.loads(archive.read(name + "/meitou.json"))
                assert manifest["gameVersion"] == "=0.22.1" and manifest["apiVersion"] == 2
                assert manifest["version"] == "0.1.0-playtest.2"
            else:
                data = json.loads(archive.read(name + "/husbandry.json"))
                assert data["schemaVersion"] == 1 and data["posts"][0]["buildingRecordIds"] == ["1-Stoat Hitching Post.mod"]
        packs[path.name] = digest(path.read_bytes())
    original = ROOT / "saves/Two Garru outside Stoat"
    assigned = ROOT / "saves/First Garru assigned"
    assert tree(original) == tree(args.prepare / "Two Garru at Stoat")
    assert tree(assigned) == tree(args.play / "Two Garru at Stoat")
    assert payload(original) == []
    pair = payload(assigned)
    assert len(pair) == 1
    match = re.search(r"restored exact Garru (\d+); same-record sibling (\d+) free", logs["play"])
    assert match and pair[0]["Animal"]["Portrait"] == int(match[1])
    assert pair[0]["Animal"]["Handle"] is not None
    assert pair[0]["Post"] == "39.16:2-Stoat Hitching Post.mod" and pair[0]["Animal"]["Record"] == "42201-rebirth.mod"
    assert (args.play / "mods.cfg").read_text(encoding="utf-8-sig").splitlines() == [CORE + ".mod", TOWN + ".mod"]
    assert (args.core / "mods.cfg").read_text(encoding="utf-8-sig").splitlines() == [CORE + ".mod"]
    result = dict(archives=packs, readySaveFiles=len(tree(original)), assignedSaveFiles=len(tree(assigned)), restored=pair[0],
                  otherGarruPortrait=int(match[2]), profiles={k: p.name for k, p in vars(args).items()})
    (ROOT / "evidence/v0.22.1/audit.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {len(packs)} archives and both native save trees. Restored {match[1]}; {match[2]} stayed free.")


if __name__ == "__main__":
    main()
