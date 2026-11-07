# CMO 일본어 최소 UI 후보 (ja-minimal-ui-v1)

작성일: 2026-11-07. 근거 조사: [JAPANESE_LOCALIZATION_FEASIBILITY.md](JAPANESE_LOCALIZATION_FEASIBILITY.md) §6.

이 후보는 **주요 메뉴, `Game Options` 창, 그리고 시나리오 설명·진영 브리핑·특별 메시지 본문**(현재 Flight Tutorial 1~8)을 일본어로 표시한다. 중국어 엔진을 수정하지 않고 별도 DLL로 동작하므로, 중국어 패치와 섞이지 않고 upstream 변경을 그대로 받을 수 있다. 실기에서는 엔진 시작과 메뉴 처리를 1회 확인했고, Game Options 번역은 아직 확인하지 않았다(§6).

## 1. 무엇이 바뀌는가

| 범위 | 번역 대상 | 건드리지 않는 것 |
|---|---|---|
| `menu` (363항목 + 규칙 2개) | 모든 창의 메뉴 바·하위 메뉴·컨텍스트 메뉴·툴바 드롭다운의 항목 텍스트, 단축키 표시 문자열, 툴팁 | 단축키 바인딩 자체, 툴바 버튼, 최근 파일 경로, 동적 항목(진영명·임무명) |
| `options` (101항목 + 규칙 2개) | `Game Options` 창의 제목·탭·라벨·체크박스·라디오·버튼·GroupBox·`SectionHeader`·툴팁·그리드 열 머리글 | 텍스트 입력, ComboBox 항목·선택값, 숫자 입력, 체크 상태, 열 `Name` |
| `prose` (312문단) | `Load Scenario` 창의 시나리오 설명, `Side selection and briefing` 창의 브리핑, `Special Messages` 팝업의 본문(모두 WebView2). 문단 단위로 통째 교체 | 링크·이미지가 섞인 문단, 입력칸, 편집 가능한 영역, 그 밖의 창에 있는 WebView2 |

- 사전에 정확히 있는 영어 문자열만 바꾼다. 없는 문자열은 영어로 남고 `CMOLocalizerJa_untranslated.tsv`에 기록된다.
- `Every N seconds/minutes`, `Vertical scaling: xN`은 옵션 창 전용 규칙으로 처리한다(`N`이 숫자일 때만).
- 메뉴 전용 규칙 2개: `Air Sensors (Current: ON)` → `対空センサー (現在: ON)`, `Use latest DB3000: DB3000 v519` → `最新の DB3000 を使用: DB3000 v519`. 앞부분이 사전에 정확히 있을 때만 적용하고 뒷부분은 그대로 둔다.
- `OK`, `Shift + Ins` 같은 키 이름은 일본어 Windows 관례대로 영어로 둔다.

**하지 않는 것**: 문자열 풀 주입, 지도 타일 서버, 날짜 문화권 변경, 글꼴 교체, 오류 창 자동 닫기, WPF·데이터베이스·이미지 처리, 게임 HTML·시나리오 파일 수정(본문은 화면에 표시된 문서만 바꾼다). 이는 테스트로 확인한다(§5의 `isolation`).

## 2. 구성 파일

| 파일 | 역할 |
|---|---|
| `dict/src/ja_menu.json`, `dict/src/ja_options.json` | 영어 원문 → 일본어. 유일한 번역 원본 |
| `scripts/japanese_dict.py` | 두 파일만 읽어 `CMOLocalizerJa.json` 생성·검증. `dict/` 안에는 쓰지 않음 |
| `src/ja/JapaneseUiLocalization.cs` | 일본어 엔진. 이 파일만 단독 컴파일되어 `CMOLocalizerJa.dll`이 됨 |
| `scripts/build_ja.ps1` | DLL + 사전 + SHA256 매니페스트를 `build/ja-candidate/`에 생성 |
| `scripts/test_ja.ps1` | 전체 테스트(게임을 조작하지 않음) |
| `scripts/manage_ja_patch.py` | 게임 폴더에 적용·복원 |
| `src/tests/Japanese*` | 엔진·사전·배포 테스트 |

중국어 빌더 `build_dict.py`는 `ja_*.json`을 읽지 않는다. 이 제외가 없으면 한자가 들어간 일본어 값(`表示`, `適用` 등)이 중국어 사전의 exact 70건과 풀 주입 37건을 덮어쓴다(버그 대장 R80).

## 3. 빌드와 테스트

PowerShell 또는 Git Bash에서 저장소 루트 기준으로 실행한다.

```powershell
python -B -X utf8 scripts/japanese_dict.py --check
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build_ja.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test_ja.ps1 -Candidate build/ja-candidate
```

- `build_ja.ps1`은 기존 출력 폴더가 있으면 멈춘다. 다시 빌드하려면 `-Replace`를 붙인다(이전 후보 폴더만 지움).
- `test_ja.ps1`은 `build/ja-tests/<시각>/`에 로그와 `test-report.json`(단계별 결과, SHA256)을 남긴다. `host` 단계는 테스트 프로그램을 `Command.exe`라는 이름으로 가짜 게임 폴더에 넣고 `manage_ja_patch.py`로 config를 패치해 실제 CLR 로딩 경로를 확인한다. 테스트 창이 화면 밖에 1~2초 열렸다 닫힌다.
- 사전 번역만 고쳤다면 `japanese_dict.py --check` 후 `build_ja.ps1 -Replace`면 된다.

