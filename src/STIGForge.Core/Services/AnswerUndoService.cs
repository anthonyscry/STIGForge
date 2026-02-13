using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class AnswerUndoService
{
  private readonly Stack<ManualAnswer> _undoStack = new();
  private const int MaxUndoDepth = 50;

  public void Push(ManualAnswer previousState)
  {
    if (_undoStack.Count >= MaxUndoDepth)
    {
      var list = _undoStack.ToList();
      list.RemoveAt(list.Count - 1);
      _undoStack.Clear();
      foreach (var item in list.AsEnumerable().Reverse())
        _undoStack.Push(item);
    }
    _undoStack.Push(previousState);
  }

  public ManualAnswer? Pop() => _undoStack.Count > 0 ? _undoStack.Pop() : null;

  public bool CanUndo => _undoStack.Count > 0;

  public void Clear() => _undoStack.Clear();
}
