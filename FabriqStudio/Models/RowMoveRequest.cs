namespace FabriqStudio.Models;

/// <summary>
/// DataGrid 行の D&D 並べ替え要求を表す軽量 record。
/// <see cref="Helpers.DataGridRowDragDropBehavior"/> が Drop イベントで構築し、
/// ViewModel 側の MoveRowCommand に引き渡す。
/// <para>
/// インデックスはどちらも 0 始まりの <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>
/// 上の位置。<c>Move(SourceIndex, TargetIndex)</c> にそのまま渡せるよう、Behavior 側で
/// ObservableCollection.Move の仕様（抜き取り後の位置指定）に合わせて補正済みの値を格納する。
/// </para>
/// </summary>
public sealed record RowMoveRequest(int SourceIndex, int TargetIndex);
