# fabriq studio

Windows PC のデプロイ・構成管理を行うデスクトップアプリケーションです。
**fabriq** フレームワークの GUI フロントエンドとして、端末設定の一括管理、スクリプトモジュールの編成、Pianist Profile (RPA + 手順書ハイブリッド) の編集とテスト実行などを提供します。

## 主な機能

| 機能 | 概要 |
|------|------|
| **端末管理** | 対象 PC のネットワーク (IP / サブネット / ゲートウェイ / DNS)、プリンタ (最大10台)、BitLocker 等を CSV ベースで一括管理 |
| **モジュール編集** | 標準 / 拡張スクリプトモジュールのメタデータ (カテゴリ・実行順序・スクリプトパス) を編集 |
| **プロファイル管理** | スクリプト実行シーケンスをプロファイルとして定義・割り当て |
| **Pianist Profile Editor** | RPA + 手順書ハイブリッドな Pianist プロファイル (`modules/extended/pianist/profiles/`) を 5 タブ (メタ / Phase 一覧 / 変数 / ショートカット / プレビュー) で編集。Phase ごとの instruction は section marker DSL (`[RPA]` / `[Manual]` / `[Variables]` / `[Samples]`) を 4 sub-tab エディタで編集。Pianist テスト実行を Studio から子プロセス起動 |
| **Script Looper** | リトライ条件 (OnError / Always) 付きタスクの繰り返し実行を構成 |
| **レジストリ辞書** | 100 件以上のプリセットレジストリテンプレート (RDP / UAC / SMBv1 等) をカタログから選択・エクスポート |
| **プリンタドライバ検出** | INF ファイルを解析してプリンタドライバ情報を一覧化、hostlist へ転記 |
| **ホスト一覧エクスポート** | hostlist.csv を Excel 等向けに整形してエクスポート |
| **fabriq バックアップ** | ワークスペース全体をバックアップフォルダへ複製 (PS1 等は除外、`USER_MEMO.txt` / `BACKUP_INFO.txt` 同梱) |
| **fabriq オーバーレイ更新** | 同梱テンプレートから本体ファイルを SemVer 比較・preflight・自動バックアップ付きで安全に上書き更新 |
| **暗号化** | AES-256-CBC (PBKDF2-HMAC-SHA256) による機密値の暗号化。PowerShell 側 (`Unprotect-FabriqValue`) と互換 |
| **ワークスペース切替** | 複数の fabriq 環境を切り替えて管理。テンプレートからの新規作成にも対応 |

## スクリーンショット

