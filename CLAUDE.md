# fabriq studio - プロジェクトガイドライン

## プロジェクト概要
Windowsキッティングフレームワーク「fabriq」の管理GUIツール「fabriq studio」。

## 開発ルール

### リソースの参照
実装にあたっては、必ず `C:\Users\szk-WIN01\Desktop\fabriq` ディレクトリ内の既存のCSV構造、フォルダ構成、PowerShellロジックを読み取り、その仕様に準拠すること。ツールの勝手な解釈でfabriq本体のフォーマットを変更してはならない。

### アーキテクチャ方針
- C# + WPF (.NET 8) を使用し、MVVMパターンを厳守する
- Viewへのロジック記述は禁止し、ViewModelおよび共通Service/Helperクラスへの責務分離を徹底する
- CommunityToolkit.Mvvm (ObservableProperty, RelayCommand, source generators) を使用

### DRY原則
CSV操作やロギングなどの共通処理は、再利用可能なServiceクラスに集約する。

### 作業範囲
指定されたフェーズのコードのみを出力し、大規模なリファクタリングを一度に行わない。
