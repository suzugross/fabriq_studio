# タスク管理表 — fabriq_studio

<!-- このファイルは TM アプリが .tm/tasks.json から自動生成します。
     直接編集しないでください（次回保存で上書きされます）。
     タスクの追加・更新は tasks.json か TM アプリから行ってください。 -->
最終更新: 2026-06-15 17:50

## 未着手 (2)

### [t-0001] パスフレーズリセット機能について

**内容:**

パスフレーズのリセットについて、アプリ内の案内では、空のパスフレーズを設定したり、正しいパスフレーズを入力したうえで変更したいパスフレーズを入力すると、リセットされるような案内があるが、これは正確な表現ではないので、パスフレーズをリセットする場合はFabriqカーネル内のパスフレーズを直接削除することとそうした場合、そのパスフレーズで暗号化されたCSVカラムは復号できなくなることを注意書きするようにする。

<sub>更新: 2026-06-09 23:09 ／ 作成: 2026-06-09 23:06</sub>

### [t-0002] __SELF__の対応について

**内容:**

fabriqではホストリストの任意のカラムに__SELF__マーカを使用すると、実行マシンの情報を使ってキッティングを開始する自己参照モードを実装しました。そのため、FabriqStuidoでも簡単に、もしくはそういった機能があることが意識できる形でホスト編集画面になんらかの変更を加えたいと考えています。

例１：入力欄について入力可能なプルダウンメニュー化し、__SELF__を選べるようにする。
例２：__SELF__が使用できる旨を注意書きする。

<sub>更新: 2026-06-15 02:49 ／ 作成: 2026-06-10 16:43</sub>

## 完了 (2)

### [t-0003] クラッシュバグ１

**内容:**

Profile編集画面のモジュール一覧セクション（ドラッグアンドドロップ操作ができるところ）ですが、メモ欄の文字入力が可能な状態で、おそらく以下のどちらかの操作をするとクラッシュする。

１．入力されている文字をドラッグ選択しようとする
２．モジュールエントリをドラッグする。

正直、どっちの操作でクラッシュしているのかは不明ですが、
クラッシュするときは、マウスカーソルがバツのマークがつくようになる（ドラッグできないときにでてくる警告と同じもの）

**Claudeメモ:**

実装完了（ビルド成功・要動作確認）。

【根本原因】Helpers/DataGridRowDragDropBehavior.cs。セル編集中（メモ欄TextBox内）のテキスト選択ドラッグ等で DragDrop.DoDragDrop が誤起動し、編集中TextBoxがマウスキャプチャした状態でOLEドラッグが走って例外→未捕捉クラッシュ（Xカーソルは『DnDが実際に開始された』証拠）。OnPreviewMouseLeftButtonDownのIsEditingガードは、編集開始クリック時点でIsEditing=falseのため不十分。加えて早期returnでstate.Sourceを未クリア（残留）の二重穴。

【検証】ステートマシン図（Idle/Armed/Dragging × 編集コンテキスト）を作成し、危険状態C=(DoDragDrop ∧ editor開)の到達可能性を解析。当初4点案では塞げない穴を2件発見: (F)CommitEdit失敗時にeditor開のまま続行、(C)DoDragDropモーダル中のPreviewMouseMove再入による二重起動。

【修正6点（DataGridRowDragDropBehavior.csのみ）】①Down冒頭でSource/SourceIndex常時リセット ②MoveでOriginalSource∈TextBoxならreturn（テキスト選択を奪わない／編集可能ComboBoxのPART_EditableTextBoxも捕捉） ③DoDragDrop前にCommitEdit(Row,true)、失敗ならreturnで中止 ④DoDragDropをtry/catch ⑤DragState.IsDragging再入ガード ／既存IsEditingチェックは多層防御で残置。到達経路Armed→Dragmingのガードが(¬TextBox ∧ CommitEdit=true)となり、CommitEdit成功直後はeditor閉=危険状態C到達不能を確認。通常の行D&D（非編集セルから掴む）には影響なし。

【副次】実装前ステートマシン図作成ルールをCLAUDE.md『開発ルール』に追記。

未実施: 実機での再現操作（メモ編集中のテキスト選択ドラッグ／編集中の行ドラッグ）でクラッシュ解消と通常D&Dの正常動作確認。

<sub>更新: 2026-06-15 17:50 ／ 作成: 2026-06-14 06:04</sub>

### [t-0004] セグメントについて

**内容:**

Profile編集画面でのセグメント指定について
モジュール編集画面で設定エントリに対して付与したセグメント値を、Profileのモジュール一覧画面でプルダウンメニューで選択できるようにしたい。
なぜなら、モジュールに付与したセグメントしかProfileで指定することはないし、それぞれが手入力だとタイポなどで意図しない不一致が発生することがあるため。
ただし、一応自由記述も可能な状態にはしておく。モジュールの値がPresetの値も自由記述も可能なように。

**Claudeメモ:**

実装完了（ビルド成功・要動作確認）。方針: ユーザー選択により『行ごとにモジュール別（精密）』候補を採用。各Profile行のセグメント列を、その行が参照するモジュールの設定CSVに実在するSegment値だけをプリセット表示する編集可能ComboBox(IsEditable=True/自由入力可)に変更。

変更ファイル:
- Models/ProfileScriptEntry.cs: 行ごとの候補リスト SegmentOptions ([ObservableProperty]+[Ignore]) を追加。
- Services/IModuleService.cs + ModuleService.cs: GetModuleSegmentsAsync() 追加。全モジュールの設定CSV(module.csv/preset.csv除外)を走査し moduleDir→非空Segment値(distinct/昇順) の辞書を返す。壊れたCSVはtry/catchでスキップ。IFileService を注入。
- ViewModels/ProfileDetailViewModel.cs: LoadAsyncで候補辞書を並列ロードし各行へApplySegmentOptions()。AddModule/ImportProfileの新規行にも適用。ScriptPath変更時はOnModuleItemChangedで再解決。SegmentOptions変更はDirty対象外に。
- Views/ProfileDetailView.xaml: セグメント列をDataGridTextColumn→DataGridTemplateColumn(編集可能ComboBox)に。ItemsSourceは行のSegmentOptions、Textは Segment(LostFocus)、ロックはIsHitTestVisibleでGroup列と統一。

既知の限界: ScriptPathを手編集すると候補が新モジュールへ自動再解決される(対応済)。特殊コマンド行/解決不能モジュールは候補空(自由入力は可能)。

未実施: 実機での動作確認(プルダウン表示・選択・保存往復)。

<sub>更新: 2026-06-15 17:50 ／ 作成: 2026-06-15 02:45</sub>

