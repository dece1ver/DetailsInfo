using System.ComponentModel;

namespace DetailsInfo.Data
{
    public class ToolNote : INotifyPropertyChanged
    {
        private int _toolNo;

        public int ToolNo
        {
            get => _toolNo;
            set
            {
                if (_toolNo == value)
                    return;
                _toolNo = value;
                OnPropertyChanged(nameof(ToolNo));
            }
        }


        private string _toolDescription;

        public string ToolDescription
        {
            get => _toolDescription;
            set
            {
                if (_toolDescription == value)
                    return;
                _toolDescription = value;
                OnPropertyChanged(nameof(ToolDescription));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
