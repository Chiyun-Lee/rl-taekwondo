using System.Collections.Generic;
using System.Text;
using Godot;

public partial class ActionLog : CanvasLayer
{
    static ActionLog _instance;

    readonly LinkedList<string> _entries = new();
    readonly StringBuilder _sb = new();
    const int MaxEntries = 12;
    Label _label;

    public override void _Ready()
    {
        _instance = this;
        _label = new Label();
        _label.Position = new Vector2(10f, 10f);
        AddChild(_label);
    }

    public override void _ExitTree() { if (_instance == this) _instance = null; }

    public static void Log(string action)
    {
        if (_instance == null) return;
        _instance._entries.AddLast(action);
        if (_instance._entries.Count > MaxEntries)
            _instance._entries.RemoveFirst();
        _instance.Refresh();
    }

    void Refresh()
    {
        _sb.Clear();
        var node = _entries.Last;
        while (node != null)
        {
            _sb.Append(node.Value).Append('\n');
            node = node.Previous;
        }
        _label.Text = _sb.ToString();
    }
}
