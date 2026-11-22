using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace kursa.Infrastructure;

/// <summary>Сравнивает текущие поля с последним успешно применённым набором.</summary>
public sealed class FilterIndicator
{
    private readonly Func<object?[]> _values;
    private readonly Expander _expander;
    private readonly Button _button;
    private readonly Viewbox _icon;
    private string _applied;
    private bool _forced;
    public bool IsDirty { get; private set; }
    public event EventHandler? StateChanged;

    public FilterIndicator(Button button, Expander expander, Func<object?[]> values)
    {
        _values=values; _button=button; _expander=expander;
        var canvas=new Canvas {Width=24,Height=24};
        canvas.Children.Add(new Path {Stroke=Brushes.White,StrokeThickness=2.2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,
            StrokeLineJoin=PenLineJoin.Round,Data=Geometry.Parse("M 3,11 A 9,9 0 0 1 18,5 L 21,8 M 21,3 L 21,8 L 16,8 M 21,13 A 9,9 0 0 1 6,19 L 3,16 M 3,21 L 3,16 L 8,16")});
        _icon=new Viewbox {Width=16,Height=16,Margin=new Thickness(0,0,7,0),Child=canvas,Visibility=Visibility.Collapsed};
        var content=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        content.Children.Add(_icon); content.Children.Add(new TextBlock {Text="Применить",VerticalAlignment=VerticalAlignment.Center});
        button.Content=content; _applied=Snapshot(); Update();
    }
    private string Snapshot() => JsonSerializer.Serialize(_values());

    public void Track(params Control[] controls)
    {
        foreach (var control in controls)
        {
            switch (control)
            {
                case DatePicker picker:
                    picker.SelectedDateChanged += (_,_) => Update();
                    picker.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_,_) => Update()), true);
                    break;
                case TextBox text:
                    text.TextChanged += (_,_) => Update(); break;
                case ComboBox combo:
                    combo.SelectionChanged += (_,_) => Update(); break;
                case CheckBox check:
                    check.Checked += (_,_) => Update(); check.Unchecked += (_,_) => Update(); check.Indeterminate += (_,_) => Update(); break;
            }
        }
    }
    public void MarkApplied() { _forced=false; _applied=Snapshot(); Update(); }
    public void ForceDirty() { _forced=true; Update(); }
    public void Update()
    {
        var dirty=_forced || Snapshot()!=_applied;
        _icon.Visibility=dirty ? Visibility.Visible : Visibility.Collapsed;
        _expander.Header=dirty ? "Фильтры · есть неприменённые изменения" : "Фильтры";
        _button.ToolTip=dirty ? "Показан прежний результат. Нажмите, чтобы применить новые параметры." : "Применить фильтры";
        if (dirty != IsDirty) { IsDirty=dirty; StateChanged?.Invoke(this,EventArgs.Empty); }
    }
}
