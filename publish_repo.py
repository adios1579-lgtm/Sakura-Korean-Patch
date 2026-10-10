"""저장소에 문서·소스·기록을 올린다(git 없이 GitHub 내용 API 사용). 릴리스 자산은 다루지 않는다.

같은 내용이 이미 올라가 있으면 건너뛴다. 토큰은 gh 가 관리하며 이 스크립트는 다루지 않는다.
사용:  python -X utf8 -B publish_repo.py
"""
import base64
import hashlib
import json
import subprocess
import tempfile
import urllib.parse
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = "adios1579-lgtm/Sakura-Korean-Patch"
SHOTS = HERE.parent / "screenshots"
FILES = {
    "README.md": HERE / "github/README.md",
    "USER_GUIDE.md": HERE / "github/USER_GUIDE.md",
    "PUBLISHER_GUIDE.md": HERE / "github/PUBLISHER_GUIDE.md",
    "RELEASE_NOTES_1.0.0.md": HERE / "github/RELEASE_NOTES_1.0.0.md",
    "TEST_RESULTS.txt": HERE / "tests-result.txt",
    "build_release.py": HERE / "build_release.py",
    "publish_repo.py": HERE / "publish_repo.py",
    "managed_files.json": HERE / "managed_files.json",
    "baseline.json": HERE / "baseline.json",
    "history/1.0.0.json": HERE / "history/1.0.0.json",
    "업데이트_만들기.bat": HERE / "업데이트_만들기.bat",
    "src/Bootstrap.cs": HERE / "src/Bootstrap.cs",
    "src/Updater.cs": HERE / "src/Updater.cs",
    "src/UpdaterTests.cs": HERE / "src/UpdaterTests.cs",
    "tests/make_test_install.py": HERE / "tests/make_test_install.py",
    "tests/launcher_check.ps1": HERE / "tests/launcher_check.ps1",
    "screenshots/01.png": SHOTS / "01_타이틀.png",
    "screenshots/02.png": SHOTS / "27_대화_아스카_선택지.png",
    "screenshots/03.png": SHOTS / "08_CG_아스카_보름달.png",
    "screenshots/04.png": SHOTS / "12_CG_아스카_벚꽃.png",
    "screenshots/05.png": SHOTS / "20_CG_헤이안_타마모_2.png",
    "screenshots/06.png": SHOTS / "04_불러오기_1.png",
}


def gh(*args, input_file=None):
    cmd = ["gh", "api", *args]
    if input_file:
        cmd += ["--input", str(input_file)]
    return subprocess.run(cmd, capture_output=True, encoding="utf8", errors="replace")


def blob_sha(data):
    return hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest()


def main():
    for name, src in FILES.items():
        data = src.read_bytes()
        url = "repos/%s/contents/%s" % (REPO, urllib.parse.quote(name))
        got = gh(url)
        sha = None
        if got.returncode == 0:
            sha = json.loads(got.stdout)["sha"]
            if sha == blob_sha(data):
                print("같음   ", name)
                continue
        body = {"message": ("Update " if sha else "Add ") + name, "content": base64.b64encode(data).decode()}
        if sha:
            body["sha"] = sha
        with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False, encoding="utf8") as f:
            json.dump(body, f)
        try:
            r = gh("-X", "PUT", url, input_file=f.name)
        finally:
            Path(f.name).unlink()
        if r.returncode != 0:
            raise SystemExit("실패: %s\n%s" % (name, r.stderr[-500:]))
        print("올림   ", name, len(data))


if __name__ == "__main__":
    main()
