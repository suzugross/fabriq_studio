# fabriq studio - プロジェクトガイドライン

## プロジェクト概要
Windowsキッティングフレームワーク「fabriq」の管理GUIツール「fabriq studio」。

## 開発ルール

### リソースの参照
実装にあたっては、必ず `E:\fabriq` ディレクトリ内の既存のCSV構造、フォルダ構成、PowerShellロジックを読み取り、その仕様に準拠すること。ツールの勝手な解釈でfabriq本体のフォーマットを変更してはならない。

### アーキテクチャ方針
- C# + WPF (.NET 8) を使用し、MVVMパターンを厳守する
- Viewへのロジック記述は禁止し、ViewModelおよび共通Service/Helperクラスへの責務分離を徹底する
- CommunityToolkit.Mvvm (ObservableProperty, RelayCommand, source generators) を使用

### DRY原則
CSV操作やロギングなどの共通処理は、再利用可能なServiceクラスに集約する。

### パブリッシュ
パブリッシュ時は以下のコマンドで `E:\publish_fabriq_studio` へ自己完結型exeとして出力する。
プロジェクト直下（`e:\fabriq_studio`）から実行すること。

```
dotnet publish FabriqStudio/FabriqStudio.csproj -c Release -o "E:/publish_fabriq_studio" --self-contained true -r win-x64
```

注意点:
- **必ず `.csproj` を直接指定する**。`.sln` を対象にすると `-o` が無視され（`NETSDK1194`）、出力先が `<project>/bin/.../publish/` 既定に流れてしまう。
- **出力パスは引用符付きスラッシュ（`"E:/publish_fabriq_studio"`）で書く**。bash 経由で実行するとバックスラッシュが食われて相対パス化し、`E:\fabriq_studio\publish_fabriq_studio` に誤出力される。PowerShell 直接実行なら `E:\...` でも可だが、シェル非依存のためスラッシュ表記で統一する。
- 出力後、`FabriqStudio.exe` / `registry_collection/catalog.json` / `template/template_fabriq/` が同梱されていれば成功（`.csproj` 内の `AfterTargets="Publish"` ターゲットで自動コピー）。

### 作業範囲
指定されたフェーズのコードのみを出力し、大規模なリファクタリングを一度に行わない。
