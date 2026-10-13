using ItchDB.Core;

namespace ItchDB.Web.Services;

/// <summary>Picks the Russian or English UI string for the current circuit.</summary>
public sealed class UiText(UiState ui)
{
    public UiLanguage Lang => ui.Language;

    public string S(string russian, string english) => ui.Language == UiLanguage.En ? english : russian;

    public string RowsLabel => S("Строк:", "Rows:");

    public string PagerInfo => S("{first_item}–{last_item} из {all_items}", "{first_item}–{last_item} of {all_items}");

    public string SortLabel => S("Сортировка", "Sort");

    public string SearchPlaceholder => S("Поиск", "Search");
}
