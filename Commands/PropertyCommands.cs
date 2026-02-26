using System;
using System.Reflection;
using FigCrafterApp.Models;

namespace FigCrafterApp.Commands
{
    public class PropertyChangeCommand : IUndoableCommand
    {
        private readonly GraphicObject _targetObject;
        private readonly string _propertyName;
        private readonly object? _oldValue;
        private readonly object? _newValue;
        private readonly PropertyInfo? _propertyInfo;

        public PropertyChangeCommand(GraphicObject targetObject, string propertyName, object? oldValue, object? newValue)
        {
            _targetObject = targetObject;
            _propertyName = propertyName;
            _oldValue = oldValue;
            _newValue = newValue;
            _propertyInfo = _targetObject.GetType().GetProperty(propertyName);
        }

        public void Execute()
        {
            _propertyInfo?.SetValue(_targetObject, _newValue);
        }

        public void Undo()
        {
            _propertyInfo?.SetValue(_targetObject, _oldValue);
        }
    }

    public class MoveCommand : IUndoableCommand
    {
        private readonly GraphicObject _targetObject;
        private readonly float _oldX;
        private readonly float _oldY;
        private readonly float _newX;
        private readonly float _newY;

        public MoveCommand(GraphicObject targetObject, float oldX, float oldY, float newX, float newY)
        {
            _targetObject = targetObject;
            _oldX = oldX;
            _oldY = oldY;
            _newX = newX;
            _newY = newY;
        }

        public void Execute()
        {
            _targetObject.X = _newX;
            _targetObject.Y = _newY;
        }

        public void Undo()
        {
            _targetObject.X = _oldX;
            _targetObject.Y = _oldY;
        }
    }

    public class MoveLineEndCommand : IUndoableCommand
    {
        private readonly LineObject _targetObject;
        private readonly float _oldX;
        private readonly float _oldY;
        private readonly float _oldEndX;
        private readonly float _oldEndY;
        private readonly float _newX;
        private readonly float _newY;
        private readonly float _newEndX;
        private readonly float _newEndY;

        public MoveLineEndCommand(LineObject targetObject, float oldX, float oldY, float oldEndX, float oldEndY, float newX, float newY, float newEndX, float newEndY)
        {
            _targetObject = targetObject;
            _oldX = oldX;
            _oldY = oldY;
            _oldEndX = oldEndX;
            _oldEndY = oldEndY;
            _newX = newX;
            _newY = newY;
            _newEndX = newEndX;
            _newEndY = newEndY;
        }

        public void Execute()
        {
            _targetObject.X = _newX;
            _targetObject.Y = _newY;
            _targetObject.EndX = _newEndX;
            _targetObject.EndY = _newEndY;
        }

        public void Undo()
        {
            _targetObject.X = _oldX;
            _targetObject.Y = _oldY;
            _targetObject.EndX = _oldEndX;
            _targetObject.EndY = _oldEndY;
        }
    }

    public class ResizeCommand : IUndoableCommand
    {
        private readonly GraphicObject _targetObject;
        private readonly float _oldX, _oldY, _oldWidth, _oldHeight;
        private readonly float _newX, _newY, _newWidth, _newHeight;

        public ResizeCommand(GraphicObject targetObject, float oldX, float oldY, float oldWidth, float oldHeight, float newX, float newY, float newWidth, float newHeight)
        {
            _targetObject = targetObject;
            _oldX = oldX; _oldY = oldY; _oldWidth = oldWidth; _oldHeight = oldHeight;
            _newX = newX; _newY = newY; _newWidth = newWidth; _newHeight = newHeight;
        }

        public void Execute()
        {
            _targetObject.X = _newX;
            _targetObject.Y = _newY;
            _targetObject.Width = _newWidth;
            _targetObject.Height = _newHeight;
        }

        public void Undo()
        {
            _targetObject.X = _oldX;
            _targetObject.Y = _oldY;
            _targetObject.Width = _oldWidth;
            _targetObject.Height = _oldHeight;
        }
    }
}
