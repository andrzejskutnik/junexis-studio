using Avalonia.Controls;
using Junexis.Studio.Abstractions;
using System;
using System.Collections.Generic;


namespace Junexis.Studio.Shell;

/// <summary>
/// Implementacja <see cref="IViewRegistry"/> dla Avalonia.
/// Tworzy widoki jako <see cref="Control"/> i ustawia im <see cref="Control.DataContext"/>.
/// </summary>
public sealed class AvaloniaViewRegistry : IViewRegistry
{
    private readonly Dictionary<Type, Func<object, Control>> _factories = new();

    /// <inheritdoc />
    public void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : class, new()
    {
        var viewModelType = typeof(TViewModel);

        _factories[viewModelType] = viewModel =>
        {
            var view = new TView();
            if (view is not Control control)
            {
                throw new InvalidOperationException(
                    $"Registered view type '{typeof(TView).FullName}' is not an Avalonia Control.");
            }

            control.DataContext = viewModel;
            return control;
        };
    }

    /// <inheritdoc />
    public object? Resolve(object viewModel)
    {
        if (viewModel is null)
        {
            throw new ArgumentNullException(nameof(viewModel));
        }

        if (viewModel is Control control)
        {
            return control;
        }

        for (var type = viewModel.GetType(); type is not null; type = type.BaseType)
        {
            if (_factories.TryGetValue(type, out var factory))
            {
                return factory(viewModel);
            }
        }

        return null;
    }
}
