# 배포자 설명 — SAKURA～설월화～ 한글패치

배포 도구는 제작자의 PC 에 맞춘 경로(`build_release.py` 위쪽의 `GAME`, `CODEX`, `PYDEPS`)를 씁니다. 다른 PC 에서 쓰려면 그 경로부터 맞춥니다.

## 구성

| 파일 | 역할 |
|---|---|
| `src/Bootstrap.cs` | 실행기. `SAKURA_한글판.exe`(자동 확인 후 게임 실행)와 `한글패치_업데이트.exe`(수동 업데이트)로 같은 파일을 두 이름으로 배포 |
| `src/Updater.cs` | 업데이트 엔진(`updates/SakuraUpdater.dll`). 조회·동의 창·검증·다운로드·교체·복구·게임 실행 |
| `src/UpdaterTests.cs` | 합성 파일로 하는 오프라인 시험 |
| `build_release.py` | 관리 파일 목록, 컴파일, 시험, 첫 배포본, 누적 업데이트 |
| `managed_files.json` | 한글패치가 교체하는 게임 파일 목록(실행 파일·글꼴·대본 219·그림 230) |
| `baseline.json`, `history/` | 배포한 버전마다의 파일 크기·해시 기록(파일 내용의 복제본이 아님) |
| `tests/` | 격리 시험 폴더 만들기, 숨김·음소거 실행기 시험 |

## 고정 계약

- 앱 ID `sakura-ko`, 저장소 `adios1579-lgtm/Sakura-Korean-Patch`, 묶음 이름 `sakura-update-버전-번호.zip`.
- `SAKURA_한글판.exe`, `한글패치_업데이트.exe`, `updates/settings.json` 은 업데이트로 바꾸지 않습니다(실행 중인 실행기는 자기 자신을 덮어쓸 수 없음). 바꿔야 하면 새 전체 배포가 필요합니다.
- 엔진 DLL 은 업데이트로 교체할 수 있지만, 실행 중에는 옛 엔진이 돌고 새 엔진은 다음 실행부터 쓰입니다. 옛 엔진이 읽지 못하는 목록 형식으로 바꾸지 않습니다.
- 버전은 정수 세 부분(`1.0.1`)만 씁니다. 시험판 접미사는 쓰지 않습니다.
- 세이브(`AdvData/SaveData`)·음성·음악·효과음·동영상은 관리 대상이 아닙니다. 엔진의 `Safe()` 가 허용하는 경로는 실행 파일, 글꼴, `AdvData/MES/*.mes`, `AdvData/GRP/(battle|BG|bst|SYSTEM)/*.CRX`, 설명서, 엔진 DLL, 본편 확인 정보뿐입니다.

## 업데이트 만들기

1. 수정한 파일을 게임 폴더(`GAME`)에 반영합니다. 새로 바뀌는 파일이 `managed_files.json` 에 없으면 목록에 추가합니다(허용 경로 안에서만).
2. `업데이트_만들기.bat` 을 실행하거나 아래 명령을 씁니다.

   ```text
   python -X utf8 -B build_release.py update --version 1.0.1 --notes "수정 안내"
   ```

3. `updates/v1.0.1/` 에 만들어진 **모든 파일**(전체 패치 ZIP, 업데이트 묶음 ZIP 들, `update-manifest.json`, `FILE_HASHES.json`)을 새 릴리스 초안에 올립니다. 태그는 `v1.0.1` 처럼 `v`+버전입니다.
4. 초안에 파일이 다 올라간 것을 확인하고 최신(Latest)으로 공개합니다. 커밋/푸시만으로는 업데이트되지 않습니다.
5. 공개 후 `releases/latest/download/update-manifest.json` 이 새 버전을 가리키는지, 내려받은 자산의 크기·해시가 `FILE_HASHES.json` 과 같은지 확인합니다.

이미 공개한 버전의 파일을 바꿔 끼우지 않습니다. 고칠 것이 있으면 새 번호로 냅니다.

엔진 소스를 고쳤을 때만 `build_release.py compile` → `build_release.py test` 를 먼저 돌립니다. `update` 는 컴파일하지 않습니다(다시 컴파일하면 DLL 이 달라져 불필요하게 업데이트 대상이 됩니다).

## 실행 파일(SAKURA.EXE)을 다시 만들 때

게임 실행 파일은 검수 통합본에 패치 세 개를 순서대로 적용한 것입니다. 순서를 지킵니다.

1. 검수 통합본(`review_integration/final/SAKURA.EXE`)
2. 디스크 확인 제거 (`patch_nocd.py --apply`)
3. 세이브 칸 장 제목·날짜 한글화 (`titles/patch_titles.py`)
4. 게임 폴더에서 글꼴 읽기 (`fontdir/patch_fontdir.py`)

## 시험

- `build_release.py test` — 엔진의 오프라인 시험(교체·복구·경로 차단·사용자 수정 보호·누적 업데이트·본편 확인). 결과는 `TEST_RESULTS.txt`.
- `tests/make_test_install.py` — 배포 ZIP 을 본편에 덮어 푼 상태를 격리 폴더로 만듭니다. 본편 자료는 하드 링크로 걸어 용량을 쓰지 않습니다.
- `tests/launcher_check.ps1` — 격리 폴더의 실행기를 숨김 데스크톱·음소거로 띄워 창과 프로세스를 기록합니다.

HTTPS 와 SHA-256 은 전송 중 손상을 잡는 용도입니다. 목록에 전자서명은 없으므로 저장소가 탈취된 경우까지 막아 주지는 않습니다.