## 4. 게임에 적용·복원

**게임을 종료한 상태에서** 실행한다. 기본 게임 경로는 Steam 기본 위치이며 `--game-dir`로 바꿀 수 있다. `--yes`가 없으면 무엇을 바꿀지만 출력한다.

```powershell
python -B -X utf8 scripts/manage_ja_patch.py status
python -B -X utf8 scripts/manage_ja_patch.py apply            # 계획만 출력
python -B -X utf8 scripts/manage_ja_patch.py apply --yes      # 적용
python -B -X utf8 scripts/manage_ja_patch.py restore --yes    # 영어로 복원
```

적용 시 바뀌는 것은 세 가지뿐이다: `CMOLocalizerJa.dll`, `CMOLocalizerJa.json` 추가, `Command.exe.config`의 `<runtime>` 바로 아래에 두 줄 삽입. config의 나머지 바이트(BOM, CRLF, 탭)는 그대로 둔다. 원본은 `CMOLocalizerJa_backup/<시각>/`에 해시와 함께 보관된다.

거부 조건(아무것도 쓰지 않음):

- 그 폴더의 `Command.exe`가 실행 중이거나 실행 여부를 확인할 수 없을 때
- config에 이미 다른 `appDomainManager`가 있을 때(예: 중국어 패치). AppDomainManager는 하나만 지정할 수 있으므로 **중국어 패치와 동시에 사용할 수 없다.** 중국어 패치를 먼저 그 설치기로 제거해야 한다.
- 후보 파일의 SHA256이 매니페스트와 다를 때, 이미 적용된 상태일 때

복원은 config가 적용 직후 그대로이면 백업 원본을 되돌린다. 그 사이 게임 업데이트·Steam 파일 무결성 검사·수동 편집으로 config가 바뀌었다면 **옛 백업으로 덮지 않고 일본어 loader 두 줄만 제거**한다. 설치한 DLL/JSON은 우리가 설치한 해시 그대로일 때만 지운다. 로그·TSV·백업 폴더는 남긴다.

게임 업데이트나 Steam 무결성 검사가 config를 원본으로 되돌리면 일본어 표시가 사라진다. 이때는 `restore --yes` 후 다시 `apply --yes`를 실행한다. 이 후보는 풀 offset에 의존하지 않으므로 게임 빌드가 바뀌어도 메뉴 문자열이 같으면 그대로 동작할 것으로 예상한다(미검증).

## 5. 측정 결과 (2026-11-07, 이 PC, 게임 미실행)

| 검사 | 결과 |
|---|---|
| `test_ja.ps1` 전체 | 14단계 통과: unit 71, isolation 5, 별도 프로세스 세션 2회, host E2E 7, WebView2 본문 18, 사전 44, 배포 28 |
| WebView2 본문 | 실제 Edge WebView2(런타임 157)에서 문단·목록·`<br>`·굵게 포함 문단 번역, 링크·textarea·contenteditable 불변, 재탐색 자동 번역, 범위 밖 창 불변. **게임의 Flight Tutorial HTML 56개 전부 일본어 표시, 미번역 문단 0**. 본문 기능이 없는 이전 엔진으로 돌리면 번역 검사가 FAIL(음성 대조) |
| 본문 사전 | Flight Tutorial 1~8의 고유 문단 312개, `extract_ja_prose.py coverage` 누락 0. 번역은 FT1을 직접, 나머지 281문단을 용어집 기준으로 3개 묶음 병렬 번역 후 `--check-prose` 통과 |
| host E2E | CLR이 config에서 `JapaneseDomainManager` 로드 → 감시 스레드가 실제 창의 `File`→`ファイル`, Game Options `Use Autosave`→`自動保存を使用` 번역; en-US 요일명 불변; 중국어 엔진 로그 미생성; 복원 후 config SHA256 원본과 동일 |
| isolation | 일본어 DLL: 중국어 엔진 타입·WPF 참조·중국어 런타임 문자열 0건. 같은 탐침이 중국어 DLL에서는 9건 검출(음성 대조) |
| 변이 테스트 | 재적용 조건을 중국어 엔진처럼 "한자 포함"으로 바꾸면 `kana re-applied`만 실패(조사 문서 §4.1 재현), 입력 보호를 제거하면 `text input never written`만 실패 |
| 중국어 사전 | `ja_*.json` 제외 후 재빌드 SHA256 `27ec76b3…`, HEAD와 바이트 동일 |
| 사전 키 | 454개 전부 `dict/strings_pool.json`, `docs/menu_strings_dump.txt` 또는 실기 관찰 목록(`ja_menu.json`의 `_live_observed_1900.24`)에 존재(예외 `Performance` 1개, 허용 목록에 명시) |

## 6. 실기 기록

