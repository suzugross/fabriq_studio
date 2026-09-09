namespace FabriqStudio.ViewModels;

/// <summary>
/// 編集先データセット（<see cref="Services.IDataSetContext"/>）に依存する画面のマーカー。
/// GPO 辞書 / レジストリ辞書の書き出し、プリンタドライバ検出、Pianist Profile がこれに当たる。
/// <para>
/// これらの画面は <see cref="Services.IDataSetContext.Changed"/> を受けて自分で再読込する。
/// MainViewModel は編集先を切り替える前に、表示中の画面がこのマーカーを持つ場合だけ未保存の確認をする
/// （他の画面は編集先に影響されないので、切替で編集中の内容を失わせない）。
/// </para>
/// </summary>
public interface IDataSetDependentViewModel
{
}
