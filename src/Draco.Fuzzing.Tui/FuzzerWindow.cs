using System.Collections.Generic;
using System.Linq;
using Draco.Fuzzing.Tracing;
using Draco.Fuzzing.Tui.Addons;
using Terminal.Gui;

namespace Draco.Fuzzing.Tui;

/// <summary>
/// A fuzzer window that's extensible with addons.
/// </summary>
public abstract class FuzzerWindow : Window, IFuzzerApplication
{
    private readonly EventTracer<object?> tracer;
    private readonly Dictionary<string, IFuzzerAddon> addons = [];

    private FuzzerWindow(EventTracer<object?> tracer)
    {
        this.tracer = tracer;
    }

    public IFuzzerAddon GetAddon(string name) => this.addons[name];

    /// <summary>
    /// Adds an addon to the fuzzer window.
    /// </summary>
    /// <param name="addon">The addon to add.</param>
    public void AddAddon(IFuzzerAddon addon) => this.addons.Add(addon.Name, addon);

    /// <summary>
    /// Initializes the fuzzer window with all addons registered.
    /// </summary>
    public void Initialize()
    {
        // Remove borders
        this.Border = new();

        // First we register all addons
        foreach (var addon in this.addons) addon.Value.Register(this, this.tracer);

        // Create a dictionary of views
        var views = this.addons.Values
            .Select(addon => (Name: addon.Name, View: addon.CreateView()))
            .Where(pair => pair.View is not null)
            .ToDictionary(view => view.Name, view => view.View!);

        // Lay them out using the user-defined logic
        var laidOutViews = this.Layout(views);

        // Menu-bar
        var menuBar = this.ConstructMenuBar();

        // Status-bar
        var statusBar = new StatusBar(this.addons.Values
            .Select(addon => addon.CreateStatusItem())
            .OfType<StatusItem>()
            .ToArray());

        // Add all views
        this.Add(laidOutViews.ToArray());
        Application.Top.Add(menuBar, this, statusBar);
    }

    /// <summary>
    /// Called to layout the views.
    /// </summary>
    /// <param name="views">The views to layout.</param>
    /// <returns>The laid out views to add to the window.</returns>
    protected abstract IEnumerable<View> Layout(IReadOnlyDictionary<string, View> views);

    private MenuBar ConstructMenuBar()
    {
        // Constructing the menu-bar is quite complex as it requires recursive merging of menu items
        return new(this.addons.Values
            .Select(addon => addon.CreateMenuBarItem())
            .OfType<MenuBarItem>()
            .GroupBy(item => item.Title)
            .Select(g => Merge(g, toplevel: true))
            .Cast<MenuBarItem>()
            .ToArray());

        static MenuItem Merge(IEnumerable<MenuItem> sameNameMenuItems, bool toplevel)
        {
            var asList = sameNameMenuItems.ToList();
            if (asList.Count == 1 && !toplevel)
            {
                // We can keep it a menu item
                return asList[0];
            }
            else
            {
                // There are multiple, we need a MenuBarItem
                return new MenuBarItem(
                    title: asList[0].Title,
                    children: asList
                        // If there are multiple, they have to be MenuBarItems to contain children
                        .Cast<MenuBarItem>()
                        .SelectMany(c => c.Children)
                        .GroupBy(i => i.Title)
                        .Select(g => Merge(g, toplevel: false))
                        .ToArray());
            }
        }
    }
}
