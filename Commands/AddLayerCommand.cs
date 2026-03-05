using System.Collections.ObjectModel;
using FigCrafterApp.Models;
using FigCrafterApp.ViewModels;

namespace FigCrafterApp.Commands
{
    public class AddLayerCommand : IUndoableCommand
    {
        private readonly CanvasViewModel _canvasViewModel;
        private readonly ObservableCollection<Layer> _layers;
        private readonly Layer _layerToAdd;
        private readonly Layer? _previousActiveLayer;

        public AddLayerCommand(CanvasViewModel canvasViewModel, Layer layerToAdd)
        {
            _canvasViewModel = canvasViewModel;
            _layers = canvasViewModel.Layers;
            _layerToAdd = layerToAdd;
            _previousActiveLayer = canvasViewModel.ActiveLayer;
        }

        public void Execute()
        {
            _layers.Insert(0, _layerToAdd);
            _canvasViewModel.ActiveLayer = _layerToAdd;
            _canvasViewModel.UpdateLayerCommands();
        }

        public void Undo()
        {
            _layers.Remove(_layerToAdd);
            if (_previousActiveLayer != null && _layers.Contains(_previousActiveLayer))
            {
                _canvasViewModel.ActiveLayer = _previousActiveLayer;
            }
            else if (_layers.Count > 0)
            {
                _canvasViewModel.ActiveLayer = _layers[0];
            }
            else
            {
                _canvasViewModel.ActiveLayer = null;
            }
            
            _canvasViewModel.UpdateLayerCommands();
        }
    }
}
