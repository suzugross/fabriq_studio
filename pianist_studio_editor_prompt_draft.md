# Pianist Profile Editor 実装プロンプト（たたき台 v0.9）

> **改訂履歴**
> - v0.9 (2026-05-02): 内部整合性レビューによる修正のみ（新規決定なし）。①目的セクションの "UTF-8 BOM + CRLF の CSV/JSON" 表記を §10 規約準拠（CSV / JSON / TXT で異なる）に書き直し ②§3 / §4 / §5 / §6 末尾のエンコーディング記述を §10 への参照形式に統一（source-of-truth 二重化を解消） ③§9 の "Studio 起動時に確立されている前提 / 透過復号" 文言を §16 参照に置換（v0.3 の決定事項を反映） ④§13 の "Shortcuts ランチャ UI" を pianist.ps1 実行時 launcher の話だと明示し、§6 の Studio 編集 grid v1 実装と区別 ⑤§13 に hostlist.csv rename 自動追従が v1 スコープ外である旨を §5.2.D から再掲 ⑥§5.2.A.2 のメモリ参照をマシン固有絶対パスから auto-memory 名のみの記述に変更。
> - v0.8 (2026-05-02): §4.3 Key プリセットの canonical 形式を `%{F4}` に確定（自由入力で `%(F4)` も受理、保存時正規化なし）。§14 で pianist banner が `v1.0.0` のままである事実を注記し、本書は CHANGELOG.md の `[Unreleased]` 1.1.0 仕様を Source of Truth とする旨を明記。
> - v0.7 (2026-05-02): §5.2.A.1 / §5.2.A.2 を新設。hostlist.csv の NewPCName が 0 件 / 全消費済み の 2 ケースで異なるプレースホルダと誘導動作を規定。values.csv editor が hostlist.csv を書き換えない責務分離を明記。
> - v0.6 (2026-05-02): §7 に Phase の新規 / リネーム / 削除と `instructions/<PhaseID>.txt` の追従ルールを明文化。リネームは既定 ON で自動追従（実害回避）、削除は既定 OFF で保守的（誤削除リカバリ）の非対称設計。§11 で `PhaseID` セルは Step グリッド直接編集不可とする責務分離を補強。
> - v0.5 (2026-05-02): §5.2.E に列リネーム / 列削除の `$VarName` 参照検出ロジックを `\$Old\b`（単語境界付き）正規表現として明文化。列リネーム時の procedure.csv 一括書換を v1 機能として確定（§5.4 の "v2 候補" 表記を撤回し §5.2.E.2 へ統合）。
> - v0.4 (2026-05-02): §10 / §15 を「常に Studio 規約で正規化」モデルに fix。CSV / JSON / TXT のエンコーディングと改行コードを既存サンプル profile の実態（CSV=BOM+CRLF / JSON=BOM なし+LF / TXT=BOM なし+LF）に合わせて確定。架空メモリ参照（`feedback_ps1_utf8_bom.md`）を削除し、根拠を [CsvService.cs](FabriqStudio/Services/CsvService.cs) のコメントへ置換。
> - v0.3 (2026-05-02): §16 を「既存パターン + 復号モードトグル」に再 fix（自動発火 / 透過復号は既存実装に存在しないため、明示トグルで導入する形に修正）。連動して §5.2.F を rewrite。
> - v0.2 (2026-05-02): §4.4 / §5.2 / §16 の設計判断を確定。
> - v0.1 (2026-05-02): 初版

## 決定事項

| 議題 | 決定 |
|---|---|
| §4.4 Wait 列の二面性 | **CSV スキーマ不変、Studio UI 側で Action 別にラベル / 書き込み先を切り替えて吸収**（pianist 改修なし） |
| §5.2 行 = ホスト UX | **行追加は hostlist.csv の `NewPCName` ドロップダウンからのみ**（厳格、free-text 不可）／**`*` 行は先頭固定 1 行 / 削除不可 / 新規 values.csv 作成時に自動生成**／**空セルは `*` 行の値を dim italic で継承表示**／**保存時に orphan 行（hostlist にない PC 名）を警告**／**ドロップダウン空時は 2 ケース別プレースホルダ**（hostlist 0 件は BasicParams へ誘導、全消費済みは inline 通知のみ）／**values.csv editor は hostlist.csv を書き換えない**（責務分離） |
| §5.2.E 列リネーム / 削除 | **検出正規表現は `\$Old\b`（単語境界付き）+ `Regex.Escape(old)`** で `$AdminUser` 等への前方一致誤爆を防止／検出対象は **procedure.csv `Value` 列のみ**（pianist.ps1 の Expand-Variables の挙動準拠）／リネーム時は **procedure.csv 一括書換を既定 ON のチェックボックス**で提示し v1 で実装、削除時は影響行プレビュー表示のみで procedure.csv は触らない |
| §7 Phase / instructions の追従 | **新規作成時**: `instructions/<新 PhaseID>.txt` を空ファイルとして自動生成／**リネーム時**: 既定 ON のチェックボックスで `<旧>.txt` → `<新>.txt` を追従リネーム（OFF だと孤児 .txt 発生 + 新 ID で実行時プレースホルダ表示）／**削除時**: 既定 OFF のチェックボックスで `.txt` も削除（OFF だと孤児として残る、誤削除リカバリ用に保守的既定）／**Step グリッドで `PhaseID` セルを直接編集不可**にし、PhaseID 変更は Phase 一覧画面の専用ダイアログに集約 |
| §10 ファイル保存規約 | **「常に Studio 規約で正規化」モデル**を採用。CSV=BOM+CRLF、JSON=BOM なし+LF、TXT=BOM なし+LF で固定書き出し（読み込み時のメタは保持しない）／§15 の「未編集保存 = diff ゼロ」は規約準拠ファイルに対してのみ成立する仕様 |
| §16 パスフレーズ運用 | **既存「🔑 パスフレーズ」ボタンによる明示設定を踏襲**（ワークスペース open 時の自動発火はしない）／**values.csv editor 固有の追加 UI として「🔓 復号モード」トグル**を持たせ、ON のときに ENC: セルを透過復号して平文編集可能化／**保存時の暗号化判定は §5.2.F の鍵アイコン ON/OFF**に従う |
| §4.3 Key プリセット表記 | **canonical 形式は `%{F4}`** をプリセット採用／**自由入力では `%(F4)` パーレン形式も受理**（SendKeys として両方有効）／**保存時の正規化は行わない**（既存サンプルへの不要な書換を避ける） |
| §14 pianist バージョン基準 | **CHANGELOG.md の `[Unreleased]` 1.1.0 エントリを Source of Truth**とする／pianist.ps1 の banner / runtime ログが `v1.0.0` のままなのは fabriq 本体側の積み残しで、Studio 仕様としては 1.1.0（wide format / `*` 行 / ENC: セル単位暗号化）を前提に進める |