### 1회차 (2026-11-07, 빌드 1900.24, 2세션)

- 두 세션 모두 엔진이 시작·사전 로드에 성공했다. 인식한 창: `MainForm`, `CPEBannerForm`, `LoadScenario`, `ChooseSide`, `NewMessageForm`, `DarkMessageBox`. 오류·쓰기 폭주 기록 없음.
- **Game Options 창은 열지 않았다**(로그에 `Command.Options` 없음) → 옵션 번역은 미검증.
- 미번역 메뉴 69개(중복 제외). 51개를 사전에 추가했고(`_live_observed_1900.24`), 상태 접미사 9개와 DB 버전 4개는 메뉴 규칙으로 처리한다. 진영명(`Israel`, `Syria`)·시나리오 제목·고유명(`OpenTopoMap`, `SBR`)은 번역하지 않는다.
- 메인 창 첫 스캔이 156 ms였다. 이후 스캔 비용은 기록되지 않았으므로, 통계를 약 1분마다·종료 시 남기도록 바꾸고(`avgScanMs`), 비싼 창은 재스캔 간격을 비용에 비례해 늘렸다(156 ms → 15초).
- 미번역 TSV가 세션마다 같은 줄을 다시 붙였다 → 시작 시 기존 파일을 읽어 중복을 막는다(별도 프로세스 2회 테스트로 고정).
- 빌더의 SQL 탐지가 `Select tanker from mission:`을 코드로 오인했다 → SQL 키워드는 대문자만 탐지.

## 7. 한계와 실기 확인 항목


1. **Game Options 창 식별**: 설치된 `Command.exe`를 읽기 전용 리플렉션으로 조사한 결과, 옵션 창은 WinForms `Command.Options`(기반 `DarkSecondaryFormBase` → `Form`)라서 타입 이름 조건에 걸린다. 실제 로드 여부는 `CMOLocalizerJa.log`의 `Game Options window detected` 줄로 확인한다.
2. 옵션 창의 컨트롤은 DarkUI 자체 그리기 컨트롤(`DarkCheckBox` 46, `DarkLabel` 29, `DarkGroupBox` 10, 탭 7, `DarkDataGridView`)이다. `DarkButton`은 `Text`를 `new`로 숨기므로 엔진은 쓴 뒤 `Invalidate()`로 다시 그리기를 요청한다. `ToolTip` 필드는 없다. **`Hover Info` 탭은 WPF(`HoverInfoOptions`)라서 영어로 남는다.**
3. ComboBox 항목(`Knots` 등)은 게임이 텍스트로 분기할 수 있어(R8) 의도적으로 영어로 둔다.
4. 글꼴을 바꾸지 않는다. 일본어 글리프는 Windows 글꼴 연결에 맡긴다. 자형·잘림·DPI(100/125/150%)를 화면으로 확인해야 한다.
5. 시작 화면(WPF), 툴바 버튼, 유닛 패널, 데이터베이스, 지도 지명은 범위 밖이다.
6. 본문은 Flight Tutorial 1~8만 번역했다. 다른 시나리오의 설명·브리핑·메시지는 영어로 남고 `prose` 줄로 TSV에 기록된다. 시나리오 파일(`.scen`) 안에 직접 들어 있는 설명도 같은 WebView2로 표시되므로, 문단을 사전에 추가하면 같은 방식으로 번역된다.
7. 메뉴에서 여는 `Scenario Description`/`Side Briefing` 창 등 다른 창의 WebView2는 아직 범위 밖이다. 로그의 `WebView2 in … is outside the prose scope` 줄로 어떤 창인지 확인한 뒤 추가한다.

실기 확인 순서: 게임 종료 → `apply --yes` → 게임 실행 → 메뉴 열기/닫기/재열기, 단축키 동작, `Game > Game Options` 열기·저장·취소·재열기, 설정값이 영어 상태와 같게 저장되는지 → 게임 종료 → `CMOLocalizerJa.log`, `CMOLocalizerJa_untranslated.tsv` 확인 → 필요하면 `restore --yes`.

`CMOLocalizerJa_untranslated.tsv`(범위·컨트롤 종류·영어 원문)는 다음 사전 작업의 입력이다. 최근 파일 경로·`.scen` 파일명은 기록하지 않지만 시나리오의 진영·임무 이름이 섞일 수 있으므로 공유 전에 확인한다.

## 8. 다음 단계

1. 다음 플레이에서 `ゲーム > ゲーム設定`를 열어 옵션 번역을 확인하고, 로그의 `stats`/`exit` 줄로 스캔 비용을 확인한다.
2. 범위를 창 단위로 늘린다: 유닛 명령 창, 미션 편집기, 공통 버튼(`Cancel`, `Close`) 등. 각 범위마다 사전 파일 1개 + 엔진의 창 식별 + 테스트를 추가한다.
3. 이후 WPF 패널, 데이터베이스, 브리핑, 지명은 조사 문서 §6.4의 순서대로 별도 설계가 필요하다.

배포 전에는 조사 문서 §5.3의 라이선스·재배포 허가 확인이 필요하다. 이 후보의 패키징·릴리스는 하지 않았다.
