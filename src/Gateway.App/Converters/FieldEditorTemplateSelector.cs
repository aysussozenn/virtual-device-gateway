using System.Windows;
using System.Windows.Controls;
using Gateway.App.ViewModels;

namespace Gateway.App.Converters;

/// <summary>
/// Picks the editor template for an outgoing field by its <see cref="FieldEditVm.Widget"/> hint,
/// resolving an application resource named "editor.&lt;widget&gt;". This is the GUI extension
/// point: ship a new editor by adding an "editor.dial" DataTemplate resource and setting
/// "widget": "dial" on the ICD field — no selector or core change. Falls back to "editor.text".
/// </summary>
public sealed class FieldEditorTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is FieldEditVm vm && Application.Current is { } app)
        {
            if (app.TryFindResource($"editor.{vm.Widget}") is DataTemplate exact) return exact;
            if (app.TryFindResource("editor.text") is DataTemplate fallback) return fallback;
        }
        return base.SelectTemplate(item, container);
    }
}