## 目的

fabriq 拡張モジュール `extended/pianist` (v1.1.0) の **Profile 作成・編集機能** を
Fabriq Studio に組み込む。実行は fabriq 本体側で完結しているため、Studio の責務は
**「読みやすく書きやすい編集 UX」と「ファイル契約に厳密に従った保存」** の 2 点に絞る。

オペレータ（多くは PC キッティング業務の現場担当）が日本語 UI で組み立て、保存時には
fabriq が読める英語キーワードと **§10 ファイル保存規約**（CSV: UTF-8 BOM + CRLF / JSON: UTF-8 BOM なし + LF / TXT: UTF-8 BOM なし + LF）に変換する、というのが基本方針。

---

## 1. Pianist とは何か（前提知識）

業務アプリ等で GUI 操作が必須な設定作業を、Phase × Step の手順マトリクスとして
実行する extended モジュール。1 Profile = 1 業務手順（例: kintone テナントの初期
設定 / Notepad でメモを書いて保存）。

オペレータは fabriq 起動 → Pianist 選択 → Profile 選択 → 表示された各 Phase の
`[Run Phase]` ボタンで自動操作実行 → 結果を OK/Warning/Error/Skip で記録 → 次 Phase へ。
Studio は **「この Profile を新規作成 / 編集する」UI** を提供する。

UI ポリシーで覚えておくべきこと:
- 実行時の Pianist GUI は **マウスのみ**（キーボード accel 禁止、SendKeys 競合回避）
- これは実行時 UX のルール。**Studio 編集側はキーボード操作 OK**（むしろ推奨）

---

## 2. Profile のディレクトリ構造（編集対象）

```
modules/extended/pianist/profiles/<profile_name>/
  pianist.json                   # メタデータ
  procedure.csv                  # 操作手続マトリクス（中心）
  values.csv                     # 変数プール（wide format, since 1.1.0）
  shortcuts.csv                  # 起動先ショートカット（v1.0 では参照のみ）
  instructions/
    <PhaseID>.txt                # Phase ごとのオペレータ向け手順テキスト
  screenshots/                   # Screenshot Step が出力するエビデンス置き場（編集対象外）
```

`<profile_name>` は **半角英数 + アンダースコアのみ**（フォルダ名 = ID として扱われ
プロファイル CSV から `Segment` 値で参照される）。

---

## 3. pianist.json スキーマ

```json
{
  "schema": 1,
  "label": "Kintone 管理者初期設定",
  "description": "Kintone のテナント管理者画面で初期パスワード変更とテナント名確認を行うサンプル",
  "target_app": "ブラウザ → kintone 管理コンソール",
  "default_phase": "P01",
  "version": "0.1.0"
}
```

- `label` / `description` / `target_app`: 完全自由記述。Pianist 実行 UI のヘッダーに表示
- `default_phase`: Pianist 起動時に最初に表示される Phase ID（省略時は先頭 Phase）
- `version`: profile 自身の SemVer（profile 作者管理、fabriq は読まない）
- `schema`: 現行 1 固定

エンコーディング: §10 表参照（**UTF-8 BOM なし + LF + 末尾改行あり**。pianist.ps1 の `ConvertFrom-Json` は BOM 有無不問で読めるが、Studio は §10 規約で正規化保存する）

---

## 4. procedure.csv スキーマ（編集の中心）

```csv
PhaseID,PhaseLabel,Color,StepNo,Action,Value,Wait,Note,Screenshot
P01,管理コンソールを開く,Blue,1,Open,$TenantUrl,,既定ブラウザで起動,
P01,管理コンソールを開く,Blue,2,WaitWin,Kintone,15000,タイトルに Kintone を含むまで待機,
P02,ログイン,Green,1,AppFocus,Kintone,,,
P02,ログイン,Green,2,Type,$AdminUser,800,ID 入力,
P02,ログイン,Green,3,Key,{TAB},300,,
```

| 列 | 用途 | 編集 UI の方針 |
|---|---|---|
| `PhaseID` | Phase の一意 ID（`P01` 等） | 自動採番 + 手動上書き可。重複禁止バリデーション |
| `PhaseLabel` | Phase ヘッダーバーに表示するラベル | 自由記述（日本語 OK） |
| `Color` | Phase ヘッダーの色 9 色 | カラーピッカー（後述パレット） |
| `StepNo` | Phase 内の通番（昇順実行） | 自動採番 + 行ドラッグ並べ替え対応推奨 |
| `Action` | アクション種別（10 種、後述） | **日本語ラベルでドロップダウン → 英名で書き出し** |
| `Value` | アクション引数（`$VarName` 展開対応） | Action 依存で UI 切替（後述） |
| `Wait` | 後続待機 ms / WaitWin の timeout ms | 数値入力。Action ごとに意味が違うのでラベル切替 |
| `Note` | プレビュー欄 / ログに表示するメモ | 自由記述 |
| `Screenshot` | screenshots/ 配下の参照画像（任意） | ファイルピッカー or ドロップ。**実行とは無関係の参照表示用** |

