namespace FabriqStudio.ViewModels;

/// <summary>
/// 未保存編集の有無を集中ガード（画面遷移／アプリ終了／ワークスペース切替時の警告）から
/// 問い合わせるための共通インターフェース。
/// MainViewModel が CurrentPage を IDirtyAwareViewModel として参照し、
/// 必要に応じて破棄確認ダイアログを表示する設計のための基盤。
///
/// 実装時の注意:
///   - 複数 Dirty フラグを持つ画面（ModuleDetail / AppConfig / BasicParams 等）では OR で集約する。
///   - HasUnsavedChanges は遷移時に都度評価されるため、PropertyChanged 通知は不要。
///   - DiscardChanges() は in-memory 状態のロールバックを担当する。HostDetail のように
///     親リストとエンティティを共有している画面では、これを呼ばないと「破棄」後も
///     親画面に編集中の値が残って見える。
/// </summary>
public interface IDirtyAwareViewModel
{
    /// <summary>未保存の編集があるか。</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>警告ダイアログ本文に表示する画面・対象の識別子（例「端末詳細 (PC-001)」「プロファイル: foo」）。</summary>
    string DirtyDescription { get; }

    /// <summary>
    /// 未保存編集を in-memory でロールバックする。
    /// 共有エンティティを直接編集している画面では、最後に Load した状態へ戻す。
    /// 自前データのみで親へのリークが無い画面では、ディスクから再読み込み or クリアでも可。
    /// 呼び出し後は <see cref="HasUnsavedChanges"/> が false に戻ることを期待する。
    /// </summary>
    void DiscardChanges();
}
