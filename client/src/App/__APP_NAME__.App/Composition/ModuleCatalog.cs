using System.Collections.Generic;
// kakehashi:module-imports:begin
// kakehashi:unit-activity:begin
using __ROOT_NAMESPACE__.Modules.Activity.UI;
// kakehashi:unit-activity:end
using __ROOT_NAMESPACE__.Modules.Auth.UI;
// kakehashi:unit-notes:begin
using __ROOT_NAMESPACE__.Modules.Notes.UI;
// kakehashi:unit-notes:end
// kakehashi:module-imports:end
using __ROOT_NAMESPACE__.UI.Contracts;

namespace __ROOT_NAMESPACE__.App.Composition;

/// <summary>
/// The single place that lists the feature modules composed into the application. Add a module by
/// referencing its UI project and adding its <see cref="IModule"/> here.
/// </summary>
/// <remarks>
/// The markers below — kakehashi:module-imports:begin and its kind — delimit the wiring a
/// generator writes and a removable unit takes back: docs/BOILERPLATE.md.
/// </remarks>
internal static class ModuleCatalog
{
    public static IReadOnlyList<IModule> Modules { get; } = [
        // kakehashi:module-registrations:begin
        // kakehashi:unit-notes:begin
        new NotesModule(),
        // kakehashi:unit-notes:end
        // kakehashi:unit-activity:begin
        new ActivityModule(),
        // kakehashi:unit-activity:end
        new AuthModule(),
        // kakehashi:module-registrations:end
    ];
}