エンコーディング: §10 表参照（**UTF-8 BOM 付き + CRLF + 末尾改行あり**）

### 4.1 Phase Color パレット（9 色固定）

```
Blue / Green / Yellow / Orange / Red / Purple / Cyan / Pink / Gray
```

CSV には英名で書く。UI では各色の小プレビューと共に「青 / 緑 / 黄 / 橙 / 赤 / 紫 / シアン / ピンク / 灰」と表示してよい。

### 4.2 アクション 10 種と日本語化方針（重要）

**UI では日本語ラベル、CSV には Action 列の英名を書く**。実行時に pianist.ps1 の
`switch ($action)` が英名を見るので、ここは絶対に英名を保つ。

| Action（CSV 値） | 日本語ラベル（UI 表示候補） | Value の意味 | Wait の意味 |
|---|---|---|---|
| `Open` | アプリ・URL を開く | URL / 実行ファイル / `ms-settings:` / `shell:::{GUID}` | 後続待機 ms |
| `WaitWin` | ウィンドウが現れるのを待つ | 待つウィンドウタイトルの **部分一致**文字列 | **timeout ms**（このアクションだけ意味が違う） |
| `AppFocus` | ウィンドウを前面にする | フォーカスしたいウィンドウタイトルの部分一致 | 後続待機 ms |
| `Type` | キーボードで文字入力 | 送信文字列（`$VarName` 展開可） | 後続待機 ms |
| `Key` | 特殊キーを押す | SendKeys 形式: `{ENTER}` / `{TAB}` / `^v` / `%{F4}` 等 | 後続待機 ms |
| `Wait` | 一時停止 | 停止 ms（数値） | （同じく停止 ms。Value 優先） |
| `Copy` | クリップボードにコピー | コピーする文字列（`$VarName` 展開可） | 後続待機 ms |
| `Paste` | 貼り付け（コピー → Ctrl+V） | 貼り付ける文字列（`$VarName` 展開可） | 後続待機 ms |
| `Screenshot` | スクリーンショットを撮る | エビデンスのタグ名（任意） | 後続待機 ms |
| `Prompt` | オペレータに確認を求める | ダイアログに表示する説明文（`$VarName` 展開可） | 後続待機 ms |

### 4.3 Action ごとの Value 入力 UI（推奨）

| Action | Value 入力欄の UI |
|---|---|
| `Open` | テキストボックス + 補助メニュー（`shell:::` GUID 集 / `ms-settings:` 一覧 / ファイルピッカー） |
| `WaitWin` / `AppFocus` | テキストボックス + ヒント「ウィンドウタイトルの一部でマッチします（日本語/英語版で異なる場合あり）」 |
| `Type` / `Paste` / `Copy` / `Prompt` | テキストエリア + **`$VarName` オートコンプリート**（values.csv の列名から） |
| `Key` | プリセットドロップダウン（`{ENTER}` / `{TAB}` / `^v`(Ctrl+V) / `^c`(Ctrl+C) / `^s`(Ctrl+S) / `%{F4}`(Alt+F4) / `^a`(Ctrl+A)）+ 自由入力。**プリセットは canonical 形式 `%{F4}` を採用**するが、自由入力では `%(F4)` のパーレン形式も受理する（SendKeys として両方有効）。**保存時の正規化は行わない**（既存サンプル profile の `%(F4)` を勝手に書き換えない方針） |
| `Wait` | 数値入力（ms 単位、プリセット 300/1000/3000/5000） |
| `Screenshot` | 短いタグ名テキスト |

### 4.4 Wait 列の二面性（決定済み: UI 側で吸収）

procedure.csv の `Wait` 列は実装上 3 つの意味を持つ:

| Action | Wait 列の意味 |
|---|---|
| `WaitWin` | ウィンドウ出現の **タイムアウト ms**（未指定時 10000 default） |
| `Wait` | 停止 ms（**Value 列が優先**、Value 空なら Wait を見る） |
| 残り 8 種 | 完了後の追加スリープ ms（任意） |

CSV スキーマは触らず、Studio 編集 UI が Action 選択に応じてラベルと書き込み先を切り替えて吸収する:

| Action 選択時 | 入力欄ラベル | 書き込み先列 |
|---|---|---|
| `WaitWin` | **タイムアウト (ms)**（未入力で 10000 自動補完） | `Wait` |
| `Wait` | **停止時間 (ms)** | `Value`（pianist が優先する側） |
| その他 9 種 | **実行後の待機 (ms)**（任意） | `Wait` |

---

## 5. values.csv スキーマ（since pianist 1.1.0、wide format）

```csv
NewPCName,TenantUrl,AdminUser,AdminPass,NewPass
*,https://default.example.com,,,
PC-001,https://t1.example.com,admin01,ENC:abc123==,ENC:def456==
PC-002,https://t2.example.com,admin02,ENC:ghi789==,ENC:jkl012==
```

### 5.1 解決規則（pianist.ps1 側で実装済み、編集側は理解だけ）

1. 1 行目 = ヘッダー、`NewPCName` 必須 + 任意の変数列
2. **`*` 行（または NewPCName 空欄行）** = 全ホスト共通デフォルト
3. 実行時、`$env:SELECTED_NEW_PCNAME` と一致する行を検索
4. 該当行 + その列にセル値あり → 採用
5. セル空 / 該当行なし → `*` 行の同列で補完

### 5.2 編集 UI 要件（決定済み）

