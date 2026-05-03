# Pianist Profile Editor: Window Picker 機能追加プロンプト

## 背景

Fabriq Studio の Pianist Profile Editor は既に実装完了している。本機能追加は、editor の `procedure.csv` 編集タブで `WaitWin` / `AppFocus` アクションの Value 欄を書く作業を支援するための **「現在開いているウィンドウのタイトル一覧から選択して挿入する」** ピッカー機能を追加するもの。

## 何のための機能か

Pianist の `WaitWin` / `AppFocus` アクションは、対象ウィンドウのタイトルバー文字列の **部分一致サブストリング** を Value 列に書く。これを手作業で調べるには:

1. 対象アプリ / ダイアログを実際に開く
2. ウィンドウ最上部のタイトルバーを目視で読む
3. 一意に識別できる断片を選んで Value 欄にコピペ

という地道な作業が必要で、特にダイアログ系（`ファイル名を指定して実行` / `名前を付けて保存` 等）はフォーカスを奪われると目視確認しにくいため、profile 作成体験のボトルネックになっている。

本機能は、editor 内から **現在開いている全ウィンドウのタイトルを Win32 EnumWindows で列挙 → 一覧表示 → クリックで Value 欄へ挿入** する流れを提供する。

## 機能仕様

### 1. 起動位置

`procedure.csv` 編集タブの Step エディタにおいて、選択中の Action が `WaitWin` または `AppFocus` のときに **Value 入力欄の隣に「ウィンドウを選択...」ボタン** を表示する。それ以外の Action では非表示。

### 2. ピッカーダイアログ

ボタンクリックでモーダルダイアログを開く。レイアウト案:

```
┌─────────────────────────────────────────────────────────────┐
│ ウィンドウを選択                                       [×] │
├─────────────────────────────────────────────────────────────┤
│  [ 🔄 更新 ]   [ ⏱ 5 秒待ってから取得 ]   [ ☑ ノイズを隠す ]  │
├─────────────────────────────────────────────────────────────┤
│ タイトル                                プロセス             │
│─────────────────────────────────────────────────────────────│
│ PC - エクスプローラー                   explorer.exe        │
│ share - エクスプローラー                explorer.exe        │
│ Understand Pianist… - fabriq - VSCode  Code.exe             │
│ 管理者: Windows PowerShell              powershell.exe      │
│ ファイル名を指定して実行                explorer.exe        │
│ ...                                                         │
├─────────────────────────────────────────────────────────────┤
│ 選択中: 「ファイル名を指定して実行」                         │
│ Value 欄に挿入する文字列: [ ファイル名を指定して実行    ▼] │
│  → 選択肢: フルタイトル / 「ファイル名を指定して実行」      │
│           ／「を指定して実行」など分割候補                  │
├─────────────────────────────────────────────────────────────┤
│                            [ キャンセル ]  [ 挿入して閉じる ]│
└─────────────────────────────────────────────────────────────┘
```

### 3. コア動作

#### 3.1 ウィンドウ列挙
- Win32 `EnumWindows` で全トップレベルウィンドウを総当たり
- `IsWindowVisible` が true のもののみ
- `GetWindowTextW`（512 char buffer）でタイトル取得
- 空タイトルは除外
- `GetWindowThreadProcessId` でプロセス名を取得して列に表示

#### 3.2 カウントダウン取得（ダイアログ捕捉用）
- 「5 秒待ってから取得」ボタンを押すと 5 秒のカウントダウン後に列挙実行
- カウントダウン中は **ダイアログ自体を非アクティブ化** し、ユーザが Win+R / Ctrl+S 等で対象ダイアログを開けるようにする
- カウントダウン残秒数を表示
- カウントダウン中に取得済みリストを更新表示（リアルタイムスナップショットでも可）

#### 3.3 ノイズフィルタ
「ノイズを隠す」チェック ON（デフォルト ON）で以下を除外:

