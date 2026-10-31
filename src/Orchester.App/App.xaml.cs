using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Orchester.Localization;
namespace Orchester.App;
public partial class App : Application {
 public static string DataRoot=>System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Orchester");
 protected override void OnStartup(StartupEventArgs e) {
  base.OnStartup(e);
  var settings=UserSettings.Load(); LanguageManager.Current.SetLanguage(settings.Language ?? (CultureInfo.InstalledUICulture.Name=="ru-RU"?"ru-RU":"en-US")); ApplyTheme(settings.Dark);
  new MainWindow().Show();
 }
 public static void ApplyTheme(bool dark) {
  Current.Resources["Surface"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#172334":"#F3F5F8"));
  Current.Resources["Ink"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#EDF2F8":"#172334"));
  Current.Resources["Panel"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#24354A":"#FFFFFF"));
 }
}
