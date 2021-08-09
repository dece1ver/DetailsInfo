using System.ComponentModel;
using System.Windows.Forms;
using Control = System.Windows.Forms.Control;
using UserControl = System.Windows.Controls.UserControl;

namespace DetailsInfo
{
    /// <summary>
    /// Логика взаимодействия для Keyboard.xaml
    /// </summary>
    public partial class KeyBoardControl : UserControl, INotifyPropertyChanged
    {
        public KeyBoardControl()
        {
            InitializeComponent();
            DataContext = this;
        }
        public char PropKeyQ
        {
            get => Control.IsKeyLocked(Keys.CapsLock) ? 'Q' : 'q';
            set
            {
                PropKeyQ = Control.IsKeyLocked(Keys.CapsLock) ? 'Q' : 'q';
                OnPropertyChanged(nameof(PropKeyQ));
            }
        }
        public char PropKeyW => Control.IsKeyLocked(Keys.CapsLock) ? 'W' : 'w';
        public char PropKeyE => Control.IsKeyLocked(Keys.CapsLock) ? 'E' : 'e';
        public char PropKeyR => Control.IsKeyLocked(Keys.CapsLock) ? 'R' : 'r';
        public char PropKeyT => Control.IsKeyLocked(Keys.CapsLock) ? 'T' : 't';
        public char PropKeyY => Control.IsKeyLocked(Keys.CapsLock) ? 'Y' : 'y';
        public char PropKeyU => Control.IsKeyLocked(Keys.CapsLock) ? 'U' : 'u';
        public char PropKeyI => Control.IsKeyLocked(Keys.CapsLock) ? 'I' : 'i';
        public char PropKeyO => Control.IsKeyLocked(Keys.CapsLock) ? 'O' : 'o';
        public char PropKeyP => Control.IsKeyLocked(Keys.CapsLock) ? 'P' : 'p';
        public char PropKeyA => Control.IsKeyLocked(Keys.CapsLock) ? 'A' : 'a';
        public char PropKeyS => Control.IsKeyLocked(Keys.CapsLock) ? 'S' : 's';
        public char PropKeyD => Control.IsKeyLocked(Keys.CapsLock) ? 'D' : 'd';
        public char PropKeyF => Control.IsKeyLocked(Keys.CapsLock) ? 'F' : 'f';
        public char PropKeyG => Control.IsKeyLocked(Keys.CapsLock) ? 'G' : 'g';
        public char PropKeyH => Control.IsKeyLocked(Keys.CapsLock) ? 'H' : 'h';
        public char PropKeyJ => Control.IsKeyLocked(Keys.CapsLock) ? 'J' : 'j';
        public char PropKeyK => Control.IsKeyLocked(Keys.CapsLock) ? 'K' : 'k';
        public char PropKeyL => Control.IsKeyLocked(Keys.CapsLock) ? 'L' : 'l';
        public char PropKeyZ => Control.IsKeyLocked(Keys.CapsLock) ? 'Z' : 'z';
        public char PropKeyX => Control.IsKeyLocked(Keys.CapsLock) ? 'X' : 'x';
        public char PropKeyC => Control.IsKeyLocked(Keys.CapsLock) ? 'C' : 'c';
        public char PropKeyV => Control.IsKeyLocked(Keys.CapsLock) ? 'V' : 'v';
        public char PropKeyB => Control.IsKeyLocked(Keys.CapsLock) ? 'B' : 'b';
        public char PropKeyN => Control.IsKeyLocked(Keys.CapsLock) ? 'N' : 'n';
        public char PropKeyM => Control.IsKeyLocked(Keys.CapsLock) ? 'M' : 'm';

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
