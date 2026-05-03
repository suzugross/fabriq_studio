using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace FabriqStudio.Converters;

/// <summary>
/// PianistSampleEntry.File（screenshots/ 配下のファイル名）を、BitmapImage に変換する。
///
/// XAML 側で MultiBinding を使い、(file, profileFolderPath) を入力する想定:
/// <code>
///   &lt;MultiBinding Converter="{StaticResource SampleFileToImageConverter}"&gt;
///     &lt;Binding Path="File" /&gt;
///     &lt;Binding Path="DataContext.CurrentData.Entry.FolderPath"
///              RelativeSource="{RelativeSource AncestorType=ListBox}" /&gt;
///   &lt;/MultiBinding&gt;
/// </code>
///
/// ただし WPF の Image.Source の MultiBinding は実用上のレイアウト負荷が大きいため、
/// 今回は単一 Binding で File 名のみを受け取り、DataContext を辿って
/// FolderPath を解決する変種を IMultiValueConverter ではなく
/// IValueConverter + Application.Current.MainWindow 経由のフォールバックで対応する。
/// </summary>
public class SampleFileToImageConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2) return null;
        if (values[0] is not string fileName) return null;
        if (values[1] is not string profileFolder) return null;
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(profileFolder)) return null;

        var path = Path.Combine(profileFolder, "screenshots", fileName);
        if (!File.Exists(path)) return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            // CacheOption=OnLoad + Uri 経由で読み込み、ファイルロックを避ける
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            // 大きな画像をサムネイル用に縮小ロード（メモリ削減）
            bmp.DecodePixelHeight = 160;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
