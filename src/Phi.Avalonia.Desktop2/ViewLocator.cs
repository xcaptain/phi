using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Phi.Avalonia.Desktop2.ViewModels;
using Phi.Avalonia.Desktop2.Views;

namespace Phi.Avalonia.Desktop2;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;

        // Strongly-typed mapping avoids runtime reflection
        return data switch
        {
            MainViewModel => new MainWindow(),
            _ => new TextBlock { Text = $"Not Found: {data.GetType().Name}" }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}

