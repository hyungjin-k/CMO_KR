# 일본어 번역 용어집·규칙 (ja)

`dict/src/ja_*.json`을 번역할 때 따르는 기준이다. 본문(`ja_prose.json`: 시나리오 설명·브리핑·메시지)은 아래 규칙을 `python scripts/japanese_dict.py --check-prose FILE`이 기계적으로 검사한다.

## 1. 형식 규칙 (검사기가 강제)

1. 키(영어 원문)는 바이트 그대로 둔다. 본문 키는 공백이 정규화된 한 문단이다.
2. 값(일본어)은 한 줄이다. 줄바꿈·연속 공백을 넣지 않는다. 문단을 합치거나 나누지 않는다.
3. 그대로 둘 것: 모든 숫자, `F1`~`F12`, `CTRL`/`SHIFT`/`ALT`/`ESC`/`ENTER`, `Ctrl + X` 형태의 조합, `{0}`·`%s` 같은 자리표시자, Windows 경로(`C:\...`, 폴더 이름 포함), ASCII 괄호의 짝.
4. **큰따옴표로 묶인 UI 이름은 영어 그대로 둔다**(`"Ready/Arm"`, `"Launch Individually"`, `"SPACE"`). 게임의 해당 버튼·패널이 아직 영어로 표시되기 때문이다. 필요하면 뒤에 일본어 설명을 덧붙인다: `"Launch Individually"(個別発進)ボタン`.

## 2. 문체

- 본문은 です・ます調. 지시문은 「〜してください」「〜します」.
- 무기·기체·함정 제식명, 장비명, 탑재 구성명(`Light 202`), 콜사인(`Sufa #4`), 인명·닉네임은 원문 그대로.
- 지명은 일반적인 일본어 표기(가타카나): Ramat David → ラマト・ダビド, Northern Israel → イスラエル北部.
- 약어는 처음 나올 때 `日本語 (略語)` 형태로 쓰고, 이후에는 어느 쪽이든 좋다.

## 3. 용어

| 영어 | 일본어 |
|---|---|
| Air Ops (window) | 航空運用 (Air Ops) ウィンドウ |
| Unit Status panel | "Unit Status" パネル（ユニット状態） |
| loadout | 搭載兵装 |
| Quick Turnaround | クイックターンアラウンド |
| sortie | 出撃 |
| ready time | 準備時間 |
| Surge / Sustained Operations | 集中運用 (Surge Operations) / 持続運用 (Sustained Operations) |
| Doctrine, Doctrine Settings | ドクトリン、ドクトリン設定 |
| Side Doctrine Window | 陣営ドクトリンウィンドウ |
| scenario designer / Editor | シナリオ作成者 / エディター |
| Reserve / Ferry / Maintenance (status) | 予備 / 回航 (Ferry) / 整備 |
| AEW, AWACS | 早期警戒 (AEW)、AWACS |
| patrol / CAP | 哨戒 / 戦闘空中哨戒 (CAP) |
| waypoint, course, plot a course | ウェイポイント、航路、航路を設定する |
| speed and altitude | 速度と高度 |
| MANPADS | 携帯式防空ミサイル (MANPADS) |
| Winchester / Bingo | 弾薬切れ (Winchester) / 燃料限界 (Bingo) |
| return to base (RTB) | 基地へ帰還 (RTB) |
| mission, Mission Editor | ミッション、ミッションエディター |
| Reference Point | 基準点 |
| Air-to-Air / Air-to-Ground missile | 空対空ミサイル / 空対地ミサイル |
| Anti-radiation missile | 対レーダーミサイル |
| glide bomb | 滑空爆弾 |
| contact | 探知目標 |
| detect / engage / intercept | 探知する / 交戦する / 迎撃する |
| emissions, EMCON | 電波放射、EMCON（電波管制） |
| target / hostile / unfriendly / unknown | 目標 / 敵対 / 非友好 / 不明 |
| side | 陣営 |
| time compression | 時間圧縮 |
| simulation | シミュレーション |
| WEAPONS FREE / TIGHT / HOLD | 兵器使用自由 / 兵器使用制限 / 兵器使用禁止 |
| Rules of Engagement (RoE) | 交戦規定 (RoE) |
| Weapon Control Status (WCS) | 兵器管制状態 (WCS) |
| tanker, aerial refueling | 空中給油機、空中給油 |
| formation | 編隊 |
| fighter / bomber / strike | 戦闘機 / 爆撃機 / 攻撃 |
| air base | 空軍基地 |
| Message Log | メッセージログ |
| Special Message | 特別メッセージ |
| pop-up | ポップアップ |
| All-Aspect / Rear-Aspect | オールアスペクト（全方位） / リアアスペクト（後方限定） |
| (Semi-)Active Radar Homing | セミアクティブ・レーダー・ホーミング (SARH) / アクティブ・レーダー・ホーミング (ARH) |
| mid-course / terminal guidance | 中間誘導 / 終末誘導 |
| High Off-Boresight | 高オフボアサイト |
| WRA (Weapon Release Authorization) | WRA（兵器発射許可） |
| flight size | 編隊の機数 (flight size) |
| bogey | 国籍不明機 (bogey) |
| Stern-Chase | 後方追尾 (Stern-Chase) |

라운드 1(Flight Tutorial 1~8)에서는 같은 개념이 `全方位`/`オールアスペクト`처럼 묶음마다 다르게 쓰인 곳이 있다. 새 번역은 위 표기를 따른다.

## 4. 번역 회차 진행 방법

1. `python scripts/extract_ja_prose.py worklist "<게임>/Scenarios/..." --out build/ja-prose/<이름>.json`으로 미번역 문단 목록을 만든다(값은 출처 HTML 파일명).
2. 번역 결과 `{영어: 일본어}`를 `python scripts/japanese_dict.py --check-prose <파일>`로 검사한다.
3. `dict/src/ja_prose.json`에 합친 뒤 `extract_ja_prose.py coverage`로 누락 0을 확인한다.
4. 실기에서 기록된 `CMOLocalizerJa_untranslated.tsv`의 `prose` 줄도 같은 방식으로 처리한다.
