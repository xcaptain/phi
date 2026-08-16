using PhiCoding.Providers;

namespace PhiCoding.Prompt;

/// <summary>
/// One row in a model picker's dropdown. <see cref="IsHeader"/> rows
/// are styled as provider group dividers and ignored on selection; the
/// remaining rows carry the <see cref="Entry"/> + <see cref="Model"/> pair
/// passed to <see cref="ISession.SwitchProvider"/>.
/// </summary>
public sealed record ModelPickerItem
{
    public required string Label { get; init; }
    public required bool IsHeader { get; init; }
    public ProviderCatalogEntry? Entry { get; init; }
    public string? Model { get; init; }

    /// <summary>True for the currently-active provider/model row.</summary>
    public bool IsCurrent { get; init; }
}

/// <summary>
/// One row in a workspace picker's dropdown. <see cref="IsSentinel"/>
/// is the trailing "📁 Choose folder…" entry that opens the native
/// folder dialog; the remaining rows switch to a fresh session in their
/// <see cref="Cwd"/>.
/// </summary>
public sealed record WorkspacePickerItem
{
    public required string Label { get; init; }
    public required bool IsSentinel { get; init; }
    public required string Cwd { get; init; }
}

/// <summary>
/// Pure builders for the prompt input's picker item lists. UI-agnostic so
/// every shell (Avalonia desk, tests) renders the same rows.
/// </summary>
public static class PromptInputPickerBuilder
{
    /// <summary>
    /// Ordered list backing the model dropdown. The current provider's
    /// models come first, followed by the rest alphabetically. Each
    /// provider group is preceded by a header row carrying its
    /// <see cref="ProviderCatalogEntry.DisplayName"/>.
    /// </summary>
    public static IReadOnlyList<ModelPickerItem> BuildModelPickerItems(
        IEnumerable<ProviderCatalogEntry> providers,
        string currentProviderName,
        string currentModel,
        Func<ProviderCatalogEntry, bool> hasApiKey)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(hasApiKey);

        var ordered = providers
            .OrderBy(p => string.Equals(p.Name, currentProviderName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<ModelPickerItem>();
        foreach (var p in ordered)
        {
            items.Add(new ModelPickerItem
            {
                Label = $"  {p.DisplayName}",
                IsHeader = true,
            });
            foreach (var m in p.Models)
            {
                var isCurrent = string.Equals(p.Name, currentProviderName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m, currentModel, StringComparison.Ordinal);
                items.Add(new ModelPickerItem
                {
                    Label = isCurrent
                        ? $"    ✓ {p.Name} · {m}"
                        : $"    {p.Name} · {m}",
                    IsHeader = false,
                    Entry = p,
                    Model = m,
                    IsCurrent = isCurrent,
                });
            }
        }
        return items;
    }

    /// <summary>
    /// Ordered list backing the workspace dropdown: distinct workspaces
    /// derived from session records, plus the current cwd if missing, plus
    /// a trailing "📁 Choose folder…" sentinel row.
    /// </summary>
    public static IReadOnlyList<WorkspacePickerItem> BuildWorkspacePickerItems(
        IEnumerable<string> knownWorkspaces,
        string cwd)
    {
        ArgumentNullException.ThrowIfNull(knownWorkspaces);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<WorkspacePickerItem>();
        foreach (var w in knownWorkspaces)
        {
            var full = Path.GetFullPath(w);
            if (!seen.Add(full)) continue;
            list.Add(new WorkspacePickerItem
            {
                Label = WorkspaceLabel(full),
                IsSentinel = false,
                Cwd = full,
            });
        }
        if (!string.IsNullOrEmpty(cwd))
        {
            var fullCwd = Path.GetFullPath(cwd);
            if (seen.Add(fullCwd))
                list.Insert(0, new WorkspacePickerItem
                {
                    Label = WorkspaceLabel(fullCwd),
                    IsSentinel = false,
                    Cwd = fullCwd,
                });
        }
        list.Add(new WorkspacePickerItem
        {
            Label = "📁 Choose folder…",
            IsSentinel = true,
            Cwd = string.Empty,
        });
        return list;
    }

    private static string WorkspaceLabel(string fullPath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return fullPath.StartsWith(home, StringComparison.Ordinal)
            ? "~" + fullPath[home.Length..]
            : fullPath;
    }
}

/// <summary>Index lookup helpers over the picker item lists.</summary>
public static class PromptInputPickerExtensions
{
    /// <summary>Index of the first selectable (non-header, non-sentinel) item.</summary>
    public static int IndexOfFirstSelectable(this IReadOnlyList<ModelPickerItem> items)
    {
        for (var i = 0; i < items.Count; i++)
            if (!items[i].IsHeader) return i;
        return -1;
    }

    /// <summary>Index of the row whose <see cref="WorkspacePickerItem.Cwd"/> matches.</summary>
    public static int IndexOfCwd(this IReadOnlyList<WorkspacePickerItem> items, string cwd)
    {
        if (string.IsNullOrEmpty(cwd)) return -1;
        var full = Path.GetFullPath(cwd);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsSentinel) continue;
            if (string.Equals(Path.GetFullPath(items[i].Cwd), full, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
