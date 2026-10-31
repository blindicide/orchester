using System.ComponentModel;
using System.Globalization;
using System.Windows;
namespace Orchester.Localization;
public sealed class LanguageManager : INotifyPropertyChanged {
 public static LanguageManager Current { get; }=new();
 public string Language { get; private set; }="en-US";
 private ResourceDictionary? dictionary;
 public event PropertyChangedEventHandler? PropertyChanged;
 public string this[string key] => Application.Current.TryFindResource(key) as string ?? key;
 public void SetLanguage(string language) {
  Language=language=="ru-RU"?"ru-RU":"en-US";
  var next=new ResourceDictionary { Source=new Uri($"/Orchester.Localization;component/Strings/{Language}.xaml",UriKind.Relative) };
  if(dictionary is not null) Application.Current.Resources.MergedDictionaries.Remove(dictionary);
  Application.Current.Resources.MergedDictionaries.Add(next); dictionary=next;
  CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(Language); CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(Language);
  PropertyChanged?.Invoke(this,new("Item[]")); PropertyChanged?.Invoke(this,new(nameof(Language)));
 }
}