| 除外対象 | 理由 |
|---|---|
| `Program Manager` | デスクトップシェル本体、操作対象にならない |
| `Windows 入力エクスペリエンス` | IME の背景常駐ウィンドウ |
| `MSCTFIME UI` | IME 関連の隠れ窓 |
| `Default IME` | 同上 |
| `Microsoft Text Input Application` | 同上 |
| 自身の Studio ウィンドウ | `Process.GetCurrentProcess().Id` と `GetWindowThreadProcessId` で照合して除外 |
| ピッカーダイアログ自身 | 同上 |

OFF にすると全件表示（デバッグ用途）。

#### 3.4 リスト操作
- ソート: タイトル昇順がデフォルト、列ヘッダクリックでプロセス名ソート切替
- ダブルクリック: 即「挿入して閉じる」
- 単クリック: 選択 + 下部プレビューに「Value 欄に挿入する文字列」のドロップダウン候補を表示

#### 3.5 挿入文字列の候補生成
選択行のフルタイトルから、以下の候補をドロップダウンに並べる:

1. **フルタイトル**（例: `share - エクスプローラー`）
2. **`-` で区切った各部分**
   - `share`
   - `エクスプローラー`
3. **`：` / `:` で区切った各部分**（`管理者: Windows PowerShell` → `管理者` / `Windows PowerShell`）
4. **半角スペース連続区切りで先頭の "アプリ識別子っぽい部分"**

候補の並び順はそのままドロップダウンに（フルタイトル優先）。ユーザは編集も可能。

### 4. C# 実装ガイド

#### 4.1 P/Invoke 定義
fabriq の `pianist.ps1` 内 `PianistWin32` クラスと対称な定義を使うことで、Pianist 実行時の挙動と Picker の見つけ方が必ず一致する:

```csharp
internal static class WindowEnumNative
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
```

#### 4.2 列挙ロジック
```csharp
public sealed record WindowInfo(string Title, string ProcessName, uint ProcessId, IntPtr Handle);

public static List<WindowInfo> EnumerateVisibleWindows()
{
    var result = new List<WindowInfo>();
    WindowEnumNative.EnumWindows((hWnd, _) =>
    {
        if (!WindowEnumNative.IsWindowVisible(hWnd)) return true;
        var sb = new StringBuilder(512);
        var len = WindowEnumNative.GetWindowTextW(hWnd, sb, sb.Capacity);
        if (len == 0) return true;
        var title = sb.ToString();
        WindowEnumNative.GetWindowThreadProcessId(hWnd, out var pid);
        string procName = "(unknown)";
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            procName = p.ProcessName + ".exe";
        }
        catch { /* process exited between enumeration and GetProcessById */ }
        result.Add(new WindowInfo(title, procName, pid, hWnd));
        return true;
    }, IntPtr.Zero);
    return result;
}
```

#### 4.3 ノイズフィルタ
```csharp
private static readonly HashSet<string> NoiseTitles = new(StringComparer.OrdinalIgnoreCase)
{
    "Program Manager",
    "Windows 入力エクスペリエンス",
    "MSCTFIME UI",
    "Default IME",
    "Microsoft Text Input Application",
};

public static IEnumerable<WindowInfo> FilterNoise(IEnumerable<WindowInfo> windows, uint selfPid)
{
    return windows.Where(w =>
        !NoiseTitles.Contains(w.Title) &&
        w.ProcessId != selfPid);
}
```

#### 4.4 カウントダウン
WPF の場合、`DispatcherTimer` で 1 秒刻みでカウントダウン表示を更新し、満了時に `EnumerateVisibleWindows()` を実行。カウントダウン中はダイアログを最小化または半透明化してユーザがバックグラウンドで操作できるようにする。

```csharp
private async Task RunCountdownAsync(int seconds)
{
    this.WindowState = WindowState.Minimized; // ユーザに作業領域を譲る
    for (int i = seconds; i > 0; i--)
    {
        countdownLabel.Text = $"{i} 秒後に取得...";
        await Task.Delay(1000);
    }
    this.WindowState = WindowState.Normal;
    this.Activate();
    RefreshWindowList();
}
```

### 5. UI 配置詳細

#### 5.1 トリガーボタン
- procedure.csv Step エディタの Value テキストボックスの **右側に隣接配置**
- ボタンラベル: `ウィンドウを選択...`（または ⊞ アイコン）
- 選択中 Action が `WaitWin` / `AppFocus` 以外のときは **Visibility=Collapsed**

