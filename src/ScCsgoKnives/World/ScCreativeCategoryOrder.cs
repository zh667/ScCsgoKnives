namespace Game;

/// <summary>API 1.9 exposes both lists publicly. Preserve the selected category while moving ours.</summary>
public static class ScCreativeCategoryOrder {
    static int Weapons<T>(IList<T> entries, Func<T, string> name) {
        for (int i = 0; i < entries.Count; i++) if (name(entries[i]) == "Weapons") return i;
        for (int i = 0; i < entries.Count; i++) if (name(entries[i]) == "武器") return i;
        return -1; // Never match CS武器 or another mod's category by substring.
    }
    static bool Move<T>(IList<T> entries, Func<T, string> name) {
        int weapons = Weapons(entries, name), current = -1;
        for (int i = 0; i < entries.Count; i++) if (name(entries[i]) == "CS武器") { current = i; break; }
        if (weapons < 0 || current < 0 || current == weapons + 1) return false;
        T entry = entries[current]; entries.RemoveAt(current);
        if (current < weapons) weapons--;
        entries.Insert(weapons + 1, entry);
        return true;
    }
    public static void Global() => Move(BlocksManager.m_categories, x => x);
    public static void Inventory(CreativeInventoryWidget widget) {
        var list = widget.m_categories;
        var inventory = widget.m_componentCreativeInventory;
        int index = inventory?.CategoryIndex ?? -1;
        var selected = index >= 0 && index < list.Count ? list[index] : null;
        if (!Move(list, x => x.Name)) return;
        if (inventory is not null && selected is not null) inventory.CategoryIndex = list.IndexOf(selected);
        widget.m_activeCategoryIndex = -1;
        foreach (var panel in widget.AllChildren.OfType<CreativeInventoryPanel>()) {
            panel.m_assignedCategoryIndex = -1;
            panel.m_assignedPageIndex = -1;
        }
    }
    public static void WidgetTree(Widget root) {
        if (root is CreativeInventoryWidget inventory) Inventory(inventory);
        if (root is ContainerWidget container)
            foreach (var child in container.Children) WidgetTree(child);
    }
}
