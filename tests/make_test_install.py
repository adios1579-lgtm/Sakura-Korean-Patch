"""배포 ZIP을 시험하기 위한 격리 설치 폴더를 만든다.

 - 본편 자료(한글패치가 건드리지 않는 파일)는 복사하지 않고 하드 링크로 건다(용량을 쓰지 않는다).
 - 한글패치가 제공하는 파일은 링크하지 않고 배포 ZIP에서 풀어 넣는다(이용자가 본편에 덮어 푸는 것과 같다).
 - 링크한 파일과 ZIP의 파일이 하나라도 겹치면 중단한다. 겹치면 압축을 풀 때 링크를 통해 실제 게임 파일이
   바뀔 수 있기 때문이다.
 - 세이브 폴더는 링크하지 않고 빈 폴더로 만든다.

사용:  python -X utf8 -B make_test_install.py <배포 ZIP> <시험 폴더>
"""
import json
import os
import sys
import zipfile
from pathlib import Path

GAME = Path(r"D:\ＳＡＫＵＲＡ～雪月華～")
HERE = Path(__file__).resolve().parent.parent
SKIP_TOP = ("backups",)


def main():
    archive, target = Path(sys.argv[1]), Path(sys.argv[2])
    assert not target.exists(), "시험 폴더가 이미 있다"
    assert target.drive.lower() == GAME.drive.lower(), "하드 링크는 같은 드라이브에서만 된다"
    listing = json.loads((HERE / "managed_files.json").read_text("utf8"))
    managed = {n.lower() for n in listing["root"] + listing["game_files"]}
    with zipfile.ZipFile(archive) as z:
        names = z.namelist()
    in_zip = {n.lower() for n in names}
    assert managed <= in_zip, "ZIP에 관리 파일이 빠져 있다"
    links = []
    for p in GAME.rglob("*"):
        if not p.is_file():
            continue
        rel = p.relative_to(GAME).as_posix()
        low = rel.lower()
        if low.split("/")[0] in SKIP_TOP or low.startswith("advdata/savedata/"):
            continue
        if "/" not in rel:  # 최상위에는 본편 실행에 필요한 원본이 없다(실행 파일·글꼴은 ZIP이 제공)
            continue
        if ".ko-" in low or low in in_zip:
            continue
        links.append(rel)
    assert not ({n.lower() for n in links} & in_zip), "링크와 ZIP이 겹친다"
    copied = 0
    for rel in links:
        dst = target / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        if rel.lower().startswith("advdata/dat/"):
            # 게임이 고쳐 쓸 수 있는 작은 표 파일은 링크하지 않고 복사한다(실제 게임 파일 보호)
            dst.write_bytes((GAME / rel).read_bytes())
            copied += 1
        else:
            os.link(GAME / rel, dst)
    print("그중 복사한 파일 %d개(AdvData/DAT)" % copied)
    (target / "AdvData" / "SaveData").mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as z:
        for n in names:
            dst = target / n
            assert not dst.exists(), "이미 있는 파일 위에 풀려고 한다: " + n
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_bytes(z.read(n))
    print("하드 링크 %d개, ZIP에서 푼 파일 %d개 -> %s" % (len(links), len(names), target))


if __name__ == "__main__":
    main()
