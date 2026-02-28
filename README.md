# fabriq studio

Windows PC のデプロイ・構成管理を行うデスクトップアプリケーションです。
**fabriq** フレームワークの GUI フロントエンドとして、端末設定の一括管理、スクリプトモジュールの編成、UI 自動化レシピの作成などを提供します。

## 主な機能

| 機能 | 概要 |
|------|------|
| **端末管理** | 対象 PC のネットワーク (IP / サブネット / ゲートウェイ / DNS)、プリンタ (最大10台)、BitLocker 等を CSV ベースで一括管理 |
| **モジュール編集** | 標準 / 拡張スクリプトモジュールのメタデータ (カテゴリ・実行順序・スクリプトパス) を編集 |
| **プロファイル管理** | スクリプト実行シーケンスをプロファイルとして定義・割り当て |
| **Autokey レシピ** | キー入力・ウィンドウ操作などの UI 自動化レシピを GUI で作成 |
| **Script Looper** | リトライ条件 (OnError / Always) 付きタスクの繰り返し実行を構成 |
| **デジタル魚拓** | タスクベースのワークフローエンジン。手順書とコマンド実行を組み合わせた作業定義 |
| **レジストリ辞書** | 100 件以上のプリセットレジストリテンプレート (RDP / UAC / SMBv1 等) をカタログから選択・エクスポート |
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
├── fabriq/                # ← fabriq 本体を配置
│   ├── kernel/
│   ├── modules/
│   ├── profiles/
│   ├── commands/
│   ├── evidence/
│   ├── Fabriq.bat
│   └── Deploy.bat
├── autokey_template/
├── gyotaq_template/
└── looper_template/
```

## プロジェクト構成

```
fabriq_studio/
├── FabriqStudio.sln              # ソリューションファイル
├── FabriqStudio/                 # メインプロジェクト
│   ├── Views/                    # XAML UI (19 画面)
│   │   ├── MainWindow.xaml       #   メインウィンドウ (ナビゲーション + コンテンツ)
│   │   ├── WelcomeView.xaml      #   ワークスペース選択
│   │   ├── BasicParamsView.xaml   #   基本パラメータ (Worker / ログ出力先 / プロファイル)
│   │   ├── HostListView.xaml     #   端末一覧
│   │   ├── HostDetailView.xaml   #   端末詳細編集
│   │   ├── ModuleDetailView.xaml #   モジュール詳細
│   │   ├── ProfileDetailView.xaml #  プロファイル詳細
│   │   ├── AutokeyRecipeEditorView.xaml  # Autokey レシピエディタ
│   │   ├── LooperEditorView.xaml         # Script Looper エディタ
│   │   ├── DigitalGyotaqEditorView.xaml  # デジタル魚拓エディタ
│   │   ├── RegistryCollectionView.xaml   # レジストリ辞書
│   │   └── ...                   #   ダイアログ各種
│   ├── ViewModels/               # ViewModel (14 クラス)
│   ├── Models/                   # データモデル (14 クラス)
│   ├── Services/                 # ビジネスロジック (11 サービス, Interface + 実装)
│   ├── Converters/               # XAML 値コンバータ
│   ├── Helpers/                  # ユーティリティ (暗号化ヘルパー等)
│   ├── Messages/                 # MVVM Messenger メッセージ
│   ├── registry_collection/      # レジストリテンプレートカタログ (catalog.json)
│   ├── template/                 # 新規ワークスペース用テンプレート
│   ├── App.xaml / App.xaml.cs    # エントリポイント・DI 構成
│   └── appsettings.json          # アプリケーション設定
└── dev_fabriq/                   # 開発・テスト用 fabriq ワークスペース
    ├── kernel/                   #   fabriq コア関数 (PowerShell)
    ├── modules/                  #   スクリプトモジュール (standard / extended)
    ├── profiles/                 #   構成プロファイル
    ├── commands/                 #   コマンド定義
    └── evidence/                 #   エビデンス収集設定
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

### サービス一覧

| サービス | 責務 |
|----------|------|
| `IWorkspaceService` | fabriq ルートディレクトリの管理・永続化・変更通知 |
| `ICsvService` | 汎用 CSV 読み書き (CsvHelper ラッパー) |
| `IProfileService` | プロファイル CSV の読み込み・保存 |
| `IModuleService` | モジュールメタデータの管理 |
| `IFileService` | ファイルシステム操作ユーティリティ |
| `IAutokeyService` | UI 自動化レシピの管理・モジュールエクスポート |
| `ILooperService` | リトライタスクリストの管理 |
| `IDigitalGyotaqService` | デジタル魚拓タスクの管理 |
| `IRegistryCollectionService` | レジストリテンプレートカタログの JSON 永続化・エクスポート |
| `ICryptoService` | AES-256-CBC 暗号化・復号 (パスフレーズベース) |
| `IAppSettingsService` | アプリケーション設定の読み書き |

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
