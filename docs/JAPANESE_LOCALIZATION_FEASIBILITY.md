# CMO 일본어 패치 제작 가능성 조사

조사일: 2026-11-07, Asia/Tokyo. 목표 언어는 **일본어(ja)**이며 로컬 폴더명 `CMO_KR`은 언어를 뜻하지 않는다.

**결론: 기술적으로 제작할 수 있다. 다만 이 코드는 범용 다국어 엔진이 아니라 중국어 전용 동작이 섞인 엔진이므로, 번역 JSON 교체만으로 안정적인 일본어 패치가 되지는 않는다.** 기존 UI 탐색·갱신·바인딩 보호 기반을 재사용하고, 언어별 데이터 선택과 일본어 판정, 중국어 하드코딩의 분리가 필요하다. 최소 범위는 주요 텍스트 메뉴와 `Game Options` 창이다. 파생 패치의 공개 배포 허용 범위는 별도로 확인해야 한다.

> **구현 현황(2026-11-07):** 최소 UI 후보를 구현했다 — [JAPANESE_MINIMAL_UI.md](JAPANESE_MINIMAL_UI.md). §6.1 제안과 달리 중국어 엔진(`src/engine/`)은 수정하지 않고, 단독 컴파일되는 별도 DLL(`src/ja/JapaneseUiLocalization.cs` → `CMOLocalizerJa.dll`)로 구현했다. 아래 본문의 "만들지 않았다"는 조사 시점 기준이다.

이 문서는 구현 계획이다. 일본어 소스 사전, 일본어 엔진, 설치기 또는 배포 패키지를 만들지 않았다. 기존 소스의 격리 컴파일과 메모리상의 소규모 진단만 수행했다. 게임을 실행·종료하거나 설치 디렉터리를 수정하지 않았다.

## 1. 조사 기준과 증거 구분

| 항목 | 확인 결과 |
|---|---|
| 작업 디렉터리 | `C:\Hyungjin\git_repos\CMO_KR` |
| 적용 지침 | 루트 `AGENTS.md`, 변경 전 읽은 `docs/ROUTING.md`; 하위 `AGENTS.md`는 발견되지 않음 |
| 기준 커밋 | `0f880ccf9e3bbc3af719c2458526a03343c9b9e2` |
| 기준 커밋 설명·작성 시각 | `README 下载区同步 4.3.0（稳定版 4.2.5）`, `2026-11-06T20:33:36+08:00` |
| 최초 브랜치·작업 상태 | `master`, 추적·미추적 변경 없음 |
| 조사 브랜치 | `localization/ja`; 로컬 목록과 GitHub API에서 기존 동명 브랜치가 없음을 확인한 뒤 생성 |
| origin | `https://github.com/hyungjin-k/CMO_KR.git` |
| 포크·권한 | GitHub API: 로그인 `hyungjin-k`, `fork=true`, 부모·원본 `mj8724/CMO_CN`, `permissions.push=true` |
| upstream | 처음에는 없었으며 `https://github.com/mj8724/CMO_CN.git` 추가; origin 유지 |
| 패치 버전 | `VERSION`: `4.3.0`; `STABLE_VERSION`: `4.2.5` |
| 저장소의 지원 게임 빌드 | `GAME_BUILD`: **1900.24**; `README.md:40`, `src/shared/CMOShared.cs:36`과 일치 |

