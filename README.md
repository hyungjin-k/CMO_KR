# CMO_KR

> 이 저장소는 [mj8724/CMO_CN](https://github.com/mj8724/CMO_CN)(《Command: Modern Operations》 중국어 패치)의 포크입니다.
> 이 포크의 `localization/ja` 브랜치에서는 같은 기반 위에 **일본어 패치**를 중국어 엔진과 분리된 별도 DLL로 개발하고 있습니다.
> 사용법·적용 범위·검증 결과는 [docs/JAPANESE_MINIMAL_UI.md](docs/JAPANESE_MINIMAL_UI.md)를 보세요. 일본어 패치는 아직 배포하지 않습니다.
>
> 아래는 원 프로젝트(중국어 패치)의 설명을 한국어로 옮긴 것입니다. 다운로드 링크와 Issue는 원 프로젝트를 가리킵니다.

《Command: Modern Operations》의 완전한 중국어 패치이자 현지화 프로젝트입니다.

.NET `AppDomainManager` 메커니즘으로 로드되며, 게임 본체인 `Command.exe` 파일은 수정하지 않습니다.

---

## 다운로드 (원 프로젝트의 중국어 패치)

아래 표와 안내(중국어)는 `scripts/sync_readme_downloads.py`가 생성하고 게이트가 바이트 단위로 검사하는 영역이라 원문 그대로 둡니다. 요약:

- **안정판**(관리자가 안정성을 확인한 버전)과 **테스트판**(최신 수정 포함, 검증이 덜 됨) 두 채널이 있습니다. 어느 쪽인지 모르겠다면 안정판을 고르세요.
- 각 채널마다 **설치기**(`CMO_Chinese_Setup_v<버전>.exe`)와 **녹색 패키지**(ZIP)를 받을 수 있습니다. 전체 이력은 원 프로젝트의 [Releases](https://github.com/mj8724/CMO_CN/releases)에 있습니다.
- 녹색 패키지는 **압축만 풀어서는 적용되지 않습니다.** 별도 임시 폴더에 풀고, 게임을 종료한 뒤 패키지 안의 같은 버전 설치기를 `--install "<게임 루트>" --silent`로 실행하세요. 버전을 섞어 쓰지 마세요.
- `--adopt "<게임 루트>" --silent`는 이미 설치된 파일을 설치 상태로 등록만 하며, 최초 적용을 대신하지 않습니다.

<!-- downloads:begin -->
## 下载

| 通道 | 适合谁 | 版本 | 下载 |
|---|---|---|---|
| **稳定版** | 想安稳玩、不想折腾 | **`4.2.5`**（维护者人工确认） | [安装器](https://github.com/mj8724/CMO_CN/releases/download/v4.2.5/CMO_Chinese_Setup_v4.2.5.exe) · [绿色包](https://github.com/mj8724/CMO_CN/releases/download/v4.2.5/CMO_Chinese_Pack_4.2.5.zip) |
| **测试版** | 想第一时间拿到最新修复 | **`4.3.0`**（本页登记版本） | [安装器](https://github.com/mj8724/CMO_CN/releases/download/v4.3.0/CMO_Chinese_Setup_v4.3.0.exe) · [绿色包](https://github.com/mj8724/CMO_CN/releases/download/v4.3.0/CMO_Chinese_Pack_4.3.0.zip) |

> **稳定版**链接固定指向 `v4.2.5`，只有维护者确认新版本稳定后才会提升。
> **测试版**链接固定指向本页标注的 `v4.3.0`，可能包含尚未充分验证的修复。
> 两个下载都绑定明确版本；最近一次发布与全部历史版本见 [Releases](https://github.com/mj8724/CMO_CN/releases)。
>
> 不确定选哪个？**选稳定版**。想帮忙验证修复，再用测试版。

### 绿色包首次启用（不是只解压即生效）

把 ZIP 解压到独立临时目录（不要先覆盖游戏目录），退出游戏后在该目录运行包内同版本安装器：

```powershell
& ".\CMO_Chinese_Setup_v4.3.0.exe" --install "<游戏根目录>" --silent
```

稳定版绿色包使用它对应的 `CMO_Chinese_Setup_v4.2.5.exe` 执行相同参数；历史 ZIP 若未附安装器，须从同一 tag 另行下载该版本安装器到临时目录，不要混用版本。
安装器统一部署内嵌载荷、合并目标自己的官方 runtime/bindingRedirects、保留首次 `.cmo_bak`/`.orig` 备份与玩家设置，并登记安装状态。
`--adopt "<游戏根目录>" --silent` 仅登记已有完整载荷，不代替首次 apply；不要改名/覆盖 `Command.exe.config.patch` 或其他旧备份。
<!-- downloads:end -->

---

## 버전과 대응 게임 빌드

- **대응하는 게임 내부 빌드: `1900.24`** —— 게임 제목에 표시되는 `Command: Modern Operations v1.10 - Build 1900.24`입니다
  (저장소 루트의 `GAME_BUILD`가 유일한 기준이며, `verify_all`이 모든 곳의 일치 여부를 검사합니다).
- 이 패치 자체의 버전은 저장소 루트의 `VERSION`에 있습니다. 설치 파일 이름, 설치기 제목, 업데이트 도우미 안내에 표시됩니다.
- **게임이 업데이트되어 빌드 번호가 바뀔 때마다 패치가 동작하지 않을 수 있습니다**(게임이 내부 난독화 멤버 이름과 화면 문구를 바꾸기 때문입니다).
  이때 이 저장소는 **부 버전을 1 올리고 수정 번호를 0으로 되돌린 뒤**(예: 4.1.8 → **4.2.0**) 다시 대응합니다. 맞는 버전인지는 위의 빌드 번호와 게임 제목의 Build 번호가 같은지로 판단하세요.

---

## 포함 내용

- **화면과 메뉴**: 메인 화면, 상단 툴바, 10개 주 메뉴 전체와 다단계 하위 메뉴, 우클릭 메뉴.
- **업무 창**: 항공 운용(Air Ops), 준비 완료 항공기(Ready Aircraft), 교전 규칙(RoE), 미션 편집기, 전투 서열(OoB), 게임 옵션 등.
- **장비 데이터베이스**: 항공기·함선·잠수함·지상 시설·무기·센서 등 8개 분류의 일반 제원, 전술 능력, 작동 주파수 대역, 무기 탑재 구성.
- **튜토리얼과 전황 메시지**: 공식 튜토리얼 45개 전체(비행, 다영역 타격, 대지 타격, 수상, 잠수함, 대잠, 전자전, 공중급유, Uncle Mark 튜토리얼). 튜토리얼 안내의 키(F1~F12 등)와 버튼 이름을 실제 화면에 맞췄습니다.
- **지도 지명**: 전 세계 321개 국가·해역과 4,287개 도시 지명의 중국어화.

---

## 실기 스크린샷

### 튜토리얼 전황 메시지와 준비 완료 항공기 창
![튜토리얼 메시지와 준비 완료 항공기](evidence/live/tutorials/17_message_2_and_ready_aircraft_side_by_side.png)

### 항공 운용 (Air Ops) 창
![항공 운용](evidence/live/tutorials/08_air_ops_ramat_david_fixed.png)

### 데이터베이스 뷰어(외부 무장과 탑재 구성)
![데이터베이스 뷰어](evidence/live/tutorials/07_db_viewer_weapons_translated.png)

### 전장 메인 화면
![전장 메인 화면](evidence/live/tutorials/10_right_panel_flying_frame1.png)

---

## 설치와 사용

### 1. 설치기 사용(권장)

배포 패키지의 `CMO_Chinese_Setup.exe`를 받습니다.

- **그래픽 화면**: 더블클릭해 실행하고 게임 설치 폴더를 고른 뒤 「一键安装汉化」(한 번에 중국어 패치 설치)를 누릅니다. 영어 원본으로 되돌리려면 「还原英文原版」(영어 원본 복원)을 누릅니다.
- **명령줄 무인 설치**:
  ```cmd
  CMO_Chinese_Setup.exe /install "게임 폴더 경로"
  ```
- **명령줄 무인 제거**:
  ```cmd
  CMO_Chinese_Setup.exe /uninstall "게임 폴더 경로"
  ```

### 2. 수동 설치

배포 패키지를 풀어 게임 루트 폴더에 복사합니다(같은 이름의 파일을 덮어쓰기 전에 원본을 백업하세요).
- `CMOLocalizer.dll` -> 게임 루트 폴더
- `CMOLocalizer.json` -> 게임 루트 폴더
- `Command.exe.config` -> 게임 루트 폴더(AppDomainManager 로더 설정 추가)
- `DB/Templates/*.html` -> `DB/Templates/` 폴더에 덮어쓰기
- `Symbols/Menu/*.png` -> `Symbols/Menu/` 폴더에 덮어쓰기

---

## 소스에서 빌드

Windows 환경과 .NET Framework 4.8, Python 3.10 이상이 필요합니다.

```bash
# 1. 사전 생성
python scripts/build_db_enums.py
python scripts/build_dict.py

# 2. 엔진과 설치기 컴파일
bash scripts/build.sh

# 3. 검증 게이트 실행
python scripts/verify_all.py
```

빌드 결과물은 `build/` 폴더에 생깁니다.
- `build/CMOLocalizer.dll`
- `build/CMO_Chinese_Setup.exe`

일본어 패치는 별도 스크립트로 빌드·검증합니다: `scripts/build_ja.ps1`, `scripts/test_ja.ps1`([docs/JAPANESE_MINIMAL_UI.md](docs/JAPANESE_MINIMAL_UI.md) 참고).

---

## 폴더 구조

```text
├── src/
│   ├── engine/       # 현지화 엔진 소스 (CMOLocalizer.cs)
│   ├── installer/    # 원클릭 설치기 소스 (CMO_Chinese_Setup.cs)
│   └── ja/           # 일본어 패치 엔진 (이 포크, CMOLocalizerJa.dll)
├── dict/
│   ├── CMOLocalizer.json     # 실행용 메인 사전
│   ├── glossary.json         # 핵심 군사 용어집
│   ├── brief_translations.json # 브리핑·튜토리얼 번역 사전
│   └── src/                  # 모듈별 원본 사전 (메뉴/항공 운용/데이터베이스 열거형 등, ja_*.json은 일본어)
├── assets/           # 중국어화한 데이터베이스 HTML 템플릿
├── scripts/          # 빌드·패키징·검증·배포 스크립트
├── docs/             # 용어집, 실측 보고서, 변경 기록
└── evidence/live/    # 실기 테스트 스크린샷
```

---

## 안내

이 프로젝트에는 패치 관련 소스·사전·리소스만 들어 있으며, 게임 본체나 상용 파일은 포함하지 않습니다. 정품 게임을 사용하는 것을 전제로 이용해 주세요.

## 커뮤니티

문제가 있거나 번역 누락을 발견하면 원 프로젝트의 [Issues](https://github.com/mj8724/CMO_CN/issues)에 바로 질문해 주세요.

> Issue는 계속 남아 있어 나중에 오는 사람도 검색할 수 있으므로, 보통 다른 채널보다 빨리 해결됩니다.

---

## 자주 묻는 질문(FAQ)

**Q: Steam에서 게임 파일을 검사한 뒤 중국어 패치가 사라졌어요.**
A: Steam의 「게임 파일 무결성 확인」이나 게임 업데이트는 Steam이 관리하는 파일(지도·브리핑·버튼 이미지 포함)을 원래대로 되돌립니다.
`CMO_Chinese_Setup.exe`를 한 번 다시 실행하면 복구됩니다. 녹색 패키지 사용자는 다시 풀어서 덮어써도 됩니다.

**Q: 지도나 툴바 버튼이 여전히 영어예요.**
A: **v4.1.4 이상**을 설치했는지 확인하세요. 그 이전 버전은 중국어 지명 세트와 중국어 버튼 이미지를 함께 배포하지 않았으므로,
옛 패키지를 다시 설치해도 바뀌지 않습니다. 설치 후에는 엔진에 내장된 로컬 중국어 타일 서버가 지도 라벨을 실시간으로 만들어, 어느 지역을 보든 중국어로 표시됩니다.

**Q: 새 버전으로 어떻게 업데이트하나요?**
A: v4.1.4부터 「업데이트 도우미」가 들어 있습니다. Windows와 함께 시작해 6시간마다 자동으로 확인하며, 트레이 아이콘 우클릭 메뉴에서
즉시 확인·설정·로그 보기를 할 수 있습니다. 설치기에도 「检查升级」(업그레이드 확인)·「不再提醒」(다시 알리지 않음) 버튼이 있습니다. **v4.1.3 이하에는 업데이트 도우미가 없으므로
v4.1.4를 한 번 직접 내려받아 설치해야** 그 뒤부터 자동으로 업그레이드됩니다.

**Q: 영어로 완전히 되돌리려면?**
A: `CMO_Chinese_Setup.exe` → 「一键还原原版英文」(원클릭 영어 원본 복원)을 실행하세요. 복원은 업데이트 도우미 프로세스를 종료하고 시작 프로그램 항목을 지우지만,
설정해 둔 업데이트 주기·알림 방식 같은 설정은 **지우지 않습니다**(다시 설치하면 그대로 적용됩니다). 설정까지 함께 지우려면
옆의 「还原时同时清除我的全部设置」(복원할 때 내 설정도 모두 지우기)를 체크하세요(명령줄은 `--uninstall ... --purge-settings`).

**Q: 새 버전을 설치했는데 맞지 않아요. 이전 버전으로 돌아갈 수 있나요?**
A: 됩니다. 설치·업그레이드할 때 설치 패키지를 이 PC에 자동으로 캐시해 두므로, 설치기의 「回退到上一版本 vX」(이전 버전 vX로 되돌리기) 버튼이나
업데이트 도우미 트레이 메뉴의 「回退到上一版本…」로 바로 오프라인 롤백할 수 있습니다(롤백 대상은 이 PC에 캐시된 버전이어야 합니다).
명령줄로는 다음과 같습니다.

```
CMO_Chinese_Setup.exe --rollback "<게임 폴더>" --silent
CMO_Chinese_Setup.exe --list-versions "<게임 폴더>"      # 설치된 버전과 로컬 캐시 버전 보기
```

**Q: 녹색 패키지(ZIP)는 어떻게 쓰나요? 설치 후 상태 표시가 맞나요?**
A: 압축 파일 안의 파일을 게임 폴더에 풀어 덮어쓰면 됩니다. 설치 상태·업데이트·롤백이 제대로 동작하도록,
푼 뒤 **같은 패키지의 `CMO_Chinese_Setup.exe` 또는 `CMO_Updater.exe`를 적어도 한 번 실행하세요.** 이 프로그램들이 자동으로
**설치 상태를 등록**합니다(실제로 배치된 파일을 읽어 목록에 기록하며, 파일을 다시 덮어쓰지 않습니다). 직접 실행할 수도 있습니다.

```
CMO_Chinese_Setup.exe --adopt "<게임 폴더>"
```

등록하지 않아도 중국어 패치는 동작합니다. 다만 설치기에 「已安装汉化补丁（已登记当前状态）」(중국어 패치 설치됨, 현재 상태 등록됨)으로 표시됩니다.

**Q: 업데이트를 확인할 때 「发现可回退版本」(롤백 가능한 버전 발견)이라고 나오는 건 무슨 뜻인가요?**
A: 이 PC에 그 버전의 설치 패키지가 **이미 캐시되어 있을 때만** 나오는 안내입니다(롤백용).
온라인보다 새로운(미발표) 버전을 쓰고 있고 이 PC에 캐시가 없다면, 업데이트 확인은 조용히 넘어가며 방해하지 않습니다.
