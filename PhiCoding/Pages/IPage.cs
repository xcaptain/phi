using XenoAtom.Terminal.UI;

namespace PhiCoding.Pages;

/// <summary>
/// A route-bound screen. The <see cref="Routing.PageRegistry"/> resolves an
/// <see cref="Routing.AppRoute"/> to a page; the page builds its own view and
/// view state and renders them via <see cref="Build"/>.
/// <para>
/// Focus after mounting is not a page contract: pages mark their default
/// focus target with <c>AutoFocus</c> and the terminal host restores it.
/// </para>
/// </summary>
public interface IPage
{
    /// <summary>Builds the page's visual tree for the current route.</summary>
    Visual Build();
}