이하 줄 번호는 위 **기준 커밋**의 소스를 기준으로 한다. 기준 소스는 [원본 커밋](https://github.com/mj8724/CMO_CN/tree/0f880ccf9e3bbc3af719c2458526a03343c9b9e2)에서 확인할 수 있다. 검색 엔진의 저장소 페이지 캐시는 과거 버전을 보여 주므로 최신 지원 버전의 근거로 사용하지 않았다.

- **[확인]** 소스를 읽거나 이번 세션에서 직접 실행·조회하여 확인한 사실.
- **[추론]** 확인한 코드로부터 예상되는 동작·위험 또는 제안하는 설계. 게임에서 검증한 결과가 아니다.
- **[실행 필요]** 실제 CMO에서 확인해야 하는 사항. 과거 중국어판 검증 기록을 일본어판 검증으로 간주하지 않는다.

### 설치 게임의 읽기 전용 확인

**[확인]** `C:\Program Files (x86)\Steam\steamapps\common\Command - Modern Operations\Command.exe`가 존재한다. PE 파일의 FileVersion/ProductVersion은 모두 `1.0.0.0`이다. `steamapps/appmanifest_1076160.acf`에는 appid `1076160`, Steam buildid `26429119`가 있다. 이 숫자는 게임 제목에 표시하는 `1900.xx`와 별개이므로 서로 대응한다고 단정할 수 없다.

조회 당시 실행 중인 `Command` 프로세스는 없었다. 해당 게임 디렉터리에 `CMOLocalizer.dll`, `CMOLocalizer.json`, `CMOChinese_Installed.json`, `Command.exe.config.cmo_bak`는 없었다. **설치 게임의 내부 빌드와 일본어 패치 호환성은 미확인**이다. 게임 파일·개인 설정은 보고서에 복사하거나 커밋하지 않았다.

## 2. 실제 작동 경로

### 2.1 게임 시작부터 화면 번역까지

```text
dict/src/*.json + 기존 보조 사전
  → scripts/build_dict.py
  → CMOLocalizer.json (UTF-8, exact/offsets/words/comments/partial)

Command.exe.config의 runtime/appDomainManager 설정
  → CLR이 CMOLocalizer.dll 로드
  → LocalizerDomainManager → CustomAppDomainManager.InitializeNewDomain
  → 날짜 문화권 처리 + WPF 보충 타이머 + HookWorker
      ├─ 게임 루트의 CMOLocalizer.json 읽기
      ├─ WinForms 생명주기·TextChanged·메뉴·툴팁 번역
      ├─ WPF/ElementHost 탐색·값 갱신·바인딩 표시 투영
      ├─ WebBrowser/WebView2 텍스트 번역
      └─ 지도 타일 서버 시작 → 문자열 풀 메모리 주입

별도 설치 리소스
  → DB HTML 템플릿, 시나리오/QuickBattle HTML, WWP 지명,
    지도 캐시, 버튼 PNG, Launcher resx
```

**[확인]** 설치기가 `Command.exe.config`의 기존 XML에 `appDomainManagerType=CMOLocalizer.LocalizerDomainManager`와 `appDomainManagerAssembly=CMOLocalizer, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null`을 넣는다. 기존 runtime 설정에 병합하며, `Command.exe` 자체를 다시 쓰는 코드 경로는 아니다. [설치기:993](../src/installer/CMO_Chinese_Setup.cs#L993), [엔진 진입점:250](../src/engine/CMOLocalizer.cs#L250).

**[확인]** `HookWorker`가 실행 파일 기준 디렉터리의 단일 파일 `CMOLocalizer.json`을 읽는다. 현재 locale 인수나 언어별 사전 선택은 없다. UTF-8 읽기와 수동 JSON 파서로 각 섹션을 적재한다. [엔진:507](../src/engine/CMOLocalizer.cs#L507), [엔진:5852](../src/engine/CMOLocalizer.cs#L5852).

**[확인]** WinForms는 생명주기 감지와 UI 스레드 큐, `TextChanged`, 초기 600ms/이후 1500ms 탐색을 결합한다. 메뉴에는 텍스트와 툴팁, 펼쳐진 하위 메뉴의 처리가 있다. WPF는 Loaded·값 변경·보충 타이머와 `SetCurrentValue`를 사용하고, 일부 무기행은 번역 converter를 넣은 바인딩으로 교체한다. 따라서 모든 표시가 단일 `Control.Text` 치환으로 해결되는 구조가 아니다. [엔진:819](../src/engine/CMOLocalizer.cs#L819), [엔진:1682](../src/engine/CMOLocalizer.cs#L1682), [엔진:3910](../src/engine/CMOLocalizer.cs#L3910), [WPF 바인딩 투영:388](../src/engine/WpfValueTranslation.cs#L388).

**[확인]** 일반 번역은 완전 일치·앞뒤 공백 제거 일치를 먼저 확인하고, 경로 보호·특수 문구 처리·기존 번역 판정·캐시를 거친다. `DoTranslate`는 정규화된 일치와 동적 조합·부분 문장 등의 규칙을 적용하고, 실패하면 원문을 반환한다. `partial`은 현재 산출물에서 비어 있고 `words`는 조합용 용어다. [엔진:4618](../src/engine/CMOLocalizer.cs#L4618), [엔진:5467](../src/engine/CMOLocalizer.cs#L5467), [엔진:5835](../src/engine/CMOLocalizer.cs#L5835), [빌더:245](../scripts/build_dict.py#L245).

**[확인]** 문자열 풀 주입은 게임 내부 `List<string>`, `List<int>`, `byte[]`를 반사로 찾아 번역을 추가하고 버퍼의 offset 위치에 새 문자열 인덱스를 기록한다. 이는 **실행 중 게임 메모리 변경**이다. 혼동된 클래스명을 먼저 시도하고 필드 자료형 조합으로 대체 탐색한다. 오프셋이 범위 안에 있다는 검사만으로 해당 위치가 여전히 같은 원문임을 보장하지는 않는다. [엔진:572](../src/engine/CMOLocalizer.cs#L572), [엔진:652](../src/engine/CMOLocalizer.cs#L652).

**[확인]** 지도는 WWP 파일을 UTF-8로 읽고 loopback 임의 포트에서 PNG 타일을 제공한다. 게임의 지도 URL 및 `zh-CN`을 세 개의 숫자 offset에 직접 넣는다. **사전의 `offsets`를 비우기만 해도 주입이 완전히 꺼지는 것은 아니다.** [엔진:349](../src/engine/CMOLocalizer.cs#L349), 특히 `1789870`, `21564`, `62636`을 설정하는 [엔진:383](../src/engine/CMOLocalizer.cs#L383).

추가로 시작 과정에는 날짜 문화권을 바꾸는 처리와 오류 창을 자동으로 닫는 처리가 있다. 일본어 UI 실험 시 오류가 숨겨지지 않도록 이를 검토해야 한다. 이는 번역 데이터와 무관한 동작이다. [엔진:286](../src/engine/CMOLocalizer.cs#L286), [엔진:983](../src/engine/CMOLocalizer.cs#L983).

### 2.2 디스크 변경·설치·업데이트·복원

아래는 **코드가 수행하는 작업을 조사한 결과**이며 이번 세션에서 실행한 작업이 아니다.

| 대상 | 설치·업데이트 시 동작 | 제거·영어 복원 시 동작 |
|---|---|---|
| `Command.exe` | 존재 여부 확인; 이 경로에서 실행 파일 바이트 패치 없음 | 그대로 사용 |
| `Command.exe.config` | 최초 `.cmo_bak` 보존 후 loader 두 항목 병합 | 백업이 있으면 전체 config 복사 후 백업 삭제; 없으면 loader 두 항목 제거 |
| `CMOLocalizer.dll/json` | 설치기에 내장된 바이트 추출, DLL은 임시 파일+원자 교체 | 설치기 실행 위치가 게임 루트와 다르면 삭제; 같은 위치이면 남을 수 있음 |
| `DB/Templates/*.html` | 8개 번역 템플릿; 첫 `Templates_Original_Backup` 보존 | 백업 템플릿 복사 |
| `Scenarios/**/*.html` | 내장 ZIP 및 추가 제목 번역; 기존 `.orig` 보존 | `.orig` 복원 |
| `QuickBattle/*/Form.html` | 별도 내장 ZIP; `.orig` 보존 | `.orig` 복원 |
| `WW/PlacenameSets/{Synoptic,Cities}/0000_0000.wwp` | 번역 지명 교체; `.orig` 보존 | `.orig` 복원 |
| `WW/Cache/Stamen Terrain - Labels` | 기존 타일 백업 및 번역 지명으로 재작성 | 백업 캐시 복원 경로 |
| `Symbols/Menu/*.png`, `Launcher/Localization/stringsChinese.resx` | 번역 이미지·문자열 배치; `.orig` 보존 | 원본 백업 복원 경로 |
| 업데이트 상태 | `CMO_Updater.exe`, 설치 manifest, HKCU 시작 항목, AppData 설정·패키지 캐시 | 도우미와 시작 항목 정리; 기본적으로 사용자 설정 유지, 명시적 purge만 설정 삭제 |

근거: [설치 진입:938](../src/installer/CMO_Chinese_Setup.cs#L938), [리소스 배치:1018](../src/installer/CMO_Chinese_Setup.cs#L1018), [이미지·런처:1108](../src/installer/CMO_Chinese_Setup.cs#L1108), [HTML 백업:1255](../src/installer/CMO_Chinese_Setup.cs#L1255), [지도 캐시:1480](../src/installer/CMO_Chinese_Setup.cs#L1480), [제거:1944](../src/installer/CMO_Chinese_Setup.cs#L1944), [설정 경로:74](../src/shared/CMOShared.cs#L74).

**[확인]** 업데이트는 원본 `mj8724/CMO_CN`의 최신 release API를 조회하며 `CMO_Chinese_Setup*.exe`와 SHA256 파일을 찾는다. 기본 자동 확인 간격은 6시간이다. 설치 시 자기 패키지를 캐시에 넣어 이전 캐시 패키지 재설치 방식의 rollback을 제공한다. [공유 상수:28](../src/shared/CMOShared.cs#L28), [기본 설정:248](../src/shared/CMOShared.cs#L248), [업데이트 검색:317](../src/updater/CMO_Updater.cs#L317), [설치 캐시:1084](../src/installer/CMO_Chinese_Setup.cs#L1084).

**[추론]** 일본어 배포에서 위 업데이트 주소·파일명·`CMOChinese` 레지스트리/캐시/manifest 이름을 그대로 사용하면 중국어 패치로 교체되거나 상태가 충돌할 수 있다. 초기 시험판에는 도우미를 포함하지 않고, 정식 배포 전 일본어 배포 채널과 상태를 분리해야 한다.

**[추론]** 복원은 “최초 백업이 정상적인 해당 게임 버전의 영어 원본”이라는 조건에 의존한다. 최초 설치 이후 게임 업데이트가 있었다면 옛 config/HTML/템플릿을 복사하는 복원이 현재 공식 파일과 다를 수 있다. 설치기의 일부 자원 처리에는 예외를 삼키는 경로도 있어 성공 반환만으로 완전 복원을 보증할 수 없다. 게임 버전별 백업과 SHA256 확인, 필요시 Steam 원본 검증을 별도 복원 절차로 설계해야 한다.

개발용 `scripts/deploy.sh`도 DLL/JSON만 복사하는 스크립트가 아니다. DB 템플릿·WWP·QuickBattle·지도 캐시까지 변경한다. 최소 UI 시험에 그대로 사용하면 범위를 벗어난다. [배포 스크립트:86](../scripts/deploy.sh#L86).

### 2.3 게임 업데이트에 취약한 부분

- **[확인]** 혼동된 풀 클래스 `AoIBWWlDJbaf7LijnA.oMu6jVbdhHEH79DDhU`와 과거 anti-debug 필드 `gTbYFnJa7EN`, 숫자 pool offset에 의존한다. 구조 탐색이 있어도 offset 재배치까지 보정하지 않는다. [엔진:587](../src/engine/CMOLocalizer.cs#L587), [엔진:677](../src/engine/CMOLocalizer.cs#L677).
- **[확인]** `MainForm`, `UnitWeapons_WPF`, `InternalDBViewer`, `DarkTreeView`, `DarkToolStrip2` 등 타입·컨트롤 이름과 toolbar 인덱스에 따른 특수 처리가 있다. 일부 진단 명령도 `RenderedHTMLString`, `SelectedObjectID`, `DisplaySelection`, 혼동된 필드명 등에 의존한다. 진단 프로브가 깨진 것과 번역이 깨진 것을 구분해야 한다. [엔진:1354](../src/engine/CMOLocalizer.cs#L1354), [엔진:2210](../src/engine/CMOLocalizer.cs#L2210), [엔진:3793](../src/engine/CMOLocalizer.cs#L3793), [진단:7031](../src/engine/CMOLocalizer.cs#L7031).
- **[추론]** UI 문자열·공백·구두점 변경은 exact miss, UI 프레임워크·DOM 변경은 hook 미작동, DB 템플릿 변수 변경은 렌더 실패, 업데이트의 config/리소스 덮어쓰기는 패치 해제를 일으킬 수 있다.
- **[확인]** `NoteGameBuild`는 제목에서 읽은 빌드와 manifest를 비교하여 로그에 경고한다. 실행을 차단하는 호환성 잠금은 아니다. [엔진:5350](../src/engine/CMOLocalizer.cs#L5350).

## 3. 일본어 재사용 범위

| 영역 | 재사용할 기반 | 일본어 데이터·코드 작업 |
|---|---|---|
| 메뉴·툴팁 | WinForms 메뉴 탐색, 이벤트, 하위 메뉴 재탐색 | 영어 키→일본어 exact; 카나를 거부하는 메뉴 갱신 조건 수정 |
| 설정 창·일반 UI | 생명주기 큐, 읽기 전용 표시 처리, WPF 바인딩 보존 | 라벨·탭·체크박스·툴팁 번역; 직접 넣은 중국어·동적 문구 분리; 실제 컨트롤 확인 |
| ComboBox/목록 | 원시 키·선택값과 표시를 분리하려는 전용 처리 | 항목 문자열을 무조건 교체하지 말고 표시 계층만 번역; 선택 동작 검증 |
| 데이터베이스 | 목록 표시, WPF converter, WebView/HTML 경로 | `db_*.json`, 별도 `comments` 사전, 8개 HTML 템플릿, 코드 내 제목·단위·문구를 일본어화 |
| 브리핑·튜토리얼 | WebView2 브리지, 일부 WinForms WebBrowser 경로, HTML 보존 exporter | 영문 문단을 직접 번역; 정적 HTML와 런타임 본문을 각각 처리; 중국어 판정·요약 규칙 수정 |
| QuickBattle | HTML 텍스트 노드 치환 및 attribute fingerprint | `dict/quickbattle_html.json`에 대응하는 일본어 소스 분리; option 표시만 번역하고 `value`/Lua 계약 유지 |
| 지도 지명 | WWP I/O, 좌표 기반 동일 지명 판정, 타일 생성·큐 | 원문 `name` 기준 일본어 지명, `zh` 전용 스키마·font·locale·offset 처리 수정 |
| 이미지 버튼 | 파일 로딩·toolbar 이미지 지정 구조 | 일본어 PNG 별도 제작; 메뉴 exact 변경만으로 픽셀 속 문자가 바뀌지 않음 |
| Launcher | resx 자원 형식 | 중국어 resx를 덮어쓰는 현 방식과 실제 런처 언어 선택 계약을 조사 후 일본어 자원 구성 |
| 설치·업데이트 | 백업·manifest·원자 파일 교체·캐시 rollback의 설계 | 일본어 UI·이름·release endpoint·상태 경로·자원 선택 수정 및 재빌드 |

관련 코드: [ComboBox:83](../src/engine/ComboBoxTranslation.cs#L83), [입력 보호:4330](../src/engine/CMOLocalizer.cs#L4330), [DB 별도 comments:30](../src/engine/CMOLocalizer.cs#L30), [WebView 호스트:495](../src/engine/WebViewTranslation.cs#L495), [WebView JS:38](../src/engine/WebViewTranslation.js#L38), [HTML exporter:58](../scripts/localize_html_files.py#L58), [QuickBattle:51](../scripts/quickbattle_html.py#L51), [지명 resolver:48](../scripts/placename_translation.py#L48).

### 3.1 영어에서 직접 번역할 수 있는 데이터

**[확인]** `dict/src/menu_terms.json`, `labels.json`, `db_record_names.json`, `db_descriptions.json`, 각 `*_side*`/`*_message*`의 다수 항목은 **영어 원문을 키, 중국어를 값**으로 갖는다. 중국어를 중간 언어로 사용하지 않고 영어 키에서 일본어로 번역할 수 있다. `_note` 등 메타 키는 번역 항목이 아니다. 숫자 키인 `force_offsets.json`은 `dict/strings_pool.json`에서 원문을 다시 연결해야 한다.

이번 기준의 규모는 다음과 같다. 이는 번역률이나 실제 화면 커버리지 수치가 아니다.

| 데이터 | 실제 개수 |
|---|---:|
| `dict/src/*.json` | 70 파일 |
| 런타임 `exact` | 70,936 |
| 런타임 `offsets` | 8,760 |
| 런타임 `words` / `comments` / `partial` | 3,173 / 4,191 / 0 |
| `menu_terms.json` 번역쌍(메타 제외) | 308 |
| `labels.json` 번역쌍(메타 제외) | 176 |
| `db_record_names.json` / `db_descriptions.json` 번역쌍 | 20,533 / 16,328 |
| 원문 풀 `strings_pool.json` | 28,833; 코드·식별자도 포함하므로 전부 번역하면 안 됨 |
| 도시 / 광역 지명 | 4,287 / 321 |

**[확인]** 빌더는 `dict/src/*.json`만 읽는 것이 아니다. `dict/glossary.json`, `dict/briefing_dict.json`, `dict/brief_translations.json`도 읽고, 마지막에는 일부 메뉴·UI 사전을 다시 우선 적용한다. 따라서 일본어 파일을 단순히 추가하면 기존 중국어 자료와 혼합·덮어쓰기될 수 있다. 파일명 정렬을 이용한 우회도 후반 우선순위를 해결하지 못한다. [빌더:86](../scripts/build_dict.py#L86), [빌더:278](../scripts/build_dict.py#L278), [빌더:361](../scripts/build_dict.py#L361), [빌더:468](../scripts/build_dict.py#L468).

### 3.2 upstream 변경을 반영하기 쉬운 구조 — 제안

**[추론/설계]** 기존 중국어 소스는 유지하고, 프로젝트의 `dict/src/*.json` 원칙에 맞게 `dict/src/ja_menu.json`, `dict/src/ja_options.json`처럼 일본어 소스를 별도 파일로 둔다. 동시에 빌더에 명시적 locale별 입력 목록을 추가해야 한다. 기본 중국어 빌드에서 `ja_*.json`이 자동 합쳐지지 않도록 바꾸고, 일본어 빌드는 중국어 legacy·glossary·briefing 값을 읽지 않아야 한다. 미번역은 영어로 남긴다.

영어 키와 일본어 값의 관계는 유지하고, 원문이 변경·삭제되면 검토 목록으로 보내며 자동 번역 승계하지 않는다. offset은 언어 자산의 영구 ID로 사용하지 않고 **게임 빌드별 원문 풀에서 재계산·검증**한다. 용어·comments·본문·지명·이미지는 서로 다른 문맥으로 관리한다. 엔진의 언어별 날짜·단위·동적 문구·글꼴 정책은 작은 C# 5 호환 모듈로 분리하는 것이 좋다.

이번 세션에서는 merge/rebase/cherry-pick/PR을 하지 않았다. 이후 upstream 반영 역시 별도 작업으로 차이를 검토하고 진행한다.

## 4. 일본어 지원의 구체적인 문제

### 4.1 카나를 번역으로 인정하지 않는 코드 — 직접 재현

**[확인]** 빌더의 `sanitize`는 `[\u4e00-\u9fff]` 한자가 없는 번역을 제외한다. `load_comments`에도 한자 필수 조건이 있다. 엔진의 `ContainsChinese`는 더 좁은 `U+4E00..U+9FA5`만 검사한다. 히라가나·가타카나·반각 카타카나는 여기에 포함되지 않는다. [빌더:141](../scripts/build_dict.py#L141), [빌더:184](../scripts/build_dict.py#L184), [엔진:4573](../src/engine/CMOLocalizer.cs#L4573).

| 실제 호출 | 이번 결과 |
|---|---|
| `sanitize("File", "ファイル")` | `None` — 제외 |
| `sanitize("Options", "オプション")` | `None` — 제외 |
| `sanitize("Show", "ひょうじ")` | `None` — 제외 |
| `sanitize("Apply", "適用")` | `適用` — 통과하는 한자 대조군 |
| `sanitize("Menu", "Menu")` | `None` — 미번역 대조군 |

**[확인]** 기존 소스를 격리 컴파일한 DLL에 메모리 JSON만 전달하면 exact 파서 자체는 `ファイル`을 읽는다. `TranslateToolStripItem`의 최초 호출에서는 `File → ファイル`이 되지만, 메뉴 Text를 다시 `File`로 설정해 갱신 이벤트를 발생시키면 `File`이 남았다. `Apply → 適用`은 같은 이벤트 뒤에도 `適用`이었다. 원인은 이벤트의 `ContainsChinese(tr)` 조건이다. [엔진:3918](../src/engine/CMOLocalizer.cs#L3918).

이는 화면 픽셀 실험이 아니라 독립 `ToolStripMenuItem` 객체의 실제 이벤트 실험이다. 이후 주기 스캔이 다시 일본어로 바꿀 가능성이 있어 **영구 미번역이라고 단정하지 않는다**. 그러나 일본어 첫 표시·갱신 일관성을 사전 교체만으로 보장할 수 없다는 증거다.

**[설계]** 문자 범위만 확장하는 것으로 끝내지 말고, “검토된 번역값인가”, “이미 이 노드에 적용한 값인가”, “영어 잔여가 있는 혼합문인가”를 분리한다. 유효한 라틴 문자 모델명과 숫자도 허용해야 하므로 “일본어 글자가 반드시 있어야 한다”만으로 성공을 판단해서도 안 된다. `translationTerminals`, 메뉴·브리핑·comments·검증 스크립트의 모든 중국어 판정을 함께 점검한다.

### 4.2 글꼴·인코딩·레이아웃

- **[확인]** JSON은 UTF-8, .NET 문자열은 Unicode로 읽는다. 파서 단계에서 카나가 보존됨을 확인했다. WWP도 UTF-8이다. 일본어 바이트 저장을 위해 새 엔진 형식이 필요한 것은 아니다.
- **[확인]** `PatchMainFormFonts`와 지도 renderer는 `Microsoft YaHei`를 직접 지정하고, DB 템플릿도 같은 글꼴을 우선한다. 일본어 자형·가독성·fallback은 별도 문제다. [엔진:1800](../src/engine/CMOLocalizer.cs#L1800), [지도:59](../src/shared/MapLabelRenderer.cs#L59), [DB 템플릿:2](../assets/db_templates/Aircraft.html#L2).
- **[확인]** 이 PC에는 `Yu Gothic UI`, `Yu Gothic`, `Meiryo`, `Meiryo UI`, `MS Gothic` 등이 설치되어 있다. **[설계]** 일본어용 글꼴 우선순위와 미설치 fallback을 두고, 타 사용자 환경에서도 확인한다. 글꼴 파일을 패치에 묶어 배포하는 것은 별도 권리 확인 대상이다.
- **[확인]** HTML exporter에는 charset·placeholder 보호가 있지만 `fix_static_html_charset.py`는 한자 포함 여부로 검사 대상을 고른다. 카나만 있는 페이지를 놓칠 수 있다. 구형 HTML 디코딩에 `gb18030`, `cp1252`를 시도하는 코드도 있어 기존 파일을 무조건 일본어 ANSI로 해석하면 안 된다. [HTML 처리:127](../scripts/localize_html_files.py#L127), [charset 검사:36](../scripts/fix_static_html_charset.py#L36), [검사 대상:74](../scripts/fix_static_html_charset.py#L74).
- **[확인]** 전역 자동 레이아웃 엔진은 없다. 예를 들어 FormationEditor 버튼을 70px로 늘리는 개별 보정이 있다. [엔진:2192](../src/engine/CMOLocalizer.cs#L2192). **[실행 필요]** 카타카나가 긴 메뉴·버튼·툴팁, 100/125/150/200% DPI, 창 크기, 다중행 높이, WPF Run 줄바꿈, 웹 CSS, 일본어 금칙처리·한자 자형을 확인한다.

### 4.3 사전 밖 중국어와 번역 금지 요소

| 코드 안의 중국어 처리 | 일본어화 시 필요한 판단 |
|---|---|
| 요일 `星期…`/`周…`, `en-US`와 invariant culture 직접 변경 (`CMOLocalizer.cs:286`) | 최소판은 이 전역 변형 비활성화; 일본어 날짜는 표시 계층에서 처리하고 숫자 파싱 문화권 유지 |
| `Inherited` → `继承自上级，` (`:4590`) | 일본어 조합·문맥 정책으로 이동 |
| 제목 `现代行动：指挥`, `[未授权]`, `使用数据库:` (`:4668`) | 영어 fallback 또는 일본어 자원; 라이선스 상태 표시 의미 유지 |
| 단위 `英尺`, `千米`, `节` 및 수량·군사 명칭 규칙 (`:4834`) | 일본어 단위·어순 규칙과 Python mirror를 함께 수정 |
| 창 제목·Clear·`Every N seconds` (`:4342`, `:4452`, `:4476`) | `Game Options`에서도 적용될 수 있으므로 exact 함수만 바꾸어서는 부족 |
| DB marker·필드와 WebBrowser `.Replace` (`:2372`, `:2827`) | 일본어 자원화, DOM/속성 안전성 재검토 |
| WPF fuel·군사명 fallback (`WpfValueTranslation.cs:699`) | 일본어 모드에서 기존 중국어 fallback을 우회하거나 교체 |

**[확인]** 금지 데이터는 SQL 조각, 파일·디렉터리명, INI/DB 키, Lua 함수·코드, dispatch token, 버전·빌드 문자열, 형식 문자열이다. `inject_filter.py`, `blacklist.json`, `identifiers.json`이 이를 구분한다. 예를 들어 표시용 `Game Preferences`를 풀에 주입하면 설정 키까지 바뀔 수 있다. [빌더:258](../scripts/build_dict.py#L258), [주입 보호:21](../scripts/inject_filter.py#L21), [엔진 보호:557](../src/engine/CMOLocalizer.cs#L557).

**[설계]** `{0}`, `{0:F2}`, `%s`, 줄바꿈·탭, HTML `<%Category%>` 등 템플릿 변수, HTML 속성·링크 경로, `[LOADDOC]파일[/LOADDOC]`, Lua 식별자, DBID/모델명은 계약에 따라 보존한다. `&`는 WinForms mnemonic, `&&`는 리터럴일 수 있으므로 위치를 이해한 일본어 표기와 충돌 검사가 필요하다. F1~F12/Shift/Ctrl 등 실제 단축키 동작은 유지한다. 단축키 설명을 번역하는 것과 키 바인딩을 바꾸는 것을 구분한다.

빌더의 일반 `sanitize`는 괄호 불균형과 줄 수를 비교하지만, 모든 placeholder의 개수·타입을 보증하는 함수는 아니다. HTML exporter의 placeholder 비교는 별도 경로다. 일본어 UI 사전에도 동일한 토큰 보존 검사를 마련한다. exact lookup이 경로 보호보다 먼저 일어나므로 잘못 들어간 exact 키를 런타임 보호가 모두 막아 준다고 가정하지 않는다. [엔진:4645](../src/engine/CMOLocalizer.cs#L4645), [빌더:186](../scripts/build_dict.py#L186), [HTML 토큰 검사:84](../scripts/localize_html_files.py#L84).

### 4.4 영어 fallback과 언어 선택

**[확인]** 일반 `DoTranslate`의 마지막은 `return raw`이고 HTML exporter도 미등록 문구를 그대로 두고 누락을 기록한다. 독립 호출에서 `__JA_UNKNOWN_7F21__`이 그대로 반환됐다. 그러나 중국어 하드코딩과 기존 중국어 데이터가 남아 있으면 일본어 미등록 문구가 중국어로 변할 수 있다. 이미 중국어로 덮어쓴 HTML·PNG·풀은 일본어 사전의 빈 항목으로 영어 복원되지 않는다.

**[설계]** 초기에는 빌드 시 일본어 사전을 선택하고 게임 재시작 시 고정해도 된다. 사용자용 언어 선택 UI는 최소판의 필수 조건이 아니다. 반면 **언어별 입력 데이터와 동작 정책의 분리는 필수**다. 현재 `RELOAD_DICT`는 언어 전환 기능이 아니며 이미 주입된 풀·교체된 리소스의 원본 복구까지 제공하지 않는다. 실시간 언어 변경은 원문 보관·캐시 무효화·리소스 전환까지 설계한 후 확장한다.

## 5. 개발·검증·배포 조건

### 5.1 도구와 빌드의 현실적인 범위

**[확인]** 엔진은 Windows .NET Framework `Framework64/v4.0.30319/csc.exe`와 WinForms/WPF/System.Data/System.Xml 등 framework 참조로 컴파일한다. `scripts/build.ps1`은 `src/engine/*.cs`와 지도 renderer를 모으고 `WebViewTranslation.js`를 내장한다. 이 경로에는 게임 assembly나 별도 NuGet 참조가 없다. C# 5 문법 제약을 유지해야 한다. [빌드 입력:1](../scripts/build.ps1#L1), [참조·리소스:19](../scripts/build.ps1#L19).

이 PC에서 기존 엔진의 격리 빌드가 성공했다. Python `3.14.7`, Git for Windows Bash `5.3.15`, Node `v24.19.0`, .NET v4 Full Release 값 `534296`을 확인했다. Node/Edge/WebView2 SDK는 관련 웹 fixture에 필요할 수 있지만 최소 엔진 컴파일과 동일한 요구사항은 아니다. 본 세션에서는 SDK·실제 WebView2 호스트 검증을 하지 않았다.

전체 `scripts/build.sh`는 Bash/cygpath/PowerShell/Python과 게임 기반 HTML bundle을 요구한다. 기본 `PY`는 원 작성자 PC의 절대경로로 이 PC에 없으며, 기본 게임 경로는 `G:/soft/steam/...`이다. Git Bash를 명시적으로 사용하고 `PY`, `GAME_DIR`를 맞춰야 한다. 기본 `bash` 명령은 이 PC에서 WindowsApps 경로로 잡히므로 동일하게 동작한다고 가정하면 안 된다. [build.sh:8](../scripts/build.sh#L8), [HTML bundle:66](../scripts/build.sh#L66).

**[확인]** 이 클론에는 `assets/symbols_menu`, `assets/donate/wechat_pay.png`, `assets/donate/alipay.jpg`가 없다. 앞의 이미지는 조건부 ZIP 생성이나 패키징에 쓰이고, 후원 이미지는 설치기 컴파일 리소스로 직접 요구된다. 시나리오·QuickBattle ZIP도 소스 사전만으로 자동 확보되는 입력이 아니다. 따라서 **엔진 빌드 성공을 전체 설치기/릴리스 재현 성공으로 보고하지 않는다**. [build.sh:103](../scripts/build.sh#L103), [build.sh:135](../scripts/build.sh#L135), [package.sh:45](../scripts/package.sh#L45). 후원·개인 자산은 일본어판에 무심코 복제하지 말고 포함 여부와 권리를 따로 결정한다.

### 5.2 게임 없이 가능한 검사와 실제 게임이 필요한 검사

| 게임 없이 가능한 범위 | 실제 게임·적합 빌드에서 필요한 범위 |
|---|---|
| 엔진 컴파일, UTF-8/JSON 로딩, 토큰 보존 | config를 통한 CLR 로드와 시작 오류 |
| 원문→번역→재번역의 멱등성, 미등록 원문 보존 | 게임이 반복 갱신하는 메뉴·라벨의 언어 안정성 |
| 독립 WinForms/WPF 객체의 event·binding·입력 보호 | 실제 창/컨트롤의 이름·구조, owner-drawn UI |
| 합성 HTML의 DOM/속성/placeholder 유지 | 실제 브리핑·DB·QuickBattle WebView 동작 |
| 소스만의 사전 재빌드, 중복·누락·locale 혼입 검사 | 실제 pool offset과 표시 문자열의 대응 |
| 격리된 fixture 백업·복원 시뮬레이션 | 실제 게임 업데이트 후 복원·설정 유지·재시작 |
| 큐·취소·우선순위·성능 probe의 음성 대조군 | 화면 잘림·픽셀 깜박임·프레임 지연·게임 시간 진행 |

`scripts/test_ui.ps1`은 실제 게임을 실행하는 스크립트가 아니라 독립 fixture를 컴파일·실행하는 경로다. 라우팅 표의 설명과 차이가 있다. `test_subwindows.py`는 실행 중인 게임에 명령을 보내고 스크린샷을 만든다. `verify_all`과 `verify_regressions --static`도 전부 순수 파일 검사라고 가정할 수 없으며 빌드 산출물·설치 게임 파일 의존성을 읽어야 한다. [fixture:1](../scripts/test_ui.ps1#L1), [실기 창 검사:16](../scripts/test_subwindows.py#L16), [static:328](../scripts/verify_regressions.py#L328).

향후 설치기/실기 테스트는 별도 허용된 단계에서 수행한다. 프로젝트 지침대로 `--static --installer`와 `--live`를 분리하고, 게임 종료 가능성을 고려한다. 패키징·릴리스·tag·버전 변경은 별도 명시적 승인 대상이다.

### 5.3 라이선스·저작권·출처

**[확인]** 조사 커밋의 추적 파일명에서 LICENSE/LICENCE/COPYING/NOTICE/EULA를 찾지 못했다. README와 조사한 소스·스크립트·자원·문서에서 프로젝트 전체에 적용되는 명시적인 이용·변경·재배포 허가문도 확인하지 못했다. GitHub API의 원본·포크 `license` 값은 모두 `null`이다. 이는 자동 인식 결과이며 그 자체가 법적 판정은 아니다.

README는 정품 게임 사용을 요청하고 게임 본체를 포함하지 않는다고 설명한다([README:140](../README.md#L140)). 저장소의 “오픈소스”라는 소개나 공개 포크 가능성만으로 파생 번역·바이너리·자원의 외부 재배포 권한이 확정되지는 않는다. GitHub도 라이선스가 없는 저장소와 공개 저장소의 열람·포크 권한을 구분하여 설명한다. [GitHub 공식 라이선스 안내](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository).

**[추가 확인]** 배포 전에 원 유지관리자에게 코드·사전·이미지·지명 자료별 일본어 파생물 수정 및 소스/바이너리 재배포 허용 범위, 요구 고지, 적절한 라이선스를 확인해야 한다. 게임에서 추출한 DB 설명·시나리오·튜토리얼·HTML/이미지에 대한 게임 권리자의 조건도 별개다. 설치 게임 EULA와 원문 자산의 재배포 권한은 이번 조사에서 확정하지 못했다. 기술적으로 가능하다는 판단은 배포 권한 확보를 뜻하지 않는다.

**[설계]** 원 Git 이력과 원본 README·저작권 문구를 보존하고, 일본어판 문서에는 원본 저장소 URL, 기준 커밋, 원 프로젝트명, 일본어 파생판의 변경 범위와 작성자를 구분해 적는다. 허가가 정해지면 요구되는 LICENSE/NOTICE와 자산별 출처를 패키지에도 유지한다. 원본 전체를 임의로 새 라이선스로 선언하지 않는다. 이 세션에서는 보고서와 라우팅 등록만 커밋하며 허가 요청 메시지·release·패키지 배포를 수행하지 않았다.

## 6. 최소 구현 계획 — 주요 메뉴 + Game Options

아래는 **다음 구현 단계의 제안**이다. 파일 생성·코드 수정은 이번 단계에서 수행하지 않았다.

### 6.1 수정 파일과 순서

| 순서 | 파일 | 작업 |
|---|---|---|
| 1 | `docs/ROUTING.md`, 구현 명세 | 일본어 소스·테스트·정책 모듈의 새 파일 등록; 게임 빌드와 대상 메뉴/창의 수집 방법 확정 |
| 2 | `dict/src/ja_menu.json`, `dict/src/ja_options.json` (신규 제안) | 현재 원문의 exact key만 수록; 영어→일본어 직접 번역; 검토된 UI 항목만 포함 |
| 3 | `scripts/build_dict.py` | locale별 명시적 입력 선택, 일본어 유효성·토큰 검사, 격리 output 지원; 기본 중국어 출력과 입력 우선순위 보존 |
| 4 | `src/engine/CMOLocalizer.cs`, 필요시 `src/engine/LocalizationPolicy.cs` (신규 제안) | locale를 시작 부작용 전에 결정; 일본어 UI 전용 exact 경로, kana 멱등성·메뉴 갱신, 선택한 font, 범위 제한 |
| 5 | `src/engine/WpfValueTranslation.cs` 및 실제 대상 경로 | 일본어 모드에서 중국어 fuel/군사명 fallback 우회; 기존 바인딩·입력 보호 유지 |
| 6 | `src/tests/JapaneseLocalizationTests.cs` (신규 제안), 관련 Python 테스트·검증 | kana/한자/미번역 대조군, 이벤트 갱신, 동일 문구 재번역, 토큰·선택값·입력 보호, locale 혼입·음성 사례 |
| 7 | `scripts/build.ps1` 사용, `scripts/build.sh`는 필요 시 선택적 경로 추가 | 엔진만 격리 재빌드; 전체 설치기·지도·HTML·이미지 빌드와 분리 |
| 8 | 범위를 제한한 배치·복원 도구/절차 (후속 설계·등록) | 허용된 실행 검증 단계에서 DLL/JSON/config만 백업·적용·복원; 현 deploy.sh/설치기를 그대로 사용하지 않음 |

**중요한 범위 제한:** 최소 일본어 프로필에서는 `StartLocalTileServer`와 pool 주입, 중국어 전역 culture 변경, 오류 창 자동 종료, 비대상 창의 중국어 특수 처리, DB/웹/이미지 교체를 실행하지 않도록 해야 한다. 단순히 `offsets={}`로 설정하는 것과 다르다. 화면 탐색·이벤트 큐를 재사용하되 대상 컨트롤과 일반 exact 표시 경로를 연결한다. `Game Options`에 WPF나 사용자 정의 컨트롤이 포함되는지는 실기 수집으로 확정한다.

설정 창에는 `Every N seconds` 같은 동적 라벨이 있다는 코드 근거가 있다. 최소판은 지원 값을 확인하여 whole-string 번역하거나 별도의 작은 일본어 표시 규칙을 추가하고, 알 수 없는 값은 영어로 남긴다. `scenario_settings.json`은 게임 설정 창이 아니라 시나리오 지리적 설정 데이터이므로 이 작업 파일로 잘못 선택하지 않는다.

### 6.2 최소 데이터 계약

영어 키를 유지한 flat JSON 두 파일이면 시작할 수 있다. `File`, `View`, `Map`, `Simulation`, `Editor`, `Help`, `Game Options`는 현재 exact에 실제 존재한다. 아래는 **형식 설명용 예시**이며 새 번역 파일을 생성한 것이 아니다.

```json
{
  "File": "ファイル",
  "View": "表示",
  "Game Options": "ゲーム設定"
}
```

최소 산출물은 `_meta`에 명시적인 언어 식별(새 계약)을 갖고 `exact`에 검토된 UI 번역만 넣는다. `offsets`, `words`, `partial`, `comments`는 빈 맵으로 둔다. 시작 시 엔진도 이 언어 식별을 읽도록 바꾸어야 한다. 현재 코드는 `_meta` 언어 선택을 지원하지 않는다. `dict/CMOLocalizer.json`을 손으로 수정하지 않고 일본어 후보 산출물은 `build/ja-candidate/`에 만든다.

주요 메뉴의 하위 항목·툴팁과 `Game Options`의 탭/라벨/체크박스/선택 표시/버튼을 수집한 뒤 정확한 수량을 정한다. 현재 308개 메뉴 번역쌍은 후보 원문 목록이며, 그 숫자를 현 게임의 모든 메뉴 개수라고 보지 않는다. 동명 키가 다른 창에서 다른 의미를 가진다면 컨텍스트 허용 목록 또는 별도 표시 사전이 필요하다.

### 6.3 빌드·실행·복원 검증

1. 일본어 프로필로 데이터가 섞이지 않는지 검사하고 사전을 두 번 생성해 바이트 동일성을 확인한다. 현재 clone의 CRLF 문제는 아래 §7대로 먼저 개발 환경에서 정리할 계획을 세운다.
2. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -OutputDir build/ja-candidate`로 수정 엔진을 빌드한다. `--locale ja` 같은 Python CLI는 **새로 구현할 인터페이스**이지 현재 사용 가능한 명령이 아니다.
3. 독립 fixture에서 `File→ファイル`의 첫 표시와 TextChanged 재적용, 한자/카나/혼합·숫자·원문 fallback, `T(T(x))=T(x)`, 선택 인덱스·원시 값·WPF binding·편집 입력 보존을 확인한다. 일부 키를 제거하거나 placeholder를 변조했을 때 검사 실패도 입증한다. 기존 중국어 fixture 결과도 별도로 유지한다.
4. 실제 내부 빌드 `1900.24`를 확인한 게임에서 기준 영어 화면/설정 상태를 기록한다. 허용된 단계에서만 게임 종료 후 원본 config와 기존 DLL/JSON 존재 여부·SHA256을 백업하고 최소 세 파일만 적용한다. 임의 게임 실행 중 교체를 성공으로 간주하지 않는다.
5. 메뉴 열기/닫기/재열기, 단축키, 툴팁, Game Options 저장·취소·재열기와 영어 기준의 설정값 일치를 검증한다. DPI/잘림/갱신 중 영어 노출/중국어 잔류/응답 지연을 화면과 로그로 확인한다. 단순 카나 포함 개수만으로 화면 품질을 판정하지 않는다.
6. 복원 시 게임을 종료하고 같은 빌드에서 저장한 config를 복원한다. 시험 전 없던 DLL/JSON만 제거하고 기존 파일이 있었다면 백업을 복원한다. 원본과 SHA256을 비교한 뒤 영어 UI·설정 동작을 다시 확인한다. config가 시험 중 공식 업데이트로 바뀌었다면 과거 config를 덮어쓰지 말고 현 config에서 패치 loader만 제거하는 절차를 검토한다.

카나·언어 정책을 고치므로 **C# 재빌드는 필요**하다. 첫 최소판에는 설치기/업데이트 도우미 재빌드는 필요하지 않다. 이후 번역값만 변경하고 엔진 계약이 그대로라면 사전 재빌드·게임 재시작으로 검증할 수 있다. 완전 배포판의 내장 리소스 변경에는 설치기 재빌드가 필요하다.

### 6.4 전체 일본어화로 확장할 때

설정·명령 창별 실제 표시 수집 → 동적 상태·단위·군사 용어 → DB 목록·comments·8개 HTML 템플릿 → 브리핑·튜토리얼·QuickBattle → 지명·타일·이미지 버튼·Launcher 순으로 독립 검증 범위를 늘린다. 누락된 상용 원문은 사용자가 보유한 게임에서 읽기 전용으로 추출하고 출처·게임 빌드와 번역 검토 상태를 유지한다. 일본어 군사 용어와 UI 단축키를 함께 검토할 사람이 필요하다.

pool 주입은 UI 경로만으로 처리할 수 없는 표시를 찾은 뒤 게임 빌드별 원문 대응과 안전성을 검증해 제한적으로 도입한다. 지도 font/locale·주입 offset, 지명 검토 schema, 이미지 자산, 일본어판 updater/manifest/복원 계약은 각각 추가 작업이다. 전체 7만여 exact 항목을 번역하는 것만으로 모든 자원과 동적 문장이 완성되지 않는다.

## 7. 이번 세션의 실행 증거와 한계

### 7.1 실행한 검사

| 검사 | 결과·해석 |
|---|---|
| Git status/branch/remotes/log, GitHub user/repo/branch API | 깨끗한 최초 상태, 기준 커밋·포크·push 권한 확인; 작업 브랜치 생성 |
| `scripts/build.ps1 -OutputDir build/ja-feasibility` | **PASS**, 기존 엔진만 컴파일; 게임 파일 불필요 |
| 빌더 `sanitize` 직접 호출 | 카나 3개 제외, 한자 대조군 통과, 영어 대조군 제외; §4.1 |
| DLL reflection + 독립 ToolStripMenuItem | exact는 카나를 읽지만 TextChanged 재적용이 한자에만 성공; §4.1 |
| `python -B -X utf8 scripts/verify_engine_parse.py` | **PASS**, 다섯 섹션의 개수·값이 JSON과 동일 |
| `python -B -X utf8 src/tests/DictionarySourceTests.py` | **FAIL (exit 1)**; 작은 음성 fixture는 통과, 두 전체 재빌드 동일성 통과, 현 작업 파일과의 바이트 비교에서 실패. 이후 assertion들은 실행되지 않음 |
| `python -B -X utf8 scripts/verify_routing.py` | **PASS**, 변경 전 323개 패턴/414개 대상, 문서 추가 후 324개 패턴/415개 대상 |
| `python -B -X utf8 scripts/verify_no_secrets.py` | **PASS**, 검사 범위에서 의심 항목 0; 5MB를 넘는 기존 사전 2개는 검사기 정책상 제외됨. 변경 문서 2개는 크기 제한 이하 |

`verify_all`, 전체 static 회귀, installer/live 회귀, `deploy.sh`, `package.sh`, `publish_release.py`는 실행하지 않았다. 문서 링크의 파일 존재·줄 번호 범위와 변경 공백도 별도 검사했다. 기존 `ROUTING.md`는 저장된 blob과 작업 파일 모두 CRLF이므로 이를 보존했으며, Git 공백 검사는 `git -c core.whitespace=cr-at-eol diff --check`로 CRLF를 인정하여 수행한다. 새 보고서는 UTF-8/LF이다.

격리 DLL SHA256: `ccc8bbc19d099320023906725303be44f7d622f1180cd1658c6ab01cbd7d4f14`.
이는 이번 컴파일 결과의 식별자이며 C# DLL의 바이트 재현성을 주장하는 값은 아니다.

**사전 실패의 확인된 원인:** `core.autocrlf=true`이고 Git은 `dict/CMOLocalizer.json`을 `i/lf w/crlf`로 보고한다. 작업 파일은 13,813,221바이트, CRLF 87,081개이고 SHA256은 `355ab3950673bfd9f6fed3dce6ee364272105a70e89571e22557a961f3ec4787`이다. 두 재빌드는 각각 41.028초와 40.415초, 13,726,140바이트였으며 SHA256은 모두 다음과 같다.

```text
27ec76b33ae29d4ef28d77d0e4ae6e0fba60403e1a11a98ecfa6f2ad7b5067f4
```

`git show HEAD:dict/CMOLocalizer.json`의 SHA256도 위와 같고, 작업 파일의 CRLF를 메모리에서 LF로 치환한 바이트가 Git blob과 일치했다. 따라서 **커밋 원본에 대한 재빌드는 재현되지만, 현재 작업 트리에 대한 전체 테스트는 실패**라고 구분한다. 사전 파일·Git 줄바꿈 설정은 이번에 수정하지 않았다. 향후 적절한 `.gitattributes`/checkout 정책을 별도 검토해야 하며, 이 조사를 근거로 전 파일 줄바꿈을 정규화해서는 안 된다.

Python 3.14에서 `scripts/triage.py:266`의 기존 잘못된 escape에 대한 SyntaxWarning도 나왔다. 이번 실패의 원인은 아니며 수정하지 않았다.

재현 시 Python에 비ASCII 문자열을 PowerShell 기본 파이프로 넘기면 `?`로 바뀔 수 있으므로 아래처럼 Unicode escape를 쓰거나 UTF-8 파이프를 명시해야 한다. 첫 진단의 손실된 입력은 결과에서 제외하고 올바른 입력으로 재실행했다.

```powershell
python -B -X utf8 -c "import sys; sys.path.insert(0,'scripts'); import build_dict as b; print(b.sanitize('File','\u30d5\u30a1\u30a4\u30eb')); print(b.sanitize('Apply','\u9069\u7528'))"
```

메뉴 이벤트 재현은 컴파일한 DLL을 reflection으로 로드한 뒤 `ParseJsonSections`에 `{"comments":{},"offsets":{},"exact":{"File":"\u30d5\u30a1\u30a4\u30eb","Apply":"\u9069\u7528"},"partial":{},"words":{}}`를 전달했다. 각 원문을 가진 독립 `ToolStripMenuItem`에 private static `TranslateToolStripItem`을 호출하고 원문을 다시 대입했다. PowerShell에서는 reflection 인수에 `item.PSObject.BaseObject`를 사용해야 한다. `InitializeNewDomain`/`HookWorker`는 호출하지 않았으며 실제 게임 UI·pool·타일 서버를 시작하지 않았다.

### 7.2 문서 설명과 구현의 차이

| 문서 설명 | 이번 확인 |
|---|---|
| README의 실행 파일 비수정 | 로딩은 config, 런타임은 메모리·UI; 설치가 리소스까지 변경한다는 범위를 함께 설명해야 함 |
| ROUTING의 게임 빌드 예시 `1900.23` | 실제 단일 기준 `GAME_BUILD`와 공유 상수는 `1900.24` |
| 모든 번역 입력이 `dict/src/*.json`이라는 요약 | glossary/briefing 등 바깥 입력도 실제 빌더가 읽음 |
| README의 수동 config 덮어쓰기·ZIP 해제 FAQ | 최신 다운로드 구역은 같은 버전 설치기로 첫 apply를 요구하고, 실제 설치기는 대상 config 병합; ZIP 단순 복사로 최초 로드가 보장되지 않음 |
| ROUTING의 `test_ui.ps1` 게임 실행 필요 설명 | 실제 파일은 독립 fixture 전용이라고 명시하고 구현도 그 경로 |
| README만의 일반적인 소스 빌드 안내 | 개인 절대경로·누락 자산·HTML bundle·동결 버전 보호 조건이 있어 깨끗한 클론에서 전체 설치기 재현과 다름 |
| 과거 전체 중국어 검증 기록 | 현재 커밋/설치 게임/일본어판의 검증 증거가 아님 |

조사 범위를 지키기 위해 기존 README/코드/검증기/버그 대장은 수정하지 않았다. 새 버그를 고친 세션이 아니므로 `BUG_REGISTRY.md`에 수정 완료 항목도 추가하지 않았다.

### 7.3 남은 정보와 완료 조건

- 실제 설치 게임의 내부 `Build 1900.xx`, 최소 대상 창의 타입·컨트롤·표시 문자열, 게임 업데이트 이후 pool/타일 offset의 대응.
- 원 유지관리자의 파생 번역·소스/바이너리/자산 재배포 허가, 게임 원문 자원·이미지·지명 자료의 사용 조건.
- 일본어 군사 용어 기준과 메뉴 mnemonic, 전체 배포에 필요한 누락 자산의 출처 또는 합법적인 대체물.
- 최소 프로필 구현 후 일본어 갱신·입력/설정 보존·DPI·잘림·원문 fallback·영어 복원 검증.
- 전체 배포 전 빌드별 리소스 백업, 복원 후 해시, updater의 일본어 채널과 상태 격리, 실제 게임 장시간 검증.

## 8. 조사 변경의 Git 관리

조사 문서는 `docs/JAPANESE_LOCALIZATION_FEASIBILITY.md`이며 `docs/ROUTING.md`에 함께 등록한다. 보고서와 라우팅 등록만 커밋한다. `build/ja-feasibility/`의 기존 엔진 컴파일 결과는 무시되는 로컬 조사 산출물이며 커밋하지 않는다. 게임 본체·개인 설정·인증 정보·토큰·API 키를 문서나 커밋에 넣지 않는다.

작업 브랜치는 `localization/ja`, 푸시 대상은 **origin의 동일한 브랜치**다. upstream 직접 푸시·강제 푸시·이력 재작성·merge·PR·tag·릴리스·버전 변경은 이번 범위에 포함하지 않는다. 보고서가 자신의 커밋 해시를 내포하는 순환을 피하기 위해 최종 커밋 해시와 실제 푸시 성공/실패는 세션 최종 응답에서 구분하여 보고한다. 이후에는 `git log -1 --format=%H -- docs/JAPANESE_LOCALIZATION_FEASIBILITY.md`로 문서 커밋을 확인할 수 있다.
