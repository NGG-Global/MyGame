#!/usr/bin/env python3
"""Create missing Unity .meta files for paths under Assets/.

Cloud sessions have no Unity Editor, so every new file or folder under Assets/
needs a .meta written by hand. This script does that with a fresh random GUID
and the importer block Unity uses for the asset type.

Usage:
    python3 tools/unity_meta.py Assets/_Game/Scripts/Runtime/Foo.cs [more paths...]
    python3 tools/unity_meta.py --check        # list assets under Assets/ with no .meta,
                                               # orphan .meta files and duplicate GUIDs
    python3 tools/unity_meta.py --guid-only Assets/.../Model.fbx
        # for a type with a type-specific importer (models, images, audio): a .meta with
        # only the GUID, no importer block. Unity adds its default importer settings on
        # first import and keeps the GUID, so every machine and CI share it.

Existing .meta files are never modified, so existing GUIDs never change.
Folders are processed parent-first, so passing a file also creates metas for
any new parent folders below Assets/.
"""
import os
import re
import sys
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

MONO = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

ASMDEF = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

TEXT = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

NATIVE = """fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

DEFAULT = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

BY_EXT = {
    ".cs": MONO,
    ".asmdef": ASMDEF,
    ".asmref": ASMDEF.replace("AssemblyDefinitionImporter", "AssemblyDefinitionReferenceImporter"),
    ".txt": TEXT, ".json": TEXT, ".md": TEXT, ".xml": TEXT, ".csv": TEXT, ".bytes": TEXT,
    # ScriptableObject assets created as YAML. mainObjectFileID 11400000 is
    # Unity's fixed fileID for a MonoBehaviour/ScriptableObject main object.
    ".asset": NATIVE,
}

# Images, audio, models, fonts, scenes and prefabs have large type-specific
# importer blocks. Do not hand-write those: let the Editor or a CI run create
# them, or generate the asset from code. This script refuses them.
REFUSE = {".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".exr", ".hdr",
          ".wav", ".mp3", ".ogg", ".fbx", ".obj", ".blend", ".ttf", ".otf",
          ".unity", ".prefab", ".mat", ".anim", ".controller", ".shader"}


def is_hidden(path):
    rel = os.path.relpath(path, ROOT)
    return any(p.startswith(".") or p.endswith("~") for p in rel.split(os.sep))


def new_guid(existing):
    while True:
        g = uuid.uuid4().hex  # 32 lowercase hex chars
        if g not in existing:
            existing.add(g)
            return g


def all_guids():
    guids = {}
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        for f in filenames:
            if f.endswith(".meta"):
                p = os.path.join(dirpath, f)
                with open(p, encoding="utf-8") as fh:
                    m = re.search(r"^guid: ([0-9a-f]{32})\s*$", fh.read(), re.M)
                if m:
                    guids.setdefault(m.group(1), []).append(p)
    return guids


GUID_ONLY_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
"""

GUID_ONLY_ALLOWED = {".fbx", ".obj", ".blend", ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".exr", ".hdr",
                     ".wav", ".mp3", ".ogg", ".ttf", ".otf"}


def write_meta(path, existing, guid_only=False):
    meta = path + ".meta"
    if os.path.exists(meta):
        return False
    if os.path.isdir(path):
        template = FOLDER
    else:
        ext = os.path.splitext(path)[1].lower()
        if guid_only and ext in GUID_ONLY_ALLOWED:
            template = GUID_ONLY_TEMPLATE
        elif ext in REFUSE:
            raise SystemExit(f"Refusing to hand-write a .meta for {path}: type-specific importer. "
                             "Use --guid-only for a GUID-only .meta, or see CLAUDE.md.")
        else:
            template = BY_EXT.get(ext, DEFAULT)
    with open(meta, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(template.format(guid=new_guid(existing)))
    print("created", os.path.relpath(meta, ROOT))
    return True


def ensure(path, existing, guid_only=False):
    path = os.path.abspath(path)
    if not path.startswith(ASSETS + os.sep):
        raise SystemExit(f"{path} is not under Assets/")
    if is_hidden(path):
        return
    chain, p = [], path
    while p != ASSETS:
        chain.append(p)
        p = os.path.dirname(p)
    for p in reversed(chain):
        write_meta(p, existing, guid_only and p == path)


def check():
    problems = 0
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = [d for d in dirnames if not d.startswith(".") and not d.endswith("~")]
        for name in dirnames + [f for f in filenames if not f.endswith(".meta") and not f.startswith(".")]:
            p = os.path.join(dirpath, name)
            if not os.path.exists(p + ".meta"):
                print("missing meta:", os.path.relpath(p, ROOT)); problems += 1
        for f in filenames:
            if f.endswith(".meta") and not os.path.exists(os.path.join(dirpath, f[:-5])):
                print("orphan meta:", os.path.relpath(os.path.join(dirpath, f), ROOT)); problems += 1
    for g, paths in all_guids().items():
        if len(paths) > 1:
            print("duplicate guid", g, [os.path.relpath(p, ROOT) for p in paths]); problems += 1
    print("OK" if problems == 0 else f"{problems} problem(s)")
    return problems


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print(__doc__); sys.exit(1)
    if args == ["--check"]:
        sys.exit(1 if check() else 0)
    guid_only = "--guid-only" in args
    existing = set(all_guids())
    for a in args:
        if a != "--guid-only":
            ensure(a, existing, guid_only)