#### 5.2.A 行（ホスト名）の入力ソース: **厳格**
- 行追加は `kernel/csv/hostlist.csv` の `NewPCName` 列をソースとした **ドロップダウンからの選択のみ**（free-text override 不可）
- 既に values.csv に書かれた `NewPCName` はドロップダウンの候補から除外（重複防止）
- これによりタイポによる「どの PC にも紐づかない孤立行」を構造的に発生させない

##### 5.2.A.1 ドロップダウンが空になる 2 ケースの UX

| 状況 | プレースホルダ | 「行追加」ボタン押下時の挙動 | 誘導先 |
|---|---|---|---|
| **(1) hostlist.csv に NewPCName が 0 件** | `(hostlist.csv に登録された PC 名がありません)` | 確認ダイアログ「hostlist.csv に NewPCName が登録されていません。BasicParams 画面で追加しますか？」→「移動」/「キャンセル」 | 「移動」選択時、未保存差分があれば破棄確認（既存 [ReloadWorkspace](FabriqStudio/ViewModels/MainViewModel.cs#L249) と同等のパターン）→ BasicParams（HostList）画面へ navigate |
| **(2) 全 NewPCName が values.csv に消費済み** | `(追加可能な PC 名がありません)` | ボタン押下時に inline メッセージ「追加できる NewPCName がありません。すべての登録済み PC 名が既に values.csv にあります」を一定時間表示（または status bar に出す） | 誘導先なし（ユーザーは hostlist.csv に追加するか既存行を編集する） |
| **通常** | （候補表示） | 即追加 | — |

##### 5.2.A.2 責務分離

- **values.csv editor は hostlist.csv を書き換えない**。空時に inline で簡易追加させる UX は、values.csv editor が hostlist.csv の owner になってしまう責務逸脱のため採用しない（必ず BasicParams 画面経由）
- ナビゲーションは既存の [MainViewModel.NavigateCommand](FabriqStudio/ViewModels/MainViewModel.cs#L150) または `WeakReferenceMessenger` 経由でメッセージ送信（実装フェーズで判断）
- ナビゲーション後、ユーザーが hostlist.csv に NewPCName を追加して戻ってきた場合、**values.csv editor の `WorkspaceChanged` 受信または再表示時にドロップダウン候補を再構築**する必要がある（auto-memory `feedback_combobox_itemssource` のとおり、共有 ObservableCollection を ItemsSource にしている場合は **Clear+Add 禁止 / 差分マージで更新**。Reset 通知が他行 ComboBox の SelectedItem を null 化するため）

#### 5.2.B `*` 行（共通デフォルト）: **先頭固定 1 行**
- 新規 values.csv 作成時、Studio が自動的に `NewPCName='*'` 行を生成
- 常に 1 行のみ、削除不可、位置は先頭固定
- ユーザはセル値の編集のみ可能、行そのものの操作は不可
- これにより「`*` 行不在で保存」「複数 `*` 行で挙動曖昧」の事故が構造的に発生しない

#### 5.2.C 空セルの視覚化: **dim italic で継承表示**
- セル自体は技術的に空（CSV にも空文字で書く）
- 表示時に `*` 行の同列値を **dim grey + italic** で重ねて描画（Excel の数式継承表示に近い感覚）
- セルにフォーカスを当てて編集を開始した瞬間、dim 表示は消えて空文字エディタになる
- 編集後に空のまま確定 → またフォールバック表示に戻る
- `*` 行が空のセルは表示も空（dim 表示なし）

#### 5.2.D hostlist.csv との sync: **保存時 orphan 警告のみ**
- 保存時に values.csv の `NewPCName` 値群を hostlist.csv の `NewPCName` 列と照合
- hostlist にない PC 名があれば「該当ホストが hostlist.csv にありません」警告ダイアログ + 一覧表示
- **5.2.A の厳格化により orphan は通常発生しない** が、外部編集や hostlist 側の事後削除で発生し得るため保険として残す
- hostlist の rename を values.csv へ自動追従する機能は v1 スコープ外（v2 候補）

#### 5.2.E 列・行追加 UX

| 操作 | 動作 |
|---|---|
| 列追加（変数） | ダイアログで列名を尋ね、`^[A-Za-z_][A-Za-z0-9_]*$` を即時バリデート、`NewPCName` は予約として弾く、既存列名との重複チェック、既存全行に空セル生成 |
| 列削除 | 確認ダイアログ + procedure.csv の `$VarName` 参照箇所を §5.2.E.1 の正規表現で grep して影響行を提示。procedure.csv は **自動書換しない**（ユーザーが手動で対処） |
| 列リネーム | §5.2.E.2 を参照（v1 で実装、procedure.csv の自動一括書換含む） |
| 行追加（ホスト） | hostlist.csv ドロップダウンで未使用の PC 名を選択、追加時は全セル空（= default フォールバック） |
| 行削除 | 確認のみ。`*` 行は対象外（5.2.B） |

##### 5.2.E.1 `$VarName` 参照検出の正規表現（共通）

列削除のインパクト表示および列リネームの一括書換で使う、`procedure.csv` の `$VarName` 参照を検出する正規表現:

```csharp
// 例: 列名 "Admin" の参照を検出（"$Admin" にはヒット、"$AdminUser" にはヒットしない）
var pattern = @"\$" + Regex.Escape(columnName) + @"\b";
```

- `\b` 単語境界で **前方一致誤爆を防止**（`$Admin` を `$User` にリネームしても `$AdminUser` は無傷）
- `Regex.Escape` は防御策（§5.3 列名バリデーションにより通常はメタ文字を含まないが、念のため）
- 検出対象列: `procedure.csv` の **`Value` 列のみ**（pianist.ps1 の [Expand-Variables](E:/fabriq/modules/extended/pianist/pianist.ps1#L342) が `Value` 経由でしか変数展開しないため）
- `Note` 列・`instructions/*.txt` は実行時に変数展開されないが、人間向けドキュメントとして `$VarName` を含む可能性がある。**v1 では検出対象外**（残課題 §17 へ）

##### 5.2.E.2 列リネーム時の動作

1. 列ヘッダーのコンテキストメニュー or 専用ボタンから「列名を変更...」ダイアログ起動
2. 新しい列名を入力。**§5.3 列名バリデーション**を即時適用、既存列名との重複（自分自身を除く）も即時警告
3. ダイアログ内に **「procedure.csv の `$<旧列名>` を `$<新列名>` に一括書換する」チェックボックスを既定 ON で表示**
4. 「プレビュー」ボタンで影響行を別ダイアログに一覧表示（PhaseID / StepNo / 旧 Value / 新 Value）
5. 確定時:
   - values.csv 上の列ヘッダーを変更
   - チェックボックス ON なら procedure.csv の `Value` 列を §5.2.E.1 の正規表現で一括置換: `Regex.Replace(value, @"\$" + Regex.Escape(old) + @"\b", "$" + newName)`
   - チェックボックス OFF なら values.csv のみ変更（既存 procedure.csv の参照は壊れる → 手動修正の責任はユーザー）

##### 5.2.E.3 既定値 ON の根拠

- リネーム時に procedure.csv の参照を残すと **実行時 silent failure**（pianist.ps1 は未定義 `$VarName` を文字列のままにする → 想定外の入力 / コピペ / フォーカスとなる）
- 既定 OFF だと「リネーム = 参照壊れ」が頻発するため、ON 既定でユーザーが意図的に外す UX が妥当

#### 5.2.F セル単位の暗号化トグル（鍵アイコン）
- 各セルに「🔑 鍵アイコン」を配置。**保存時に暗号化するかどうかの状態フラグ**として機能
- 既定値:
  - 既存 ENC: セル → 鍵アイコン ON（読み込み時に判定）
  - 平文セル → 鍵アイコン OFF
- 鍵アイコン ON のセル: 保存時に `ENC:<Base64>` 形式で書き出し
- 鍵アイコン OFF のセル: 平文のまま書き出し
- ユーザーは ON ⇄ OFF を任意のタイミングで切替可能
- 鍵アイコンの状態は復号モード（§16）の ON/OFF と独立。復号モード OFF でも鍵アイコンの個別 ON/OFF は触れる（ただし復号モード OFF の ENC: セルは値編集自体が不可）
- master passphrase 解決は §9 / §16 を参照

### 5.3 列名の制約（バリデーション必須）

- 正規表現: `^[A-Za-z_][A-Za-z0-9_]*$`（procedure.csv 側の `Expand-Variables` が
  `\$([A-Za-z_][A-Za-z0-9_]*)` で参照するため）
- **日本語列名は不可**（procedure.csv 側で `$変数名` と書けないため）
- 予約語: `NewPCName`（行キー専用）

### 5.4 procedure.csv との連動

- procedure.csv の `Value` 列で `$AdminUser` のように書かれた変数を、values.csv の
  列名から **オートコンプリート + 未定義警告** する（オートコンプリートは §4.3 参照）
- values.csv 側の列リネーム → procedure.csv の `$Old` → `$New` 一括書換は **v1 で実装**（§5.2.E.2）。検出ロジックは §5.2.E.1 の単語境界付き正規表現で誤爆を防ぐ

### 5.5 後方互換（重要）

- 旧形式 `Key,Value,Encrypted,Note` の values.csv が存在する古い profile も
  pianist.ps1 は読める（`Build-PianistValuesDict` がヘッダーで自動判別）
- **Studio 編集側は新規保存時は wide format で書く**。読み込み時に旧形式を
  検出したら「新形式に変換しますか？」ダイアログを出すのが望ましい
- 移行ロジック: 旧 `Key` 列の値が wide の列名へ、`Value` セル値が `*` 行へ、
  `Encrypted=1` の値は `ENC:` prefix を付けて格納（既に prefix がある場合はそのまま）

エンコーディング: §10 表参照（**UTF-8 BOM 付き + CRLF + 末尾改行あり**）

---

## 6. shortcuts.csv（v1 では参照のみ）

```csv
Label,Type,Path,Args,Note
Run dialog (manual),exe,explorer.exe,shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0},手動で Run ダイアログ
Open Desktop,exe,explorer.exe,%USERPROFILE%\Desktop,デスクトップを目視確認
```

pianist.ps1 v1 では UI から呼ばれていない（CSV ファイル参照のみ）。Studio エディタは
**シンプルな表 grid で編集できる程度で OK**。将来 Pianist 側に Shortcuts ランチャ UI が
できたときに肉付け。

エンコーディング: §10 表参照（**UTF-8 BOM 付き + CRLF + 末尾改行あり**、他 CSV と統一）

---

## 7. instructions/<PhaseID>.txt

各 Phase に対応する **オペレータ向けプレーンテキスト手順書**。Pianist の Phase ビューで
そのまま画面表示される。Markdown 不要・記法不要。改行で読みやすく区切るだけ。

```
P03: 初期パスワード変更

ログイン直後にパスワード変更ダイアログが表示されます。
[Run Phase] を押すと:
  1. 旧パスワードを入力
  2. 新パスワードを 2 回入力
  3. 送信

期待される画面: 「パスワードが変更されました」のメッセージが表示される。

達成したら → ボタンで次の Phase へ。失敗したら [Phase Status...] で
Error をマークし、状況をメモに残してください。
```

### 7.1 ファイル名規約と pianist.ps1 の挙動

- 1 Phase = 1 ファイルの一対一対応。ファイル名は `<PhaseID>.txt` 固定（`P01.txt` 等）
- pianist.ps1 は Phase 表示時に `instructions/<PhaseID>.txt` を都度 read（[pianist.ps1:698-716](E:/fabriq/modules/extended/pianist/pianist.ps1#L698-L716)）
- **ファイルが無くても実行は続行**。プレースホルダ「(no instruction file at instructions/P01.txt)」が手順書欄に表示されるだけで、致命的エラーではない
- procedure.csv に存在しない PhaseID の孤児 .txt も実行時は完全に無視される
- 編集側のエンコーディング規約は §10 参照（UTF-8 BOM なし + LF + 末尾改行）

### 7.2 PhaseID 変更時の追従ルール（決定済み）

実行時の機能維持には影響しないが、**オペレータ向け手順書の整合性**を保つため、Phase の新規 / リネーム / 削除と instructions/<PhaseID>.txt の操作を Studio 側で追従させる。

| ケース | 動作 | 既定 |
|---|---|---|
| **Phase 新規作成** | `instructions/<新 PhaseID>.txt` を空ファイルとして自動生成（確認ダイアログなし） | 自動 |
| **Phase ID リネーム** | 確認ダイアログにチェックボックス「`instructions/<旧>.txt` を `<新>.txt` にリネームする」を **既定 ON** で提示。OFF 時は孤児ファイル発生（ユーザー責任） | チェック ON |
| **Phase 削除** | 確認ダイアログにチェックボックス「`instructions/<削除する>.txt` も削除する」を **既定 OFF** で提示。OFF 時は手順書ファイルが残る（v1 仕様、誤操作リカバリ用に保守的） | チェック OFF |

非対称（リネーム ON / 削除 OFF）の根拠:
- リネーム時にファイルを残すと「P01 を開いたら手順書欄がプレースホルダ」になり実害（手順書が表示されない）
- 削除時にファイルを残しても実行時無害（孤児として無視される）。Phase を再作成したい場合の保険として残す価値がある。Studio に Undo 履歴がないため、削除を確実に避ける既定が妥当

### 7.3 衝突時の挙動

| 状況 | 挙動 |
|---|---|
| Phase ID リネーム先の PhaseID が procedure.csv に既存 | ダイアログでバリデーションエラー（リネーム不可、ユーザーは別 ID を入力） |
| リネーム先の `<新>.txt` が既存ファイルとして残っている（過去のリネーム OFF / 削除 OFF で発生した孤児） | 確認ダイアログ「既存ファイルがあります。上書きしますか？ / リネームを中止しますか？」 |
| Phase 新規作成時、`<新>.txt` が既存（孤児）として残っている | 既存ファイルを再利用（上書きしない、空ファイル生成もしない）。ユーザーが過去の手順書を再利用したいケースを尊重 |

### 7.4 編集 UI 要件

- 1 Phase = 1 ファイルの一対一対応
- マルチライン textarea で編集（Phase 詳細画面に同居）
- プレビュー機能は v2 候補
- §11 の Phase 一覧画面に **Phase 単位の操作（PhaseID 変更ダイアログ / Phase 削除）** を集約。procedure.csv の Step グリッド上で `PhaseID` セルを直接編集させない（同 PhaseID の複数行で食い違い発生を防ぐ）

エンコーディング: §10 表参照（**UTF-8 BOM なし + LF + 末尾改行あり**、pianist.ps1 が読み込み時に CRLF 正規化）

---

## 8. screenshots/

procedure.csv の `Screenshot` 列で参照される **画像ファイルの置き場**。Studio 編集側の
責務は:
- ドロップ or ファイルピッカーで画像を投入
- ファイル名を procedure.csv の `Screenshot` セルに書き戻す
- 表示プレビュー（任意）

**Pianist 実行時に出力されるエビデンス画像は別フォルダ（kernel が管理）**。Studio が
編集対象として触るのは、procedure.csv に紐づく **参考画像**（例: 「この画面が出たら
P03 で確認」みたいなオペレータ向け資料）の方。

---

## 9. 暗号化統合（fabriq 既存規約に準拠）

values.csv のセル単位暗号化は fabriq 全体規約と完全一致:

- アルゴリズム: AES-256-CBC + PBKDF2-HMAC-SHA256
- 反復回数: 100,000
- ソルト: `fabriq-fixed-salt-2024`（固定）
- エンコード: 平文 UTF-8、暗号文 Base64
- セル形式: `ENC:<Base64>`（prefix あり）

実装は Studio 既存の [CryptoService.cs](FabriqStudio/Services/CryptoService.cs) を再利用（hostlist.csv 編集で実績あり）。同じシングルトンを values.csv editor から DI 経由で参照する。

master passphrase の運用フロー（確立タイミング / 復号モードトグル / 保存時の再暗号化判定 / パスフレーズ未確立時の挙動）は **§16 参照**。**平文での永続化は禁止**。

---

## 10. ファイル保存規約まとめ（決定済み: 常に Studio 規約で正規化）

Studio は読み込み時のバイト形態を保持せず、**保存時に常に下表の規約で書き出す**（"open → save = 正規化" モデル）。fabriq 既存サービス [CsvService.WriteAsync](FabriqStudio/Services/CsvService.cs#L37-L50) が既に BOM 付き UTF-8 で書いており、サンプル profile も全 CSV が下表の規約と一致しているため、**規約に従った既存 profile を未編集で保存してもバイト一致が保たれる**（§15 の検証基準を達成）。

| ファイル種別 | エンコーディング | 改行 | 末尾改行 | 根拠 |
|---|---|---|---|---|
| `*.csv`（pianist 配下すべて） | **UTF-8 BOM 付き** | **CRLF** | あり | PS 5.1 `Import-Csv -Encoding UTF8` が BOM を手掛かりに UTF-8 を判定。BOM なしだと日本語環境で ANSI 誤判定して文字化け（既存 [CsvService.cs:43-44](FabriqStudio/Services/CsvService.cs#L43-L44) コメントの懸念） |
| `pianist.json` | **UTF-8 BOM なし** | **LF** | あり | `ConvertFrom-Json` は BOM 有無不問。サンプル profile が BOM なし+LF で書かれている。新規作成時にこの形に揃える |
| `instructions/*.txt` | **UTF-8 BOM なし** | **LF** | あり | pianist.ps1 が `[System.IO.File]::ReadAllText(..., UTF8)` で読込み、内部で CRLF/LF を吸収するため LF で揃える |

### 10.1 実装メモ

- **CSV**: 既存 [CsvService.WriteAsync](FabriqStudio/Services/CsvService.cs#L37) を再利用。CsvHelper の `CsvConfiguration.NewLine` が `Environment.NewLine` 依存だと将来クロスプラットフォーム動作で破綻するため、values.csv / procedure.csv / shortcuts.csv の保存時は `NewLine = "\r\n"` を明示する（実装フェーズで CsvService 側へ反映するか pianist editor 側で個別設定するかは判断）
- **JSON**: `JsonSerializer.Serialize` + `new StreamWriter(path, false, new UTF8Encoding(false))` + `WriteIndented = true` + `NewLine = "\n"`（System.Text.Json 8.0 の `JsonWriterOptions.NewLine`）で書き出す helper を pianist editor 用に追加
- **TXT**: `File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(false))` 相当

### 10.2 規約破りファイルの扱い

外部エディタ（VS Code の改行設定 LF / 一部の SaaS 系エクスポートで BOM なし）で規約外に保存されたファイルを Studio が開いた場合:
- 読込みは可能（CsvHelper / JsonSerializer / File.ReadAllText いずれも BOM 有無・LF/CRLF 不問）
- **保存時は §10 規約へ正規化される**（仕様）。「未編集保存で diff ゼロ」の保証は §10 規約準拠ファイルに対してのみ
- profile を Studio 上で開いて保存し、PS 側で正常に読める状態になる方がプロジェクト全体の整合性として優先される

---

## 11. 編集 UX の全体構造（推奨）

```
[Profile 一覧]
  └─ 既存 Profile を選ぶ or 新規作成
      └─ [Profile エディタ]
            ├─ メタタブ        (pianist.json)
            ├─ Phase 一覧      (procedure.csv の Phase 集約ビュー)
            │    └─ Phase 詳細 (Step グリッド + instructions/<ID>.txt エディタ)
            ├─ 変数            (values.csv wide grid)
            ├─ ショートカット  (shortcuts.csv 表 grid)
            └─ プレビュー      (procedure 全体ダンプ / 整合性チェック結果)
```

### Phase 詳細画面の Step グリッド（最重要）
- 行ドラッグで StepNo 並べ替え
- アクションをドロップダウンから選ぶと Value 入力欄が動的に切り替わる
- `$VarName` の入力中は values.csv 列名からサジェスト
- Wait 列のラベルが `WaitWin` 時だけ「タイムアウト」に変わる
- **`PhaseID` セルは Step グリッド上で直接編集させない**（読み取り専用表示）。PhaseID 変更は Phase 一覧画面の「Phase 編集」ダイアログから行う（§7.2 / §7.3）。これは同 PhaseID を共有する複数 Step 行の整合性を保つため

---

## 12. バリデーション一覧

実装時に揃えておきたいチェック:

| 項目 | 重要度 |
|---|---|
| Phase ID 重複なし | 必須 |
| Phase 内 StepNo 重複なし（連番でなくても OK だが昇順） | 推奨 |
| Action は 10 種のいずれか | 必須 |
| Color は 9 色のいずれか | 必須 |
| Value 内の `$VarName` 参照が values.csv 列に存在する | 警告 |
| values.csv 列名が `^[A-Za-z_][A-Za-z0-9_]*$` | 必須 |
| values.csv に `NewPCName` 列がある | 必須 |
| `*` 行（共通 default）が高々 1 行 | 推奨 |
| `default_phase` が procedure.csv に存在する PhaseID | 警告 |
| instructions/<PhaseID>.txt の有無 | 警告（無くても動く） |

---

## 13. v1 スコープ外（やらないこと）

- Pianist 側の機能拡張（録画 / UIA / IF/LOOP / 並列）— 実行モジュール側の話で Studio
  には関係ない
- pianist.ps1 **実行時** の Shortcuts ランチャ UI（pianist.ps1 v1.x 側で未実装）。**Studio 側の shortcuts.csv 編集 grid（§6）は v1 で実装する**ので混同注意
- Profile 間の変数共有 / グローバルプール（後で必要なら別契約として設計）
- procedure.csv の自動録画機能（操作トレースから生成）
- hostlist.csv 側で `NewPCName` がリネームされた時の values.csv への自動追従（§5.2.D で v2 候補として既に明示）

---

## 14. 参照すべき既存実装・ドキュメント

| ファイル | 何が書いてあるか |
|---|---|
| `modules/extended/pianist/pianist.ps1` | kernel 側読み手の決定版実装。Action パーサ、変数展開、ENC: 復号など。⚠ banner / runtime ログは `v1.0.0` のままだが、コード本体（`Build-PianistValuesDict` の wide format 対応）と内部コメント（L225 / L229）は 1.1.0 仕様。本書は **CHANGELOG.md の `[Unreleased]` を Source of Truth として v1.1.0 仕様で進める**。banner 未更新は fabriq 本体側の積み残しで Studio の責務外 |
| `modules/extended/pianist/Guide.txt` | オペレータ視点での Profile 構造解説（編集 UX のメンタルモデル参考） |
| `modules/extended/pianist/profiles/notepad_memo_to_desktop/` | 動くサンプル 1（ローカルアプリ系） |
| `modules/extended/pianist/profiles/example_kintone_admin/` | 動くサンプル 2（Web アプリ + ENC: + Prompt 介入）。`Key` 列に `%(F4)` パーレン形式が含まれている — §4.3 の通り Studio はこれを正規化しない |
| `kernel/csv/hostlist.csv` | NewPCName 列の既存値（values.csv 行候補の供給源） |
| `kernel/KERNEL_API.md §3` | `SELECTED_NEW_PCNAME` 等の env var 契約 |
| `CHANGELOG.md` の `[Unreleased] / Changed` の pianist 1.1.0 エントリ | values.csv 移行の決定経緯（**本書のバージョン基準**） |

---

## 15. 開発手順の推奨

1. **読み込みと表示先行** — まず既存 Profile（notepad / kintone サンプル）を読み込んで
   各タブに展開できるところまで作る。書き込みは後回し
2. **書き込みでサンプルが壊れないか検証** — Studio で開いて何も変えずに保存 → diff が
   生成されない（バイト一致）ことを確認。エンコーディング規約（§10）を守れている証拠。
   ⚠ この検証は **§10 規約準拠ファイル**（= サンプル 2 profile および Studio で新規作成した
   profile）に対してのみ成立する。外部エディタで規約破りに保存されたファイルは Studio
   保存時に §10 規約へ正規化されるため diff が出るが、これは仕様（§10.2 参照）
3. **新規 Profile 作成 UI** — テンプレから雛形を生成して Pianist で実行できるところまで
4. **values.csv の wide grid 編集 + 復号モードトグル + 暗号化** — §16.2 の復号モード ON で
   平文編集 → §5.2.F の鍵アイコン ON のセルだけ保存時に再暗号化、を一周実機検証
5. **バリデーション** — 上記 §12 の項目を順に実装

---

## 16. パスフレーズ運用（決定済み: 既存パターン踏襲 + 復号モードトグル）

Studio の暗号化パスフレーズ運用は既存実装（[MainViewModel.SetPassphrase](FabriqStudio/ViewModels/MainViewModel.cs#L171) / [HostDetailViewModel.EncryptField/DecryptField](FabriqStudio/ViewModels/HostDetailViewModel.cs#L180-L224)）の哲学を踏襲する: **「自動発火しない、ユーザーが明示操作した時のみ動く」**。

values.csv editor 固有の追加 UI として「🔓 復号モード」トグルを 1 つだけ追加し、wide grid の実用性を確保する。

### 16.1 パスフレーズ設定（既存挙動を維持）

- 設定経路: 左ペイン下「🔑 パスフレーズ」ボタン → 既存 `MainViewModel.SetPassphraseCommand` が起動
- `passphrase_verify.txt` が既にあれば、入力されたパスフレーズで照合 → 不一致なら警告で中断
- 検証成功時のみ `ICryptoService.MasterPassphrase` にセッション保存
- **ワークスペースを open しただけでは何も発火しない**（既存挙動を変更しない）
- pianist profile editor を開いただけでも自動発火しない

### 16.2 復号モードトグル（values.csv editor 固有）

values.csv editor のツールバーに `🔓 復号モードで表示` トグルボタンを 1 つ配置:

| 状態 | grid の見た目 | 編集可否 |
|---|---|---|
| OFF（既定） | ENC: セルは `ENC:abc123==` の文字列のまま、平文セルは平文表示 | ENC: セルは IsReadOnly=True、平文セルのみ編集可 |
| ON | ENC: セルを透過復号して平文表示、平文セルもそのまま | 全セル編集可 |

トグル OFF → ON の切替動作:
1. `_crypto.HasPassphrase` が false なら `MainViewModel.SetPassphraseCommand` 相当を起動（既存ダイアログを再利用、専用ダイアログは作らない）
2. ユーザーがキャンセル / 検証失敗 → トグルは OFF に戻す
3. パスフレーズ確立後、grid 内の `ENC:` セルを順次復号して `ViewModel` 上の表示用プロパティへ展開
4. 復号失敗セルは `(復号エラー)` のプレースホルダ + 当該セルだけ IsReadOnly=True にしてユーザーに状態を見せる

トグル ON → OFF の切替動作:
1. 編集中に未保存差分があれば確認ダイアログ（破棄して OFF にするか）
2. OFF にしたら、表示用プロパティを再描画（鍵アイコン ON のセルは `ENC:...` 文字列、OFF のセルは平文）

### 16.3 保存時の暗号化判定（§5.2.F と連動）

- 保存処理は **復号モードの ON/OFF に依存しない**
- 各セルの「鍵アイコン状態」を見て、ON のセルだけ `_crypto.Encrypt(value, _crypto.MasterPassphrase)` で `ENC:` 形式へ
- 鍵アイコン OFF のセルはそのまま平文書き出し
- 鍵アイコン ON だがパスフレーズ未確立で保存しようとした場合: 保存を中断し、「🔑 パスフレーズを設定してください」エラー（既存 [HostDetailViewModel.cs:184-185](FabriqStudio/ViewModels/HostDetailViewModel.cs#L184-L185) と同じメッセージ規約）

### 16.4 既存実装との関係

- `ICryptoService` / `CryptoService` は無改修で再利用（HostDetailView と同じシングルトンを参照）
- `MainViewModel.SetPassphraseCommand` は無改修。values.csv editor 側からは public な `ICommand` 経由で呼び出すか、`SetPassphrase` を `ICryptoService` 拡張に切り出して共有するかの実装判断は実装フェーズで詰める
- HostDetailView 側は本書の対象外（既存仕様維持）

---

## 17. 残課題（v1 実装中に判断する小粒項目）

- procedure.csv の Phase / Step のドラッグ並べ替え UX のキーボード操作対応範囲
- 旧形式 values.csv 検出時の自動移行ダイアログの文言
- Profile を別の Profile からコピーする「複製」UI を v1 で出すか
- §5.2.E.1 の `$VarName` 検出を `Note` 列および `instructions/*.txt` にも広げるか（実行時には影響なし、ドキュメント整合性のためだけの拡張）

ここから先は実装を進めながら詰める段階。
