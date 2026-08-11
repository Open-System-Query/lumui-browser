using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lumui.Browser.Rendering;

public static partial class ReadingTextFormatter
{
    private static readonly ConditionalWeakTable<TextBlock, String> Originals =
        new ConditionalWeakTable<TextBlock, String>();
    private static readonly ConditionalWeakTable<Control, StrongBox<Boolean>> TreeStates =
        new ConditionalWeakTable<Control, StrongBox<Boolean>>();

    public static void Apply(
        TextBlock textBlock,
        String value,
        Boolean bionicReading)
    {
        textBlock.Inlines?.Clear();
        if (!bionicReading)
        {
            textBlock.Text = value;
            return;
        }

        textBlock.Text = null;
        foreach (Match match in TokenPattern().Matches(value))
        {
            String token = match.Value;
            if (String.IsNullOrWhiteSpace(token))
            {
                textBlock.Inlines?.Add(new Run(token));
                continue;
            }

            Int32 prefixLength = Math.Max(1, (token.Length + 1) / 2);
            textBlock.Inlines?.Add(new Run(token[..prefixLength])
            {
                FontWeight = FontWeight.Bold
            });
            if (prefixLength < token.Length)
            {
                textBlock.Inlines?.Add(new Run(token[prefixLength..]));
            }
        }
    }

    public static void ApplyTree(Control root, Boolean bionicReading)
    {
        if (!BeginTreeUpdate(root, bionicReading))
        {
            return;
        }
        foreach (TextBlock textBlock in TextBlocks(root))
        {
            ApplyTracked(textBlock, bionicReading);
        }
    }

    public static async Task ApplyTreeAsync(
        Control root,
        Boolean bionicReading,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, Task>? yield = null)
    {
        if (!BeginTreeUpdate(root, bionicReading))
        {
            return;
        }
        HashSet<Control> visited = new HashSet<Control>();
        Stack<Control> pending = new Stack<Control>();
        pending.Push(root);
        Stopwatch budget = Stopwatch.StartNew();
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Control control = pending.Pop();
            if (!visited.Add(control))
            {
                continue;
            }
            if (control is TextBlock textBlock)
            {
                ApplyTracked(textBlock, bionicReading);
            }
            AddChildren(control, pending);
            if (budget.ElapsedMilliseconds >= 2)
            {
                if (yield is null)
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
                else
                {
                    await yield(cancellationToken);
                }
                budget.Restart();
            }
        }
    }

    private static void AddChildren(
        Control control,
        Stack<Control> pending)
    {
        if (control is Panel panel)
        {
            foreach (Control child in panel.Children)
            {
                pending.Push(child);
            }
        }
        if (control is Decorator decorator
            && decorator.Child is Control decoratedChild)
        {
            pending.Push(decoratedChild);
        }
        if (control is ContentControl contentControl
            && contentControl.Content is Control content)
        {
            pending.Push(content);
        }
        if (control is ItemsControl itemsControl)
        {
            foreach (Control itemControl in itemsControl.GetRealizedContainers())
            {
                pending.Push(itemControl);
            }
        }
    }

    private static Boolean BeginTreeUpdate(
        Control root,
        Boolean bionicReading)
    {
        if (!TreeStates.TryGetValue(
                root,
                out StrongBox<Boolean>? state))
        {
            state = new StrongBox<Boolean>(bionicReading);
            TreeStates.Add(root, state);
            return bionicReading;
        }
        if (!state.Value && !bionicReading)
        {
            return false;
        }
        state.Value = bionicReading;
        return true;
    }

    private static IReadOnlyCollection<TextBlock> TextBlocks(Control root)
    {
        HashSet<TextBlock> textBlocks = new HashSet<TextBlock>();
        CollectTextBlocks(root, textBlocks, new HashSet<Control>());
        foreach (TextBlock textBlock in root
            .GetVisualDescendants()
            .OfType<TextBlock>())
        {
            textBlocks.Add(textBlock);
        }
        return textBlocks;
    }

    private static void CollectTextBlocks(
        Control control,
        ISet<TextBlock> textBlocks,
        ISet<Control> visited)
    {
        if (!visited.Add(control))
        {
            return;
        }
        if (control is TextBlock textBlock)
        {
            textBlocks.Add(textBlock);
        }
        if (control is Panel panel)
        {
            foreach (Control child in panel.Children)
            {
                CollectTextBlocks(child, textBlocks, visited);
            }
        }
        if (control is Decorator decorator && decorator.Child is Control childControl)
        {
            CollectTextBlocks(childControl, textBlocks, visited);
        }
        if (control is ContentControl contentControl
            && contentControl.Content is Control content)
        {
            CollectTextBlocks(content, textBlocks, visited);
        }
        if (control is ItemsControl itemsControl)
        {
            foreach (Control itemControl in itemsControl.GetRealizedContainers())
            {
                CollectTextBlocks(itemControl, textBlocks, visited);
            }
        }
    }

    private static void ApplyTracked(
        TextBlock textBlock,
        Boolean bionicReading)
    {
        if (bionicReading)
        {
            if (Originals.TryGetValue(textBlock, out _))
            {
                if (String.IsNullOrEmpty(textBlock.Text))
                {
                    return;
                }
                Originals.Remove(textBlock);
            }
            String value = textBlock.Text ?? String.Empty;
            if (value.Length == 0)
            {
                return;
            }
            Originals.Add(textBlock, value);
            Apply(textBlock, value, true);
            return;
        }
        if (Originals.TryGetValue(textBlock, out String? valueToRestore))
        {
            String current = textBlock.Text ?? String.Empty;
            String value = current.Length == 0
                ? valueToRestore ?? String.Empty
                : current;
            Originals.Remove(textBlock);
            Apply(textBlock, value, false);
        }
    }

    [GeneratedRegex(@"\s+|\S+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
