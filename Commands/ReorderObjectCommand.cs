using System.Collections.ObjectModel;
using FigCrafterApp.Models;

namespace FigCrafterApp.Commands
{
    public class ReorderObjectCommand : IUndoableCommand
    {
        private readonly ObservableCollection<GraphicObject> _collection;
        private readonly GraphicObject _targetObject;
        private readonly int _oldIndex;
        private readonly int _newIndex;

        public ReorderObjectCommand(ObservableCollection<GraphicObject> collection, GraphicObject targetObject, int oldIndex, int newIndex)
        {
            _collection = collection;
            _targetObject = targetObject;
            _oldIndex = oldIndex;
            _newIndex = newIndex;
        }

        public void Execute()
        {
            _collection.Move(_oldIndex, _newIndex);
        }

        public void Undo()
        {
            _collection.Move(_newIndex, _oldIndex);
        }
    }
}
