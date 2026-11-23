"""Verify the installed Linux release used for the authoring capture against its downloaded archive."""
import argparse
import hashlib
import tarfile
from pathlib import Path, PurePosixPath

def digest(f):
    return hashlib.file_digest(f, "sha256").hexdigest()

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("archive",type=Path)
    ap.add_argument("extracted_parent",type=Path)
    ap.add_argument("sha256_file",type=Path)
    args=ap.parse_args()
    checks={line[66:].strip():line[:64].lower() for line in args.sha256_file.read_text().splitlines()}
    with args.archive.open("rb") as f: actual=digest(f)
    assert checks[args.archive.name]==actual
    count=0
    with tarfile.open(args.archive,"r:gz") as tar:
        for entry in tar:
            if not entry.isfile(): continue
            path=PurePosixPath(entry.name)
            if path.is_absolute() or ".." in path.parts: raise ValueError(entry.name)
            with (args.extracted_parent/path).open("rb") as installed, tar.extractfile(entry) as shipped:
                assert digest(installed)==digest(shipped),entry.name
            count+=1
    print(Path("/etc/os-release").read_text().split('PRETTY_NAME=')[1].splitlines()[0].strip('"'))
    print(f"{args.archive.name} {actual}; {count} installed files matched the release.")
if __name__=="__main__": main()