[> ダークテーマの左サイドバーナビゲーション + 右コンテンツ領域のレイアウト](https://github.com/user-attachments/assets/b2339c53-f8a3-415c-8509-c92d6ae35b07)

## 技術スタック

- **.NET 8.0** / **WPF** (Windows Presentation Foundation)
- **C# 12** (Nullable Reference Types 有効)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) v8.3.2 — MVVM パターン + Source Generators
- [CsvHelper](https://joshclose.github.io/CsvHelper/) v33.0.1 — CSV 読み書き
- Microsoft.Extensions.DependencyInjection v8.0.1 — DI コンテナ
- Microsoft.Extensions.Configuration.Json v8.0.1 — 設定管理

## 前提条件

- **Windows 10 / 11**
- **.NET 8.0 SDK** 以上
- **Visual Studio 2022** (推奨) または `dotnet` CLI

## ビルド・実行

```bash
# リポジトリのクローン
git clone https://github.com/<your-org>/fabriq-studio.git
cd fabriq-studio

# ビルド
dotnet build FabriqStudio.sln

# 実行
dotnet run --project FabriqStudio
```

ビルド後、`registry_collection/` および `template/` が出力ディレクトリへ自動コピーされます。

### fabriq 本体の配置

「テンプレートから新規作成」機能を使用するには、fabriq フレームワーク本体を以下のパスに配置する必要があります。

```
FabriqStudio/template/template_fabriq/fabriq/
```

このディレクトリは `.gitignore` で追跡対象外のため、別途 fabriq 本体のリポジトリから取得し配置してください。
配置後のディレクトリ構成は以下のようになります。

```
FabriqStudio/template/template_fabriq/
├── fabriq/                # ← fabriq 本体を配置（.gitignore 対象）
│   ├── kernel/
│   ├── modules/
│   ├── profiles/
│   ├── commands/
│   ├── evidence/
│   ├── Fabriq.bat
│   └── Deploy.bat
└── looper_template/       # Script Looper モジュール出力テンプレート
```

## プロジェクト構成

```
fabriq_studio/
├── FabriqStudio.sln              # ソリューションファイル (単一プロジェクト)
├── FabriqStudio/                 # メインプロジェクト
│   ├── Views/                    # XAML UI (29 ファイル: 画面 + ダイアログ)
│   │   ├── MainWindow.xaml              # メインウィンドウ (ナビゲーション + コンテンツ)
│   │   ├── WelcomeView.xaml             # ワークスペース選択
│   │   ├── BasicParamsView.xaml         # 基本パラメータ
│   │   ├── HostListView.xaml            # 端末一覧
│   │   ├── HostDetailView.xaml          # 端末詳細編集
│   │   ├── ModuleEditView.xaml          # モジュール一覧
│   │   ├── ModuleDetailView.xaml        # モジュール詳細
│   │   ├── ProfileDetailView.xaml       # プロファイル詳細
│   │   ├── LooperEditorView.xaml        # Script Looper エディタ
│   │   ├── PianistProfileEditorView.xaml # Pianist Profile エディタ (5 タブ + 4 sub-tab)
│   │   ├── PrinterDriverDetectorView.xaml # プリンタドライバ検出
│   │   ├── RegistryCollectionView.xaml  # レジストリ辞書
│   │   ├── AppConfigView.xaml           # アプリ設定
│   │   ├── Pianist*Dialog.xaml          # Pianist 系ダイアログ (新規 / 列名 / Phase 編集 / 削除 / テスト実行 / Window Picker / List 編集) ×8
│   │   └── ...                          # その他ダイアログ (Backup / Update / Export / Passphrase / Report / LogViewer / RegistryPicker 等)
│   ├── ViewModels/               # ViewModel (15 クラス + IDirtyAwareViewModel インターフェース)
│   ├── Models/                   # データモデル (37 クラス)
│   ├── Services/                 # ビジネスロジック (16 Interface / 16 実装、15 を DI 登録)
│   ├── Converters/               # XAML 値コンバータ (13 個)
│   ├── Helpers/                  # ユーティリティ (10 個 / 暗号化・DSL パーサ・DataGrid 行 D&D 等)
│   ├── Messages/                 # MVVM Messenger メッセージ (NavigationMessage)
│   ├── registry_collection/      # レジストリテンプレートカタログ (catalog.json)
│   ├── template/                 # 新規ワークスペース用テンプレート
│   ├── App.xaml / App.xaml.cs    # エントリポイント・DI 構成
│   └── appsettings.json          # アプリケーション設定 (現状は空、将来追加用の枠)
└── dev_fabriq/                   # 開発・テスト用 fabriq ワークスペース置き場
                                  # (.gitignore 対象。fabriq 本体を別途配置する想定)
```

## アーキテクチャ

### MVVM パターン

```
View (XAML)  ←──バインディング──→  ViewModel  ──→  Service  ──→  ファイルシステム
                                      ↑                           (CSV / JSON)
                                      │
                               WeakReferenceMessenger
                            (ページ間ナビゲーション・通知)
```

- **View**: XAML によるデータバインディング駆動の UI
- **ViewModel**: `ObservableProperty` / `RelayCommand` (CommunityToolkit.Mvvm) による状態管理
- **Service**: インターフェース分離。全サービスを Singleton で DI 登録
- **非同期 I/O**: CSV・JSON・ファイル操作は全て `async/await` で UI スレッドをブロックしない

### サービス一覧 (DI 登録分)

| サービス | 責務 |
|----------|------|
| `IWorkspaceService` | fabriq ルートディレクトリの管理・永続化 (`config/workspace.json`)・変更通知 |
| `ICsvService` | 汎用 CSV 読み書き (CsvHelper ラッパー) |
| `IFileService` | ファイルシステム操作ユーティリティ |
| `IProfileService` | プロファイル CSV (`profiles/*.csv`) の読み込み・保存 |
| `IModuleService` | モジュールメタデータ (`module.csv`) の管理 |
| `IModulePresetService` | モジュール毎のプリセット値管理 |
| `IHostListExportService` | hostlist.csv の整形エクスポート |
| `IPrinterDriverDetectorService` | INF ファイルを解析してプリンタドライバ情報を抽出 |
| `IRegistryCollectionService` | レジストリテンプレートカタログ (`catalog.json`) の永続化・エクスポート |
| `ILooperService` | リトライタスクリストの管理・テスト実行 (PowerShell 子プロセス) |
| `IPianistProfileService` | Pianist Profile (`modules/extended/pianist/profiles/<name>/`) の I/O・新規作成・削除 |
| `IPianistTestRunService` | Pianist Profile を fabriq エンジン (`pianist.ps1`) で子プロセス実行 |
| `IFabriqBackupService` | ワークスペース全体のバックアップ複製 |
| `IFabriqUpdateService` | 同梱テンプレートから本体ファイルを SemVer 比較・preflight・自動バックアップ付きで上書き更新 |
| `ICryptoService` | AES-256-CBC + PBKDF2-HMAC-SHA256 による暗号化・復号 (PowerShell `Unprotect-FabriqValue` と互換) |

## fabriq ワークスペース

fabriq studio は **ワークスペース** 単位でデプロイ設定を管理します。
各ワークスペースは以下のディレクトリ構成を持つ fabriq フレームワークのインスタンスです。

```
<workspace_root>/
├── kernel/       # コア関数・共通ユーティリティ (PowerShell)
├── modules/      # 実行スクリプトモジュール
│   ├── standard/ #   標準モジュール (組み込み)
│   └── extended/ #   拡張モジュール (カスタム)
├── profiles/     # 構成プロファイル (CSV)
├── commands/     # コマンド定義
└── evidence/     # エビデンス収集設定
```

「テンプレートから新規作成」機能でワークスペースの雛形を自動生成できます。

## ライセンス

このプロジェクトは [MIT License](LICENSE) の下で公開されています。
