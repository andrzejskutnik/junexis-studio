using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Junexis.Studio.Abstractions;

namespace Junexis.Studio.Host;

/// <summary>Expandable debugger value shown in variables and watches trees.</summary>
public sealed class DebugInspectionNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isLoading;
    private bool _isLoaded;
    private string _value;
    private string? _type;
    private int _variablesReference;

    internal DebugInspectionNode(
        string name,
        string value,
        string? type,
        int variablesReference,
        int containerVariablesReference = 0,
        bool placeholder = false)
    {
        Name = name;
        _value = value;
        _type = type;
        _variablesReference = variablesReference;
        ContainerVariablesReference = containerVariablesReference;
        IsPlaceholder = placeholder;
        if (variablesReference > 0)
            Children.Add(new DebugInspectionNode("Loading…", string.Empty, null, 0, placeholder: true));
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Variable name or watch expression.</summary>
    public string Name { get; }

    /// <summary>Formatted value.</summary>
    public string Value => _value;

    /// <summary>Optional adapter type name.</summary>
    public string? Type => _type;

    /// <summary>DAP reference used to load nested values.</summary>
    public int VariablesReference => _variablesReference;

    /// <summary>DAP reference of the parent container used by setVariable and dataBreakpointInfo.</summary>
    public int ContainerVariablesReference { get; }

    /// <summary>Child values loaded from the adapter.</summary>
    public ObservableCollection<DebugInspectionNode> Children { get; } = [];

    /// <summary>Whether the tree node is expanded.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>Whether nested variables are currently being loaded.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        internal set => SetField(ref _isLoading, value);
    }

    /// <summary>Whether nested variables have already been loaded.</summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        internal set => SetField(ref _isLoaded, value);
    }

    internal bool IsPlaceholder { get; }

    internal static DebugInspectionNode FromVariable(DebugVariable variable, int containerVariablesReference = 0) =>
        new(variable.Name, variable.Value, variable.Type, variable.VariablesReference, containerVariablesReference);

    internal static DebugInspectionNode FromEvaluation(DebugEvaluation evaluation) =>
        new(evaluation.Expression, evaluation.Result, evaluation.Type, evaluation.VariablesReference);

    internal void ReplaceChildren(IEnumerable<DebugVariable> variables)
    {
        Children.Clear();
        foreach (var variable in variables)
            Children.Add(FromVariable(variable, VariablesReference));
        IsLoaded = true;
        IsLoading = false;
        IsExpanded = true;
    }

    internal void SetLoadError(string message)
    {
        Children.Clear();
        Children.Add(new DebugInspectionNode("Error", message, null, 0));
        IsLoaded = true;
        IsLoading = false;
        IsExpanded = true;
    }

    internal void ApplyUpdatedValue(DebugVariable variable)
    {
        _value = variable.Value;
        _type = variable.Type;
        _variablesReference = variable.VariablesReference;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VariablesReference)));
        Children.Clear();
        if (_variablesReference > 0)
            Children.Add(new DebugInspectionNode("Loading…", string.Empty, null, 0, placeholder: true));
        IsLoaded = false;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
