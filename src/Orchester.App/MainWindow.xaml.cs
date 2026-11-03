using System.Windows;
namespace Orchester.App;
public partial class MainWindow : Window {
 private readonly MainViewModel model;
 public MainWindow(){InitializeComponent();model=new();DataContext=model;Loaded+=async(_,_)=>await model.InitializeAsync();Closing+=(_,e)=>{if(model.Busy)e.Cancel=true;};}
}
