using System.Globalization;
using PhiCoding.Providers;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Controls;

namespace PhiCoding.Tui.Components;

public sealed partial class PromptInput
{
    // ──────── /sessions dialog ────────

    internal void ShowSessionsDialog()
    {
        var sessions = _navigator.ListRecentSessions(7);
        if (sessions.Count == 0)
        {
            ShowInfo("No sessions in the last 7 days");
            return;
        }

        var (list, recordsByIndex) = BuildSessionPicker(sessions);

        list.ItemActivated((_, e) =>
        {
            var target = (uint)e.Index < (uint)recordsByIndex.Count
                ? recordsByIndex[e.Index]
                : null;
            if (target is null) return;

            if (list.Parent is Dialog d) d.Close();
            _ = NavigateToSessionAsync(target.Id);
        });

        var dialog = new Dialog(new Markup("[bold]Sessions (last 7 days)[/]"), list)
        {
            IsResizable = false,
            IsDraggable = true,
            IsModal = true,
        };
        dialog.KeyDownRouted += (_, ev) =>
        {
            if (ev.Key == TerminalKey.Escape)
            {
                dialog.Close();
                Editor.App?.Focus(Editor);
            }
        };
        dialog.Show();
    }

    /// <summary>
    /// Builds the /sessions picker items and a position-parallel record map.
    /// <c>OptionList</c>'s <c>ItemActivated</c> index is the raw item position
    /// (date-group headers included), so activation must look the record up by
    /// position — a record-counting loop drifts once sessions span multiple
    /// day groups.
    /// </summary>
    internal static (OptionList<OptionListItem> List, List<SessionRecord?> Records)
        BuildSessionPicker(IReadOnlyList<SessionRecord> sessions)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var grouped = sessions
            .GroupBy(r => DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeMilliseconds(r.UpdatedAt).DateTime))
            .OrderByDescending(g => g.Key)
            .ToList();

        var list = new OptionList<OptionListItem>().ActivateOnClick(true);
        var recordsByIndex = new List<SessionRecord?>();
        foreach (var group in grouped)
        {
            var label = group.Key == today ? "Today"
                : group.Key == today.AddDays(-1) ? "Yesterday"
                : group.Key.ToString("MMM d", CultureInfo.InvariantCulture);
            list.Items.Add(new OptionListItem(label) { IsEnabled = false });
            recordsByIndex.Add(null);

            foreach (var r in group.OrderByDescending(x => x.UpdatedAt))
            {
                var time = DateTimeOffset.FromUnixTimeMilliseconds(r.UpdatedAt)
                    .ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
                var title = r.Title ?? r.Id[..8];
                var model = r.ProviderName.Length > 0
                    ? $"{r.ProviderName}/{r.Model}"
                    : r.Model;
                list.Items.Add(new OptionListItem($"  {title} · {time} · {model}"));
                recordsByIndex.Add(r);
            }
        }
        return (list, recordsByIndex);
    }

    // ──────── /connect dialog ────────

    internal void ShowConnectDialog()
    {
        var list = new OptionList<OptionListItem>().ActivateOnClick(true);
        foreach (var entry in _providers.Providers)
        {
            var label = FormatProviderLabel(
                entry, _session.State.ProviderName, _providers.HasApiKey(entry), _session.State.Model);
            list.Items.Add(new OptionListItem(label));
        }

        list.ItemActivated((_, e) =>
        {
            var entry = _providers.Providers[e.Index];
            if (list.Parent is Dialog d) d.Close();
            ConnectProvider(entry);
        });

        var dialog = new Dialog(new Markup("[bold]Connect a provider[/]"), list)
        {
            IsResizable = false,
            IsDraggable = true,
            IsModal = true,
        };
        dialog.KeyDownRouted += (_, ev) =>
        {
            if (ev.Key == TerminalKey.Escape)
            {
                dialog.Close();
                Editor.App?.Focus(Editor);
            }
        };
        dialog.Show();
    }

    internal void ConnectProviderByName(string name)
    {
        var entry = _providers.Providers.FirstOrDefault(
            p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            ShowInfo($"Unknown provider '{name}'. Run /connect to pick one.");
            return;
        }
        ConnectProvider(entry);
    }

    internal void ConnectProvider(ProviderCatalogEntry entry)
    {
        // Always prompt so the user can change an existing key (or keep it by
        // confirming the pre-filled value) — /connect must be able to modify
        // a stored/derived API key, not just set it the first time.
        var existingKey = _providers.ResolveApiKey(entry);
        PromptApiKey(entry, existingKey,
            enteredKey => ApplyApiKeyAndConnect(entry, enteredKey));
    }

    /// <summary>
    /// Persists the entered key to the credential store and connects with it.
    /// </summary>
    internal void ApplyApiKeyAndConnect(ProviderCatalogEntry entry, string apiKey)
    {
        _providers.SetApiKey(entry, apiKey);
        ConnectWithKey(entry, apiKey);
    }

    internal void ConnectWithKey(ProviderCatalogEntry entry, string apiKey) =>
        ConnectWithModel(entry, apiKey, _providers.ResolveDefaultModel(entry));

    /// <summary>
    /// Builds a runtime provider for <paramref name="entry"/>, switches the
    /// session to it with <paramref name="model"/>, and persists the default.
    /// </summary>
    internal void ConnectWithModel(ProviderCatalogEntry entry, string apiKey, string model)
    {
        var provider = _providers.CreateProvider(entry, apiKey);
        _session.SwitchProvider(provider, entry.Name, model);
        _providers.SaveDefault(entry, model);
        ShowInfo($"Connected to {entry.Name} · {model}");
    }

    private void PromptApiKey(
        ProviderCatalogEntry entry, string? existingKey, Action<string> onKey)
    {
        var textBox = new TextBox { IsPassword = true, Text = existingKey };
        var hint = existingKey is { Length: > 0 }
            ? $"[dim]API key for {entry.DisplayName} ({entry.Name}) — current key pre-filled; edit to replace, Enter to confirm[/]"
            : $"[dim]API key for {entry.DisplayName} ({entry.Name}) — Enter to confirm, Esc to cancel[/]";
        var body = new VStack(
            new Markup(hint),
            textBox).Spacing(1);

        var dialog = new Dialog(new Markup($"[bold]Connect {entry.DisplayName}[/]"), body)
        {
            IsResizable = false,
            IsDraggable = true,
            IsModal = true,
        };
        dialog.KeyDownRouted += (_, ev) =>
        {
            if (ev.Key == TerminalKey.Enter)
            {
                var value = (textBox.Text ?? "").Trim();
                if (value.Length == 0) return;
                dialog.Close();
                Editor.App?.Focus(Editor);
                onKey(value);
            }
            else if (ev.Key == TerminalKey.Escape)
            {
                dialog.Close();
                Editor.App?.Focus(Editor);
            }
        };
        dialog.Show();
        Editor.App?.Focus(textBox);
    }

    // ──────── /models dialog ────────

    internal void ShowModelsDialog()
    {
        var providers = BuildModelPickerProviders(
            _providers.Providers, _session.State.ProviderName, _providers.HasApiKey);
        if (providers.Count == 0)
        {
            ShowInfo("No provider connected. Run /connect first.");
            return;
        }

        var (items, map) = BuildModelPicker(
            providers, _session.State.ProviderName, _session.State.Model);
        var list = new OptionList<OptionListItem>().ActivateOnClick(true);
        foreach (var item in items)
            list.Items.Add(new OptionListItem(item.Label) { IsEnabled = item.IsEnabled });

        list.ItemActivated((_, e) =>
        {
            var target = (uint)e.Index < (uint)map.Count ? map[e.Index] : null;
            if (target is not { } selection) return;
            if (list.Parent is Dialog d) d.Close();
            SwitchToModel(selection.Entry, selection.Model);
        });

        var dialog = new Dialog(new Markup("[bold]Models[/]"), list)
        {
            IsResizable = false,
            IsDraggable = true,
            IsModal = true,
        };
        dialog.KeyDownRouted += (_, ev) =>
        {
            if (ev.Key == TerminalKey.Escape)
            {
                dialog.Close();
                Editor.App?.Focus(Editor);
            }
        };
        dialog.Show();
    }

    /// <summary>
    /// Switches to a model. A model of the current provider is a pure model
    /// switch; a model of another provider rebuilds the live provider from
    /// that provider's stored API key.
    /// </summary>
    private void SwitchToModel(ProviderCatalogEntry entry, string model)
    {
        if (entry.Name.Equals(_session.State.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            _session.SwitchModel(model);
            _providers.SaveDefault(entry, model);
            ShowInfo($"Model: {model}");
            return;
        }

        if (_providers.ResolveApiKey(entry) is { } apiKey)
        {
            ConnectWithModel(entry, apiKey, model);
            return;
        }

        ShowInfo($"No API key for {entry.Name}. Run /connect first.");
    }

    /// <summary>
    /// Providers shown in the <c>/models</c> picker: the current provider
    /// (even when keyless) plus every provider with a configured key, in
    /// catalog order, deduplicated.
    /// </summary>
    internal static IReadOnlyList<ProviderCatalogEntry> BuildModelPickerProviders(
        IReadOnlyList<ProviderCatalogEntry> catalog,
        string? currentProviderName,
        Func<ProviderCatalogEntry, bool> hasKey)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providers = new List<ProviderCatalogEntry>();
        foreach (var entry in catalog)
        {
            var isCurrent = entry.Name.Equals(currentProviderName, StringComparison.OrdinalIgnoreCase);
            if ((isCurrent || hasKey(entry)) && seen.Add(entry.Name))
                providers.Add(entry);
        }
        return providers;
    }

    /// <summary>One row in the <c>/models</c> picker; disabled rows are group headers.</summary>
    public sealed record ModelPickerItem(string Label, bool IsEnabled);

    /// <summary>
    /// Builds the models picker with a disabled header row per provider,
    /// its models, and a position-parallel map. The active model on the
    /// current provider is marked with a check, so the picker doubles as a
    /// cross-provider model switcher.
    /// </summary>
    internal static (IReadOnlyList<ModelPickerItem> Items, IReadOnlyList<(ProviderCatalogEntry Entry, string Model)?> Map)
        BuildModelPicker(
            IReadOnlyList<ProviderCatalogEntry> providers,
            string currentProviderName,
            string currentModel)
    {
        var items = new List<ModelPickerItem>();
        var map = new List<(ProviderCatalogEntry Entry, string Model)?>();
        foreach (var entry in providers)
        {
            items.Add(new ModelPickerItem($"  {entry.DisplayName}", IsEnabled: false));
            map.Add(null);

            var isCurrentProvider = entry.Name.Equals(currentProviderName, StringComparison.OrdinalIgnoreCase);
            foreach (var model in entry.Models)
            {
                var mark = isCurrentProvider && model == currentModel ? "✓ " : "  ";
                items.Add(new ModelPickerItem($"  {mark}{model}", IsEnabled: true));
                map.Add((entry, model));
            }
        }
        return (items, map);
    }

    /// <summary>Renders one <c>/connect</c> provider row.</summary>
    internal static string FormatProviderLabel(
        ProviderCatalogEntry entry,
        string? currentProviderName,
        bool hasKey,
        string? currentModel)
    {
        var connected = entry.Name.Equals(currentProviderName, StringComparison.OrdinalIgnoreCase);
        var model = connected && !string.IsNullOrEmpty(currentModel) ? $" · {currentModel}" : "";
        var noKey = hasKey ? "" : "  (no key)";
        return $"  {(connected ? "✓ " : "  ")}{entry.DisplayName} — {entry.Name}{model}{noKey}";
    }
}