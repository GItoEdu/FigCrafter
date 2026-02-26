using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FigCrafterApp.Models;

namespace FigCrafterApp.Commands
{
    public class GroupingCommand : IUndoableCommand
    {
        private readonly ObservableCollection<GraphicObject> _collection;
        private readonly List<GraphicObject> _objectsToGroup;
        private readonly GroupObject _group;
        private readonly int _insertIndex;
        private readonly List<(GraphicObject Obj, int OriginalIndex)> _originalPositions = new();

        public GroupingCommand(ObservableCollection<GraphicObject> collection, IEnumerable<GraphicObject> objectsToGroup, GroupObject group, int insertIndex)
        {
            _collection = collection;
            _objectsToGroup = objectsToGroup.ToList();
            _group = group;
            _insertIndex = insertIndex;

            // 元の位置を記録しておく
            foreach (var obj in _objectsToGroup)
            {
                _originalPositions.Add((obj, _collection.IndexOf(obj)));
            }
            // Indexの降順にソート (Undoで復元する際にIndexがずれないようにする)
            _originalPositions.Sort((a, b) => b.OriginalIndex.CompareTo(a.OriginalIndex));
        }

        public void Execute()
        {
            // 個別のオブジェクトをキャンバスから削除
            foreach (var item in _originalPositions)
            {
                _collection.Remove(item.Obj);
            }

            // グループを挿入（インデックスが範囲外にならないよう補正）
            int actualIndex = Math.Min(_insertIndex, _collection.Count);
            if (actualIndex < 0) actualIndex = 0;
            _collection.Insert(actualIndex, _group);
        }

        public void Undo()
        {
            // グループをキャンバスから削除
            _collection.Remove(_group);

            // 個別のオブジェクトを元の位置に復元（昇順で行う）
            var toRestore = new List<(GraphicObject Obj, int OriginalIndex)>(_originalPositions);
            toRestore.Sort((a, b) => a.OriginalIndex.CompareTo(b.OriginalIndex));

            foreach (var item in toRestore)
            {
                int restoreIndex = Math.Min(item.OriginalIndex, _collection.Count);
                if (restoreIndex < 0) restoreIndex = 0;
                _collection.Insert(restoreIndex, item.Obj);
            }
        }
    }

    public class UngroupingCommand : IUndoableCommand
    {
        private readonly ObservableCollection<GraphicObject> _collection;
        private readonly List<GroupObject> _groupsToUngroup;
        private readonly List<(GroupObject Group, int OriginalIndex)> _groupPositions = new();

        public UngroupingCommand(ObservableCollection<GraphicObject> collection, IEnumerable<GroupObject> groupsToUngroup)
        {
            _collection = collection;
            _groupsToUngroup = groupsToUngroup.ToList();

            foreach (var group in _groupsToUngroup)
            {
                _groupPositions.Add((group, _collection.IndexOf(group)));
            }
        }

        public void Execute()
        {
            // グループの削除と、子要素の展開
            // 元のグループがあった位置に子要素を展開するよう実装
            foreach (var groupInfo in _groupPositions)
            {
                var group = groupInfo.Group;
                int index = _collection.IndexOf(group);
                if (index < 0) continue; // 万が一なかったらスキップ

                _collection.Remove(group);

                int insertAt = Math.Min(index, _collection.Count);
                if (insertAt < 0) insertAt = 0;

                foreach (var child in group.Children)
                {
                    _collection.Insert(insertAt, child);
                    insertAt++;
                }
            }
        }

        public void Undo()
        {
            // 展開した子要素を一旦すべて削除
            foreach (var groupInfo in _groupPositions)
            {
                foreach (var child in groupInfo.Group.Children)
                {
                    _collection.Remove(child);
                }
            }

            // グループを元の位置に復元
            // 降順にソートして復元時のズレを防ぐ
            var toRestore = new List<(GroupObject Group, int OriginalIndex)>(_groupPositions);
            toRestore.Sort((a, b) => b.OriginalIndex.CompareTo(a.OriginalIndex));

            foreach (var info in toRestore)
            {
                int restoreIndex = Math.Min(info.OriginalIndex, _collection.Count);
                if (restoreIndex < 0) restoreIndex = 0;
                _collection.Insert(restoreIndex, info.Group);
            }
        }
    }
}
