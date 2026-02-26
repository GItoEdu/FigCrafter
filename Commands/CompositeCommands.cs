using System.Collections.Generic;
using System.Linq;
using FigCrafterApp.Models;

namespace FigCrafterApp.Commands
{
    public class MoveObjectsCommand : IUndoableCommand
    {
        private readonly List<(GraphicObject Obj, float OldX, float OldY, float NewX, float NewY)> _moves;

        public MoveObjectsCommand(IEnumerable<(GraphicObject Obj, float OldX, float OldY, float NewX, float NewY)> moves)
        {
            _moves = moves.ToList();
        }

        public void Execute()
        {
            foreach (var move in _moves)
            {
                move.Obj.X = move.NewX;
                move.Obj.Y = move.NewY;
            }
        }

        public void Undo()
        {
            foreach (var move in _moves)
            {
                move.Obj.X = move.OldX;
                move.Obj.Y = move.OldY;
            }
        }
    }

    public class CompositeCommand : IUndoableCommand
    {
        private readonly List<IUndoableCommand> _commands;

        public CompositeCommand(IEnumerable<IUndoableCommand> commands)
        {
            _commands = commands.ToList();
        }

        public void Execute()
        {
            foreach (var command in _commands)
            {
                command.Execute();
            }
        }

        public void Undo()
        {
            // 複数のコマンドをアンドゥする時は逆順に実行する
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Undo();
            }
        }
    }
}
