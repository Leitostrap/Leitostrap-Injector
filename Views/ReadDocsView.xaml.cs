using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace LeitostrapV7.Views;


public partial class ReadDocsView : UserControl
{
    public ReadDocsView()
    {
        InitializeComponent();
    }


    private void Back_Click(object sender, MouseButtonEventArgs e)
    {
        MainWindow.Instance?.NavigateTo(9);
    }
}
