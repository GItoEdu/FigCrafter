using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FigCrafterApp.Models
{
    public class Layer : INotifyPropertyChanged
    {
        private string _name = "新規レイヤー";
        private bool _isVisible = true;
        private bool _isLocked = false;
        private float _opacity = 1.0f;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        public Layer()
        {
            GraphicObjects = new ObservableCollection<GraphicObject>();
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public bool IsLocked
        {
            get => _isLocked;
            set => SetProperty(ref _isLocked, value);
        }

        public float Opacity
        {
            get => _opacity;
            set => SetProperty(ref _opacity, value);
        }

        public ObservableCollection<GraphicObject> GraphicObjects { get; set; }
    }
}
