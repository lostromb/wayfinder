using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WayfinderUI
{
    /// <summary>
    /// Interaction logic for ModalDialogPromptFilePath.xaml
    /// </summary>
    public partial class ModalDialogPromptFilePath : Window
    {
        public bool Submitted = false;
        public string Path { get; private set; }

        public ModalDialogPromptFilePath()
        {
            InitializeComponent();
            Path = string.Empty;
            PathTextBox.Focus();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Submitted = true;
            Path = PathTextBox.Text;
            this.Close();
        }
    }
}
