"""SAKURA～설월화～ 한글패치 배포본 / 누적 업데이트 제작 도구.

게임 폴더는 읽기만 한다. 세이브·설정·음성·음악 등 본편 자료는 수집하지 않는다.
수집 대상은 managed_files.json 에 적힌 게임 파일과 payload 폴더(실행기·엔진·설명서)뿐이다.

    lists     본문·이미지 매니페스트에서 관리 파일 목록(managed_files.json)을 만든다(처음 한 번)
    compile   실행기와 업데이트 엔진을 컴파일한다
    test      업데이트 엔진의 오프라인 시험을 돌린다
    full      첫 배포본을 만든다          예: build_release.py full --version 1.0.0
    update    누적 업데이트를 만든다      예: build_release.py update --version 1.0.1 --notes "오탈자 수정"

Python 은 Codex 런타임의 python.exe 를 -X utf8 -B 로 실행한다.
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
GAME = Path(r"D:\ＳＡＫＵＲＡ～雪月華～")
PAYLOAD = HERE / "payload"
BUILD = HERE / "build"
BASE = HERE / "baseline.json"
HISTORY = HERE / "history"
OUT = HERE / "release"
LISTS = HERE / "managed_files.json"
APP_ID = "sakura-ko"
REPOSITORY = "adios1579-lgtm/Sakura-Korean-Patch"
LAUNCHER = "SAKURA_한글판.exe"
MANUAL = "한글패치_업데이트.exe"
DLL = "updates/SakuraUpdater.dll"
# 실행 중인 실행기는 자기 자신을 덮어쓸 수 없고, 저장소 주소는 고정 계약이다. 업데이트로 바꾸지 않는다.
UNMANAGED = {LAUNCHER, MANUAL, "updates/settings.json"}
# 한글패치가 교체하는 최상위 파일
ROOT_FILES = ["SAKURA.EXE", "SkrKR034.ttf"]
# 본편(이용자가 준비)이 있는지 확인할 원본 파일. 한글패치가 바꾸지 않는 작은 파일만 고른다.
BASE_FILES = [
    "AdvData/MES/main.mes", "AdvData/MES/LOAD.mes", "AdvData/MES/SAVE.mes", "AdvData/MES/OPTION.mes",
    "AdvData/DAT/names.dat", "AdvData/GRP/BG/bg001a.CRX", "AdvData/GRP/bst/BT1001.CRX",
    "AdvData/MUSIC/SAKURA_01.pcm", "AdvData/SE/SE0001.pcm",
]
# 관리 파일 목록의 근거(첫 배포 때 게임 폴더가 검증된 상태인지 대조하는 데 쓴다)
CODEX = Path(r"C:\Users\Admin\Documents\Codex\2026-09-23\codex-1-2-3-4-codex\work")
TEXT_MANIFEST = CODEX / "review_integration" / "build_manifest.json"
IMAGE_MANIFEST = CODEX / "image_localization" / "final_manifest.json"
PYDEPS = r"C:\Users\Admin\Documents\Codex\2026-08-06\new-chat-6\work\pydeps"
ALLOWED = re.compile(r"^(SAKURA\.EXE|SkrKR034\.ttf|AdvData/MES/[A-Za-z0-9_]+\.mes|AdvData/GRP/(battle|BG|bst|SYSTEM)/[A-Za-z0-9_]+\.CRX)$", re.I)


def digest(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(4 * 1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def dump(p, value):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2), "utf8")


def load(p):
    return json.loads(p.read_text("utf8"))


def version(v):
    assert re.fullmatch(r"\d+\.\d+\.\d+", v or ""), "버전은 예: 1.0.1"
    return tuple(map(int, v.split(".")))


def game_relative(path):
    """매니페스트의 절대 경로를 게임 폴더 기준 상대 경로(실제 대소문자)로 바꾼다."""
    parts = Path(path).parts
    i = [x.lower() for x in parts].index("advdata")
    rel = Path(*parts[i:])
    real = GAME
    for part in rel.parts:  # 디스크의 실제 대소문자를 따른다
        match = [c.name for c in real.iterdir() if c.name.lower() == part.lower()]
        assert len(match) == 1, "게임 폴더에 없음: " + str(rel)
        real = real / match[0]
    return real.relative_to(GAME).as_posix()


def lists():
    """본문 219개와 이미지 230개의 목록을 만들고, 게임 폴더의 파일이 검증된 산출물과 같은지 대조한다."""
    text = load(TEXT_MANIFEST)
    images = load(IMAGE_MANIFEST)
    files, mismatch = [], []
    for row in text["files"]:
        rel = game_relative(row["output"])
        key = next((k for k in ("output_sha256", "sha256", "final_sha256") if k in row), None)
        want = row[key] if key else digest(row["output"])
        if digest(GAME / rel) != want:
            mismatch.append(rel)
        files.append(rel)
    for row in images:
        rel = game_relative(row["output"])
        if digest(GAME / rel) != row["output_sha256"]:
            mismatch.append(rel)
        files.append(rel)
    assert not mismatch, "게임 폴더의 파일이 검증된 산출물과 다르다: %s" % mismatch[:10]
    files = sorted(set(files), key=str.lower)
    for n in ROOT_FILES + files:
        assert ALLOWED.match(n), "허용 범위 밖: " + n
        assert (GAME / n).is_file(), "없음: " + n
    dump(LISTS, {"note": "한글패치가 교체하는 게임 파일. 세이브·음성·음악 등은 넣지 않는다.", "root": ROOT_FILES, "game_files": files})
    print("관리 파일 목록: 최상위 %d, 본문 %d, 이미지 %d (게임 폴더와 검증 산출물 일치)" % (
        len(ROOT_FILES), sum(n.lower().startswith("advdata/mes/") for n in files), sum(n.lower().startswith("advdata/grp/") for n in files)))


def extract_icon(exe, ico):
    """게임 실행 파일의 아이콘 자원을 그대로 ICO로 묶는다(픽셀을 다시 만들지 않는다)."""
    sys.path.insert(0, PYDEPS)
    import pefile
    pe = pefile.PE(str(exe))
    resources = {e.id: e for e in pe.DIRECTORY_ENTRY_RESOURCE.entries}
    group = resources[14].directory.entries[0].directory.entries[0].data.struct
    raw = pe.get_data(group.OffsetToData, group.Size)
    count = struct.unpack_from("<H", raw, 4)[0]
    icons = {e.id: e.directory.entries[0].data.struct for e in resources[3].directory.entries}
    entries, images, offset = [], [], 6 + 16 * count
    for i in range(count):
        row = raw[6 + 14 * i:20 + 14 * i]
        r = icons[struct.unpack_from("<H", row, 12)[0]]
        data = pe.get_data(r.OffsetToData, r.Size)
        entries.append(row[:8] + struct.pack("<II", len(data), offset))
        images.append(data)
        offset += len(data)
    pe.close()
    ico.write_bytes(raw[:6] + b"".join(entries) + b"".join(images))


def csc():
    c = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework/v4.0.30319/csc.exe"
    assert c.exists(), "Windows .NET 컴파일러 없음"
    return str(c)


REFS = ["System.Windows.Forms", "System.Drawing", "System.Web.Extensions", "System.IO.Compression", "System.IO.Compression.FileSystem"]


def compile_tools(force_launcher=False):
    BUILD.mkdir(exist_ok=True)
    (PAYLOAD / "updates").mkdir(parents=True, exist_ok=True)
    ico = BUILD / "sakura.ico"
    extract_icon(GAME / "SAKURA.EXE", ico)
    exe = BUILD / "SakuraLauncher.exe"
    subprocess.run([csc(), "/nologo", "/target:winexe", "/out:" + str(exe), "/win32icon:" + str(ico), "/r:System.Windows.Forms.dll", str(HERE / "src/Bootstrap.cs")], check=True)
    if BASE.exists() and not force_launcher:
        print("이미 배포한 실행기는 고정 계약이라 payload 의 실행기는 바꾸지 않는다.")
    else:
        shutil.copyfile(exe, PAYLOAD / LAUNCHER)
        shutil.copyfile(exe, PAYLOAD / MANUAL)
    subprocess.run([csc(), "/nologo", "/target:library", "/out:" + str(PAYLOAD / DLL), *["/r:%s.dll" % x for x in REFS], str(HERE / "src/Updater.cs")], check=True)
    dump(PAYLOAD / "updates/settings.json", {"app_id": APP_ID, "repository": REPOSITORY})
    print("컴파일 완료:", LAUNCHER, MANUAL, DLL)


def test():
    BUILD.mkdir(exist_ok=True)
    dll, exe = BUILD / "SakuraUpdater.dll", BUILD / "UpdaterTests.exe"
    shutil.copyfile(PAYLOAD / DLL, dll)
    subprocess.run([csc(), "/nologo", "/target:exe", "/out:" + str(exe), "/r:" + str(dll), *["/r:%s.dll" % x for x in REFS], str(HERE / "src/UpdaterTests.cs")], check=True)
    r = subprocess.run([str(exe)], capture_output=True, encoding="utf8", errors="replace")
    (HERE / "tests-result.txt").write_text(r.stdout + r.stderr, "utf8")
    print(r.stdout[-1500:], r.stderr[-1500:])
    assert r.returncode == 0, "엔진 시험 실패"


def requirements():
    rows = [{"path": n, "size": (GAME / n).stat().st_size, "sha256": digest(GAME / n)} for n in BASE_FILES]
    target = PAYLOAD / "updates/base-requirements.json"
    value = {"schema": 1, "app_id": APP_ID, "base_game_required": True, "files": rows}
    if not target.exists() or load(target) != value:
        assert not BASE.exists() or "--refresh-base" in sys.argv, "본편 확인 기준이 바뀌었다. 의도한 변경이면 --refresh-base 를 준다."
        dump(target, value)
    managed = set(n.lower() for n in load(LISTS)["root"] + load(LISTS)["game_files"])
    assert not managed & set(n.lower() for n in BASE_FILES), "본편 확인 파일과 관리 파일이 겹친다"
    return rows


def sources():
    listing = load(LISTS)
    files = {n: GAME / n for n in listing["root"] + listing["game_files"]}
    for n in files:
        assert ALLOWED.match(n), "허용 범위 밖: " + n
        assert "savedata" not in n.lower()
    for p in PAYLOAD.rglob("*"):
        if p.is_file() and p.name not in ("installed.json", "base-check.json", "last-check.txt"):
            files[p.relative_to(PAYLOAD).as_posix()] = p
    assert len({n.lower() for n in files}) == len(files), "대소문자 경로 충돌"
    assert all(p.is_file() for p in files.values()), "없는 파일이 있다"
    for need in (LAUNCHER, MANUAL, DLL, "updates/settings.json", "updates/base-requirements.json", "사용설명서.txt"):
        assert need in files, "payload 에 없음: " + need
    return dict(sorted(files.items(), key=lambda kv: kv[0].lower()))


def inventory(files):
    rows = []
    for n, p in files.items():
        st = p.stat()
        rows.append({"path": n, "size": st.st_size, "sha256": digest(p), "mtime_ns": st.st_mtime_ns})
    return rows


def stable(files, rows):
    for row in rows:
        st = files[row["path"]].stat()
        assert (st.st_size, st.st_mtime_ns) == (row["size"], row["mtime_ns"]), "제작 도중 파일이 바뀌었다: " + row["path"]


def managed(rows):
    return [{k: r[k] for k in ("path", "size", "sha256")} for r in rows if r["path"] not in UNMANAGED]


def write_zip(target, files, names):
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9, allowZip64=True) as z:
        for n in names:
            z.write(files[n], n)
    with zipfile.ZipFile(target) as z:
        assert z.testzip() is None
        assert sorted(z.namelist()) == sorted(names)
        for n in names:  # 압축을 풀어 원본과 대조
            assert hashlib.sha256(z.read(n)).hexdigest() == digest(files[n]), "압축 대조 실패: " + n
    return {"name": target.name, "size": target.stat().st_size, "sha256": digest(target)}


def full_zip(dest, v, base_version, files, rows):
    """처음 설치하는 이용자용 전체 패치. 현재 버전의 설치 기록(installed.json)을 함께 넣는다."""
    installed = {"schema": 1, "app_id": APP_ID, "version": v, "base_version": base_version, "files": managed(rows)}
    state = BUILD / "installed.json"
    dump(state, installed)
    everything = dict(files)
    everything["updates/installed.json"] = state
    asset = write_zip(dest / ("sakura-korean-patch-%s.zip" % v), everything, sorted(everything, key=str.lower))
    return installed, asset


def secrets_check(files):
    token = re.compile(rb"gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}")
    for n, p in files.items():
        if p.stat().st_size < 8 * 1024 * 1024 and token.search(p.read_bytes()):
            raise AssertionError("토큰으로 보이는 문자열이 들어 있다: " + n)


def full(v):
    version(v)
    assert not BASE.exists(), "이미 기준 배포본이 있습니다. 신규 수정은 update 를 사용하세요."
    assert LISTS.exists(), "먼저 lists 를 실행한다"
    requirements()
    files = sources()
    secrets_check(files)
    rows = inventory(files)
    OUT.mkdir(exist_ok=True)
    assert not list(OUT.iterdir()), "release 폴더가 비어 있지 않다"
    BUILD.mkdir(exist_ok=True)
    installed, asset = full_zip(OUT, v, v, files, rows)
    stable(files, rows)
    manifest = {"schema": 1, "app_id": APP_ID, "version": v, "base_version": v, "release_tag": "v" + v, "notes": "첫 배포", "files": [], "inventory": installed["files"], "deleted": [], "packages": []}
    dump(OUT / "update-manifest.json", manifest)
    hashes = [asset, {"name": "update-manifest.json", "size": (OUT / "update-manifest.json").stat().st_size, "sha256": digest(OUT / "update-manifest.json")}]
    dump(OUT / "FILE_HASHES.json", {"version": v, "assets": hashes})
    dump(BASE, {"version": v, "package_kind": "base_game_required_patch", "files": [{k: r[k] for k in ("path", "size", "sha256")} for r in rows], "initial_managed": installed["files"]})
    dump(HISTORY / (v + ".json"), installed)
    print("첫 배포본 완료: %s (%d바이트), 파일 %d개, 원본 합계 %d바이트" % (asset["name"], asset["size"], len(files) + 1, sum(r["size"] for r in rows)))


def update(v, notes):
    version(v)
    base = load(BASE)
    histories = [load(p) for p in HISTORY.glob("*.json")]
    assert all(version(v) > version(s["version"]) for s in histories), "기존 릴리스보다 높은 버전이 필요하다"
    requirements()
    files = sources()
    secrets_check(files)
    rows = inventory(files)
    current = managed(rows)
    old = {r["path"]: r for r in base["initial_managed"]}
    for r in rows:  # 부트스트랩과 저장소 설정은 고정 계약
        if r["path"] in UNMANAGED:
            original = next(x for x in base["files"] if x["path"] == r["path"])
            assert original["sha256"] == r["sha256"], "고정 실행기/저장소 설정이 바뀌었다. 별도 전체 배포가 필요하다: " + r["path"]
    history_maps = [{r["path"]: r["sha256"] for r in s["files"]} for s in histories]
    # 초기 상태로 되돌린 파일도 이전 수정판 사용자는 받아야 한다.
    changed = [dict(r) for r in current if r["path"] not in old or r["sha256"] != old[r["path"]]["sha256"] or any(h.get(r["path"]) != r["sha256"] for h in history_maps)]
    known = set().union(*(set(r["path"] for r in s["files"]) for s in histories))
    deleted = sorted(known - set(r["path"] for r in current))
    assert changed or deleted, "바뀐 파일이 없다"
    dest = HERE / "updates" / ("v" + v)
    assert not dest.exists(), "이미 만든 버전이다"
    dest.mkdir(parents=True)
    BUILD.mkdir(exist_ok=True)
    groups, group, total = [], [], 0
    for r in changed:
        assert r["size"] < 1700 * 1024 * 1024, "단일 파일이 업데이트 한도를 넘는다"
        if total + r["size"] > 64 * 1024 * 1024 and group:
            groups.append(group)
            group, total = [], 0
        group.append(r)
        total += r["size"]
    if group:
        groups.append(group)
    packages = []
    for i, group in enumerate(groups, 1):
        name = "sakura-update-%s-%03d.zip" % (v, i)
        for r in group:
            r["package"] = name
        asset = write_zip(dest / name, files, [r["path"] for r in group])
        assert asset["size"] < 2 * 1024 ** 3
        packages.append(asset)
    m = {"schema": 1, "app_id": APP_ID, "version": v, "base_version": base["version"], "release_tag": "v" + v, "notes": notes, "files": changed, "inventory": current, "deleted": deleted, "packages": packages}
    dump(dest / "update-manifest.json", m)
    _, full_asset = full_zip(dest, v, base["version"], files, rows)
    stable(files, rows)
    hashes = [full_asset] + packages + [{"name": "update-manifest.json", "size": (dest / "update-manifest.json").stat().st_size, "sha256": digest(dest / "update-manifest.json")}]
    dump(dest / "FILE_HASHES.json", {"version": v, "assets": hashes})
    dump(HISTORY / (v + ".json"), {"schema": 1, "app_id": APP_ID, "version": v, "base_version": base["version"], "files": current})
    print("업데이트 제작 완료:", dest, "변경", len(changed), "삭제", len(deleted))


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["lists", "compile", "test", "full", "update"])
    ap.add_argument("--version")
    ap.add_argument("--notes", default="")
    ap.add_argument("--refresh-base", action="store_true")
    ap.add_argument("--force-launcher", action="store_true")
    a = ap.parse_args()
    if a.mode == "lists":
        lists()
    elif a.mode == "compile":
        compile_tools(a.force_launcher)
    elif a.mode == "test":
        test()
    elif a.mode == "full":
        full(a.version or "1.0.0")
    else:
        update(a.version or input("새 버전 (예: 1.0.1): ").strip(), a.notes or input("사용자에게 보여 줄 수정 안내: ").strip())
