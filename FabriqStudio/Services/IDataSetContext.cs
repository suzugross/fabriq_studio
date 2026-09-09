namespace FabriqStudio.Services;

/// <summary>
/// グローバルの「編集先データセット」。プロファイルに紐づかない画面（GPO 辞書の書き出し / レジストリ辞書の書き出し /
/// プリンタドライバ検出 / Pianist Profile）は、ここで選ばれたプロファイルのデータフォルダ
/// （profiles/&lt;名&gt;/modules/）を読み書きする。null = 本体（modules/）。
/// プロファイル画面の ⚙ とマスタ設計は自分のプロファイルに固定で、この選択には影響されない。
/// 別のワークスペースを開いたら本体に戻る（fabriq ブリーフ §7: 既定は「なし」、選択中は常時表示）。
/// </summary>
public interface IDataSetContext
{
    /// <summary>現在の編集先（プロファイル名）。null = 本体。</summary>
    string? Current { get; }

    event EventHandler? Changed;

    void Set(string? dataSet);
}

public sealed class DataSetContext : IDataSetContext
{
    public string? Current { get; private set; }

    public event EventHandler? Changed;

    public DataSetContext(IWorkspaceService workspace)
    {
        workspace.WorkspaceChanged += (_, e) =>
        {
            // 再読込（NewPath == OldPath）では維持し、別ワークスペースやクローズでは本体に戻す
            if (!string.Equals(e.NewPath, e.OldPath, StringComparison.OrdinalIgnoreCase)) Set(null);
        };
    }

    public void Set(string? dataSet)
    {
        var value = string.IsNullOrWhiteSpace(dataSet) ? null : dataSet.Trim();
        if (string.Equals(value, Current, StringComparison.Ordinal)) return;
        Current = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
