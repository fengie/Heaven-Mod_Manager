#nullable disable
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using MhwModManager.Core;

namespace MhwModManager.App;

public sealed class SafeImageSourceConverter:IValueConverter
{
    private const int MinimumDecodeWidth=64;
    private const int MaximumDecodeWidth=2048;

    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(value is not string path||string.IsNullOrWhiteSpace(path)||!File.Exists(path))return null;
        try
        {
            var image=new BitmapImage();
            image.BeginInit();
            image.CacheOption=BitmapCacheOption.OnLoad;
            image.CreateOptions=BitmapCreateOptions.IgnoreImageCache;
            var decodeWidth=ParseDecodeWidth(parameter);
            if(decodeWidth>0)image.DecodePixelWidth=decodeWidth;
            image.UriSource=new Uri(Path.GetFullPath(path),UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or UriFormatException or System.Runtime.InteropServices.COMException)
        {
            MasterDebugLog.Write("VISUALS",$"Thumbnail decode rejected path={path}",ex);
            return null;
        }
    }

    private static int ParseDecodeWidth(object parameter)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(parameter is int width)return Math.Clamp(width,MinimumDecodeWidth,MaximumDecodeWidth);
        return int.TryParse(parameter?.ToString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out var parsed)
            ?Math.Clamp(parsed,MinimumDecodeWidth,MaximumDecodeWidth)
            :0;
    }

    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
