"""Encode the native game captures from a passing single-post run."""
import argparse
import itertools
import subprocess
from pathlib import Path

CAPTIONS = {
    "assign": "Select the first Garru and assign it to the Stoat post",
    "settle": "The assigned Garru walks to its standing spot",
    "occupied": "Select the second Garru beside the occupied post",
    "check occupied": "The occupied post belongs to the first Garru",
    "handler away": "The handler and the second Garru leave",
    "direct order": "A player order takes the assigned Garru outside the radius",
    "return": "With its order finished, the Garru walks back",
    "second order": "After reload, the same Garru is assigned and accepts a new order",
    "release on return": "The player releases the Garru on its return walk",
    "released": "The post is free",
    "walk away": "The released Garru accepts another ordinary move",
    "stay released": "The Garru stays away",
}

def stamp(seconds):
    centis = round(seconds * 100)
    return f"{centis // 360000}:{centis // 6000 % 60:02}:{centis // 100 % 60:02}.{centis % 100:02}"

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trial", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    args = parser.parse_args()
    trial = args.trial.resolve()
    output = args.output.resolve()
    if "HITCH QA PASS" not in (trial / "client.log").read_text(encoding="utf-8", errors="replace"):
        raise ValueError("The in-client check must pass before composing the evidence.")
    records = []
    for line in (trial / "capture.tsv").read_text(encoding="utf-8").splitlines():
        name, tick, stage, distance = line.split("\t")
        if (trial / name).is_file() and stage in CAPTIONS:
            records.append((name, int(tick), stage))
    if not {"assign","handler away","direct order","return","second order","walk away","stay released"}.issubset(r[2] for r in records):
        raise ValueError("A recorded phase is missing.")
    concat = ["ffconcat version 1.0"]
    clock = 0.0
    spans = []
    for i, (file, tick, stage) in enumerate(records):
        gap = records[i+1][1]-tick if i+1 < len(records) else -1
        duration = min(gap/30, 0.5) if gap > 0 else 0.6
        concat.extend([f"file '{file}'", f"duration {duration:.5f}"])
        spans.append((stage,clock,clock+duration))
        clock += duration
    concat.append(f"file '{records[-1][0]}'")
    (trial/"clip.ffconcat").write_text("\n".join(concat)+"\n",encoding="utf-8")
    ass = ["[Script Info]","ScriptType: v4.00+","PlayResX: 1104","PlayResY: 770","[V4+ Styles]",
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding",
        "Style: Note,Arial,25,&H00FFFFFF,&H00FFFFFF,&H001A1512,&H00000000,0,0,0,0,100,100,0,0,1,0,0,2,10,10,10,1",
        "[Events]","Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text"]
    for stage, steps in itertools.groupby(spans, key=lambda s:s[0]):
        steps=list(steps)
        ass.append(f"Dialogue: 0,{stamp(steps[0][1])},{stamp(steps[-1][2])},Note,,0,0,0,,{CAPTIONS[stage]}")
    (trial/"captions.ass").write_text("\n".join(ass)+"\n",encoding="utf-8")
    subprocess.run([args.ffmpeg,"-hide_banner","-loglevel","error","-y","-f","concat","-safe","0","-i","clip.ffconcat",
        "-vf","pad=iw:ih+50:0:0:0x171b20,subtitles=captions.ass","-r","24","-an","-c:v","libx264",
        "-pix_fmt","yuv420p","-threads","2","-crf","20","-movflags","+faststart",str(output)],check=True,cwd=trial)
    print(f"{len(records)} captured game frames, {clock:.1f} seconds: {output}")
if __name__=="__main__":
    main()