#### 5.2 ピッカーダイアログ
- WPF Window、ShowDialog で開く
- サイズ: 600 × 500 程度、リサイズ可
- StartPosition: CenterOwner
- Editor の他の dialog（暗号化トグル等）と視覚的トーンを統一

#### 5.3 リスト
- DataGrid または ListView
- カラム: タイトル（メイン、可変幅）／プロセス名（固定 150px 程度）
- 行ダブルクリック = 挿入確定
- ホバーでハイライト

### 6. 検証手順（Studio 側 QA）

1. Studio 起動 → Pianist Editor で適当な Profile を開く
2. 既存 Step の Action を `WaitWin` に変更 → Value 欄横に「ウィンドウを選択...」ボタンが現れること
3. ボタンクリック → 現在開いているウィンドウ一覧が表示されること（Studio 自身は除外されていること）
4. 「ノイズを隠す」OFF にすると `Program Manager` 等が現れること
5. 適当な行をダブルクリック → Value 欄にタイトルが挿入されること
6. 「5 秒待ってから取得」ボタン → ダイアログが最小化、5 秒待つ間に Win+R を押す → 5 秒後にダイアログが復帰し、リストに `ファイル名を指定して実行` が含まれていること
7. 候補ドロップダウンで「分割候補」を選んでも挿入できること
8. Action を `Type` 等に切替 → ボタンが非表示になること

### 7. やらないこと（v1 スコープ外）

- AutomationId / UIA 取得（Pianist 自体が UIA 非対応のため、editor 側で先行実装する意味がない）
- ウィンドウのスクリーンショット表示（実装コスト高、Pianist の Screenshot Step とは別概念で混乱の元）
- 隠れウィンドウ（DwmGetWindowAttribute DWMWA_CLOAKED）の取得 — IsWindowVisible で十分
- ホットキーで Picker を起動するショートカット — マウス前提で OK
- リアルタイム更新（タイマーで列挙し続ける） — 「更新」ボタン押下時のみ列挙でよい
- 「**最短一意サブストリング**」の自動推定 — 候補ドロップダウンで「分割候補」を提供する程度に留める。完全自動化は v2 候補

### 8. 受け入れ基準

- 既存 Pianist Editor を破壊しない（Action が `WaitWin` / `AppFocus` 以外の Step ではボタンが現れない）
- カウントダウン中に Studio 自体が応答停止しない（async 適切）
- ノイズフィルタで Studio 自身が必ず除外される
- 挿入される文字列が procedure.csv の Value 列に正しく書き込まれ、保存後 fabriq で Pianist 実行 → 当該ウィンドウを `WaitWin` で正しく掴める
- 国際化されたタイトル（日本語 / 英語混在）が文字化けせず取得・表示・挿入できる（UTF-16 → string で全工程通す）

### 9. 参照すべき既存実装

| ファイル | 確認ポイント |
|---|---|
| `modules/extended/pianist/pianist.ps1` の `PianistWin32` クラス（C# inline） | EnumWindows / GetWindowTextW の使い方が完全に対称になることで、Picker で見つかったウィンドウは Pianist 実行時にも必ず同じロジックで見つかる |
| `modules/extended/pianist/Guide.txt` の `WaitWin` 節 | ユーザ向けに「タイトルの部分一致で動く」と明記されているので、Picker のヘルプ文言もこれと一致させる |
| Pianist Profile Editor 既存の **Step エディタ** 部分 | ボタン埋め込み位置 / 既存 dialog の visual tone を踏襲 |

### 10. 補足: 言語ポリシー

- C# コード（識別子・コメント）は英語
- UI 文字列（ボタンラベル / ダイアログタイトル / メッセージ）は **日本語**
- ログ出力（あれば）は英語

---

## このプロンプトをエージェントに渡す際の使い方

そのままコピペしてエージェントへ。実装後は §6 の検証手順をエージェントに実行させ、§8 の受け入れ基準を満たしているか確認したうえでマージする想定。実装中に判断に迷ったら §9 の参照実装を優先する（特に EnumWindows / GetWindowTextW 周りの挙動は Pianist と完全対称であることが必須）。
