using System.Collections.ObjectModel;
using FigCrafterApp.Models;

namespace FigCrafterApp.Commands
{
    public interface IUndoableCommand
    {
        void Execute();
        void Undo();
    }
}
