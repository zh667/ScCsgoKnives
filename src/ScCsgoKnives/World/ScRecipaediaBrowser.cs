using System.Reflection;
namespace Game;

/// <summary>Only the verified native and RecipaediaEX 2.0.0.1 browser contracts.
/// The extension passes a BlockItem, while native help passes an integer.</summary>
public static class ScRecipaediaBrowser {
    public static bool Selection(Widget widget, out ButtonWidget recipes, out int value) {
        recipes = null; value = 0;
        if (widget is RecipaediaScreen native) {
            recipes = native.m_recipesButton;
            if (native.m_blocksList.SelectedItem is int v) { value = v; return true; }
            return false;
        }
        Type type = widget?.GetType();
        if (type?.FullName != "RecipaediaEX.UI.RecipaediaEXScreen") return false;
        recipes = type.GetField("m_recipesButton")?.GetValue(widget) as ButtonWidget;
        var list = type.GetField("m_blocksList")?.GetValue(widget) as ListPanelWidget;
        object item = list?.SelectedItem;
        if (recipes is null || item?.GetType().FullName != "RecipaediaEX.Implementation.BlockItem") return false;
        if (item.GetType().GetProperty("Value")?.GetValue(item) is not int blockValue) return false;
        value = blockValue; return true;
    }
}
