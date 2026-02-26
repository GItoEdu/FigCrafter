using System.Collections.Generic;
using System.Collections.ObjectModel;
using FigCrafterApp.Models;

namespace FigCrafterApp.Commands
{
    public class AddObjectCommand : IUndoableCommand
    {
        private readonly ObservableCollection<GraphicObject> _collection;
        private readonly GraphicObject _objectToAdd;

        public AddObjectCommand(ObservableCollection<GraphicObject> collection, GraphicObject objectToAdd)
        {
            _collection = collection;
            _objectToAdd = objectToAdd;
        }

        public void Execute()
        {
            _collection.Add(_objectToAdd);
        }

        public void Undo()
        {
            _collection.Remove(_objectToAdd);
        }
    }

    public class RemoveObjectCommand : IUndoableCommand
    {
        private readonly ObservableCollection<GraphicObject> _collection;
        private readonly GraphicObject _objectToRemove;
        private readonly int _index;

        public RemoveObjectCommand(ObservableCollection<GraphicObject> collection, GraphicObject objectToRemove)
        {
            _collection = collection;
            _objectToRemove = objectToRemove;
            _index = _collection.IndexOf(objectToRemove);
        }

        public void Execute()
        {
            _collection.Remove(_objectToRemove);
        }

        public void Undo()
        {
            if (_index >= 0 && _index <= _collection.Count)
            {
                _collection.Insert(_index, _objectToRemove);
            }
            else
            {
                _collection.Add(_objectToRemove);
            }
        }
    }

    public class RemoveObjectsCommand : IUndoableCommand
    {
        private readonly ObservableCollection<GraphicObject> _collection;
        private readonly List<(GraphicObject obj, int index)> _removedItems = new();

        public RemoveObjectsCommand(ObservableCollection<GraphicObject> collection, IEnumerable<GraphicObject> objectsToRemove)
        {
            _collection = collection;
            foreach (var obj in objectsToRemove)
            {
                _removedItems.Add((obj, _collection.IndexOf(obj)));
            }
            // indexの降順にソートしておく（Undo時にindexがずれないようにするため）
            _removedItems.Sort((a, b) => b.index.CompareTo(a.index));
        }

        public void Execute()
        {
            foreach (var item in _removedItems)
            {
                _collection.Remove(item.obj);
            }
        }

        public void Undo()
        {
            // 昇順に戻してInsertする
            var toAdd = new List<(GraphicObject obj, int index)>(_removedItems);
            toAdd.Sort((a, b) => a.index.CompareTo(b.index));
            foreach (var item in toAdd)
            {
                if (item.index >= 0 && item.index <= _collection.Count)
                {
                    _collection.Insert(item.index, item.obj);
                }
                else
                {
                    _collection.Add(item.obj);
                }
            }
        }
    }
}
