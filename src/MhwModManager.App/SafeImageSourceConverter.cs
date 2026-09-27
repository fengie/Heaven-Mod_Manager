#nullable disable
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using MhwModManager.Core;

namespace MhwModManager.App;

public sealed class SafeImageSourceConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        if(value is not string path||string.IsNullOrWhiteSpace(path)||!File.Exists(path))return null;
        try
        {
            var image=new BitmapImage();
            image.BeginInit();
            image.CacheOption=BitmapCacheOption.OnLoad;
            image.CreateOptions=BitmapCreateOptions.IgnoreImageCache;
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

    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}
