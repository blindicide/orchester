using System.Windows;
using System.Windows.Controls;
using Orchester.App;
using Orchester.Localization;
public static class Program {
 [STAThread] public static void Main() {
  var app=new App();app.InitializeComponent();
  foreach(var culture in new[]{"en-US","ru-RU"}) {
   LanguageManager.Current.SetLanguage(culture);
   var window=new MainWindow();
   window.Measure(new Size(1180,820));window.Arrange(new Rect(0,0,1180,820));window.UpdateLayout();
   var model=(MainViewModel)window.DataContext;
   for(int section=0;section<5;section++){model.Section=section;window.UpdateLayout();}
   for(int stage=0;stage<7;stage++){model.Stage=stage;window.UpdateLayout();}
   foreach(bool dark in new[]{true,false}){model.Dark=dark;window.UpdateLayout();}
   model.Language=culture=="en-US"?"ru-RU":"en-US";
   if(LanguageManager.Current["Install"]=="Install" || window.FindName("LanguagePicker") is not ComboBox)throw new Exception("Resource or view load failed");
   window.Close();Console.WriteLine("PASS WPF shell, sections, stages, themes and live language switch: "+culture);
  }
  app.Shutdown();
 }
}
